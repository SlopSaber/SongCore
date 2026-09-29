using System;
using MonoMod.RuntimeDetour;
using Zenject;

namespace SongCore.Hooks
{
    // TODO: Find better naming.
    internal class InternalRestartHook : IInitializable, IDisposable
    {
        private readonly Loader _loader;

        private Hook _restartGameHook = null!;

        private InternalRestartHook(Loader loader)
        {
            _loader = loader;
        }

        public void Initialize()
        {
            _restartGameHook = new Hook(typeof(MenuTransitionsHelper).GetMethod(nameof(MenuTransitionsHelper.RestartGame))!, SaveLoadedLevels, true);
        }

        public void Dispose()
        {
            _restartGameHook.Dispose();
        }

        private void SaveLoadedLevels(Action<MenuTransitionsHelper, Action<DiContainer>?> original, MenuTransitionsHelper instance, Action<DiContainer>? finishCallback)
        {
            _loader.StoreLoadedBeatmapSaveData();
            original(instance, finishCallback);
        }
    }
}
