using System;
using System.Linq;
using MonoMod.RuntimeDetour;
using Zenject;

namespace SongCore.Hooks
{
    // TODO: Remove missing characteristic. Might end up in wiped save data.
    internal class CustomCharacteristicsHook : IInitializable, IDisposable
    {
        private Hook _getBeatmapCharacteristicBySerializedNameHook = null!;
        private Hook _parseBeatmapCharacteristicHook = null!;

        private delegate bool ParseCharacteristicDelegate(string serializedName, out BeatmapCharacteristic characteristic);

        public void Initialize()
        {
            _getBeatmapCharacteristicBySerializedNameHook = new Hook(typeof(BeatmapCharacteristicCollection).GetMethod(nameof(BeatmapCharacteristicCollection.GetBeatmapCharacteristicBySerializedName))!, GetCustomCharacteristic, true);
            _parseBeatmapCharacteristicHook = new Hook(typeof(BeatmapCharacteristicExtensions).GetMethod(nameof(BeatmapCharacteristicExtensions.BeatmapCharacteristicFromSerializedName))!, ParseCharacteristic, true);
        }

        public void Dispose()
        {
            _getBeatmapCharacteristicBySerializedNameHook.Dispose();
            _parseBeatmapCharacteristicHook.Dispose();
        }

        private bool ParseCharacteristic(ParseCharacteristicDelegate original, string serializedName, out BeatmapCharacteristic characteristic)
        {
            if (serializedName == "Lawless")
            {
                characteristic = BeatmapCharacteristic.Standard;
                return true;
            }

            return original(serializedName, out characteristic);
        }

        private BeatmapCharacteristicSO? GetCustomCharacteristic(Func<BeatmapCharacteristicCollection, string, BeatmapCharacteristicSO> original, BeatmapCharacteristicCollection instance, string serializedName)
        {
            var result = original(instance, serializedName);

            if (result != null)
            {
                return result;
            }

            var customCharacteristic = Collections.customCharacteristics.FirstOrDefault(c => c.serializedName == serializedName);
            return customCharacteristic != null ? customCharacteristic : Collections.customCharacteristics.FirstOrDefault(c => c.serializedName == "MissingCharacteristic");
        }
    }
}
