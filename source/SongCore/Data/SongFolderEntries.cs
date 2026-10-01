using SongCore.OverrideClasses;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Concurrent;
using SongCore.Utilities;

namespace SongCore.Data
{
    public enum FolderLevelPack { CustomLevels, CustomWIPLevels, NewPack, CachedWIPLevels };

    [Serializable]
    public class SongFolderEntry
    {
        public string Name;
        public string Path;
        public FolderLevelPack Pack;
        public string ImagePath;
        public bool WIP;
        public bool CacheZIPs;

        public SongFolderEntry(string name, string path, FolderLevelPack pack, string imagePath = "", bool wip = false, bool cachezips = false)
        {
            Name = name;
            Path = path;
            Pack = pack;
            ImagePath = imagePath;
            WIP = wip;
            CacheZIPs = cachezips;
        }
    }

    public class SeparateSongFolder
    {
        public readonly ConcurrentDictionary<string, BeatmapLevel> Levels = new ConcurrentDictionary<string, BeatmapLevel>();

        public SongFolderEntry SongFolderEntry { get; private set; }
        public SongCoreCustomBeatmapLevelPack? LevelPack { get; private set; }
        public SeparateSongFolder? CacheFolder { get; private set; }

        public SeparateSongFolder(SongFolderEntry folderEntry, SeparateSongFolder? cacheFolder = null)
            : this(PrepareSongFolder(folderEntry), cacheFolder)
        {
        }

        internal SeparateSongFolder(PreparedSongFolder prepared, SeparateSongFolder? cacheFolder = null)
        {
            var folderEntry = prepared.Entry;
            SongFolderEntry = folderEntry;
            CacheFolder = cacheFolder;

            if (folderEntry.Pack == FolderLevelPack.NewPack)
            {
                var image = UI.BasicUI.FolderIcon!;

                if (prepared.ImageReadFailed)
                {
                    Plugin.Log.Info($"Failed to load image for separate folder \"{folderEntry.Name}\"");
                }
                else if (prepared.ImageData != null)
                {
                    try
                    {
                        var packImage = Utils.LoadSpriteRaw(prepared.ImageData);
                        if (packImage != null)
                        {
                            image = packImage;
                        }
                    }
                    catch
                    {
                        Plugin.Log.Info($"Failed to load image for separate folder \"{folderEntry.Name}\"");
                    }
                }

                LevelPack = new SongCoreCustomBeatmapLevelPack(CustomLevelLoader.kCustomLevelPackPrefixId + folderEntry.Name, folderEntry.Name, image, Levels.Values.ToArray());
            }
        }

        public SeparateSongFolder(SongFolderEntry folderEntry, UnityEngine.Sprite image)
        {
            SongFolderEntry = folderEntry;
            if (folderEntry.Pack == FolderLevelPack.NewPack)
            {
                LevelPack = new SongCoreCustomBeatmapLevelPack(CustomLevelLoader.kCustomLevelPackPrefixId + folderEntry.Name, folderEntry.Name, image, Levels.Values.ToArray());
            }
        }

        public static List<SeparateSongFolder> ReadSeparateFoldersFromFile(string filePath)
        {
            return CreateSeparateFolders(PrepareSeparateFoldersFromFile(filePath));
        }

        internal static PreparedSongFolders PrepareSeparateFoldersFromFile(string filePath)
        {
            var result = new PreparedSongFolders();
            try
            {
                var file = XDocument.Load(filePath);
                foreach (var item in file.Root!.Elements())
                {
                    var name = item.Element("Name")!.Value;
                    if (name == "Example")
                    {
                        continue;
                    }

                    var path = item.Element("Path")!.Value;
                    var pack = int.Parse(item.Element("Pack")!.Value);
                    var imagePath = "";
                    var image = item.Element("ImagePath");
                    if (image != null)
                    {
                        imagePath = image.Value;
                    }

                    var isWIP = false;
                    var wip = item.Element("WIP");
                    if (wip != null)
                    {
                        isWIP = bool.Parse(wip.Value);
                    }

                    var zipCaching = false;
                    var cachezips = item.Element("CacheZIPs");
                    if (cachezips != null)
                    {
                        zipCaching = bool.Parse(cachezips.Value);
                    }

                    var entry = new SongFolderEntry(name, path, (FolderLevelPack) pack, imagePath, isWIP, zipCaching);

                    PreparedSongFolder? cachedSeparate = null;
                    if (zipCaching)
                    {
                        var cachePack = (FolderLevelPack) pack == FolderLevelPack.CustomWIPLevels ? FolderLevelPack.CachedWIPLevels : FolderLevelPack.NewPack;

                        var cachedSongFolderEntry = new SongFolderEntry($"Cached {name}", Path.Combine(path, "Cache"), cachePack, imagePath, isWIP, false);
                        cachedSeparate = PrepareSongFolder(cachedSongFolderEntry);
                    }

                    result.Folders.Add(PrepareSongFolder(entry, cachedSeparate));
                }
            }
            catch
            {
                result.ParseFailed = true;
            }

            return result;
        }

        internal static List<SeparateSongFolder> CreateSeparateFolders(PreparedSongFolders prepared)
        {
            var result = new List<SeparateSongFolder>();
            var failed = prepared.ParseFailed;
            try
            {
                foreach (var folder in prepared.Folders)
                {
                    var cachedSeparate = folder.CacheFolder == null ? null : new SeparateSongFolder(folder.CacheFolder);
                    var separate = new SeparateSongFolder(folder, cachedSeparate);
                    result.Add(separate);
                    if (cachedSeparate != null)
                        result.Add(cachedSeparate);
                }
            }
            catch
            {
                failed = true;
            }

            if (failed)
                Plugin.Log.Warn("Error reading folders.xml! Make sure the file is properly formatted.");

            return result;
        }

        private static PreparedSongFolder PrepareSongFolder(SongFolderEntry entry, PreparedSongFolder? cacheFolder = null)
        {
            var prepared = new PreparedSongFolder(entry, cacheFolder);
            if (entry.Pack == FolderLevelPack.NewPack && !string.IsNullOrEmpty(entry.ImagePath))
            {
                try
                {
                    if (File.Exists(entry.ImagePath))
                        prepared.ImageData = File.ReadAllBytes(entry.ImagePath);
                }
                catch
                {
                    prepared.ImageReadFailed = true;
                }
            }

            return prepared;
        }

        internal sealed class PreparedSongFolder
        {
            internal readonly SongFolderEntry Entry;
            internal readonly PreparedSongFolder? CacheFolder;
            internal byte[]? ImageData;
            internal bool ImageReadFailed;

            internal PreparedSongFolder(SongFolderEntry entry, PreparedSongFolder? cacheFolder)
            {
                Entry = entry;
                CacheFolder = cacheFolder;
            }
        }

        internal sealed class PreparedSongFolders
        {
            internal readonly List<PreparedSongFolder> Folders = new List<PreparedSongFolder>();
            internal bool ParseFailed;
        }
    }

    public class ModSeparateSongFolder : SeparateSongFolder
    {
        public bool AlwaysShow { get; set; } = true;

        public ModSeparateSongFolder(SongFolderEntry folderEntry) : base(folderEntry)
        {
        }

        public ModSeparateSongFolder(SongFolderEntry folderEntry, UnityEngine.Sprite image) : base(folderEntry, image)
        {
        }
    }
}
