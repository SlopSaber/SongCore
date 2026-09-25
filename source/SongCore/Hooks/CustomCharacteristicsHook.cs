using System;
using System.Linq;
using MonoMod.RuntimeDetour;
using UnityEngine;
using Zenject;

namespace SongCore.Hooks
{
    // TODO: Remove missing characteristic. Might end up in wiped save data.
    internal class CustomCharacteristicsHook : IInitializable, IDisposable
    {
        private const BeatmapCharacteristic LawlessCharacteristic = (BeatmapCharacteristic)6;

        private Hook _getBeatmapCharacteristicBySerializedNameHook = null!;
        private Hook _parseBeatmapCharacteristicHook = null!;
        private Hook _serializedNameHook = null!;
        private Hook _compoundIdPartNameHook = null!;
        private Hook _nameLocalizationKeyHook = null!;
        private Hook _hintLocalizationKeyHook = null!;
        private Hook _getBeatmapCharacteristicIconHook = null!;

        private delegate bool ParseCharacteristicDelegate(string serializedName, out BeatmapCharacteristic characteristic);

        public void Initialize()
        {
            _getBeatmapCharacteristicBySerializedNameHook = new Hook(typeof(BeatmapCharacteristicCollection).GetMethod(nameof(BeatmapCharacteristicCollection.GetBeatmapCharacteristicBySerializedName))!, GetCustomCharacteristic, true);
            _parseBeatmapCharacteristicHook = new Hook(typeof(BeatmapCharacteristicExtensions).GetMethod(nameof(BeatmapCharacteristicExtensions.BeatmapCharacteristicFromSerializedName))!, ParseCharacteristic, true);
            _serializedNameHook = new Hook(typeof(BeatmapCharacteristicExtensions).GetMethod(nameof(BeatmapCharacteristicExtensions.SerializedName), new[] { typeof(BeatmapCharacteristic) })!, SerializedName, true);
            _compoundIdPartNameHook = new Hook(typeof(BeatmapCharacteristicExtensions).GetMethod(nameof(BeatmapCharacteristicExtensions.CompoundIdPartName))!, CompoundIdPartName, true);
            _nameLocalizationKeyHook = new Hook(typeof(BeatmapCharacteristicExtensions).GetMethod(nameof(BeatmapCharacteristicExtensions.NameLocalizationKey))!, NameLocalizationKey, true);
            _hintLocalizationKeyHook = new Hook(typeof(BeatmapCharacteristicExtensions).GetMethod(nameof(BeatmapCharacteristicExtensions.HintLocalizationKey))!, HintLocalizationKey, true);
            _getBeatmapCharacteristicIconHook = new Hook(typeof(BeatmapCharacteristicCollection).GetMethod(nameof(BeatmapCharacteristicCollection.GetBeatmapCharacteristicIcon))!, GetCharacteristicIcon, true);
        }

        public void Dispose()
        {
            _getBeatmapCharacteristicBySerializedNameHook.Dispose();
            _parseBeatmapCharacteristicHook.Dispose();
            _serializedNameHook.Dispose();
            _compoundIdPartNameHook.Dispose();
            _nameLocalizationKeyHook.Dispose();
            _hintLocalizationKeyHook.Dispose();
            _getBeatmapCharacteristicIconHook.Dispose();
        }

        private bool ParseCharacteristic(ParseCharacteristicDelegate original, string serializedName, out BeatmapCharacteristic characteristic)
        {
            if (serializedName == "Lawless")
            {
                characteristic = LawlessCharacteristic;
                return true;
            }

            return original(serializedName, out characteristic);
        }

        private string SerializedName(Func<BeatmapCharacteristic, string> original, BeatmapCharacteristic characteristic)
        {
            return characteristic == LawlessCharacteristic ? "Lawless" : original(characteristic);
        }

        private string CompoundIdPartName(Func<BeatmapCharacteristic, string> original, BeatmapCharacteristic characteristic)
        {
            return characteristic == LawlessCharacteristic ? "Lawless" : original(characteristic);
        }

        private string NameLocalizationKey(Func<BeatmapCharacteristic, string> original, BeatmapCharacteristic characteristic)
        {
            return characteristic == LawlessCharacteristic ? "Lawless" : original(characteristic);
        }

        private string HintLocalizationKey(Func<BeatmapCharacteristic, string> original, BeatmapCharacteristic characteristic)
        {
            return characteristic == LawlessCharacteristic ? "Lawless - Anything Goes" : original(characteristic);
        }

        private Sprite GetCharacteristicIcon(Func<BeatmapCharacteristicCollection, BeatmapCharacteristic, Sprite> original, BeatmapCharacteristicCollection instance, BeatmapCharacteristic characteristic)
        {
            return characteristic == LawlessCharacteristic ? UI.BasicUI.ExtraDiffsIcon! : original(instance, characteristic);
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
