using System;
using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Threading;
using ModestTree;
using MonoMod.RuntimeDetour;
using Zenject;

namespace SongCore.Hooks.BeatmapLevelCache
{
    /// <summary>
    /// Blocks blacklisted files from being read directly from disk, forcing consumers onto the cache-backed API
    /// that already holds their contents. A file is blacklisted once SongCore has loaded and cached its data, at
    /// which point reading it again from disk is redundant. Surfacing such a read as a failure keeps callers on
    /// the intended path instead of silently re-parsing the file.
    /// </summary>
    internal class IOBlacklistHook : IInitializable, IDisposable
    {
        private Hook _ctorHook = null!;

        public static ConcurrentDictionary<string, string> BlacklistedFiles { get; } = [];
        public static AsyncLocal<bool> AllowIO { get; } = new();

        public void Initialize()
        {
            _ctorHook = new Hook(typeof(FileStream).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(string), typeof(FileMode), typeof(FileAccess), typeof(FileShare), typeof(int), typeof(bool), typeof(FileOptions)], null)!, CtorHook, true);
        }

        public void Dispose()
        {
            _ctorHook.Dispose();
        }

        private void CtorHook(Action<FileStream, string, FileMode, FileAccess, FileShare, int, bool, FileOptions> original, FileStream instance, string path, FileMode mode, FileAccess access, FileShare share, int bufferSize, bool anonymous, FileOptions options)
        {
            if (!string.IsNullOrEmpty(path))
            {
                AssertFileNotBlacklisted(path);
            }

            original(instance, path, mode, access, share, bufferSize, anonymous, options);
        }

        private void AssertFileNotBlacklisted(string filePath)
        {
            if (AllowIO.Value)
            {
                return;
            }

            try
            {
                var fullPath = Path.GetFullPath(filePath);
                Assert.That(!BlacklistedFiles.TryGetValue(fullPath, out var reason), $"File '{fullPath}' is blacklisted. Reason: {reason}");
            }
            catch (ZenjectException ex)
            {
                // Ensure we don't get silenced by try-catch blocks.
                Plugin.Log.Error(ex.ToString());
                throw;
            }
        }
    }
}
