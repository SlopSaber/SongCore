using System;
using MonoMod.Utils;
using SongCore.Utilities;

namespace SongCore.Hooks.BeatmapLevelCache
{
    internal static class OriginalMethods
    {
        public static class FileSystemBeatmapLevelData
        {
            private delegate string? GetStringDelegate(global::FileSystemBeatmapLevelData self, in BeatmapKey beatmapKey);

            private static readonly Func<global::FileSystemBeatmapLevelData, string?> GetAudioDataStringCopy = typeof(global::FileSystemBeatmapLevelData).GetMethod(nameof(global::FileSystemBeatmapLevelData.GetAudioDataString))!.CreateILCopy().CreateDelegate<Func<global::FileSystemBeatmapLevelData, string?>>();
            private static readonly GetStringDelegate GetBeatmapStringCopy = typeof(global::FileSystemBeatmapLevelData).GetMethod(nameof(global::FileSystemBeatmapLevelData.GetBeatmapString))!.CreateILCopy().CreateDelegate<GetStringDelegate>();
            private static readonly GetStringDelegate GetLightshowStringCopy = typeof(global::FileSystemBeatmapLevelData).GetMethod(nameof(global::FileSystemBeatmapLevelData.GetLightshowString))!.CreateILCopy().CreateDelegate<GetStringDelegate>();

            public static string? GetAudioDataString(global::FileSystemBeatmapLevelData instance)
            {
                return GetAudioDataStringCopy(instance);
            }

            public static string? GetBeatmapString(global::FileSystemBeatmapLevelData instance, in BeatmapKey beatmapKey)
            {
                return GetBeatmapStringCopy(instance, in beatmapKey);
            }

            public static string? GetLightshowString(global::FileSystemBeatmapLevelData instance, in BeatmapKey beatmapKey)
            {
                return GetLightshowStringCopy(instance, in beatmapKey);
            }
        }
    }
}
