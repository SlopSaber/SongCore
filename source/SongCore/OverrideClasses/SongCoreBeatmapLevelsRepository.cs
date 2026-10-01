using System;
using System.Collections;
using System.Collections.Generic;
using SongCore.Utilities;

namespace SongCore.OverrideClasses
{
    public class SongCoreBeatmapLevelsRepository : BeatmapLevelsRepository
    {
        private readonly List<BeatmapLevelPack> _customBeatmapLevelPacks = new();

        private SongCoreBeatmapLevelsRepository(IEnumerable<BeatmapLevelPack> beatmapLevelPacks)
            : base(beatmapLevelPacks)
        {
        }

        public static SongCoreBeatmapLevelsRepository CreateNew()
        {
            return new SongCoreBeatmapLevelsRepository(Array.Empty<BeatmapLevelPack>());
        }

        public void AddLevelPack(BeatmapLevelPack pack)
        {
            if (pack != null && !_customBeatmapLevelPacks.Contains(pack))
            {
                _customBeatmapLevelPacks.Add(pack);
                RefreshCollections();
            }
        }

        public void RemoveLevelPack(BeatmapLevelPack pack)
        {
            if (pack != null && _customBeatmapLevelPacks.Contains(pack))
            {
                _customBeatmapLevelPacks.Remove(pack);
                RefreshCollections();
            }
        }

        public void ClearLevelPacks()
        {
            _customBeatmapLevelPacks.Clear();
        }

        internal BeatmapLevelPack[] CaptureLevelPacks()
        {
            return _customBeatmapLevelPacks.ToArray();
        }

        internal void StageLevelPacks(BeatmapLevelPack[] packs)
        {
            _customBeatmapLevelPacks.Clear();
            _customBeatmapLevelPacks.AddRange(packs);
        }

        internal RefreshSnapshot CaptureRefresh(IReadOnlyList<BeatmapLevelPack> desiredPacks,
            IReadOnlyList<SongCoreCustomBeatmapLevelPack.UpdateSnapshot>? updates = null)
        {
            return new RefreshSnapshot(this, desiredPacks, updates);
        }

        // Publication follows validation of every pack and repository in the same owner operation.
        internal void Publish(PreparedRefresh refresh)
        {
            if (!ReferenceEquals(refresh.Repository, this))
                throw new ArgumentException("Prepared lookup data belongs to another repository.", nameof(refresh));

            _customBeatmapLevelPacks.Clear();
            _customBeatmapLevelPacks.AddRange(refresh.Packs);
            var that = (BeatmapLevelsRepository)this;
            Accessors.LevelPacksByIdAccessor(ref that) = refresh.LevelPacksById;
            Accessors.LevelsByIdAccessor(ref that) = refresh.LevelsById;
            Accessors.PackIdsByLevelIdAccessor(ref that) = refresh.PackIdsByLevelId;
            Accessors.BeatmapLevelPacksAccessor(ref that) = refresh.Packs;
        }

        private void RefreshCollections()
        {
            _idToBeatmapLevelPack.Clear();
            _idToBeatmapLevel.Clear();
            _beatmapLevelIdToBeatmapLevelPackId.Clear();

            var that = (BeatmapLevelsRepository)this;
            Accessors.BeatmapLevelPacksAccessor(ref that) = _customBeatmapLevelPacks.ToArray();
            foreach (var beatmapLevelPack in _beatmapLevelPacks)
            {
                _idToBeatmapLevelPack.Add(beatmapLevelPack.packID, beatmapLevelPack);
                foreach (var beatmapLevel in beatmapLevelPack.AllBeatmapLevels())
                {
                    _beatmapLevelIdToBeatmapLevelPackId.TryAdd(beatmapLevel.levelID, beatmapLevelPack.packID);
                    _idToBeatmapLevel.TryAdd(beatmapLevel.levelID, beatmapLevel);
                }
            }
        }

        internal sealed class RefreshSnapshot
        {
            private readonly BeatmapLevelPack[] _originalPublishedPacks;
            private readonly BeatmapLevelPack[] _originalPublishedPackReferences;
            private readonly Dictionary<string, BeatmapLevelPack> _originalLevelPacksById;
            private readonly Dictionary<string, BeatmapLevel> _originalLevelsById;
            private readonly Dictionary<string, string> _originalPackIdsByLevelId;
            private readonly IEnumerator _membershipValidator;
            private readonly IEnumerator _levelPacksByIdValidator;
            private readonly IEnumerator _levelsByIdValidator;
            private readonly IEnumerator _packIdsByLevelIdValidator;
            private readonly PackSnapshot[] _packs;

            internal SongCoreBeatmapLevelsRepository Repository { get; }

