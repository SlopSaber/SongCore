using System;
using System.Collections.Generic;
using IPA.Utilities;

namespace SongCore.Utilities
{
    internal static class Accessors
    {
        public static readonly FieldAccessor<BeatmapLevel, string>.Accessor LevelIDAccessor =
            FieldAccessor<BeatmapLevel, string>.GetAccessor(nameof(BeatmapLevel.levelID));

        public static readonly FieldAccessor<BeatmapLevel, float>.Accessor SongDurationAccessor =
            FieldAccessor<BeatmapLevel, float>.GetAccessor(nameof(BeatmapLevel.songDuration));

        public static readonly FieldAccessor<BeatmapLevel, float>.Accessor PreviewDurationAccessor =
            FieldAccessor<BeatmapLevel, float>.GetAccessor(nameof(BeatmapLevel.previewDuration));

        public static readonly FieldAccessor<BeatmapLevelPack, List<BeatmapLevel>>.Accessor AllBeatmapLevelsAccessor =
            FieldAccessor<BeatmapLevelPack, List<BeatmapLevel>>.GetAccessor(nameof(BeatmapLevelPack._allBeatmapLevels));

        public static readonly FieldAccessor<BeatmapLevelPack, BeatmapLevel[]>.Accessor BaseBeatmapLevelsAccessor =
            FieldAccessor<BeatmapLevelPack, BeatmapLevel[]>.GetAccessor(nameof(BeatmapLevelPack._beatmapLevels));

        public static readonly FieldAccessor<SaberManager, SaberManager.InitData>.Accessor SaberManagerInitDataAccessor =
            FieldAccessor<SaberManager, SaberManager.InitData>.GetAccessor(nameof(SaberManager._initData));

        public static readonly FieldAccessor<BeatmapLevelsRepository, BeatmapLevelPack[]>.Accessor BeatmapLevelPacksAccessor =
            FieldAccessor<BeatmapLevelsRepository, BeatmapLevelPack[]>.GetAccessor(nameof(BeatmapLevelsRepository._beatmapLevelPacks));

        public static readonly FieldAccessor<BeatmapLevelsRepository, Dictionary<string, BeatmapLevelPack>>.Accessor LevelPacksByIdAccessor =
            FieldAccessor<BeatmapLevelsRepository, Dictionary<string, BeatmapLevelPack>>.GetAccessor(nameof(BeatmapLevelsRepository._idToBeatmapLevelPack));

        public static readonly FieldAccessor<BeatmapLevelsRepository, Dictionary<string, BeatmapLevel>>.Accessor LevelsByIdAccessor =
            FieldAccessor<BeatmapLevelsRepository, Dictionary<string, BeatmapLevel>>.GetAccessor(nameof(BeatmapLevelsRepository._idToBeatmapLevel));

        public static readonly FieldAccessor<BeatmapLevelsRepository, Dictionary<string, string>>.Accessor PackIdsByLevelIdAccessor =
            FieldAccessor<BeatmapLevelsRepository, Dictionary<string, string>>.GetAccessor(nameof(BeatmapLevelsRepository._beatmapLevelIdToBeatmapLevelPackId));

        public static readonly FieldAccessor<CustomLevelLoader, Dictionary<string, CustomLevelLoader.LoadedSaveData>>.Accessor LoadedSaveDataAccessor =
            FieldAccessor<CustomLevelLoader, Dictionary<string, CustomLevelLoader.LoadedSaveData>>.GetAccessor(nameof(CustomLevelLoader._loadedBeatmapSaveData));

        public static readonly FieldAccessor<LevelCollectionTableView, Action<LevelCollectionTableView, BeatmapLevel>>.Accessor TableViewDidSelectLevelEventAccessor =
            FieldAccessor<LevelCollectionTableView, Action<LevelCollectionTableView, BeatmapLevel>>.GetAccessor(nameof(LevelCollectionTableView.didSelectLevelEvent));

        public static readonly FieldAccessor<LevelCollectionViewController, Action<LevelCollectionViewController, BeatmapLevel>>.Accessor ViewControllerDidSelectLevelEventAccessor =
            FieldAccessor<LevelCollectionViewController, Action<LevelCollectionViewController, BeatmapLevel>>.GetAccessor(nameof(LevelCollectionViewController.didSelectLevelEvent));

        public static readonly FieldAccessor<StandardLevelDetailViewController, Action<StandardLevelDetailViewController, StandardLevelDetailViewController.ContentType>>.Accessor DidChangeContentEventAccessor =
            FieldAccessor<StandardLevelDetailViewController, Action<StandardLevelDetailViewController, StandardLevelDetailViewController.ContentType>>.GetAccessor(nameof(StandardLevelDetailViewController.didChangeContentEvent));
    }
}
