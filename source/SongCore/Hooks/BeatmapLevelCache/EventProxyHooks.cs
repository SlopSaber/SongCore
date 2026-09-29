using System;
using MonoMod.RuntimeDetour;
using MonoMod.Utils;
using Zenject;

namespace SongCore.Hooks.BeatmapLevelCache
{
    internal class EventProxyHooks : IInitializable, IDisposable
    {
        private Hook _levelCollectionTableViewAddHook = null!;
        private Hook _levelCollectionTableViewRemoveHook = null!;
        private Hook _levelCollectionViewControllerAddHook = null!;
        private Hook _levelCollectionViewControllerRemoveHook = null!;
        private Hook _standardLevelDetailViewControllerAddHook = null!;
        private Hook _standardLevelDetailViewControllerRemoveHook = null!;
        private Delegate? _levelCollectionTableViewDidSelectLevelDelegate;
        private Delegate? _levelCollectionViewControllerDidSelectLevelDelegate;
        private Delegate? _standardLevelDetailViewControllerDidChangeContentDelegate;

        public Delegate? LevelCollectionTableViewDidSelectLevelDelegate => _levelCollectionTableViewDidSelectLevelDelegate;
        public Delegate? LevelCollectionViewControllerDidSelectLevelDelegate => _levelCollectionViewControllerDidSelectLevelDelegate;
        public Delegate? StandardLevelDetailViewControllerDidChangeContentDelegate => _standardLevelDetailViewControllerDidChangeContentDelegate;

        public void Initialize()
        {
            _levelCollectionTableViewAddHook = new Hook(typeof(LevelCollectionTableView).GetMethod($"add_{nameof(LevelCollectionTableView.didSelectLevelEvent)}")!, AddLevelCollectionTableViewDidSelectLevelEvent, true);
            _levelCollectionTableViewRemoveHook = new Hook(typeof(LevelCollectionTableView).GetMethod($"remove_{nameof(LevelCollectionTableView.didSelectLevelEvent)}")!, RemoveLevelCollectionTableViewDidSelectLevelEvent, true);
            _levelCollectionViewControllerAddHook = new Hook(typeof(LevelCollectionViewController).GetMethod($"add_{nameof(LevelCollectionViewController.didSelectLevelEvent)}")!, AddLevelCollectionViewControllerDidSelectLevelEvent, true);
            _levelCollectionViewControllerRemoveHook = new Hook(typeof(LevelCollectionViewController).GetMethod($"remove_{nameof(LevelCollectionViewController.didSelectLevelEvent)}")!, RemoveLevelCollectionViewControllerDidSelectLevelEvent, true);
            _standardLevelDetailViewControllerAddHook = new Hook(typeof(StandardLevelDetailViewController).GetMethod($"add_{nameof(StandardLevelDetailViewController.didChangeContentEvent)}")!, AddDidChangeContentEvent, true);
            _standardLevelDetailViewControllerRemoveHook = new Hook(typeof(StandardLevelDetailViewController).GetMethod($"remove_{nameof(StandardLevelDetailViewController.didChangeContentEvent)}")!, RemoveDidChangeContentEvent, true);
        }

        public void Dispose()
        {
            _levelCollectionTableViewAddHook.Dispose();
            _levelCollectionTableViewRemoveHook.Dispose();
            _levelCollectionViewControllerAddHook.Dispose();
            _levelCollectionViewControllerRemoveHook.Dispose();
            _standardLevelDetailViewControllerAddHook.Dispose();
            _standardLevelDetailViewControllerRemoveHook.Dispose();
        }

        private void AddLevelCollectionTableViewDidSelectLevelEvent(Action<LevelCollectionTableView, Delegate> original, LevelCollectionTableView instance, Delegate value)
        {
            Helpers.EventAdd(ref _levelCollectionTableViewDidSelectLevelDelegate, value);
        }

        private void RemoveLevelCollectionTableViewDidSelectLevelEvent(Action<LevelCollectionTableView, Delegate> original, LevelCollectionTableView instance, Delegate value)
        {
            Helpers.EventRemove(ref _levelCollectionTableViewDidSelectLevelDelegate, value);
        }

        private void AddLevelCollectionViewControllerDidSelectLevelEvent(Action<LevelCollectionViewController, Delegate> original, LevelCollectionViewController instance, Delegate value)
        {
            Helpers.EventAdd(ref _levelCollectionViewControllerDidSelectLevelDelegate, value);
        }

        private void RemoveLevelCollectionViewControllerDidSelectLevelEvent(Action<LevelCollectionViewController, Delegate> original, LevelCollectionViewController instance, Delegate value)
        {
            Helpers.EventRemove(ref _levelCollectionViewControllerDidSelectLevelDelegate, value);
        }

        private void AddDidChangeContentEvent(Action<StandardLevelDetailViewController, Delegate> original, StandardLevelDetailViewController instance, Delegate value)
        {
            Helpers.EventAdd(ref _standardLevelDetailViewControllerDidChangeContentDelegate, value);
        }

        private void RemoveDidChangeContentEvent(Action<StandardLevelDetailViewController, Delegate> original, StandardLevelDetailViewController instance, Delegate value)
        {
            Helpers.EventRemove(ref _standardLevelDetailViewControllerDidChangeContentDelegate, value);
        }
    }
}
