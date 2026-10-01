using System;
using System.IO;
using BGLib.JsonExtension;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SongCore.Data
{
    internal static class SongDataCacheSnapshot
    {
        internal static object Capture(SongData data)
        {
            try
            {
                return Copy(data, song => new SongData
                {
                    _genreTags = CopyArray(song._genreTags),
                    contributors = CopyArray(song.contributors, author => new SongData.Contributor
                    {
                        _role = author._role,
                        _name = author._name,
                        _iconPath = author._iconPath
                    }),
                    _customEnvironmentName = song._customEnvironmentName,
                    _customEnvironmentHash = song._customEnvironmentHash,
                    _defaultCharacteristic = song._defaultCharacteristic,
                    _environmentNames = CopyArray(song._environmentNames),
                    _characteristicDetails = CopyArray(song._characteristicDetails, detail => new SongData.CharacteristicDetails
                    {
                        _beatmapCharacteristicName = detail._beatmapCharacteristicName,
                        _characteristicLabel = detail._characteristicLabel,
                        _characteristicIconFilePath = detail._characteristicIconFilePath
                    }),
                    _difficulties = CopyArray(song._difficulties, difficulty => new SongData.DifficultyData
                    {
                        _beatmapCharacteristicName = difficulty._beatmapCharacteristicName,
                        _difficulty = difficulty._difficulty,
                        _difficultyLabel = difficulty._difficultyLabel,
                        additionalDifficultyData = Copy(difficulty.additionalDifficultyData, requirements => new SongData.RequirementData
                        {
                            _requirements = CopyArray(requirements._requirements),
                            _suggestions = CopyArray(requirements._suggestions),
                            _warnings = CopyArray(requirements._warnings),
                            _information = CopyArray(requirements._information)
                        }),
                        _colorLeft = CopyColor(difficulty._colorLeft),
                        _colorRight = CopyColor(difficulty._colorRight),
                        _envColorLeft = CopyColor(difficulty._envColorLeft),
                        _envColorRight = CopyColor(difficulty._envColorRight),
                        _envColorWhite = CopyColor(difficulty._envColorWhite),
                        _envColorLeftBoost = CopyColor(difficulty._envColorLeftBoost),
                        _envColorRightBoost = CopyColor(difficulty._envColorRightBoost),
                        _envColorWhiteBoost = CopyColor(difficulty._envColorWhiteBoost),
                        _obstacleColor = CopyColor(difficulty._obstacleColor),
                        _beatmapColorSchemeIdx = difficulty._beatmapColorSchemeIdx,
                        _environmentNameIdx = difficulty._environmentNameIdx,
                        _oneSaber = difficulty._oneSaber,
                        _showRotationNoteSpawnLines = difficulty._showRotationNoteSpawnLines,
                        _styleTags = CopyArray(difficulty._styleTags)
                    }),
                    _colorSchemes = CopyArray(song._colorSchemes, scheme => new SongData.ColorScheme
                    {
                        useOverride = scheme.useOverride,
                        colorSchemeId = scheme.colorSchemeId,
                        saberAColor = CopyColor(scheme.saberAColor),
                        saberBColor = CopyColor(scheme.saberBColor),
                        environmentColor0 = CopyColor(scheme.environmentColor0),
                        environmentColor1 = CopyColor(scheme.environmentColor1),
                        obstaclesColor = CopyColor(scheme.obstaclesColor),
                        environmentColor0Boost = CopyColor(scheme.environmentColor0Boost),
                        environmentColor1Boost = CopyColor(scheme.environmentColor1Boost),
                        environmentColorW = CopyColor(scheme.environmentColorW),
                        environmentColorWBoost = CopyColor(scheme.environmentColorWBoost)
                    })
                });
            }
            catch (NotSupportedException)
            {
                // Derived models can carry native state; only their owned JSON may leave the caller.
                using var writer = new StringWriter();
                using var json = new JsonTextWriter(writer) { CloseOutput = false, Formatting = Formatting.None };
                JsonSerializer.Create(JsonSettings.compactNoDefault).Serialize(json, data, typeof(SongData));
                json.Flush();
                return new JRaw(writer.ToString());
            }
        }

        private static T Copy<T>(T value, Func<T, T> copy) where T : class
        {
            if (value is null)
                return null!;
            if (value.GetType() != typeof(T))
                throw new NotSupportedException();
            return copy(value);
        }

        private static T[] CopyArray<T>(T[]? values)
        {
            return values is null ? null! : (T[])values.Clone();
        }

        private static T[] CopyArray<T>(T[]? values, Func<T, T> copy) where T : class
        {
            if (values is null)
                return null!;
            var result = new T[values.Length];
            for (var i = 0; i < values.Length; i++)
                result[i] = Copy(values[i], copy);
            return result;
        }

        private static SongData.MapColor? CopyColor(SongData.MapColor? color)
        {
            if (color is null)
                return null;
            return Copy(color, value => new SongData.MapColor(value.r, value.g, value.b, value.a));
        }
    }
}
