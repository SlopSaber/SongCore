using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BeatmapDataLoaderVersion4;
using BeatmapLevelSaveDataVersion4;
using BeatmapSaveDataVersion4;
using BeatSaber.Destinations;
using BeatSaberMarkupLanguage.Settings;
using BGLib.JsonExtension;
using IPA.Utilities;
using IPA.Utilities.Async;
using Newtonsoft.Json;
using SongCore.Data;
using SongCore.Hooks.BeatmapLevelCache;
using SongCore.OverrideClasses;
using SongCore.UI;
using SongCore.Utilities;
using UnityEngine;
using UnityEngine.SceneManagement;
using Zenject;

namespace SongCore
{
    public class Loader : IInitializable, IDisposable, ITickable
    {
        private readonly GameScenesManager _gameScenesManager;
        private readonly LevelFilteringNavigationController _levelFilteringNavigationController;
        private readonly LevelPackDetailViewController _levelPackDetailViewController;
        private readonly BeatmapLevelsModel _beatmapLevelsModel;
        private readonly CustomLevelLoader _customLevelLoader;
        private readonly SpriteAsyncLoader _spriteAsyncLoader;
        private readonly BeatmapCharacteristicCollection _beatmapCharacteristicCollection;
        private readonly ProgressBar _progressBar;
        private readonly BSMLSettings _bsmlSettings;
        private readonly PluginConfig _config;
        private readonly SettingsController _settingsController;
        private readonly string _customWIPPath;
        private readonly string _customLevelsPath;

        private Task? _loadingTask;
        private CancellationTokenSource _loadingTaskCancellationTokenSource = new();
        private SongLoadProgress? _loadingProgress;
        private Task _levelPackRefreshTask = Task.CompletedTask;
        private bool _prepareLevelPacksOnWorkers;
        private CancellationToken? _levelPackRequestToken;
        private long _levelPackRefreshVersion;
        private bool _disposed;
        private static Task _catalogTask = Task.CompletedTask;
        private static readonly object HashLevelsLock = new();
        private static readonly object PendingSaveDataLock = new();
        private static volatile bool _stopping;

        private sealed class SongLoadProgress
        {
            internal float Value;
        }

        private Loader(GameScenesManager gameScenesManager, LevelFilteringNavigationController levelFilteringNavigationController, LevelPackDetailViewController levelPackDetailViewController, BeatmapLevelsModel beatmapLevelsModel, CustomLevelLoader customLevelLoader, SpriteAsyncLoader spriteAsyncLoader, BeatmapCharacteristicCollection beatmapCharacteristicCollection, ProgressBar progressBar, PluginConfig config, SettingsController settingsController, BSMLSettings bsmlSettings)

        {
            _gameScenesManager = gameScenesManager;
            _levelFilteringNavigationController = levelFilteringNavigationController;
            _levelPackDetailViewController = levelPackDetailViewController;
            _beatmapLevelsModel = beatmapLevelsModel;
            _customLevelLoader = customLevelLoader;
            _spriteAsyncLoader = spriteAsyncLoader;
            _beatmapCharacteristicCollection = beatmapCharacteristicCollection;
            _progressBar = progressBar;
            _bsmlSettings = bsmlSettings;
            _config = config;
            _settingsController = settingsController;
            // Path.GetFullPath is needed to normalize directory separators.
            _customWIPPath = Path.GetFullPath(Path.Combine(Application.dataPath, "CustomWIPLevels"));
            _customLevelsPath = Path.GetFullPath(CustomLevelPathHelper.customLevelsDirectoryPath);
            if (Instance != null)
            {
                Instance._loadingTaskCancellationTokenSource.Cancel();
                AreSongsLoading = false;
            }
            Instance = this;
        }

        // Actions for loading and refreshing beatmaps
        public static event Action<Loader>? LoadingStartedEvent;
        public static event Action<Loader, ConcurrentDictionary<string, BeatmapLevel>>? SongsLoadedEvent;
        public static event Action? OnLevelPacksRefreshed;

        // Temp collection to avoid concurrency issues with base game dictionary. Also used to store levels on internal restart.
        private static ConcurrentDictionary<string, CustomLevelLoader.LoadedSaveData> LoadedBeatmapSaveData = new ConcurrentDictionary<string, CustomLevelLoader.LoadedSaveData>();
        public static ConcurrentDictionary<string, BeatmapLevel> CustomLevels = new ConcurrentDictionary<string, BeatmapLevel>();
        public static ConcurrentDictionary<string, BeatmapLevel> CustomWIPLevels = new ConcurrentDictionary<string, BeatmapLevel>();
        public static ConcurrentDictionary<string, BeatmapLevel> CachedWIPLevels = new ConcurrentDictionary<string, BeatmapLevel>();
        public static readonly List<SeparateSongFolder> SeparateSongFolders = new List<SeparateSongFolder>();
        public static Sprite? defaultCoverImage;
        public static Loader? Instance;

        public static SongCoreCustomBeatmapLevelPack? CustomLevelsPack { get; private set; }
        public static SongCoreCustomBeatmapLevelPack? WIPLevelsPack { get; private set; }
        public static SongCoreCustomBeatmapLevelPack? CachedWIPLevelsPack { get; private set; }
        public static SongCoreBeatmapLevelsRepository? CustomLevelsRepository { get; private set; }
        public static bool AreSongsLoaded { get; private set; }
        public static bool AreSongsLoading { get; private set; }
        public static float LoadingProgress { get; private set; }
        private static BeatmapLevelsModel? BeatmapLevelsModelSOValue;
        public static BeatmapLevelsModel BeatmapLevelsModelSO
        {
            get => BeatmapLevelsModelSOValue ?? throw new InvalidOperationException("SongCore has not finished scene initialization.");
            private set => BeatmapLevelsModelSOValue = value;
        }
        private static CustomLevelLoader? CustomLevelLoaderValue;
        public static CustomLevelLoader CustomLevelLoader
        {
            get => CustomLevelLoaderValue ?? throw new InvalidOperationException("SongCore has not finished scene initialization.");
            private set => CustomLevelLoaderValue = value;
        }
        private static SpriteAsyncLoader? cachedMediaAsyncLoaderSOValue;
        public static SpriteAsyncLoader cachedMediaAsyncLoaderSO
        {
            get => cachedMediaAsyncLoaderSOValue ?? throw new InvalidOperationException("SongCore has not finished scene initialization.");
            private set => cachedMediaAsyncLoaderSOValue = value;
        }
        private static BeatmapCharacteristicCollection? beatmapCharacteristicCollectionValue;
        public static BeatmapCharacteristicCollection beatmapCharacteristicCollection
        {
            get => beatmapCharacteristicCollectionValue ?? throw new InvalidOperationException("SongCore has not finished scene initialization.");
            private set => beatmapCharacteristicCollectionValue = value;
        }

        public void Initialize()
        {
            _gameScenesManager.transitionDidFinishEvent += HandleSceneTransitionDidFinish;
            // BSML might fail to find the resource if done in a patched method.
            _bsmlSettings.AddSettingsMenu(nameof(SongCore), "SongCore.UI.settings.bsml", _settingsController);
        }

        public void Dispose()
        {
            _disposed = true;
            _loadingTaskCancellationTokenSource.Cancel();
            if (ReferenceEquals(Instance, this))
                AreSongsLoading = false;

            _bsmlSettings.RemoveSettingsMenu(_settingsController);

            SceneManager.activeSceneChanged -= HandleActiveSceneChanged;

            _gameScenesManager.transitionDidStartEvent -= HandleSceneTransitionDidStart;
            _gameScenesManager.transitionDidFinishEvent -= HandleSceneTransitionDidFinish;
        }

        internal static void StopCatalog()
        {
            _stopping = true;
            Instance?._loadingTaskCancellationTokenSource.Cancel();
            AreSongsLoading = false;
        }

        private bool IsCurrentLoader => !_stopping && !_disposed && ReferenceEquals(Instance, this);

        private bool OwnsLoadRequest(CancellationToken cancellationToken)
        {
            return IsCurrentLoader && cancellationToken == _loadingTaskCancellationTokenSource.Token;
        }

        private bool IsCurrentLoad(CancellationToken cancellationToken)
        {
            return OwnsLoadRequest(cancellationToken) && !cancellationToken.IsCancellationRequested;
        }

        private static Task QueueCatalogOperation(Func<Task> operation)
        {
            var previous = _catalogTask;
            var task = RunCatalogOperationAsync(previous, operation);
            _catalogTask = task;
            return task;
        }

        private static async Task RunCatalogOperationAsync(Task previous, Func<Task> operation)
        {
            // Yield before invoking callbacks so reentrant requests see this operation in the queue.
            await Task.Yield();
            try
            {
                await previous;
            }
            catch
            {
                // Each caller observes its own operation; a failure must not stop later requests.
            }
            await UnityGame.SwitchToMainThreadAsync();
            await operation();
        }

        /// <summary>
        /// Refresh songs on "R" key, full refresh on "Ctrl"+"R"
        /// </summary>
        public void Tick()
        {
            if (IsCurrentLoader && AreSongsLoading && _loadingProgress != null)
                LoadingProgress = Volatile.Read(ref _loadingProgress.Value);
            if (Input.GetKeyDown(KeyCode.R))
            {
                RefreshSongs(Input.GetKey(KeyCode.LeftControl));
            }

            if (Input.GetKeyDown(KeyCode.X) && Input.GetKey(KeyCode.LeftControl) && _loadingTask != null)
            {
                CancelSongLoading();
            }
        }

