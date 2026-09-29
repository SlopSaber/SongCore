using System;
using System.Reflection;
using MonoMod.RuntimeDetour;
using SongCore.Utilities;
using Zenject;

namespace SongCore.Hooks
{
    internal class SongDataGameHooks : IInitializable, IDisposable
    {
        private readonly BeatmapKey _beatmapKey;

        private Hook? _rotationNoteSpawnLinesHook;
        private Hook? _oneSaberHook;
        private bool _showRotationNoteSpawnLines;
        private bool _oneSaber;

        private SongDataGameHooks(BeatmapKey beatmapKey)
        {
            _beatmapKey = beatmapKey;
        }

        public void Initialize()
        {
            var difficultyData = Collections.GetCustomLevelSongDifficultyData(_beatmapKey);

            if (difficultyData == null)
            {
                return;
            }

            if (difficultyData._showRotationNoteSpawnLines != null)
            {
                _showRotationNoteSpawnLines = (bool)difficultyData._showRotationNoteSpawnLines;
                _rotationNoteSpawnLinesHook = new Hook(typeof(BeatLineManager).GetMethod(nameof(BeatLineManager.HandleNoteWasSpawned), BindingFlags.Instance | BindingFlags.NonPublic)!, ShowOrHideRotationNoteSpawnLines, true);
            }

            if (difficultyData._oneSaber != null)
            {
                _oneSaber = difficultyData._oneSaber.Value;
                _oneSaberHook = new Hook(typeof(SaberManager).GetMethod(nameof(SaberManager.Start), BindingFlags.Instance | BindingFlags.NonPublic)!, ForceOneSaber, true);
            }
        }

        public void Dispose()
        {
            _rotationNoteSpawnLinesHook?.Dispose();
            _oneSaberHook?.Dispose();
        }

        private void ShowOrHideRotationNoteSpawnLines(Action<BeatLineManager, NoteController> original, BeatLineManager instance, NoteController noteController)
        {
            if (_showRotationNoteSpawnLines)
            {
                original(instance, noteController);
            }
        }

        private void ForceOneSaber(Action<SaberManager> original, SaberManager instance)
        {
            Accessors.SaberManagerInitDataAccessor(ref instance) = new SaberManager.InitData(_oneSaber, instance._initData.oneSaberType);
            original(instance);
        }
    }
}
