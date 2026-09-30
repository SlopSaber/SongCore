using System;
using System.Threading;
using System.Collections.Generic;
using System.Threading.Tasks;
using IPA.Utilities;
using ModestTree;

namespace SongCore.Hooks.BeatmapLevelCache
{
    internal class BeatmapLevelCache
    {
        private static readonly int BeatmapDataTypeCount = Enum.GetValues(typeof(BeatmapDataType)).Length;

        private readonly string?[] _jsonData = new string?[BeatmapDataTypeCount];
        private readonly Task<string?>?[] _jsonTasks = new Task<string?>?[BeatmapDataTypeCount];
        // Keep immutable JSON for two recent difficulties of this level. Parsed/converted
        // beatmaps remain request-specific and are never shared by this cache.
        private const long MaxRecentJsonCharacters = 32L * 1024 * 1024;
        private readonly List<DifficultyJson> _recentJson = new List<DifficultyJson>(2);

        public CancellationTokenSource? CancellationTokenSource { get; private set; }
        public BeatmapLevel? BeatmapLevel { get; private set; }
        public TaskCompletionSource<bool>? BeatmapKeyTaskCompletionSource { get; private set; }
        public Task<IBeatmapLevelData?>? BeatmapLevelDataLoadingTask { get; private set; }
        public IBeatmapLevelData? BeatmapLevelData { get; set; }
        public BeatmapKey BeatmapKey { get; set; }
        public BeatmapDataRequest? BeatmapDataRequest { get; set; }
        public Task<IReadonlyBeatmapData?>? BeatmapDataLoadingTask { get; set; }

        public void Init(BeatmapLevel beatmapLevel, Func<BeatmapLevel, CancellationToken, Task<IBeatmapLevelData?>> loadBeatmapLevelDataFunc)
        {
            CancellationTokenSource?.Cancel();
            InvalidateLevel();
            CancellationTokenSource = new CancellationTokenSource();
            BeatmapKeyTaskCompletionSource = new TaskCompletionSource<bool>();
            BeatmapLevel = beatmapLevel;
            BeatmapLevelDataLoadingTask = loadBeatmapLevelDataFunc(beatmapLevel, CancellationTokenSource.Token);
        }

        public async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
        {
            Plugin.Log.Debug("Waiting for beatmap level cache to be ready");

            if (cancellationToken.IsCancellationRequested)
            {
                await Task.CompletedTask;
                return;
            }

            await BeatmapLevelDataLoadingTask!;

            if (cancellationToken.IsCancellationRequested)
            {
                await Task.CompletedTask;
                return;
            }

            await BeatmapKeyTaskCompletionSource!.Task;
        }

        public bool LevelMatches(IBeatmapLevelData beatmapLevelData)
        {
            return beatmapLevelData == BeatmapLevelData;
        }

        public bool DifficultyMatches(BeatmapKey beatmapKey)
        {
            return beatmapKey.IsValid() && beatmapKey == BeatmapKey;
        }

        public void InvalidateLevel()
        {
            _recentJson.Clear();
            BeatmapLevelData = null;
            _jsonData[0] = null;
            _jsonTasks[0] = null;
            InvalidateDifficulty();
        }

        public void InvalidateDifficulty()
        {
            BeatmapKey = new BeatmapKey();
            BeatmapDataRequest = null;
            BeatmapDataLoadingTask = null;

            Array.Clear(_jsonData, 1, _jsonData.Length - 1);
            Array.Clear(_jsonTasks, 1, _jsonTasks.Length - 1);
        }

        public string? GetJsonData(IBeatmapLevelData beatmapLevelData, BeatmapKey beatmapKey, BeatmapDataType beatmapDataType, Func<string?> original)
        {
            if (TryGetCachedJsonData(beatmapLevelData, beatmapKey, beatmapDataType, out var data))
            {
                return data;
            }

            IOBlacklistHook.AllowIO.Value = true;
            try
            {
                data = original();
                if (CanCache(beatmapLevelData, beatmapKey, beatmapDataType))
                    CacheJsonData(beatmapKey, beatmapDataType, data);

                return data;
            }
            finally
            {
                IOBlacklistHook.AllowIO.Value = false;
            }
        }