            internal RefreshSnapshot(SongCoreBeatmapLevelsRepository repository, IReadOnlyList<BeatmapLevelPack> desiredPacks,
                IReadOnlyList<SongCoreCustomBeatmapLevelPack.UpdateSnapshot>? updates)
            {
                Repository = repository;
                _originalPublishedPacks = repository._beatmapLevelPacks;
                _originalPublishedPackReferences = (BeatmapLevelPack[])_originalPublishedPacks.Clone();
                _originalLevelPacksById = repository._idToBeatmapLevelPack;
                _originalLevelsById = repository._idToBeatmapLevel;
                _originalPackIdsByLevelId = repository._beatmapLevelIdToBeatmapLevelPackId;
                _membershipValidator = repository._customBeatmapLevelPacks.GetEnumerator();
                _levelPacksByIdValidator = _originalLevelPacksById.GetEnumerator();
                _levelsByIdValidator = _originalLevelsById.GetEnumerator();
                _packIdsByLevelIdValidator = _originalPackIdsByLevelId.GetEnumerator();
                _packs = new PackSnapshot[desiredPacks.Count];
                for (var i = 0; i < desiredPacks.Count; i++)
                {
                    var captureLevels = true;
                    if (updates != null)
                    {
                        for (var j = 0; j < updates.Count; j++)
                        {
                            if (!ReferenceEquals(updates[j].Pack, desiredPacks[i]))
                                continue;
                            captureLevels = false;
                            break;
                        }
                    }
                    _packs[i] = new PackSnapshot(desiredPacks[i], captureLevels);
                }
            }

            internal bool IsCurrent()
            {
                if (!ReferenceEquals(Repository._beatmapLevelPacks, _originalPublishedPacks) ||
                    !ReferenceEquals(Repository._idToBeatmapLevelPack, _originalLevelPacksById) ||
                    !ReferenceEquals(Repository._idToBeatmapLevel, _originalLevelsById) ||
                    !ReferenceEquals(Repository._beatmapLevelIdToBeatmapLevelPackId, _originalPackIdsByLevelId))
                    return false;

                for (var i = 0; i < _originalPublishedPackReferences.Length; i++)
                    if (!ReferenceEquals(_originalPublishedPacks[i], _originalPublishedPackReferences[i]))
                        return false;

                try
                {
                    _membershipValidator.Reset();
                    _levelPacksByIdValidator.Reset();
                    _levelsByIdValidator.Reset();
                    _packIdsByLevelIdValidator.Reset();
                    foreach (var pack in _packs)
                        if (!pack.IsCurrent())
                            return false;
                    return true;
                }
                catch (InvalidOperationException)
                {
                    return false;
                }
            }

            internal PreparedRefresh Prepare(IReadOnlyList<SongCoreCustomBeatmapLevelPack.PreparedUpdate>? updates = null)
            {
                var packs = new BeatmapLevelPack[_packs.Length];
                var levelPacksById = new Dictionary<string, BeatmapLevelPack>();
                var levelsById = new Dictionary<string, BeatmapLevel>();
                var packIdsByLevelId = new Dictionary<string, string>();

                for (var i = 0; i < _packs.Length; i++)
                {
                    var pack = _packs[i];
                    packs[i] = pack.Pack;
                    levelPacksById.Add(pack.Id, pack.Pack);
                    IReadOnlyList<BeatmapLevel>? levels = pack.Levels;
                    if (updates != null)
                    {
                        for (var j = 0; j < updates.Count; j++)
                        {
                            if (!ReferenceEquals(updates[j].Pack, pack.Pack))
                                continue;
                            levels = updates[j].Levels;
                            break;
                        }
                    }

                    if (levels == null)
                        throw new InvalidOperationException("Prepared levels are missing for an updated pack.");

                    foreach (var level in levels)
                    {
                        packIdsByLevelId.TryAdd(level.levelID, pack.Id);
                        levelsById.TryAdd(level.levelID, level);
                    }
                }

                return new PreparedRefresh(Repository, packs, levelPacksById, levelsById, packIdsByLevelId);
            }
        }

        private sealed class PackSnapshot
        {
            private readonly List<BeatmapLevel> _originalLevels;
            private readonly IEnumerator _levelsValidator;

            internal BeatmapLevelPack Pack { get; }
            internal string Id { get; }
            internal BeatmapLevel[]? Levels { get; }

            internal PackSnapshot(BeatmapLevelPack pack, bool captureLevels)
            {
                Pack = pack;
                Id = pack.packID;
                _originalLevels = pack.AllBeatmapLevels();
                _levelsValidator = _originalLevels.GetEnumerator();
                Levels = captureLevels ? _originalLevels.ToArray() : null;
            }

            internal bool IsCurrent()
            {
                if (!ReferenceEquals(Pack.AllBeatmapLevels(), _originalLevels))
                    return false;
                _levelsValidator.Reset();
                return true;
            }
        }

        internal sealed class PreparedRefresh
        {
            internal SongCoreBeatmapLevelsRepository Repository { get; }
            internal BeatmapLevelPack[] Packs { get; }
            internal Dictionary<string, BeatmapLevelPack> LevelPacksById { get; }
            internal Dictionary<string, BeatmapLevel> LevelsById { get; }
            internal Dictionary<string, string> PackIdsByLevelId { get; }

            internal PreparedRefresh(SongCoreBeatmapLevelsRepository repository, BeatmapLevelPack[] packs,
                Dictionary<string, BeatmapLevelPack> levelPacksById, Dictionary<string, BeatmapLevel> levelsById,
                Dictionary<string, string> packIdsByLevelId)
            {
                Repository = repository;
                Packs = packs;
                LevelPacksById = levelPacksById;
                LevelsById = levelsById;
                PackIdsByLevelId = packIdsByLevelId;
            }
        }

    }
}
