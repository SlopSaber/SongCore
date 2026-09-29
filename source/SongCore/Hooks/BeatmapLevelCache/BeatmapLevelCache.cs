using System;
using System.Threading;
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
                CacheJsonData(beatmapDataType, data);

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

            var cachedTask = _jsonTasks[idx];
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
                _jsonTasks[idx] = originalTask;
                data = await originalTask;

                if (originalTask == _jsonTasks[idx])
                {
                    CacheJsonData(beatmapDataType, data);
                }

                return data;
            }
            finally
            {
                IOBlacklistHook.AllowIO.Value = false;

                if (originalTask != null && originalTask == _jsonTasks[idx])
                {
                    _jsonTasks[idx] = null;
                }
            }
        }

        private bool TryGetCachedJsonData(IBeatmapLevelData beatmapLevelData, BeatmapKey beatmapKey, BeatmapDataType beatmapDataType, out string? value)
        {
            Assert.That(UnityGame.OnMainThread, "This method must be called on the main thread.");

            if (LevelMatches(beatmapLevelData) && (beatmapDataType == BeatmapDataType.Audio || DifficultyMatches(beatmapKey)))
            {
                var cachedData = _jsonData[(int)beatmapDataType];
                if (cachedData != null)
                {
                    Plugin.Log.Debug($"Returning {beatmapDataType} JSON data from cache");
                    value = cachedData;
                    return true;
                }
            }

            value = null;
            return false;
        }

        private void CacheJsonData(BeatmapDataType beatmapDataType, string? data)
        {
            Plugin.Log.Debug($"Storing {beatmapDataType} JSON data in cache");
            _jsonData[(int)beatmapDataType] = data;
        }
    }
}
