using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace SongCore.Utilities
{
    internal static class CacheFile
    {
        private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            DefaultValueHandling = DefaultValueHandling.Ignore,
            Formatting = Formatting.None,
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            ContractResolver = new DefaultContractResolver { IgnoreSerializableAttribute = false }
        };

        internal static void Write<T>(T snapshot, string path)
        {
            using var stream = File.Open(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
            stream.SetLength(0);
            using var writer = new StreamWriter(stream) { NewLine = "\n" };
            using var json = new JsonTextWriter(writer) { CloseOutput = false, Formatting = Formatting.None };
            // Cache jobs must not invoke process-wide converters on a worker.
            JsonSerializer.Create(_settings).Serialize(json, snapshot, typeof(T));
            json.Flush();
            writer.WriteLine();
        }
    }
}
