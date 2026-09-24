using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SongCore.Utilities
{
    internal static class MenuThreadSampler
    {
        private const uint ThreadAccess = 0x0002 | 0x0008 | 0x0040;
        private const uint ContextControl = 0x00100001;
        private const int ContextFlagsOffset = 48;
        private const int RipOffset = 248;
        private const int ContextBufferSize = 1248;

        private static uint mainThreadId;
        private static int started;

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenThread(uint desiredAccess, bool inheritHandle, uint threadId);

        [DllImport("kernel32.dll")]
        private static extern uint SuspendThread(IntPtr thread);

        [DllImport("kernel32.dll")]
        private static extern uint ResumeThread(IntPtr thread);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetThreadContext(IntPtr thread, IntPtr context);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("mono-2.0-bdwgc.dll", EntryPoint = "mono_pmip", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr MonoMethodFromIp(IntPtr instructionPointer);

        internal static void CaptureMainThread()
        {
            mainThreadId = GetCurrentThreadId();
        }

        internal static void Start()
        {
            if (IntPtr.Size != 8 || mainThreadId == 0 || Interlocked.Exchange(ref started, 1) != 0)
            {
                return;
            }

            Task.Run(SampleMainThread);
        }

        private static void SampleMainThread()
        {
            IntPtr thread = OpenThread(ThreadAccess, false, mainThreadId);
            if (thread == IntPtr.Zero)
            {
                Plugin.Log.Warn($"Menu load trace: OpenThread failed: {Marshal.GetLastWin32Error()}");
                return;
            }

            IntPtr buffer = Marshal.AllocHGlobal(ContextBufferSize + 15);
            IntPtr context = new IntPtr((buffer.ToInt64() + 15) & ~15L);
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            try
            {
                for (int i = 0; i < 30; i++)
                {
                    Marshal.WriteInt32(context, ContextFlagsOffset, (int)ContextControl);
                    long instructionPointer = 0;
                    uint suspendCount = SuspendThread(thread);
                    if (suspendCount != uint.MaxValue)
                    {
                        try
                        {
                            if (GetThreadContext(thread, context))
                            {
                                instructionPointer = Marshal.ReadInt64(context, RipOffset);
                            }
                        }
                        finally
                        {
                            ResumeThread(thread);
                        }
                    }

                    if (instructionPointer != 0)
                    {
                        string name = "<native>";
                        try
                        {
                            IntPtr method = MonoMethodFromIp(new IntPtr(instructionPointer));
                            string? methodName = method == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(method);
                            if (methodName != null && methodName.Length != 0)
                            {
                                int offset = methodName.IndexOf(" + 0x", StringComparison.Ordinal);
                                name = offset < 0 ? methodName : methodName.Substring(0, offset);
                            }
                        }
                        catch (Exception exception)
                        {
                            name = $"<mono_pmip: {exception.GetType().Name}>";
                        }

                        counts.TryGetValue(name, out int count);
                        counts[name] = count + 1;
                    }

                    Thread.Sleep(100);
                }

                foreach (var result in counts.OrderByDescending(x => x.Value).Take(8))
                {
                    Plugin.Log.Info($"Menu load trace: mainThreadSamples={result.Value}/30 method={result.Key}");
                }
            }
            catch (Exception exception)
            {
                Plugin.Log.Warn($"Menu load trace: main-thread sampler failed: {exception}");
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                CloseHandle(thread);
            }
        }
    }
}
