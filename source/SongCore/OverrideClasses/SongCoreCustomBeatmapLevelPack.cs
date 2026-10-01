using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using SongCore.Utilities;
using UnityEngine;

namespace SongCore.OverrideClasses
{
    public class SongCoreCustomBeatmapLevelPack : BeatmapLevelPack
    {
        public SongCoreCustomBeatmapLevelPack(string packID, string packName, Sprite? coverImage, BeatmapLevel[] beatmapLevels, string shortPackName = "")
            : base(packID, packName, shortPackName.Length == 0 ? packName : shortPackName,  CreateCover(coverImage), coverImage ?? Loader.defaultCoverImage, PackBuyOption.Default, beatmapLevels, PlayerSensitivityFlag.Safe)
        {
        }

        internal SongCoreCustomBeatmapLevelPack(string packID, string packName, Sprite? coverImage, InitialLevels levels)
            : base(packID, packName, packName, CreateCover(coverImage), coverImage ?? Loader.defaultCoverImage, PackBuyOption.Default, Array.Empty<BeatmapLevel>(), PlayerSensitivityFlag.Safe)
        {
            // Both fields must agree before native callers can add or clear additional levels.
            var that = (BeatmapLevelPack)this;
            Accessors.BaseBeatmapLevelsAccessor(ref that) = levels.BaseLevels;
            Accessors.AllBeatmapLevelsAccessor(ref that) = levels.AllLevels;
        }

        internal sealed class InitialLevels
        {
            internal BeatmapLevel[] BaseLevels { get; }
            internal List<BeatmapLevel> AllLevels { get; }

            internal InitialLevels(BeatmapLevel[] ownedLevels)
            {
                BaseLevels = ownedLevels;
                AllLevels = new List<BeatmapLevel>(ownedLevels);
            }
        }

        private static Sprite CreateCover(Sprite? coverImage)
        {
            coverImage ??= Loader.defaultCoverImage;
            if (coverImage == null)
                throw new System.InvalidOperationException("SongCore cover sprites have not been initialized.");
            return coverImage == Loader.defaultCoverImage
                ? coverImage
                : Sprite.Create(coverImage.texture, coverImage.rect, coverImage.pivot, coverImage.texture.width);
        }

        public void UpdateBeatmapLevels(BeatmapLevel[] beatmapLevels)
        {
            var that = (BeatmapLevelPack)this;
            Accessors.AllBeatmapLevelsAccessor(ref that) = beatmapLevels.Concat(_additionalBeatmapLevels).ToList();
        }

        internal UpdateSnapshot CaptureUpdate(BeatmapLevel[] ownedLevels)
        {
            return new UpdateSnapshot(this, ownedLevels);
        }

        // The caller validates the entire refresh before publishing any pack or repository fields.
        internal void Publish(PreparedUpdate update)
        {
            if (!ReferenceEquals(update.Pack, this))
                throw new ArgumentException("Prepared levels belong to another pack.", nameof(update));

            var that = (BeatmapLevelPack)this;
            Accessors.AllBeatmapLevelsAccessor(ref that) = update.Levels;
        }

        internal sealed class UpdateSnapshot
        {
            private readonly BeatmapLevel[] _levels;
            private readonly BeatmapLevel[] _additionalLevels;
            private readonly List<BeatmapLevel> _originalAdditionalLevels;
            private readonly List<BeatmapLevel> _originalAllLevels;
            private readonly IEnumerator _additionalLevelsValidator;
            private readonly IEnumerator _allLevelsValidator;

            internal SongCoreCustomBeatmapLevelPack Pack { get; }

            internal UpdateSnapshot(SongCoreCustomBeatmapLevelPack pack, BeatmapLevel[] ownedLevels)
            {
                Pack = pack;
                _levels = ownedLevels;
                _originalAdditionalLevels = pack._additionalBeatmapLevels;
                _originalAllLevels = pack.AllBeatmapLevels();
                _additionalLevelsValidator = _originalAdditionalLevels.GetEnumerator();
                _allLevelsValidator = _originalAllLevels.GetEnumerator();
                _additionalLevels = _originalAdditionalLevels.ToArray();
            }

            internal bool IsCurrent()
            {
                if (!ReferenceEquals(Pack._additionalBeatmapLevels, _originalAdditionalLevels) ||
                    !ReferenceEquals(Pack.AllBeatmapLevels(), _originalAllLevels))
                    return false;

                try
                {
                    _additionalLevelsValidator.Reset();
                    _allLevelsValidator.Reset();
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }

            internal PreparedUpdate Prepare()
            {
                var levels = new List<BeatmapLevel>(_levels.Length + _additionalLevels.Length);
                levels.AddRange(_levels);
                levels.AddRange(_additionalLevels);
                return new PreparedUpdate(Pack, levels);
            }
        }

        internal sealed class PreparedUpdate
        {
            internal SongCoreCustomBeatmapLevelPack Pack { get; }
            internal List<BeatmapLevel> Levels { get; }

            internal PreparedUpdate(SongCoreCustomBeatmapLevelPack pack, List<BeatmapLevel> levels)
            {
                Pack = pack;
                Levels = levels;
            }
        }
    }
}
