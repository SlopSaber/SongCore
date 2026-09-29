using System;
using MonoMod.RuntimeDetour;
using Zenject;

namespace SongCore.Hooks
{
    /// <summary>
    /// Allows negative note jump speed, which is otherwise reset to the default NJS value.
    /// When a negative NJS is used, notes will come from behind the player and spin toward them.
    /// </summary>
    /// <example>
    /// https://beatsaver.com/maps/6cd
    /// </example>
    internal class AllowNegativeNoteJumpSpeedHook : IInitializable, IDisposable
    {
        private Hook _negativeNoteJumpSpeedHook = null!;

        public void Initialize()
        {
            _negativeNoteJumpSpeedHook = new Hook(typeof(BeatmapDifficultyMethods).GetMethod(nameof(BeatmapDifficultyMethods.NoteJumpMovementSpeed))!, AllowNegativeJumpSpeed, true);
        }

        public void Dispose()
        {
            _negativeNoteJumpSpeedHook.Dispose();
        }

        private float AllowNegativeJumpSpeed(Func<BeatmapDifficulty, float, bool, float> original, BeatmapDifficulty difficulty, float noteJumpMovementSpeed, bool fastNotes)
        {
            return noteJumpMovementSpeed <= -VariableMovementDataProvider.kMinNoteJumpMovementSpeed ? noteJumpMovementSpeed : original(difficulty, noteJumpMovementSpeed, fastNotes);
        }
    }
}
