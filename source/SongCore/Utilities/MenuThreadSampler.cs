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
        private const int RspOffset = 152;
        private const int RipOffset = 248;
        private const int ContextBufferSize = 1248;
        private const int StackBytes = 2048;

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

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadProcessMemory(IntPtr process, IntPtr address, [Out] byte[] buffer, UIntPtr size, out UIntPtr bytesRead);

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
            var stacks = new List<Tuple<int, byte[], int>>();
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
                                if (i == 5 || i == 12)
                                {
                                    long stackPointer = Marshal.ReadInt64(context, RspOffset);
                                    var stack = new byte[StackBytes];
                                    if (ReadProcessMemory(GetCurrentProcess(), new IntPtr(stackPointer), stack,
                                        new UIntPtr((uint)stack.Length), out UIntPtr bytesRead))
                                    {
                                        stacks.Add(Tuple.Create(i, stack, (int)bytesRead.ToUInt64()));
                                    }
                                }
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

                foreach (var snapshot in stacks)
                {
                    string previousMethod = string.Empty;
                    int found = 0;
                    for (int offset = 0; offset + 8 <= snapshot.Item3 && found < 40; offset += 8)
                    {
                        long address = BitConverter.ToInt64(snapshot.Item2, offset);
                        if (address <= 0)
                        {
                            continue;
                        }

                        IntPtr method = MonoMethodFromIp(new IntPtr(address));
                        string? name = method == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(method);
                        if (name == null || name.Length == 0)
                        {
                            continue;
                        }

                        int methodEnd = name.IndexOf(" [{", StringComparison.Ordinal);
                        if (methodEnd >= 0)
                        {
                            name = name.Substring(0, methodEnd);
                        }
                        if (name == previousMethod)
                        {
                            continue;
                        }

                        previousMethod = name;
                        Plugin.Log.Info($"Menu load trace: mainThreadStack sample={snapshot.Item1} sp+{offset:X3} method={name}");
                        found++;
                    }
                    Plugin.Log.Info($"Menu load trace: mainThreadStack sample={snapshot.Item1} resolved={found}");
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
