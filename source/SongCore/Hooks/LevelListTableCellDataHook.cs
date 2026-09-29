using System;
using System.Globalization;
using System.Linq;
using MonoMod.RuntimeDetour;
using Zenject;

namespace SongCore.Hooks
{
    internal class LevelListTableCellDataHook : IInitializable, IDisposable
    {
        private Hook _setDataFromLevelAsyncHook = null!;

        public void Initialize()
        {
            _setDataFromLevelAsyncHook = new Hook(typeof(LevelListTableCell).GetMethod(nameof(LevelListTableCell.SetDataFromLevelAsync))!, RoundBpmAndShowAuthors, true);
        }

        public void Dispose()
        {
            _setDataFromLevelAsyncHook.Dispose();
        }

        private void RoundBpmAndShowAuthors(Action<LevelListTableCell, BeatmapLevel, bool, bool, bool, bool> original, LevelListTableCell instance, BeatmapLevel beatmapLevel, bool isFavorite, bool isPromoted, bool isUpdated, bool interactable)
        {
            original(instance, beatmapLevel, isFavorite, isPromoted, isUpdated, interactable);

            // Rounding BPM display for all maps, including official ones.
            instance._songBpmText.text = Math.Round(beatmapLevel.beatsPerMinute).ToString(CultureInfo.InvariantCulture);

            var authors = string.Join(", ", beatmapLevel.allMappers.Concat(beatmapLevel.allLighters).Distinct());
            if (!string.IsNullOrWhiteSpace(authors))
            {
                instance._songAuthorText.richText = true;
                instance._songAuthorText.text = $"<size=80%>{beatmapLevel.songAuthorName.Trim()}</size> <size=90%>[<color=#ff69b4>{authors.Replace("<", "<\u200B").Replace(">", ">\u200B")}</color>]</size>";
            }
        }
    }
}
