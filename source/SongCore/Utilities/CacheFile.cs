using System.IO;
using BGLib.JsonExtension;
using Newtonsoft.Json;

namespace SongCore.Utilities
{
    internal static class CacheFile
    {
        internal static void Write<T>(T snapshot, string path)
        {
            using var stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            stream.SetLength(0);
            using var writer = new StreamWriter(stream) { NewLine = "\n" };
            using var json = new JsonTextWriter(writer) { CloseOutput = false, Formatting = Formatting.None };
            // Cache jobs must not invoke process-wide converters on a worker.
            JsonSerializer.Create(JsonSettings.compactNoDefault).Serialize(json, snapshot, typeof(T));
            json.Flush();
            writer.WriteLine();
        }
    }
}