        private async void HandleSceneTransitionDidFinish(GameScenesManager.SceneTransitionType sceneTransitionType, ScenesTransitionSetupData scenesTransitionSetupData, DiContainer container)
        {
            _gameScenesManager.transitionDidFinishEvent -= HandleSceneTransitionDidFinish;
            if (!IsCurrentLoader)
                return;

            // Ensures that the static references are still valid Unity objects.
            // They'll be destroyed on internal restart.
            BeatmapLevelsModelSO = _beatmapLevelsModel;
            CustomLevelLoader = _customLevelLoader;
            cachedMediaAsyncLoaderSO = _spriteAsyncLoader;
            defaultCoverImage = _levelPackDetailViewController._defaultCoverSprite;
            beatmapCharacteristicCollection = _beatmapCharacteristicCollection;

            try
            {
                await QueueCatalogOperation(async () =>
                {
                    await Plugin.FolderInitializationTask;
                    await UnityGame.SwitchToMainThreadAsync();
                    if (!IsCurrentLoader)
                        return;
                    if (Hashing.cachedSongHashData.IsEmpty || !AreSongsLoaded)
                    {
                        var loadCaches = Hashing.cachedSongHashData.IsEmpty;
                        if (loadCaches)
                            await Task.WhenAll(Hashing.LoadCachedSongHashesAsync(), Hashing.LoadCachedAudioDataAsync());
                        await UnityGame.SwitchToMainThreadAsync();
                        if (!IsCurrentLoader)
                            return;
                        RefreshSongs(loadCaches);
                    }
                    else
                    {
                        await RefreshLevelPacksAsync();
                    }
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("Failed to initialize song collections:");
                Plugin.Log.Error(ex);
            }

            await UnityGame.SwitchToMainThreadAsync();

            if (!IsCurrentLoader)
                return;

            SceneManager.activeSceneChanged += HandleActiveSceneChanged;

            _gameScenesManager.transitionDidStartEvent += HandleSceneTransitionDidStart;
        }

        private static void ReportDuplicateSongs()
        {
            string[] duplicateHashes;
            lock (HashLevelsLock)
            {
                duplicateHashes = Collections.HashLevelDictionary
                    .Where(entry => entry.Value.Count > 1)
                    .Select(entry => entry.Key).ToArray();
            }
            foreach (var hash in duplicateHashes)
            {
                Plugin.Log.Notice("Found duplicates:");

                foreach (var levelPath in Hashing.cachedSongHashData
                             .Where(x => x.Value.songHash == hash)
                             .Select(x => x.Key))
                {
                    Plugin.Log.Notice($"  {levelPath}");
                }
            }
        }

        private void HandleSceneTransitionDidStart(GameScenesManager.SceneTransitionType sceneTransitionType, float duration)
        {
            CancelSongLoading();
        }

        private void CancelSongLoading()
        {
            if (IsCurrentLoader && AreSongsLoading)
            {
                _loadingTaskCancellationTokenSource.Cancel();
                AreSongsLoading = false;
                LoadingProgress = 0;
                _progressBar.ShowMessage("Loading cancelled\n<size=80%>Press R to resume</size>", false);
            }
        }

        private void HandleActiveSceneChanged(Scene previousScene, Scene nextScene)
        {
            if (_loadingTaskCancellationTokenSource.IsCancellationRequested && nextScene.name == "MainMenu")
            {
                Plugin.Log.Notice("Song loading was cancelled. Resuming...");
                RefreshSongs(false);
            }
        }

        /// <summary>
        /// This function will add/remove Level Packs from the Custom Levels tab if applicable
        /// </summary>
        public async void RefreshLevelPacks()
        {
            if (_prepareLevelPacksOnWorkers)
            {
                _levelPackRefreshTask = PrepareLevelPacksAsync(++_levelPackRefreshVersion, _levelPackRequestToken);
                return;
            }
            await UnityGame.SwitchToMainThreadAsync();
            if (!IsCurrentLoader)
                return;
            ++_levelPackRefreshVersion;

            CustomLevelsPack?.UpdateBeatmapLevels([.. CustomLevels.Values]);
            WIPLevelsPack?.UpdateBeatmapLevels([.. CustomWIPLevels.Values]);
            CachedWIPLevelsPack?.UpdateBeatmapLevels([.. CachedWIPLevels.Values]);

            if (CachedWIPLevelsPack != null && CustomLevelsRepository != null)
            {
                if (!CachedWIPLevels.IsEmpty)
                {
                    CustomLevelsRepository.AddLevelPack(CachedWIPLevelsPack ?? throw new InvalidOperationException("CachedWIPLevelsPack was not created."));
                }
                else
                {
                    CustomLevelsRepository.RemoveLevelPack(CachedWIPLevelsPack);
                }
            }

            foreach (var folderEntry in SeparateSongFolders)
            {
                if (folderEntry.SongFolderEntry.Pack == FolderLevelPack.NewPack)
                {
                    var levelPack = folderEntry.LevelPack ?? throw new InvalidOperationException("Separate song folder has no level pack.");
                    levelPack.UpdateBeatmapLevels([.. folderEntry.Levels.Values]);
                    if (CustomLevelsRepository != null && (!folderEntry.Levels.IsEmpty || folderEntry is ModSeparateSongFolder { AlwaysShow: true }))
                    {
                        CustomLevelsRepository.AddLevelPack(levelPack);
                    }
                }
            }

            lock (PendingSaveDataLock)
            {
                foreach (var (levelID, loadedSaveData) in LoadedBeatmapSaveData)
                    _customLevelLoader._loadedBeatmapSaveData[levelID] = loadedSaveData;
                LoadedBeatmapSaveData.Clear();
            }

            RefreshNativeLevelPacks();
        }

        private void RefreshNativeLevelPacks(bool allowCancelledRequest = false)
        {
            _beatmapLevelsModel.ClearLoadedBeatmapLevelsCaches();
            _beatmapLevelsModel._customLevelsRepository = CustomLevelsRepository;
            _beatmapLevelsModel.LoadAllBeatmapLevelPacks();

            if ((allowCancelledRequest || !_loadingTaskCancellationTokenSource.IsCancellationRequested) && _levelFilteringNavigationController.isActiveAndEnabled)
            {
                _levelFilteringNavigationController.UpdateCustomSongs();
            }

            OnLevelPacksRefreshed?.Invoke();
        }

        private async Task RefreshLevelPacksAsync(CancellationToken? requestToken = null)
        {
            await UnityGame.SwitchToMainThreadAsync();
            if (!IsCurrentLoader || requestToken?.IsCancellationRequested == true)
                return;
            Task refresh;
            _prepareLevelPacksOnWorkers = true;
            _levelPackRequestToken = requestToken;
            try
            {
                // Keep the public entry and its mod prefixes on the owner thread.
                RefreshLevelPacks();
                refresh = _levelPackRefreshTask;
            }
            finally
            {
                _prepareLevelPacksOnWorkers = false;
                _levelPackRequestToken = null;
            }
            await refresh;
        }

        private async Task PrepareLevelPacksAsync(long version, CancellationToken? requestToken)
        {
            while (IsCurrentLoader && version == _levelPackRefreshVersion &&
                   (!requestToken.HasValue || IsCurrentLoad(requestToken.Value)))
            {
                var repository = CustomLevelsRepository;
                var customPack = CustomLevelsPack;
                var wipPack = WIPLevelsPack;
                var cachedPack = CachedWIPLevelsPack;
                var custom = new LevelCatalogSnapshot(CustomLevels);
                var wip = new LevelCatalogSnapshot(CustomWIPLevels);
                var cached = new LevelCatalogSnapshot(CachedWIPLevels);
                var folders = SeparateSongFolders.Select(folder => new FolderCatalogSnapshot(folder)).ToArray();
                ConcurrentDictionary<string, CustomLevelLoader.LoadedSaveData> pendingSource;
                KeyValuePair<string, CustomLevelLoader.LoadedSaveData>[] pending;
                lock (PendingSaveDataLock)
                {
                    pendingSource = LoadedBeatmapSaveData;
                    pending = pendingSource.ToArray();
                }
                var nativeSaveDataSource = _customLevelLoader._loadedBeatmapSaveData;
                IEnumerator nativeSaveDataValidator = nativeSaveDataSource.GetEnumerator();
                var nativeSaveData = nativeSaveDataSource.ToArray();
                var updates = new List<SongCoreCustomBeatmapLevelPack.UpdateSnapshot>();
                if (customPack != null)
                    updates.Add(customPack.CaptureUpdate(custom.Levels));
                if (wipPack != null)
                    updates.Add(wipPack.CaptureUpdate(wip.Levels));
                if (cachedPack != null)
                    updates.Add(cachedPack.CaptureUpdate(cached.Levels));

                var desiredPacks = repository?.CaptureLevelPacks().ToList() ?? new List<BeatmapLevelPack>();
                if (cachedPack != null && repository != null)
                {
                    if (cached.Levels.Length > 0)
                    {
                        if (!desiredPacks.Contains(cachedPack))
                            desiredPacks.Add(cachedPack);
                    }
                    else
                    {
                        desiredPacks.Remove(cachedPack);
                    }
                }
                foreach (var folder in folders)
                {
                    if (folder.Pack != FolderLevelPack.NewPack)
                        continue;
                    var pack = folder.LevelPack ?? throw new InvalidOperationException("Separate song folder has no level pack.");
                    updates.Add(pack.CaptureUpdate(folder.Levels.Levels));
                    if (repository != null && (folder.Levels.Levels.Length > 0 || folder.AlwaysShow) && !desiredPacks.Contains(pack))
                        desiredPacks.Add(pack);
                }
                var repositorySnapshot = repository?.CaptureRefresh(desiredPacks, updates);
                var prepared = await Task.Run(() =>
                {
                    var preparedUpdates = updates.Select(update => update.Prepare()).ToArray();
                    var saveData = new Dictionary<string, CustomLevelLoader.LoadedSaveData>(Math.Max(nativeSaveData.Length, pending.Length));
                    foreach (var pair in nativeSaveData)
                        saveData.Add(pair.Key, pair.Value);
                    foreach (var pair in pending)
                        saveData[pair.Key] = pair.Value;
                    return (updates: preparedUpdates, repository: repositorySnapshot?.Prepare(preparedUpdates), saveData,
                        pending: new ConcurrentDictionary<string, CustomLevelLoader.LoadedSaveData>());
                });
                await UnityGame.SwitchToMainThreadAsync();
                if (!IsCurrentLoader || version != _levelPackRefreshVersion ||
                    (requestToken.HasValue && !IsCurrentLoad(requestToken.Value)))
                    return;
                if (!ReferenceEquals(repository, CustomLevelsRepository) || !ReferenceEquals(customPack, CustomLevelsPack) ||
                    !ReferenceEquals(wipPack, WIPLevelsPack) || !ReferenceEquals(cachedPack, CachedWIPLevelsPack) ||
                    !custom.IsCurrent(CustomLevels) || !wip.IsCurrent(CustomWIPLevels) || !cached.IsCurrent(CachedWIPLevels) ||
                    folders.Length != SeparateSongFolders.Count || !ReferenceEquals(nativeSaveDataSource, _customLevelLoader._loadedBeatmapSaveData))
                    continue;
                try
                {
                    nativeSaveDataValidator.Reset();
                }
                catch (InvalidOperationException)
                {
                    continue;
                }
                var current = repositorySnapshot?.IsCurrent() ?? true;
                for (var i = 0; current && i < folders.Length; i++)
                    current = ReferenceEquals(folders[i].Folder, SeparateSongFolders[i]) && folders[i].IsCurrent();
                if (!current || updates.Any(update => !update.IsCurrent()))
                    continue;

                lock (PendingSaveDataLock)
                {
                    if (!ReferenceEquals(pendingSource, LoadedBeatmapSaveData) || pending.Length != pendingSource.Count ||
                        pending.Any(pair => !pendingSource.TryGetValue(pair.Key, out var value) || !SameSaveData(pair.Value, value)))
                        continue;
                    foreach (var update in prepared.updates)
                        update.Pack.Publish(update);
                    if (prepared.repository != null)
                        repository!.Publish(prepared.repository);
                    var loader = _customLevelLoader;
                    Accessors.LoadedSaveDataAccessor(ref loader) = prepared.saveData;
                    LoadedBeatmapSaveData = prepared.pending;
                }
                RefreshNativeLevelPacks(!requestToken.HasValue);
                return;
            }
        }

        private static bool SameSaveData(CustomLevelLoader.LoadedSaveData left, CustomLevelLoader.LoadedSaveData right)
        {
            return ReferenceEquals(left.standardLevelInfoSaveData, right.standardLevelInfoSaveData) &&
                   ReferenceEquals(left.beatmapLevelSaveData, right.beatmapLevelSaveData) &&
                   ReferenceEquals(left.customLevelFolderInfo.folderPath, right.customLevelFolderInfo.folderPath) &&
                   ReferenceEquals(left.customLevelFolderInfo.levelName, right.customLevelFolderInfo.levelName) &&
                   ReferenceEquals(left.customLevelFolderInfo.levelInfoJsonString, right.customLevelFolderInfo.levelInfoJsonString);
        }

        private sealed class LevelCatalogSnapshot
        {
            private readonly ConcurrentDictionary<string, BeatmapLevel> _source;
            private readonly KeyValuePair<string, BeatmapLevel>[] _entries;
            internal readonly BeatmapLevel[] Levels;

            internal LevelCatalogSnapshot(ConcurrentDictionary<string, BeatmapLevel> source)
            {
                _source = source;
                _entries = source.ToArray();
                Levels = _entries.Select(pair => pair.Value).ToArray();
            }

            internal bool IsCurrent(ConcurrentDictionary<string, BeatmapLevel> source)
            {
                return ReferenceEquals(_source, source) && _entries.Length == source.Count &&
                       _entries.All(pair => source.TryGetValue(pair.Key, out var level) && ReferenceEquals(pair.Value, level));
            }
        }

        private sealed class FolderCatalogSnapshot
        {
            internal readonly SeparateSongFolder Folder;
            internal readonly FolderLevelPack Pack;
            internal readonly SongCoreCustomBeatmapLevelPack? LevelPack;
            internal readonly LevelCatalogSnapshot Levels;
            internal readonly bool AlwaysShow;
            private readonly SongFolderEntry _entry;
            private readonly string _name, _path, _imagePath;
            private readonly bool _wip, _cacheZips;

            internal FolderCatalogSnapshot(SeparateSongFolder folder)
            {
                Folder = folder;
                _entry = folder.SongFolderEntry;
                Pack = _entry.Pack;
                LevelPack = folder.LevelPack;
                Levels = new LevelCatalogSnapshot(folder.Levels);
                AlwaysShow = folder is ModSeparateSongFolder { AlwaysShow: true };
                _name = _entry.Name;
                _path = _entry.Path;
                _imagePath = _entry.ImagePath;
                _wip = _entry.WIP;
                _cacheZips = _entry.CacheZIPs;
            }

            internal bool IsCurrent()
            {
                return ReferenceEquals(_entry, Folder.SongFolderEntry) && Pack == _entry.Pack &&
                       ReferenceEquals(LevelPack, Folder.LevelPack) && Levels.IsCurrent(Folder.Levels) &&
                       AlwaysShow == (Folder is ModSeparateSongFolder { AlwaysShow: true }) &&
                       _name == _entry.Name && _path == _entry.Path && _imagePath == _entry.ImagePath &&
                       _wip == _entry.WIP && _cacheZips == _entry.CacheZIPs;
            }
        }

        public void RefreshSongs(bool fullRefresh = true)
        {
            if (!UnityGame.OnMainThread)
            {
                _ = UnityMainThreadTaskScheduler.Factory.StartNew(() => RefreshSongs(fullRefresh));
                return;
            }
            if (!IsCurrentLoader || AreSongsLoading || SceneManager.GetActiveScene().name == SceneNames.kGameCoreSceneName)
            {
                return;
            }

            Plugin.Log.Info(fullRefresh ? "Starting full song refresh" : "Starting song refresh");
            AreSongsLoaded = false;
            AreSongsLoading = true;
            LoadingProgress = 0;
            _loadingTaskCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _loadingTaskCancellationTokenSource.Token;
            var loadingProgress = new SongLoadProgress();
            _loadingProgress = loadingProgress;
            if (LoadingStartedEvent != null)
            {
                try
                {
                    LoadingStartedEvent(this);
                }
                catch (Exception e)
                {
                    Plugin.Log.Error("Some plugin is throwing exception from the LoadingStartedEvent!");
                    Plugin.Log.Error(e);
                }
            }

            if (!IsCurrentLoad(cancellationToken))
                return;
            _loadingTask = QueueCatalogOperation(() => RetrieveAllSongs(fullRefresh, cancellationToken, loadingProgress));
            _ = ObserveRefreshAsync(_loadingTask, cancellationToken);
        }

        private async Task ObserveRefreshAsync(Task task, CancellationToken cancellationToken)
        {
            try
            {
                await task;
            }
            catch (OperationCanceledException)
            {
                Plugin.Log.Warn("Song loading task cancelled.");
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn($"Song loading task failed. {ex.Message}");
            }
            finally
            {
                await UnityGame.SwitchToMainThreadAsync();
                if (OwnsLoadRequest(cancellationToken))
                {
                    _loadingTask = null;
                    _loadingProgress = null;
                    AreSongsLoading = false;
                }
            }
        }

        private async Task RetrieveAllSongs(bool fullRefresh, CancellationToken cancellationToken, SongLoadProgress loadingProgress)
        {
            if (!IsCurrentLoad(cancellationToken))
                return;
            await Task.WhenAll(Plugin.FolderInitializationTask, Collections.LoadCachedSongDataAsync());
            await UnityGame.SwitchToMainThreadAsync();
            if (!IsCurrentLoad(cancellationToken))
                return;

            var stopwatch = new Stopwatch();
            var separateSongFolders = SeparateSongFolders.ToArray();
            var folderEntries = new Dictionary<SeparateSongFolder, SongFolderEntry>();
            foreach (var folder in separateSongFolders)
            {
                CaptureFolder(folder);
                if (folder.CacheFolder != null)
                    CaptureFolder(folder.CacheFolder);
            }
            void CaptureFolder(SeparateSongFolder folder)
            {
                var entry = folder.SongFolderEntry;
                folderEntries[folder] = new SongFolderEntry(entry.Name, entry.Path, entry.Pack, entry.ImagePath, entry.WIP, entry.CacheZIPs);
            }
            var loadedSongData = _customLevelLoader._loadedBeatmapSaveData.ToArray();
            KeyValuePair<string, CustomLevelLoader.LoadedSaveData>[] pendingSongData;
            lock (PendingSaveDataLock)
                pendingSongData = LoadedBeatmapSaveData.ToArray();

            #region ClearAllDictionaries

            // Clear all beatmap dictionaries on full refresh
            if (fullRefresh)
            {
                lock (PendingSaveDataLock)
                    LoadedBeatmapSaveData.Clear();
                CustomLevels.Clear();
                CustomWIPLevels.Clear();
                CachedWIPLevels.Clear();
                lock (HashLevelsLock)
                {
                    Collections.LevelHashDictionary.Clear();
                    Collections.HashLevelDictionary.Clear();
                }
                foreach (var folder in separateSongFolders)
                {
                    folder.Levels.Clear();
                }
            }

            #endregion

            var cachedSongPaths = fullRefresh ? null : Hashing.cachedSongHashData.Keys;
            var installPath = UnityGame.InstallPath;
            ConcurrentDictionary<string, bool> foundSongPaths = null!;

            var job = async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                foundSongPaths = fullRefresh
                    ? new ConcurrentDictionary<string, bool>()
                    : new ConcurrentDictionary<string, bool>(cachedSongPaths!.ToDictionary(path => Hashing.GetAbsolutePath(path, installPath), _ => false));
                #region AddCustomBeatmaps

                try
                {
                    #region DirectorySetup

                    if (!Directory.Exists(_customLevelsPath))
                    {
                        Directory.CreateDirectory(_customLevelsPath);
                    }

                    if (!Directory.Exists(_customWIPPath))
                    {
                        Directory.CreateDirectory(_customWIPPath);
                    }

                    #endregion

                    #region CacheZipWIPs

                    // Get zip files in CustomWIPLevels and extract them to Cache folder
                    if (fullRefresh)
                    {
                        try
                        {
                            var cachePath = Path.Combine(_customWIPPath, "Cache");
                            CacheZIPs(cachePath, _customWIPPath, cancellationToken);

                            var cacheFolders = Directory.EnumerateDirectories(cachePath);
                            LoadCachedZIPs(cacheFolders, fullRefresh, CachedWIPLevels, cancellationToken);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Plugin.Log.Error("Failed to load cached WIP levels: ");
                            Plugin.Log.Error(ex);
                        }
                    }

                    #endregion

                    #region CacheSeparateZIPs

                    if (fullRefresh)
                    {
                        foreach (var songFolder in separateSongFolders)
                        {
                            var songFolderEntry = folderEntries[songFolder];
                            if (songFolderEntry.CacheZIPs && songFolder.CacheFolder != null)
                            {
                                var cacheFolder = songFolder.CacheFolder;
                                try
                                {
                                    CacheZIPs(folderEntries[cacheFolder].Path, songFolderEntry.Path, cancellationToken);
                                }
                                catch (Exception ex) when (ex is not OperationCanceledException)
                                {
                                    Plugin.Log.Error("Failed to load cached WIP levels:");
                                    Plugin.Log.Error(ex);
                                }
                            }
                        }
                    }

                    #endregion

                    stopwatch.Start();

                    #region LoadCustomLevels

                    // Get Levels from CustomLevels and CustomWIPLevels folders
                    var songFolders = new DirectoryInfo(_customLevelsPath).EnumerateDirectories()
                        .Concat(new DirectoryInfo(_customWIPPath).EnumerateDirectories())
                        .Where(d => d.Exists && !CustomLevelPathHelper.IsHiddenDirectory(d) &&
                            !string.Equals(d.FullName, Path.Combine(_customWIPPath, "Cache"), StringComparison.OrdinalIgnoreCase))
                        .Select(d => d.FullName)
                        .ToArray();
                    var songFoldersCount = songFolders.Length;
                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2 - 1),
                        CancellationToken = cancellationToken
                    };
                    var processedSongsCount = 0;

                    // Clear removed songs from loaded data, in case they were removed manually.
                    if (loadedSongData.Length > 0 || pendingSongData.Length > 0)
                    {
                        var folders = songFolders
                            .Concat(separateSongFolders
                                .Select(f => Path.GetFullPath(folderEntries[f].Path))
                                .Select(p => new DirectoryInfo(p))
                                .Where(d => d.Exists)
                                .SelectMany(d => d.EnumerateDirectories()
                                    .Where(d => d.Exists && !CustomLevelPathHelper.IsHiddenDirectory(d))
                                    .Select(d => d.FullName)))
                            .Concat(CachedWIPLevels.Keys)
                            .ToHashSet();

                        var missing = loadedSongData.Concat(pendingSongData)
                            .Where(pair => !folders.Contains(pair.Value.customLevelFolderInfo.folderPath))
                            .GroupBy(pair => pair.Value.customLevelFolderInfo.folderPath)
                            .Select(group => (path: group.Key, levelIDs: group.Select(pair => pair.Key).ToArray())).ToArray();
                        await UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                        {
                            if (!IsCurrentLoad(cancellationToken))
                                return;
                            foreach (var (path, levelIDs) in missing)
                            {
                                Plugin.Log.Warn($"Removing {path} from loaded levels");
                                RemoveSongFromCollections(path, levelIDs);
                            }
                            if (!fullRefresh)
                                StoreLoadedBeatmapSaveData();
                        });
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    Parallel.ForEach(songFolders, parallelOptions, folder =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string[] results;
                        try
                        {
                            results = Directory.GetFiles(folder, CustomLevelPathHelper.kStandardLevelInfoFilename);
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Warn($"Skipping missing or corrupt folder: '{folder}'");
                            Plugin.Log.Warn(ex);
                            return;
                        }

                        if (results.Length == 0)
                        {
                            Plugin.Log.Warn($"Folder: '{folder}' is missing {CustomLevelPathHelper.kStandardLevelInfoFilename} file!");
                            return;
                        }

                        foreach (var result in results)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            try
                            {
                                var songPath = Path.GetDirectoryName(result)!;
                                if (Directory.GetParent(songPath)?.Name == "Backups")
                                {
                                    continue;
                                }

                                if (!fullRefresh && (CustomLevels.ContainsKey(songPath) || CustomWIPLevels.ContainsKey(songPath)))
                                {
                                    continue;
                                }

                                var wip = songPath.Contains("CustomWIPLevels");
                                var customLevel = LoadCustomLevelInternal(songPath);
                                if (!customLevel.HasValue)
                                {
                                    Plugin.Log.Error($"Failed to load custom level: {folder}");
                                    continue;
                                }

                                var (_, level) = customLevel.Value;
                                if (!wip)
                                {
                                    CustomLevels[songPath] = level;
                                }
                                else
                                {
                                    CustomWIPLevels[songPath] = level;
                                }

                                foundSongPaths.TryAdd(songPath, false);
                            }
                            catch (Exception e)
                            {
                                Plugin.Log.Error($"Failed to load song folder: {result}");
                                Plugin.Log.Error(e);
                            }
                        }

                        var progress = (float)Interlocked.Increment(ref processedSongsCount) / songFoldersCount;
                        if (!cancellationToken.IsCancellationRequested)
                            Volatile.Write(ref loadingProgress.Value, progress);
                    });

