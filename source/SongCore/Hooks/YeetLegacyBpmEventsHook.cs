using System;
using BeatmapSaveDataCommon;
using Mono.Cecil.Cil;
using MonoMod.Cil;
using MonoMod.RuntimeDetour;
using Zenject;

namespace SongCore.Hooks
{
    /// <summary>
    /// <see cref="BeatmapEventType.Event10"/> was briefly used as an official BPM change between 1.8.0 and 1.18.0,
    /// but it was never supported by custom mapping tools and later reused as a light event.
    /// The code to convert these events broke a lot of maps, so we are removing it here.
    /// </summary>
    internal class YeetLegacyBpmEventsHook : IInitializable, IDisposable
    {
        private ILHook _convertBeatmapSaveDataPreV2_5_0InlineHook = null!;

        public void Initialize()
        {
            _convertBeatmapSaveDataPreV2_5_0InlineHook = new ILHook(typeof(BeatmapSaveDataVersion2_6_0AndEarlier.BeatmapSaveData).GetMethod(nameof(BeatmapSaveDataVersion2_6_0AndEarlier.BeatmapSaveData.ConvertBeatmapSaveDataPreV2_5_0Inline))!, ctx =>
            {
                var cursor = new ILCursor(ctx);
                ILLabel? target = null;
                cursor.GotoNext(
                    MoveType.Before,
                    i => i.MatchLdloc(out _),
                    i => i.MatchCallvirt(out _),
                    i => i.MatchLdcI4(out var eventType) && eventType == (int)BeatmapEventType.LegacyBpmEventType,
                    i => i.MatchBneUn(out target));
                cursor.Emit(OpCodes.Br_S, target!);
            }, true);
        }

        public void Dispose()
        {
            _convertBeatmapSaveDataPreV2_5_0InlineHook.Dispose();
        }
    }
}
