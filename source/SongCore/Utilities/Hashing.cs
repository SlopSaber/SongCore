using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using BeatmapLevelSaveDataVersion4;
using BGLib.JsonExtension;
using SongCore.Data;
using SongCore.Hooks.BeatmapLevelCache;

namespace SongCore.Utilities
{
    public class Hashing
    {
        internal static ConcurrentDictionary<string, SongHashData> cachedSongHashData = new ConcurrentDictionary<string, SongHashData>();
        internal static ConcurrentDictionary<string, AudioCacheData> cachedAudioData = new ConcurrentDictionary<string, AudioCacheData>();
        public static readonly string cachedHashDataPath = Path.Combine(IPA.Utilities.UnityGame.UserDataPath, nameof(SongCore), "SongHashData.dat");
        public static readonly string cachedAudioDataPath = Path.Combine(IPA.Utilities.UnityGame.UserDataPath, nameof(SongCore), "SongDurationCache.dat");

        internal static async Task LoadCachedSongHashesAsync()
        {
            if (!File.Exists(cachedHashDataPath))
            {
                return;
            }

            try
            {
                var songHashData = await Task.Run(() => JsonFileHandler.ReadFromFile<ConcurrentDictionary<string, SongHashData>>(cachedHashDataPath));
                if (songHashData != null)
                {
                    cachedSongHashData = songHashData;
                    Plugin.Log.Info($"Finished loading cached hashes for {cachedSongHashData.Count} songs.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error loading cached song hashes: {ex.Message}");
                Plugin.Log.Error(ex);
            }
        }

        internal static async Task SaveCachedSongHashesAsync(ICollection<string> currentSongPaths)
        {
            foreach (var levelPath in cachedSongHashData.Keys)
            {
                var absolutePath = GetAbsolutePath(levelPath);
                if (!currentSongPaths.Contains(absolutePath) || (absolutePath == levelPath && IsInInstallPath(levelPath)))
                {
                    cachedSongHashData.TryRemove(levelPath, out _);
                }
            }

            Plugin.Log.Info($"Saving cached hashes for {cachedSongHashData.Count} songs.");

            try
            {
                await Task.Run(() => JsonFileHandler.WriteCompactWithoutDefault(cachedSongHashData, cachedHashDataPath));
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error saving cached song hashes: {ex.Message}");
                Plugin.Log.Error(ex);
            }
        }

        internal static async Task LoadCachedAudioDataAsync()
        {
            if (!File.Exists(cachedAudioDataPath))
            {
                return;
            }

            try
            {
                var audioData = await Task.Run(() => JsonFileHandler.ReadFromFile<ConcurrentDictionary<string, AudioCacheData>>(cachedAudioDataPath));
                if (audioData != null)
                {
                    cachedAudioData = audioData;
                    Plugin.Log.Info($"Finished loading cached durations for {cachedAudioData.Count} songs.");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error loading cached song durations: {ex.Message}");
                Plugin.Log.Error(ex);
            }
        }

        internal static async Task SaveCachedAudioDataAsync(ICollection<string> currentSongPaths)
        {
            foreach (var levelPath in cachedAudioData.Keys)
            {
                var absolutePath = GetAbsolutePath(levelPath);
                if (!currentSongPaths.Contains(absolutePath) || (absolutePath == levelPath && IsInInstallPath(levelPath)))
                {
                    cachedAudioData.TryRemove(levelPath, out _);
                }
            }

            Plugin.Log.Info($"Saving cached durations for {cachedAudioData.Count} songs.");

            try
            {
                await Task.Run(() => JsonFileHandler.WriteCompactWithoutDefault(cachedAudioData, cachedAudioDataPath));
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Error saving cached song durations: {ex.Message}");
                Plugin.Log.Error(ex);
            }
        }

        private static long GetDirectoryHash(string directory)
        {
            long hash = 0;
            var directoryInfo = new DirectoryInfo(directory);
            foreach (var f in directoryInfo.EnumerateFiles())
            {
                hash ^= f.CreationTimeUtc.ToFileTimeUtc();
                hash ^= f.LastWriteTimeUtc.ToFileTimeUtc();
                hash ^= f.Name.GetHashCode();
                hash ^= f.Length;
            }

            return hash;
        }

        private static bool GetCachedSongData(string customLevelPath, out long directoryHash, out string cachedSongHash)
        {
            directoryHash = GetDirectoryHash(customLevelPath);

            TryGetRelativePath(customLevelPath, out var relativePath);
            if (cachedSongHashData.TryGetValue(relativePath, out var cachedSong) && cachedSong.directoryHash == directoryHash)
            {
                cachedSongHash = cachedSong.songHash;
                return true;
            }

            cachedSongHash = string.Empty;
            return false;
        }

        public static string ComputeCustomLevelHash(BeatmapLevel level)
        {
            var hash = string.Empty;

            if (Loader.CustomLevelLoader._loadedBeatmapSaveData.TryGetValue(level.levelID, out var loadedSaveData))
            {
                if (loadedSaveData.standardLevelInfoSaveData != null)
                {
                    hash = ComputeCustomLevelHash(loadedSaveData.customLevelFolderInfo, loadedSaveData.standardLevelInfoSaveData);
                }
                else if (loadedSaveData.beatmapLevelSaveData != null)
                {
                    hash = ComputeCustomLevelHash(loadedSaveData.customLevelFolderInfo, loadedSaveData.beatmapLevelSaveData);
                }
            }

            return hash;
        }

        public static string ComputeCustomLevelHash(CustomLevelFolderInfo customLevelFolderInfo, StandardLevelInfoSaveData standardLevelInfoSaveData)
        {
            if (GetCachedSongData(customLevelFolderInfo.folderPath, out var directoryHash, out var songHash))
            {
                return songHash;
            }

            var infoPath = Path.Combine(customLevelFolderInfo.folderPath, CustomLevelPathHelper.kStandardLevelInfoFilename);
            var files = standardLevelInfoSaveData.difficultyBeatmapSets
                .SelectMany(difficultyBeatmapSet => difficultyBeatmapSet.difficultyBeatmaps)
                .Select(difficultyBeatmap => Path.Combine(customLevelFolderInfo.folderPath, difficultyBeatmap.beatmapFilename))
                .Where(File.Exists)
                .Prepend(infoPath);

            IOBlacklistHook.AllowIO.Value = true;
            try
            {
                var hash = CreateSha1FromLevelFiles(files);
                TryGetRelativePath(customLevelFolderInfo.folderPath, out var relativePath);
                cachedSongHashData[relativePath] = new SongHashData(directoryHash, hash);

                return hash;
            }
            finally
            {
                IOBlacklistHook.AllowIO.Value = false;
            }
        }

        public static string ComputeCustomLevelHash(CustomLevelFolderInfo customLevelFolderInfo, BeatmapLevelSaveData beatmapLevelSaveData)
        {
            if (GetCachedSongData(customLevelFolderInfo.folderPath, out var directoryHash, out var songHash))
            {
                return songHash;
            }

            var infoPath = Path.Combine(customLevelFolderInfo.folderPath, CustomLevelPathHelper.kStandardLevelInfoFilename);
            var audioDataPath = Path.Combine(customLevelFolderInfo.folderPath, beatmapLevelSaveData.audio.audioDataFilename);
            var files = beatmapLevelSaveData.difficultyBeatmaps.SelectMany(difficultyBeatmap => new[]
            {
                Path.Combine(customLevelFolderInfo.folderPath, difficultyBeatmap.beatmapDataFilename),
                Path.Combine(customLevelFolderInfo.folderPath, difficultyBeatmap.lightshowDataFilename)
            }).Prepend(audioDataPath).Where(File.Exists).Prepend(infoPath);

            IOBlacklistHook.AllowIO.Value = true;
            try
            {
                var hash = CreateSha1FromLevelFiles(files);
                TryGetRelativePath(customLevelFolderInfo.folderPath, out var relativePath);
                cachedSongHashData[relativePath] = new SongHashData(directoryHash, hash);

                return hash;
            }
            finally
            {
                IOBlacklistHook.AllowIO.Value = false;
            }
        }

        public static string GetAbsolutePath(string path)
        {
            path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (path.StartsWith("." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                return Path.Combine(IPA.Utilities.UnityGame.InstallPath, path.Substring(2));
            }

            return path;
        }

        public static bool TryGetRelativePath(string path, out string relativePath)
        {
            var fromPath = IPA.Utilities.UnityGame.InstallPath;

            if (!fromPath.EndsWith(Path.DirectorySeparatorChar))
            {
                fromPath += Path.DirectorySeparatorChar;
            }

            if (!path.StartsWith(fromPath, StringComparison.Ordinal))
            {
                relativePath = path;
                return false;
            }

            var fromUri = new Uri(fromPath);
            var toUri = new Uri(path);

            relativePath = Uri.UnescapeDataString(fromUri.MakeRelativeUri(toUri).ToString());

            if (!relativePath.StartsWith(".", StringComparison.Ordinal))
            {
                relativePath = Path.Combine(".", relativePath);
            }

            relativePath = relativePath.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

            return true;
        }


        public static bool IsInInstallPath(string path)
        {
            var fromPath = IPA.Utilities.UnityGame.InstallPath;

            if (!fromPath.EndsWith(Path.DirectorySeparatorChar))
            {
                fromPath += Path.DirectorySeparatorChar;
            }

            return path.StartsWith(fromPath, StringComparison.Ordinal);
        }

        private static string ToHexString(byte[] bytes)
        {
            return string.Create(bytes.Length * 2, bytes, (chars, state) =>
            {
                for (var i = 0; i < state.Length; i++)
                {
                    // https://stackoverflow.com/questions/311165/how-do-you-convert-a-byte-array-to-a-hexadecimal-string-and-vice-versa/14333437#14333437
                    var b = state[i] >> 4;
                    chars[i * 2] = (char)(b < 10 ? '0' + b : 'A' - 10 + b);
                    b = state[i] & 0xF;
                    chars[i * 2 + 1] = (char)(b < 10 ? '0' + b : 'A' - 10 + b);
                }
            });
        }

        private static string CreateSha1FromLevelFiles(IEnumerable<string> files)
        {
            using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);

            var buffer = ArrayPool<byte>.Shared.Rent(131072);
            try
            {
                foreach (var file in files)
                {
                    // Large reads go directly into our buffer, so the internal buffer size is irrelevant.
                    using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan);

                    int bytesRead;
                    while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        incrementalHash.AppendData(buffer, 0, bytesRead);
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return ToHexString(incrementalHash.GetHashAndReset());
        }
    }
}
