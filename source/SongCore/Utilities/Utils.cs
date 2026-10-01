using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using SongCore.Data;
using UnityEngine;

namespace SongCore.Utilities
{
    public static class Utils
    {
        private static Task<Dictionary<string, Task<byte[]>>>? _iconResources;
        internal static bool IconWorkStopping { get; private set; }

        internal static void StopIconWork() => IconWorkStopping = true;

        internal static Task<Dictionary<string, byte[]?>> ReadIconFilesAsync(string[] paths)
        {
            return Task.Run(() =>
            {
                var files = new Dictionary<string, byte[]?>();
                foreach (var path in paths.Distinct(StringComparer.Ordinal))
                {
                    try
                    {
                        files[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
                    }
                    catch (Exception ex)
                    {
                        files[path] = null;
                        Plugin.Log.Error($"Error reading icon: {path}");
                        Plugin.Log.Error(ex);
                    }
                }
                return files;
            });
        }

        internal static void PrepareIconResources(Assembly assembly)
        {
            _iconResources ??= Task.Run(() =>
            {
                var resources = new Dictionary<string, Task<byte[]>>();
                foreach (var name in assembly.GetManifestResourceNames().Where(name =>
                             name.StartsWith("SongCore.Icons.", StringComparison.Ordinal) && name.EndsWith(".png", StringComparison.Ordinal)))
                {
                    try
                    {
                        resources.Add(name, Task.FromResult(GetResource(assembly, name)));
                    }
                    catch (Exception ex)
                    {
                        resources.Add(name, Task.FromException<byte[]>(ex));
                    }
                }
                return resources;
            });
        }

        internal static Sprite? LoadPreparedIcon(string resourcePath)
        {
            var assembly = typeof(Plugin).Assembly;
            PrepareIconResources(assembly);
            // Immediate callers may join only byte work; the resource task never dispatches to main.
            var resources = _iconResources!.GetAwaiter().GetResult();
            var bytes = resources.TryGetValue(resourcePath, out var resource)
                ? resource.GetAwaiter().GetResult()
                : Task.Run(() => GetResource(assembly, resourcePath)).GetAwaiter().GetResult();
            return LoadSpriteRaw(bytes);
        }

        public static bool IsModInstalled(string modName)
        {
            return IPA.Loader.PluginManager.EnabledPlugins.Any(mod => mod.Id == modName || mod.Name == modName);
        }

        public static bool DiffHasColors(SongData.DifficultyData songData)
        {
            return songData._colorLeft != null || songData._colorRight != null || songData._envColorLeft != null || songData._envColorRight != null
                   || songData._envColorLeftBoost != null || songData._envColorRightBoost != null || songData._obstacleColor != null;
        }

        public static Color ColorFromMapColor(SongData.MapColor mapColor)
        {
            return new Color(mapColor.r, mapColor.g, mapColor.b, mapColor.a);
        }

        public static TEnum ToEnum<TEnum>(this string strEnumValue, TEnum defaultValue)
        {
            if (!Enum.IsDefined(typeof(TEnum), strEnumValue))
            {
                return defaultValue;
            }

            return (TEnum) Enum.Parse(typeof(TEnum), strEnumValue);
        }

        public static bool IsDirectoryEmpty(string path)
        {
            return !Directory.EnumerateFileSystemEntries(path).Any();
        }

        public static string TrimEnd(this string text, string value)
        {
            return !text.EndsWith(value, StringComparison.Ordinal) ? text : text.Remove(text.LastIndexOf(value, StringComparison.Ordinal));
        }

        public static Sprite? LoadSpriteRaw(byte[] image, float pixelsPerUnit = 100.0f)
        {
            return LoadSpriteFromTexture(LoadTextureRaw(image), pixelsPerUnit);
        }

        public static Sprite? LoadSpriteFromFile(string filePath, float pixelsPerUnit = 100.0f)
        {
            return LoadSpriteFromTexture(LoadTextureFromFile(filePath), pixelsPerUnit);
        }

        public static Sprite? LoadSpriteFromTexture(Texture2D? spriteTexture, float pixelsPerUnit = 100.0f)
        {
            return spriteTexture != null ? Sprite.Create(spriteTexture, new Rect(0, 0, spriteTexture.width, spriteTexture.height), new Vector2(0, 0), pixelsPerUnit) : null;
        }

        public static Sprite? LoadSpriteFromResources(string resourcePath, float pixelsPerUnit = 100.0f)
        {
            return LoadSpriteRaw(GetResource(Assembly.GetCallingAssembly(), resourcePath), pixelsPerUnit);
        }

        public static byte[] GetResource(Assembly asm, string resourceName)
        {
            using var stream = asm.GetManifestResourceStream(resourceName)!;
            var data = new byte[stream.Length];
            stream.Read(data, 0, (int) stream.Length);
            return data;
        }

        public static Texture2D? LoadTextureFromFile(string filePath)
        {
            return File.Exists(filePath) ? LoadTextureRaw(File.ReadAllBytes(filePath)) : null;
        }

        public static Texture2D? LoadTextureFromResources(string resourcePath)
        {
            return LoadTextureRaw(GetResource(Assembly.GetCallingAssembly(), resourcePath));
        }

        public static Texture2D? LoadTextureRaw(byte[] file)
        {
            if (file.Length <= 0)
            {
                return null;
            }

            var tex2D = new Texture2D(2, 2);
            bool loaded = false;
            try
            {
                loaded = tex2D.LoadImage(file);
                return loaded ? tex2D : null;
            }
            finally
            {
                if (!loaded)
                    UnityEngine.Object.Destroy(tex2D);
            }
        }

        public static void PrintHierarchy(Transform transform, string spacing = "|-> ")
        {
            spacing = spacing.Insert(1, "  ");
            var tempList = transform.Cast<Transform>();
            foreach (var child in tempList)
            {
                Console.WriteLine($"{spacing}{child.name}");
                PrintHierarchy(child, "|" + spacing);
            }
        }
    }
}
