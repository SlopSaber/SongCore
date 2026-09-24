using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SongCore.Utilities
{
    internal static class MenuStackResolver
    {
        [DllImport("mono-2.0-bdwgc.dll", EntryPoint = "mono_pmip", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr MonoMethodFromIp(IntPtr instructionPointer);

        internal static void Start()
        {
            string? requests = Environment.GetEnvironmentVariable("SONGCORE_PMIP_REQUESTS");
            if (!string.IsNullOrEmpty(requests))
            {
                Task.Run(() => ResolveRequests(requests));
            }
        }

        private static void ResolveRequests(string requests)
        {
            int processed = 0;
            for (int poll = 0; poll < 400; poll++)
            {
                try
                {
                    if (File.Exists(requests))
                    {
                        string[] lines = File.ReadAllLines(requests);
                        while (processed < lines.Length)
                        {
                            string[] addresses = lines[processed++].Split(',');
                            if (addresses.Length < 2)
                            {
                                continue;
                            }

                            var methods = new HashSet<string>(StringComparer.Ordinal);
                            for (int frame = 1; frame < addresses.Length && frame <= 32; frame++)
                            {
                                if (!long.TryParse(addresses[frame], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long address))
                                {
                                    continue;
                                }

                                IntPtr method = MonoMethodFromIp(new IntPtr(address));
                                string? name = method == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(method);
                                if (name == null || name.Length == 0)
                                {
                                    continue;
                                }

                                int detailStart = name.IndexOf(" [{", StringComparison.Ordinal);
                                if (detailStart >= 0)
                                {
                                    name = name.Substring(0, detailStart);
                                }

                                if (methods.Add(name))
                                {
                                    Plugin.Log.Info($"Menu load trace: stack sample={addresses[0]} frame={frame - 1} method={name}");
                                }
                            }

                            Plugin.Log.Info($"Menu load trace: stack sample={addresses[0]} resolved={methods.Count}");
                        }
                    }
                }
                catch (Exception exception)
                {
                    Plugin.Log.Warn($"Menu load trace: stack resolver {exception.GetType().Name}: {exception.Message}");
                }

                Thread.Sleep(100);
            }
        }
    }
}