                    #endregion

                    #region LoadSeparateFolders

                    // Load beatmaps in separate song folders (created in folders.xml or by other mods)
                    // Assign beatmaps to their respective pack (custom levels, wip levels, or separate)
                    await UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                    {
                        if (IsCurrentLoad(cancellationToken))
                            _progressBar.ShowMessage($"Loading {separateSongFolders.Length} separate folders", true);
                    });
                    foreach (var entry in separateSongFolders)
                    {
                        var folderEntry = folderEntries[entry];
                        cancellationToken.ThrowIfCancellationRequested();
                        try
                        {
                            if (!Directory.Exists(folderEntry.Path))
                            {
                                continue;
                            }

                            var entryFolders = Directory.GetDirectories(folderEntry.Path);

                            float count = 0;
                            foreach (var folder in entryFolders)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                                count++;
                                // Search for an info.dat in the beatmap folder
                                string[] results;
                                try
                                {
                                    results = Directory.GetFiles(folder, CustomLevelPathHelper.kStandardLevelInfoFilename);
                                }
                                catch (Exception ex)
                                {
                                    Plugin.Log.Warn($"Skipping missing or corrupt folder: '{folder}'");
                                    Plugin.Log.Warn(ex);
                                    continue;
                                }

                                if (results.Length == 0)
                                {
                                    Plugin.Log.Warn($"Folder: '{folder}' is missing {CustomLevelPathHelper.kStandardLevelInfoFilename} file!");
                                    continue;
                                }

                                foreach (var result in results)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    try
                                    {
                                        // On quick refresh: Check if the beatmap directory is already present in the respective beatmap dictionary
                                        // If it is already present on a non full refresh, it will be ignored (changes to the beatmap will not be applied)
                                        var songPath = Path.GetDirectoryName(result)!;
                                        if (!fullRefresh)
                                        {
                                            switch (folderEntry.Pack)
                                            {
                                                case FolderLevelPack.NewPack when SearchBeatmapInMapPack(entry.Levels, songPath):
                                                case FolderLevelPack.CustomLevels when SearchBeatmapInMapPack(CustomLevels, songPath):
                                                case FolderLevelPack.CustomWIPLevels when SearchBeatmapInMapPack(CustomWIPLevels, songPath):
                                                case FolderLevelPack.CachedWIPLevels when SearchBeatmapInMapPack(CachedWIPLevels, songPath):
                                                    continue;
                                            }
                                        }

                                        if (folderEntry.Pack == FolderLevelPack.CustomLevels || folderEntry is { Pack: FolderLevelPack.NewPack, WIP: false })
                                        {
                                            if (AssignBeatmapToSeparateFolder(CustomLevels, songPath, entry.Levels))
                                            {
                                                continue;
                                            }

                                            if (AssignBeatmapToSeparateFolder(CustomWIPLevels, songPath, entry.Levels))
                                            {
                                                continue;
                                            }

                                            if (AssignBeatmapToSeparateFolder(CachedWIPLevels, songPath, entry.Levels))
                                            {
                                                continue;
                                            }
                                        }

                                        var customLevel = LoadCustomLevelInternal(songPath, folderEntry);
                                        if (!customLevel.HasValue)
                                        {
                                            Plugin.Log.Error($"Failed to load custom level: {folder}");
                                        }
                                        else
                                        {
                                            var (_, level) = customLevel.Value;
                                            entry.Levels[songPath] = level;
                                            foundSongPaths.TryAdd(songPath, false);
                                        }

                                        if (!cancellationToken.IsCancellationRequested)
                                            Volatile.Write(ref loadingProgress.Value, count / entryFolders.Length);
                                    }
                                    catch (Exception e)
                                    {
                                        Plugin.Log.Error($"Failed to load song folder: {result}");
                                        Plugin.Log.Error(e);
                                    }
                                }
                            }
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        {
                            Plugin.Log.Error($"Failed to load separate folder {folderEntry.Name}");
                            Plugin.Log.Error(ex);
                        }
                    }

                    #endregion

                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    Plugin.Log.Error("RetrieveAllSongs failed:");
                    Plugin.Log.Error(e);
                }

                #endregion
            };

            await Task.Run(job, cancellationToken);
            await UnityGame.SwitchToMainThreadAsync();
            if (!IsCurrentLoad(cancellationToken))
                return;

            var prepared = await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                stopwatch.Stop();
                var songCountWSF = CustomLevels.Count + CustomWIPLevels.Count;
                var songCount = songCountWSF + separateSongFolders.Sum(folder => folder.Levels.Count);
                var folderCount = songCount - songCountWSF;
                var songOrSongs = songCount == 1 ? "song" : "songs";
                var folderOrFolders = folderCount == 1 ? "folder" : "folders";
                Plugin.Log.Info($"Loaded {songCount} new {songOrSongs} ({songCountWSF}) in CustomLevels | {folderCount} in separate {folderOrFolders}) in {stopwatch.Elapsed.TotalSeconds} seconds");
                foreach (var folderEntry in separateSongFolders)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ConcurrentDictionary<string, BeatmapLevel>? target = folderEntries[folderEntry].Pack switch
                    {
                        FolderLevelPack.CustomLevels => CustomLevels,
                        FolderLevelPack.CustomWIPLevels => CustomWIPLevels,
                        FolderLevelPack.CachedWIPLevels => CachedWIPLevels,
                        _ => null
                    };
                    if (target == null)
                        continue;
                    foreach (var (path, level) in folderEntry.Levels)
                        target.TryAdd(path, level);
                }
                return (custom: CustomLevels.Values.ToArray(), wip: CustomWIPLevels.Values.ToArray(), cached: CachedWIPLevels.Values.ToArray());
            }, cancellationToken);
            await UnityGame.SwitchToMainThreadAsync();
            if (!IsCurrentLoad(cancellationToken))
                return;

            try
            {
                CustomLevelsRepository ??= SongCoreBeatmapLevelsRepository.CreateNew();
                if (CustomLevelsRepository.CaptureLevelPacks().Length == 0)
                {
                    CustomLevelsPack = new SongCoreCustomBeatmapLevelPack(CustomLevelLoader.kCustomLevelPackPrefixId + CustomLevelPathHelper.kCustomLevelsDirectoryName, "Custom Levels", defaultCoverImage, prepared.custom);
                    WIPLevelsPack = new SongCoreCustomBeatmapLevelPack(CustomLevelLoader.kCustomLevelPackPrefixId + "CustomWIPLevels", "WIP Levels", UI.BasicUI.WIPIcon, prepared.wip);
                    CachedWIPLevelsPack = new SongCoreCustomBeatmapLevelPack(CustomLevelLoader.kCustomLevelPackPrefixId + "CachedWIPLevels", "Cached WIP Levels", UI.BasicUI.WIPIcon, prepared.cached);
                    CustomLevelsRepository.StageLevelPacks([CustomLevelsPack, WIPLevelsPack, CachedWIPLevelsPack]);
                }
                await RefreshLevelPacksAsync(cancellationToken);
                await UnityGame.SwitchToMainThreadAsync();
            }
            catch (Exception ex)
            {
                Plugin.Log.Error("Failed to setup LevelPacks:");
                Plugin.Log.Error(ex);
                return;
            }
            if (!IsCurrentLoad(cancellationToken))
                return;

            await Task.Run(ReportDuplicateSongs, cancellationToken);
            await UnityGame.SwitchToMainThreadAsync();
            if (!IsCurrentLoad(cancellationToken))
                return;
            AreSongsLoaded = true;
            AreSongsLoading = false;
            LoadingProgress = 1;
            SongsLoadedEvent?.Invoke(this, CustomLevels);
            if (!IsCurrentLoad(cancellationToken))
                return;
            var currentSongPaths = await Task.Run(() => foundSongPaths.Keys.ToHashSet(), cancellationToken);
            await Task.WhenAll(Hashing.SaveCachedSongHashesAsync(currentSongPaths), Hashing.SaveCachedAudioDataAsync(currentSongPaths), Collections.SaveCachedSongDataAsync());
        }

        internal void StoreLoadedBeatmapSaveData()
        {
            if (!UnityGame.OnMainThread)
                throw new InvalidOperationException("Loaded song data must be captured on the main thread.");
            if (!IsCurrentLoader)
                return;
            lock (PendingSaveDataLock)
                LoadedBeatmapSaveData = new ConcurrentDictionary<string, CustomLevelLoader.LoadedSaveData>(_customLevelLoader._loadedBeatmapSaveData);
        }

        /// <summary>
        /// Delete a beatmap (is only used by other mods)
        /// </summary>
        /// <param name="folderPath">Directory of the beatmap</param>
        /// <param name="deleteFolder">Option to delete the base folder of the beatmap</param>
        public void DeleteSong(string folderPath, bool deleteFolder = true)
        {
            ValidateSongDeletion(folderPath);
            var removed = UnityGame.OnMainThread
                ? RemoveSongFromCollections(folderPath)
                : UnityMainThreadTaskScheduler.Factory.StartNew(() => RemoveSongFromCollections(folderPath)).GetAwaiter().GetResult();
            if (removed && deleteFolder)
                DeleteSongDirectory(folderPath);
            if (UnityGame.OnMainThread)
            {
                if (IsCurrentLoader)
                    RefreshLevelPacks();
            }
            else
            {
                UnityMainThreadTaskScheduler.Factory.StartNew(() =>
                {
                    if (IsCurrentLoader)
                        RefreshLevelPacks();
                }).GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Delete multiple beatmaps in bulk (is only used by other mods)
        /// </summary>
        /// <param name="folderPaths">Directories of the beatmaps</param>
        /// <param name="deleteFolder">Option to delete the base folder of the beatmap</param>
        public async Task DeleteSongsAsync(List<string> folderPaths, bool deleteFolder = true)
        {
            var requestedPaths = folderPaths.ToArray();
            await UnityGame.SwitchToMainThreadAsync();
            await QueueCatalogOperation(async () =>
            {
                if (!IsCurrentLoader)
                    return;
                var savedData = _customLevelLoader._loadedBeatmapSaveData.ToArray();
                KeyValuePair<string, CustomLevelLoader.LoadedSaveData>[] pendingData;
                lock (PendingSaveDataLock)
                    pendingData = LoadedBeatmapSaveData.ToArray();
                var savedIDs = await Task.Run(() =>
                {
                    var paths = new HashSet<string>(requestedPaths);
                    return savedData.Concat(pendingData)
                        .Where(pair => paths.Contains(pair.Value.customLevelFolderInfo.folderPath))
                        .GroupBy(pair => pair.Value.customLevelFolderInfo.folderPath)
                        .ToDictionary(group => group.Key, group => group.Select(pair => pair.Key).ToArray());
                });
                await UnityGame.SwitchToMainThreadAsync();
                if (!IsCurrentLoader)
                    return;
                foreach (var folderPath in requestedPaths)
                {
                    ValidateSongDeletion(folderPath);
                    var levelIDs = savedIDs.TryGetValue(folderPath, out var saved) ? saved : Array.Empty<string>();
                    if (RemoveSongFromCollections(folderPath, levelIDs) && deleteFolder)
                        await Task.Run(() => DeleteSongDirectory(folderPath));
                    await UnityGame.SwitchToMainThreadAsync();
                    if (!IsCurrentLoader)
                        return;
                }
                await RefreshLevelPacksAsync();
            });
        }

        private static void ValidateSongDeletion(string folderPath)
        {
            if (folderPath.EndsWith("(Built in)", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Cannot delete built-in levels.");
            }
        }

        private bool RemoveSongFromCollections(string folderPath, IReadOnlyList<string>? savedIDs = null)
        {
            ValidateSongDeletion(folderPath);
            if (!IsCurrentLoader)
                return false;
            try
            {
                var levelIDs = new HashSet<string>();
                if (CustomLevels.TryRemove(folderPath, out var level))
                    levelIDs.Add(level.levelID);
                if (CustomWIPLevels.TryRemove(folderPath, out level))
                    levelIDs.Add(level.levelID);
                if (CachedWIPLevels.TryRemove(folderPath, out level))
                    levelIDs.Add(level.levelID);
                foreach (var folderEntry in SeparateSongFolders)
                {
                    if (folderEntry.Levels.TryRemove(folderPath, out level))
                        levelIDs.Add(level.levelID);
                }
                if (savedIDs == null)
                {
                    foreach (var (levelID, data) in _customLevelLoader._loadedBeatmapSaveData)
                        if (data.customLevelFolderInfo.folderPath == folderPath)
                            levelIDs.Add(levelID);
                    foreach (var (levelID, data) in LoadedBeatmapSaveData)
                        if (data.customLevelFolderInfo.folderPath == folderPath)
                            levelIDs.Add(levelID);
                }
                else
                {
                    foreach (var levelID in savedIDs)
                    {
                        if ((_customLevelLoader._loadedBeatmapSaveData.TryGetValue(levelID, out var data) && data.customLevelFolderInfo.folderPath == folderPath) ||
                            (LoadedBeatmapSaveData.TryGetValue(levelID, out data) && data.customLevelFolderInfo.folderPath == folderPath))
                            levelIDs.Add(levelID);
                    }
                }

                foreach (var levelID in levelIDs)
                {
                    lock (HashLevelsLock)
                    {
                        if (Collections.LevelHashDictionary.TryRemove(levelID, out var hash) &&
                            Collections.HashLevelDictionary.TryGetValue(hash, out var levels))
                        {
                            lock (levels)
                            {
                                levels.RemoveAll(id => id == levelID);
                                if (levels.Count == 0)
                                    Collections.HashLevelDictionary.TryRemove(hash, out _);
                            }
                        }
                    }
                    Collections.CustomSongsData.TryRemove(levelID, out _);
                    lock (PendingSaveDataLock)
                        LoadedBeatmapSaveData.TryRemove(levelID, out _);
                    _customLevelLoader._loadedBeatmapSaveData.Remove(levelID);
                }
                return true;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Exception trying to delete song: {folderPath}");
                Plugin.Log.Error(ex);
                return false;
            }
        }

        private static void DeleteSongDirectory(string folderPath)
        {
            try
            {
                if (Directory.Exists(folderPath))
                    Directory.Delete(folderPath, true);
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Exception trying to delete song: {folderPath}");
                Plugin.Log.Error(ex);
            }
        }

        /// <summary>
        /// Load a beatmap, gather all beatmap information and create beatmap preview
        /// </summary>
        /// <param name="loadedSaveData">Loaded save data of beatmap</param>
        /// <param name="hash">Resulting hash for the beatmap, may contain beatmap folder name or 'WIP' at the end</param>
        /// <param name="folderEntry">Folder entry for beatmap folder</param>
        /// <returns></returns>
        private (string hash, BeatmapLevel beatmapLevel)? LoadSong(CustomLevelLoader.LoadedSaveData loadedSaveData, SongFolderEntry? folderEntry = null)
        {
            var wip = loadedSaveData.customLevelFolderInfo.folderPath.Contains("CustomWIPLevels") || folderEntry != null && (folderEntry.Pack == FolderLevelPack.CustomWIPLevels || folderEntry.Pack == FolderLevelPack.CachedWIPLevels || folderEntry.WIP);
            string hash;
            BeatmapLevel? beatmapLevel;
            try
            {
                if (loadedSaveData.standardLevelInfoSaveData != null)
                {
                    hash = Hashing.ComputeCustomLevelHash(loadedSaveData.customLevelFolderInfo, loadedSaveData.standardLevelInfoSaveData);
                    beatmapLevel = _customLevelLoader.CreateBeatmapLevelFromV3(loadedSaveData.customLevelFolderInfo, loadedSaveData.standardLevelInfoSaveData);
                }
                else if (loadedSaveData.beatmapLevelSaveData != null)
                {
                    hash = Hashing.ComputeCustomLevelHash(loadedSaveData.customLevelFolderInfo, loadedSaveData.beatmapLevelSaveData);
                    beatmapLevel = _customLevelLoader.CreateBeatmapLevelFromV4(loadedSaveData.customLevelFolderInfo, loadedSaveData.beatmapLevelSaveData);
                }
                else
                {
                    throw new InvalidOperationException("Level save data is missing.");
                }

                var levelID = CustomLevelLoader.kCustomLevelPrefixId + hash;
                var folderName = new DirectoryInfo(loadedSaveData.customLevelFolderInfo.folderPath).Name;
                lock (HashLevelsLock)
                {
                    while (!Collections.LevelHashDictionary.TryAdd(levelID + (wip ? " WIP" : ""), hash))
                        levelID += $"_{folderName}";

                    if (wip)
                        levelID += " WIP";

                    if (Collections.HashLevelDictionary.TryGetValue(hash, out var levels))
                    {
                        lock (levels)
                            levels.Add(levelID);
                    }
                    else
                    {
                        Collections.HashLevelDictionary.TryAdd(hash, new List<string> { levelID });
                    }
                }
                BlacklistLevelFiles(loadedSaveData);
                Collections.CreateCustomLevelSongData(levelID, loadedSaveData);
                lock (PendingSaveDataLock)
                    LoadedBeatmapSaveData.TryAdd(levelID, loadedSaveData);

                Accessors.LevelIDAccessor(ref beatmapLevel) = levelID;
                GetSongDuration(loadedSaveData, beatmapLevel);
            }
            catch (Exception e)
            {
                Plugin.Log.Error($"Failed to load song: {loadedSaveData.customLevelFolderInfo.folderPath}");
                Plugin.Log.Error(e);
                return null;
            }

            return (hash, beatmapLevel);
        }

        private static void BlacklistLevelFiles(CustomLevelLoader.LoadedSaveData loadedSaveData)
        {
            const string reason = "Use IBeatmapLevelData to read level difficulty and audio data. Info.dat data can be found in CustomLevelLoader._loadedBeatmapSaveData";

            if (!string.IsNullOrEmpty(loadedSaveData.beatmapLevelSaveData?.audio.audioDataFilename))
            {
                IOBlacklistHook.BlacklistedFiles.TryAdd(Path.GetFullPath(Path.Combine(loadedSaveData.customLevelFolderInfo.folderPath, loadedSaveData.beatmapLevelSaveData!.audio.audioDataFilename)), reason);
            }

            foreach (var difficultyBeatmap in loadedSaveData.standardLevelInfoSaveData?.difficultyBeatmapSets.SelectMany(x => x.difficultyBeatmaps) ?? [])
            {
                if (!string.IsNullOrEmpty(difficultyBeatmap.beatmapFilename))
                {
                    IOBlacklistHook.BlacklistedFiles.TryAdd(Path.GetFullPath(Path.Combine(loadedSaveData.customLevelFolderInfo.folderPath, difficultyBeatmap.beatmapFilename)), reason);
                }
            }

            foreach (var difficultyBeatmap in loadedSaveData.beatmapLevelSaveData?.difficultyBeatmaps ?? [])
            {
                if (!string.IsNullOrEmpty(difficultyBeatmap.beatmapDataFilename))
                {
                    IOBlacklistHook.BlacklistedFiles.TryAdd(Path.GetFullPath(Path.Combine(loadedSaveData.customLevelFolderInfo.folderPath, difficultyBeatmap.beatmapDataFilename)), reason);
                }

                if (!string.IsNullOrEmpty(difficultyBeatmap.lightshowDataFilename))
                {
                    IOBlacklistHook.BlacklistedFiles.TryAdd(Path.GetFullPath(Path.Combine(loadedSaveData.customLevelFolderInfo.folderPath, difficultyBeatmap.lightshowDataFilename)), reason);
                }
            }
        }

        #region HelperFunctionsZIP

        /// <summary>
        /// Extracts beatmap ZIP files to the cache folder
        /// </summary>
        /// <param name="cachePath">Directory of cache folder</param>
        /// <param name="songFolderPath">Directory of folder containing the zips</param>
        private static void CacheZIPs(string cachePath, string songFolderPath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!Directory.Exists(cachePath))
            {
                Directory.CreateDirectory(cachePath);
            }

            var cache = new DirectoryInfo(cachePath);
            foreach (var file in cache.EnumerateFiles())
            {
                cancellationToken.ThrowIfCancellationRequested();
                file.Delete();
            }

            foreach (var folder in cache.EnumerateDirectories())
            {
                cancellationToken.ThrowIfCancellationRequested();
                folder.Delete(true);
            }

            var zips = Directory.EnumerateFiles(songFolderPath, "*.zip");

            foreach (var zip in zips)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    using var fileStream = File.OpenRead(zip);
                    using var archive = new ZipArchive(fileStream);
                    archive.ExtractToDirectory(Path.Combine(cachePath, Path.GetFileName(zip)));
                }
                catch (Exception ex)
                {
                    Plugin.Log.Warn($"Failed to extract zip: {zip}:");
                    Plugin.Log.Warn(ex);
                }
            }
        }

        /// <summary>
        /// Loads the beatmaps of the cached
        /// </summary>
        /// <param name="cacheFolders">Directory of cache folder</param>
        /// <param name="fullRefresh"></param>
        /// <param name="beatmapDictionary"></param>
        /// <param name="folderEntry"></param>
        private void LoadCachedZIPs(IEnumerable<string> cacheFolders, bool fullRefresh, ConcurrentDictionary<string, BeatmapLevel> beatmapDictionary, CancellationToken cancellationToken, SongFolderEntry? folderEntry = null)
        {
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2 - 1),
                CancellationToken = cancellationToken
            };
            Parallel.ForEach(cacheFolders, options, cachedFolder =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string[] results;
                try
                {
                    results = Directory.GetFiles(cachedFolder, CustomLevelPathHelper.kStandardLevelInfoFilename);
                }
                catch (DirectoryNotFoundException)
                {
                    Plugin.Log.Warn($"Skipping missing or corrupt folder: '{cachedFolder}'");
                    return;
                }

                if (results.Length == 0)
                {
                    Plugin.Log.Warn($"Folder: '{cachedFolder}' is missing {CustomLevelPathHelper.kStandardLevelInfoFilename} files!");
                    return;
                }

                foreach (var result in results)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        var songPath = Path.GetDirectoryName(result)!;
                        if (!fullRefresh)
                        {
                            if (SearchBeatmapInMapPack(beatmapDictionary, songPath))
                            {
                                continue;
                            }
                        }

                        try
                        {
                            var customLevel = LoadCustomLevelInternal(songPath, folderEntry);
                            if (!customLevel.HasValue)
                            {
                                Plugin.Log.Error($"Failed to load custom level: {folderEntry}");
                                continue;
                            }

                            var (_, level) = customLevel.Value;
                            beatmapDictionary[songPath] = level;
                        }
                        catch (Exception ex)
                        {
                            Plugin.Log.Error($"Failed to load song from {cachedFolder}:");
                            Plugin.Log.Error(ex);
                        }
                    }
                    catch (Exception ex)
                    {
                        Plugin.Log.Error($"Failed to load song from {cachedFolder}:");
                        Plugin.Log.Error(ex);
                    }
                }
            });
        }

        #endregion

        #region HelperFunctionsLoading

        private bool SearchBeatmapInMapPack(ConcurrentDictionary<string, BeatmapLevel> mapPack, string songPath)
        {
            if (mapPack.TryGetValue(songPath, out var beatmapLevel) && beatmapLevel != null)
            {
                return true;
            }

            return false;
        }

        private bool AssignBeatmapToSeparateFolder(
            ConcurrentDictionary<string, BeatmapLevel> mapPack,
            string songPath,
            ConcurrentDictionary<string, BeatmapLevel> separateFolder)
        {
            if (mapPack.TryGetValue(songPath, out var beatmapLevel) && beatmapLevel != null)
            {
                separateFolder[songPath] = beatmapLevel;
                return true;
            }

            return false;
        }

        public static (string hash, BeatmapLevel beatmapLevel)? LoadCustomLevel(string customLevelPath, SongFolderEntry? entry = null)
        {
            return (Instance ?? throw new InvalidOperationException("SongCore has not been initialized.")).LoadCustomLevelInternal(customLevelPath, entry);
        }

        private (string hash, BeatmapLevel beatmapLevel)? LoadCustomLevelInternal(string customLevelPath, SongFolderEntry? entry = null)
        {
            var infoFilePath = Path.Combine(customLevelPath, CustomLevelPathHelper.kStandardLevelInfoFilename);
            if (!File.Exists(infoFilePath))
            {
                return null;
            }

            CustomLevelLoader.LoadedSaveData loadedSaveData;
            var directoryInfo = new DirectoryInfo(customLevelPath);
            string json;

            IOBlacklistHook.AllowIO.Value = true;
            try
            {
                json = File.ReadAllText(infoFilePath);
            }
            finally
            {
                IOBlacklistHook.AllowIO.Value = false;
            }

            var version = BeatmapSaveDataHelpers.GetVersion(json);
            if (version < BeatmapSaveDataHelpers.version4)
            {
                var standardLevelInfoSaveData = StandardLevelInfoSaveData.DeserializeFromJSONString(json);
                if (standardLevelInfoSaveData == null || standardLevelInfoSaveData.difficultyBeatmapSets.Length == 0)
                {
                    return null;
                }

                var customLevelFolderInfo = new CustomLevelFolderInfo(directoryInfo.FullName, directoryInfo.Name, json);
                if (!CustomLevelLoader.CheckIfAudioExists(customLevelFolderInfo, standardLevelInfoSaveData.songFilename))
                {
                    return null;
                }

                loadedSaveData = new CustomLevelLoader.LoadedSaveData { customLevelFolderInfo = customLevelFolderInfo, standardLevelInfoSaveData = standardLevelInfoSaveData };
            }
            else
            {
                var beatmapLevelSaveData = JsonConvert.DeserializeObject<BeatmapLevelSaveData>(json, JsonSettings.readableWithDefault);
                if (beatmapLevelSaveData == null || beatmapLevelSaveData.difficultyBeatmaps.Length == 0)
                {
                    return null;
                }

                var customLevelFolderInfo = new CustomLevelFolderInfo(directoryInfo.FullName, directoryInfo.Name, json);
                if (!CustomLevelLoader.CheckIfAudioExists(customLevelFolderInfo, beatmapLevelSaveData.audio.songFilename))
                {
                    return null;
                }

                BeatmapLevelSaveDataUtils.MigrateBeatmapLevelSaveData(beatmapLevelSaveData);
                loadedSaveData = new CustomLevelLoader.LoadedSaveData { customLevelFolderInfo = customLevelFolderInfo, beatmapLevelSaveData = beatmapLevelSaveData };
            }

            return LoadSong(loadedSaveData, entry);
        }

        #endregion

        #region HelperFunctionsSearching

        /// <summary>
        /// Attempts to get a beatmap by LevelId. Returns null a matching level isn't found.
        /// </summary>
        /// <param name="levelId"></param>
        /// <returns></returns>
        public static BeatmapLevel? GetLevelById(string levelId)
        {
            if (string.IsNullOrEmpty(levelId))
            {
                return null;
            }

            return BeatmapLevelsModelSO.GetBeatmapLevel(levelId);
        }

        /// <summary>
        /// Attempts to get a custom level by hash (case-insensitive). Returns null a matching custom level isn't found.
        /// </summary>
        /// <param name="hash"></param>
        /// <returns></returns>
        public static BeatmapLevel? GetLevelByHash(string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return null;
            }

            return BeatmapLevelsModelSO.GetBeatmapLevel(CustomLevelLoader.kCustomLevelPrefixId + hash, true);
        }

        private void GetSongDuration(CustomLevelLoader.LoadedSaveData loadedSaveData, BeatmapLevel beatmapLevel)
        {
            try
            {
                var levelID = beatmapLevel.levelID;
                float length = 0;
                Hashing.TryGetRelativePath(loadedSaveData.customLevelFolderInfo.folderPath, out var relativePath);
                if (Hashing.cachedAudioData.TryGetValue(relativePath, out var data))
                {
                    if (data.id == levelID)
                    {
                        length = data.duration;
                    }
                }

                if (length == 0)
                {
                    IOBlacklistHook.AllowIO.Value = true;
                    try
                    {
                        if (loadedSaveData.standardLevelInfoSaveData != null)
                        {
                            length = GetLengthFromAudio(Path.Combine(loadedSaveData.customLevelFolderInfo.folderPath, loadedSaveData.standardLevelInfoSaveData.songFilename));
                        }
                        else if (loadedSaveData.beatmapLevelSaveData != null)
                        {
                            length = GetLengthFromAudio(Path.Combine(loadedSaveData.customLevelFolderInfo.folderPath, loadedSaveData.beatmapLevelSaveData.audio.songFilename));
                        }
                    }
                    catch (Exception)
                    {
                        length = -1;
                    }
                    finally
                    {
                        IOBlacklistHook.AllowIO.Value = false;
                    }

                    if (length <= 1)
                    {
                        // janky, but whatever
                        Plugin.Log.Warn($"Failed to parse song length from audio file, approximating using map length. Song: {loadedSaveData.customLevelFolderInfo.folderPath}");

                        IBeatmapLevelData beatmapLevelData = null!;

                        if (loadedSaveData.standardLevelInfoSaveData != null)
                        {
                            beatmapLevelData = _customLevelLoader.CreateBeatmapLevelDataFromV3(loadedSaveData.customLevelFolderInfo, loadedSaveData.standardLevelInfoSaveData);
                        }
                        else if (loadedSaveData.beatmapLevelSaveData != null)
                        {
                            beatmapLevelData = _customLevelLoader.CreateBeatmapLevelDataFromV4(loadedSaveData.customLevelFolderInfo, loadedSaveData.beatmapLevelSaveData);
                        }

                        length = GetLengthFromMap(loadedSaveData, beatmapLevelData, beatmapLevel);
                    }
                }

                if (data != null)
                {
                    data.duration = length;
                    data.id = levelID;
                }
                else
                {
                    Hashing.cachedAudioData[relativePath] = new AudioCacheData(levelID, length);
                }

                if (_config.ForceLongPreviews)
                {
                    Accessors.PreviewDurationAccessor(ref beatmapLevel) = Mathf.Max(beatmapLevel.previewDuration, length - beatmapLevel.previewStartTime);
                }

                Accessors.SongDurationAccessor(ref beatmapLevel) = length;
            }
            catch (Exception ex)
            {
                Plugin.Log.Warn("Failed to parse song duration");
                Plugin.Log.Warn(ex);
            }
        }

        public static float GetLengthFromMap(CustomLevelLoader.LoadedSaveData loadedSaveData, IBeatmapLevelData beatmapLevelData, BeatmapLevel level)
        {
            float length = 0;
            var fileSystemBeatmapLevelData = (FileSystemBeatmapLevelData)beatmapLevelData;

            if (loadedSaveData.standardLevelInfoSaveData != null)
            {
                var beatmapKey = level.GetBeatmapKeys().Last();
                string? json;

                IOBlacklistHook.AllowIO.Value = true;
                try
                {
                    json = OriginalMethods.FileSystemBeatmapLevelData.GetBeatmapString(fileSystemBeatmapLevelData, beatmapKey);
                }
                finally
                {
                    IOBlacklistHook.AllowIO.Value = false;
                }

                var beatmapSaveData = JsonUtility.FromJson<BeatmapSaveDataVersion3.BeatmapSaveData>(json);

                float highestTime = 0;
                if (beatmapSaveData.colorNotes.Count > 0)
                {
                    highestTime = beatmapSaveData.colorNotes.Max(x => x.beat);
                }
                else if (beatmapSaveData.basicBeatmapEvents.Count > 0)
                {
                    highestTime = beatmapSaveData.basicBeatmapEvents.Max(x => x.beat);
                }

                var bpmTimeProcessor = new BpmTimeProcessor(level.beatsPerMinute, beatmapSaveData.bpmEvents);
                length = bpmTimeProcessor.ConvertBeatToTime(highestTime);
            }
            else if (loadedSaveData.beatmapLevelSaveData != null)
            {
                var beatmapKey = level.GetBeatmapKeys().Last();
                string? beatmapJson;
                string? lightShowJson;
                string? audioJson;

                IOBlacklistHook.AllowIO.Value = true;
                try
                {
                    audioJson = OriginalMethods.FileSystemBeatmapLevelData.GetAudioDataString(fileSystemBeatmapLevelData);
                    beatmapJson = OriginalMethods.FileSystemBeatmapLevelData.GetBeatmapString(fileSystemBeatmapLevelData, beatmapKey);
                    lightShowJson = OriginalMethods.FileSystemBeatmapLevelData.GetLightshowString(fileSystemBeatmapLevelData, beatmapKey);
                }
                finally
                {
                    IOBlacklistHook.AllowIO.Value = false;
                }

                var beatmapSaveData = JsonUtility.FromJson<BeatmapSaveData>(beatmapJson);
                var lightshowSaveData = JsonUtility.FromJson<LightshowSaveData>(lightShowJson);
                var audioSaveData = JsonUtility.FromJson<AudioSaveData>(audioJson);

                float highestTime = 0;
                if (beatmapSaveData.colorNotes.Length > 0)
                {
                    highestTime = beatmapSaveData.colorNotes.Max(x => x.beat);
                }
                else if (lightshowSaveData.basicEvents.Length > 0)
                {
                    highestTime = lightshowSaveData.basicEvents.Max(x => x.beat);
                }

                var bpmTimeProcessor = new BpmTimeProcessor(audioSaveData);
                length = bpmTimeProcessor.ConvertBeatToTime(highestTime);
            }

            if (length == 0)
            {
                Plugin.Log.Warn($"Failed to parse song length from audio file. Song: {loadedSaveData.customLevelFolderInfo.folderPath}");
            }

            return length;
        }

        private static readonly byte[] oggBytes =
        {
            0x4F,
            0x67,
            0x67,
            0x53,
            0x00,
            0x04
        };

        private static float GetLengthFromAudio(string filePath)
        {
            if (!string.Equals(Path.GetExtension(filePath), ".wav", StringComparison.OrdinalIgnoreCase))
            {
                return GetLengthFromOgg(filePath);
            }

            using var stream = File.OpenRead(filePath);
            using var reader = new BinaryReader(stream, Encoding.ASCII);
            if (stream.Length < 12 || Encoding.ASCII.GetString(reader.ReadBytes(4)) != "RIFF")
            {
                return -1;
            }

            long end = Math.Min(stream.Length, 8L + reader.ReadUInt32());
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "WAVE")
            {
                return -1;
            }

            uint byteRate = 0;
            long dataSize = 0;
            while (stream.Position + 8 <= end)
            {
                string chunk = Encoding.ASCII.GetString(reader.ReadBytes(4));
                uint size = reader.ReadUInt32();
                long next = stream.Position + size + (size & 1);
                if (stream.Position + size > end)
                {
                    return -1;
                }

                if (chunk == "fmt " && size >= 16)
                {
                    ushort format = reader.ReadUInt16();
                    reader.ReadUInt16(); // Channels.
                    reader.ReadUInt32(); // Sample rate.
                    uint rate = reader.ReadUInt32();
                    if (format == 1 || format == 3)
                    {
                        byteRate = rate;
                    }
                }
                else if (chunk == "data")
                {
                    dataSize += size;
                }

                stream.Position = next;
            }

            return byteRate > 0 ? (float)((double)dataSize / byteRate) : -1;
        }

        public static float GetLengthFromOgg(string oggFile)
        {
            using var fs = File.OpenRead(oggFile);
            using var br = new BinaryReader(fs, Encoding.ASCII);

            /*
                 * Tries to find the array of bytes from the stream
                 */
            bool FindBytes(byte[] bytes, int searchLength)
            {
                for (var i = 0; i < searchLength; i++)
                {
                    var b = br.ReadByte();
                    if (b != bytes[0])
                    {
                        continue;
                    }

                    var by = br.ReadBytes(bytes.Length - 1);
                    // hardcoded 6 bytes compare, is fine because all inputs used are 6 bytes
                    // bitwise AND the last byte to read only the flag bit for lastSample searching
                    // shouldn't cause issues finding rate, hopefully
                    if (by[0] == bytes[1] && by[1] == bytes[2] && by[2] == bytes[3] && by[3] == bytes[4] && (by[4] & bytes[5]) == bytes[5])
                    {
                        return true;
                    }

                    var index = Array.IndexOf(by, bytes[0]);
                    if (index != -1)
                    {
                        fs.Position += index - (bytes.Length - 1);
                        i += index;
                    }
                    else
                    {
                        i += (bytes.Length - 1);
                    }
                }

                return false;
            }

            var rate = -1;
            long lastSample = -1;

            //Skip Capture Pattern
            fs.Position = 24;

            //{0x76, 0x6F, 0x72, 0x62, 0x69, 0x73} = "vorbis" in byte values
            var foundVorbis = FindBytes(new byte[]
            {
                0x76,
                0x6F,
                0x72,
                0x62,
                0x69,
                0x73
            }, 256);
            if (foundVorbis)
            {
                fs.Position += 5;
                rate = br.ReadInt32();
            }
            else
            {
                Plugin.Log.Warn($"Could not find rate for {oggFile}");
                return -1;
            }

            /*
                 * this finds the last occurrence of OggS in the file by checking for a bit flag (0x04)
                 * reads in blocks determined by seekBlockSize
                 * 6144 does not add significant overhead and speeds up the search significantly
                 */
            const int seekBlockSize = 6144;
            const int seekTries = 10; // 60 KiB should be enough for any sane ogg file
            for (var i = 0; i < seekTries; i++)
            {
                var seekPos = (i + 1) * seekBlockSize * -1;
                var overshoot = Math.Max((int) (-seekPos - fs.Length), 0);
                if (overshoot >= seekBlockSize)
                {
                    break;
                }

                fs.Seek(seekPos + overshoot, SeekOrigin.End);
                var foundOggS = FindBytes(oggBytes, seekBlockSize - overshoot);
                if (foundOggS)
                {
                    lastSample = br.ReadInt64();
                    break;
                }
            }

            if (lastSample == -1)
            {
                Plugin.Log.Warn($"Could not find lastSample for {oggFile}");
                return -1;
            }

            var length = lastSample / (float) rate;
            return length;
        }

        #endregion
    }
}
