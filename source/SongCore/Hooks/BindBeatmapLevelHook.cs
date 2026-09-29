using System;
using MonoMod.RuntimeDetour;
using Zenject;

namespace SongCore.Hooks
{
    /// <summary>
    /// This binds the <see cref="BeatmapLevel"/> to the game scene.
    /// </summary>
    internal class BindBeatmapLevelHook : IInitializable, IDisposable
    {
        private Hook _installBindingsHook = null!;

        public void Initialize()
        {
            _installBindingsHook = new Hook(typeof(GameplayCoreInstaller).GetMethod(nameof(GameplayCoreInstaller.InstallBindings))!, BindBeatmapLevel, true);
        }

        public void Dispose()
        {
            _installBindingsHook.Dispose();
        }

        private void BindBeatmapLevel(Action<GameplayCoreInstaller> original, GameplayCoreInstaller instance)
        {
            original(instance);
            instance.Container.Bind<BeatmapLevel>().FromInstance(instance._sceneSetupData.beatmapLevel).AsSingle();
        }
    }
}