        public async Task<string?> GetJsonDataAsync(IBeatmapLevelData beatmapLevelData, BeatmapKey beatmapKey, BeatmapDataType beatmapDataType, Func<Task<string?>> original)
        {
            if (TryGetCachedJsonData(beatmapLevelData, beatmapKey, beatmapDataType, out var data))
            {
                return data;
            }

            var idx = (int)beatmapDataType;
            bool cacheable = CanCache(beatmapLevelData, beatmapKey, beatmapDataType);

            var cachedTask = cacheable ? _jsonTasks[idx] : null;
            if (cachedTask != null)
            {
                Plugin.Log.Debug($"Returning {beatmapDataType} JSON task from cache");
                return await cachedTask;
            }

            Task<string?>? originalTask = null;

            IOBlacklistHook.AllowIO.Value = true;
            try
            {
                originalTask = original();
                if (cacheable) _jsonTasks[idx] = originalTask;
                data = await originalTask;

                if (cacheable && originalTask == _jsonTasks[idx] && CanCache(beatmapLevelData, beatmapKey, beatmapDataType))
                {
                    CacheJsonData(beatmapKey, beatmapDataType, data);
                }

                return data;
            }
            finally
            {
                IOBlacklistHook.AllowIO.Value = false;

                if (cacheable && originalTask != null && originalTask == _jsonTasks[idx])
                {
                    _jsonTasks[idx] = null;
                }
            }
        }

        private bool TryGetCachedJsonData(IBeatmapLevelData beatmapLevelData, BeatmapKey beatmapKey, BeatmapDataType beatmapDataType, out string? value)
        {
            Assert.That(UnityGame.OnMainThread, "This method must be called on the main thread.");

            if (CanCache(beatmapLevelData, beatmapKey, beatmapDataType))
            {
                var cachedData = _jsonData[(int)beatmapDataType];
                if (cachedData != null)
                {
                    Plugin.Log.Debug($"Returning {beatmapDataType} JSON data from cache");
                    value = cachedData;
                    return true;
                }
                if (beatmapDataType != BeatmapDataType.Audio)
                {
                    for (int i = 0; i < _recentJson.Count; i++)
                    {
                        DifficultyJson entry = _recentJson[i];
                        if (entry.Key != beatmapKey || entry.Data[(int)beatmapDataType] == null) continue;
                        value = _jsonData[(int)beatmapDataType] = entry.Data[(int)beatmapDataType];
                        _recentJson.RemoveAt(i);
                        _recentJson.Add(entry);
                        Plugin.Log.Debug($"Returning recent-difficulty {beatmapDataType} JSON data from cache");
                        return true;
                    }
                }
            }

            value = null;
            return false;
        }

        private bool CanCache(IBeatmapLevelData beatmapLevelData, BeatmapKey beatmapKey, BeatmapDataType type) =>
            LevelMatches(beatmapLevelData) && (type == BeatmapDataType.Audio || DifficultyMatches(beatmapKey));

        private void CacheJsonData(BeatmapKey key, BeatmapDataType beatmapDataType, string? data)
        {
            Plugin.Log.Debug($"Storing {beatmapDataType} JSON data in cache");
            _jsonData[(int)beatmapDataType] = data;
            if (data == null || beatmapDataType == BeatmapDataType.Audio) return;

            DifficultyJson? entry = null;
            for (int i = 0; i < _recentJson.Count; i++)
            {
                if (_recentJson[i].Key != key) continue;
                entry = _recentJson[i];
                _recentJson.RemoveAt(i);
                break;
            }
            entry ??= new DifficultyJson(key);
            entry.Data[(int)beatmapDataType] = data;
            _recentJson.Add(entry);

            long characters = 0;
            foreach (DifficultyJson recent in _recentJson)
                foreach (string? json in recent.Data) characters += json?.Length ?? 0;
            while (_recentJson.Count > 2 || characters > MaxRecentJsonCharacters)
            {
                DifficultyJson oldest = _recentJson[0];
                foreach (string? json in oldest.Data) characters -= json?.Length ?? 0;
                _recentJson.RemoveAt(0);
            }
        }

        private sealed class DifficultyJson
        {
            public readonly BeatmapKey Key;
            public readonly string?[] Data = new string?[BeatmapDataTypeCount];
            public DifficultyJson(BeatmapKey key) => Key = key;
        }
    }
}
