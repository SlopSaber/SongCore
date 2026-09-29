using System;
using MonoMod.RuntimeDetour;
using Zenject;

namespace SongCore.Hooks.BeatmapLevelCache
{
    /// <summary>
    /// This fixes a potential race condition with <see cref="ScoreModel.ComputeMaxMultipliedScoreForBeatmap"/> when called concurrently.
    /// </summary>
    internal class ComputeMaxMultipliedScoreSafelyHook : IInitializable, IDisposable
    {
        private readonly object _lock = new();

        private Hook _computeMaxMultipliedScoreForBeatmapHook = null!;

        public void Initialize()
        {
            _computeMaxMultipliedScoreForBeatmapHook = new Hook(typeof(ScoreModel).GetMethod(nameof(ScoreModel.ComputeMaxMultipliedScoreForBeatmap))!, ComputeMaxMultipliedScoreForBeatmapSafe, true);
        }

        public void Dispose()
        {
            _computeMaxMultipliedScoreForBeatmapHook.Dispose();
        }

        private int ComputeMaxMultipliedScoreForBeatmapSafe(Func<IReadonlyBeatmapData, int> original, IReadonlyBeatmapData beatmapData)
        {
            lock (_lock)
            {
                return original(beatmapData);
            }
        }
    }
}
