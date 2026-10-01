using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using IPA;
using IPA.Config;
using IPA.Config.Stores;
using IPA.Loader;
using IPA.Logging;
using IPA.Utilities;
using SiraUtil.Zenject;
using SongCore.Installers;
using SongCore.UI;

namespace SongCore
{
    [Plugin(RuntimeOptions.SingleStartInit)]
    public class Plugin
    {
        private readonly PluginMetadata _metadata;
        private bool _folderStopping;

        internal static Logger Log { get; private set; } = null!;
        internal static Task FolderInitializationTask { get; private set; } = Task.CompletedTask;

        [Init]
        public Plugin(Logger logger, PluginMetadata metadata, Zenjector zenjector)
        {
            // Workaround for creating BSIPA config in Userdata subdir
            Directory.CreateDirectory(Path.Combine(UnityGame.UserDataPath, nameof(SongCore)));

            Log = logger;
            _metadata = metadata;

            zenjector.UseLogger(logger);
            zenjector.Install<AppInstaller>(Location.App, Config.GetConfigFor(nameof(SongCore) + Path.DirectorySeparatorChar + nameof(SongCore)).Generated<PluginConfig>());
            zenjector.Install<MenuInstaller>(Location.Menu);
            zenjector.Install<GameInstaller>(Location.StandardPlayer);
        }

        [OnStart]
        public void OnApplicationStart()
        {
            BasicUI.GetIcons();

            _ = Collections.LoadCachedSongDataAsync();

            Collections.RegisterCustomCharacteristic(BasicUI.MissingCharIcon!, "Missing Characteristic", "Missing Characteristic", "MissingCharacteristic", "MissingCharacteristic", false, false, 1000);
            Collections.RegisterCustomCharacteristic(BasicUI.LightshowIcon!, "Lightshow", "Lightshow", "Lightshow", "Lightshow", false, false, 100);
            Collections.RegisterCustomCharacteristic(BasicUI.ExtraDiffsIcon!, "Lawless", "Lawless - Anything Goes", "Lawless", "Lawless", false, false, 101);

            var foldersXmlFilePath = Path.Combine(UnityGame.UserDataPath, nameof(SongCore), "folders.xml");
            FolderInitializationTask = InitializeFoldersAsync(foldersXmlFilePath, _metadata.Assembly);
        }

        private async Task InitializeFoldersAsync(string filePath, Assembly assembly)
        {
            try
            {
                var prepared = await Task.Run(() =>
                {
                    if (!File.Exists(filePath))
                    {
                        using var resourceStream = assembly.GetManifestResourceStream("SongCore.Data.folders.xml");
                        using var fileStream = File.OpenWrite(filePath);
                        resourceStream!.CopyTo(fileStream);
                    }

                    return Data.SeparateSongFolder.PrepareSeparateFoldersFromFile(filePath);
                });
                await UnityGame.SwitchToMainThreadAsync();
                if (_folderStopping)
                    return;

                Loader.SeparateSongFolders.InsertRange(0, Data.SeparateSongFolder.CreateSeparateFolders(prepared));
            }
            catch (Exception ex)
            {
                Log.Error("Error initializing separate song folders:");
                Log.Error(ex);
            }
        }

        [OnExit]
        public void OnApplicationExit()
        {
            _folderStopping = true;
            Loader.StopCatalog();
            Collections.StopCachedSongDataLoad();
        }
    }
}
