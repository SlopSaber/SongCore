using System.IO;
using BeatSaberMarkupLanguage;
using BeatSaberMarkupLanguage.Attributes;
using BeatSaberMarkupLanguage.Components;
using SongCore.Utilities;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static BeatSaberMarkupLanguage.Components.CustomListTableData;
using HMUI;
using SongCore.Data;
using Tweening;
using Zenject;

namespace SongCore.UI
{
    public class RequirementsUI : NotifiableBase, IInitializable
    {
        private readonly StandardLevelDetailViewController _standardLevelDetailViewController;
        private readonly CustomLevelLoader _customLevelLoader;
        private readonly EnvironmentsListModel _environmentsListModel;
        private readonly TimeTweeningManager _tweeningManager;
        private readonly BSMLParser _bsmlParser;
        private readonly PluginConfig _config;
        private readonly ColorsUI _colorsUI;

        private RequirementsUI(StandardLevelDetailViewController standardLevelDetailViewController, CustomLevelLoader customLevelLoader, EnvironmentsListModel environmentsListModel, TimeTweeningManager tweeningManager, BSMLParser bsmlParser, PluginConfig config, ColorsUI colorsUI)
        {
            _standardLevelDetailViewController = standardLevelDetailViewController;
            _customLevelLoader = customLevelLoader;
            _environmentsListModel = environmentsListModel;
            _tweeningManager = tweeningManager;
            _bsmlParser = bsmlParser;
            _config = config;
            _colorsUI = colorsUI;
            instance = this;
        }

        private static RequirementsUI? instanceValue;
        public static RequirementsUI instance
        {
            get => instanceValue ?? throw new System.InvalidOperationException("Requirements UI has not been initialized.");
            set => instanceValue = value;
        }

        private const string BUTTON_BSML = "<bg id='root'><action-button id='info-button' text='?' active='~button-glow' interactable='~button-interactable' anchor-pos-x='31' anchor-pos-y='0' pref-width='12' pref-height='9' on-click='button-click'/></bg>";
        private ImageView? buttonBGValue;
        private ImageView buttonBG
        {
            get => buttonBGValue ?? throw new System.InvalidOperationException("buttonBG has not been initialized.");
            set => buttonBGValue = value;
        }
        private Color originalColor0;
        private Color originalColor1;

        internal Sprite? HaveReqIcon;
        internal Sprite? MissingReqIcon;
        internal Sprite? HaveSuggestionIcon;
        internal Sprite? MissingSuggestionIcon;
        internal Sprite? WarningIcon;
        internal Sprite? InfoIcon;
        internal Sprite? ColorsIcon;
        internal Sprite? OneSaberIcon;
        internal Sprite? EnvironmentIcon;
        internal Sprite? StandardIcon;

        //Currently selected song data
        public BeatmapLevel? beatmapLevel;
        public BeatmapKey? beatmapKey;
        public SongData? songData;
        public SongData.DifficultyData? diffData;
        public bool wipFolder;

        [UIComponent("list")]
        public CustomListTableData? customListTableData;

        private CustomListTableData ListData => customListTableData ?? throw new System.InvalidOperationException("Requirements list has not been parsed.");

        private bool _buttonGlow = false;

        [UIValue("button-glow")]
        public bool ButtonGlowColor
        {
            get => _buttonGlow;
            set
            {
                _buttonGlow = value;
                NotifyPropertyChanged();
            }
        }

        private bool buttonInteractable = false;

        [UIValue("button-interactable")]
        public bool ButtonInteractable
        {
            get => buttonInteractable;
            set
            {
                buttonInteractable = value;
                NotifyPropertyChanged();
            }
        }

        private ModalView? modalValue;
        [UIComponent("modal")]
        private ModalView modal
        {
            get => modalValue ?? throw new System.InvalidOperationException("modal has not been initialized.");
            set => modalValue = value;
        }

        private Vector3 modalPosition;

        private Transform? infoButtonTransformValue;
        [UIComponent("info-button")]
        private Transform infoButtonTransform
        {
            get => infoButtonTransformValue ?? throw new System.InvalidOperationException("infoButtonTransform has not been initialized.");
            set => infoButtonTransformValue = value;
        }

        [UIComponent("root")]
        protected readonly RectTransform _root = null!;

        public void Initialize()
        {
            GetIcons();
            _bsmlParser.Parse(BUTTON_BSML, _standardLevelDetailViewController._standardLevelDetailView.gameObject, this);

            infoButtonTransform.localScale *= 0.7f; //no scale property in bsml as of now so manually scaling it
            ((RectTransform)_standardLevelDetailViewController._standardLevelDetailView._favoriteToggle.transform).anchoredPosition = new Vector2(3, -2);
            buttonBG = infoButtonTransform.Find("BG").GetComponent<ImageView>();
            originalColor0 = buttonBG.color0;
            originalColor1 = buttonBG.color1;
        }

        private void GetIcons()
        {
            if (!MissingReqIcon)
            {
                MissingReqIcon = Utils.LoadSpriteFromResources("SongCore.Icons.RedX.png")!;
            }

            if (!HaveReqIcon)
            {
                HaveReqIcon = Utils.LoadSpriteFromResources("SongCore.Icons.GreenCheck.png")!;
            }

            if (!HaveSuggestionIcon)
            {
                HaveSuggestionIcon = Utils.LoadSpriteFromResources("SongCore.Icons.YellowCheck.png")!;
            }

            if (!MissingSuggestionIcon)
            {
                MissingSuggestionIcon = Utils.LoadSpriteFromResources("SongCore.Icons.YellowX.png")!;
            }

            if (!WarningIcon)
            {
                WarningIcon = Utils.LoadSpriteFromResources("SongCore.Icons.Warning.png")!;
            }

            if (!InfoIcon)
            {
                InfoIcon = Utils.LoadSpriteFromResources("SongCore.Icons.Info.png")!;
            }

            if (!ColorsIcon)
            {
                ColorsIcon = Utils.LoadSpriteFromResources("SongCore.Icons.Colors.png")!;
            }

            if (!EnvironmentIcon)
            {
                EnvironmentIcon = Utils.LoadSpriteFromResources("SongCore.Icons.Environment.png")!;
            }

            if (!OneSaberIcon)
            {
                OneSaberIcon = Utils.LoadSpriteFromResources("SongCore.Icons.OneSaber.png")!;
            }

            if (!StandardIcon)
            {
                StandardIcon = Utils.LoadSpriteFromResources("SongCore.Icons.Standard.png")!;
            }
        }

        [UIAction("button-click")]
        internal void ShowRequirements()
        {
            if (songData == null || beatmapLevel == null || beatmapKey is not { } selectedKey)
                return;

            if (modalValue == null)
            {
                _bsmlParser.Parse(BeatSaberMarkupLanguage.Utilities.GetResourceContent(Assembly.GetExecutingAssembly(), "SongCore.UI.requirements.bsml"), _root.gameObject, this);
                modalPosition = modal!.transform.localPosition;
            }
            modal.transform.localPosition = modalPosition;
            modal.Show(true);
            ListData.Data.Clear();

            //Requirements
            if (diffData != null)
            {
                if (diffData.additionalDifficultyData._requirements.Any())
                {
                    foreach (var req in diffData.additionalDifficultyData._requirements)
                    {
                        ListData.Data.Add(!Collections.capabilities.Contains(req)
                            ? new CustomCellInfo($"<size=75%>{req}", "Missing Requirement", MissingReqIcon)
                            : new CustomCellInfo($"<size=75%>{req}", "Requirement", HaveReqIcon));
                    }
                }
            }

            //Contributors
            if (songData.contributors.Length > 0)
            {
                foreach (var author in songData.contributors)
                {
                    if (author.icon == null)
                    {
                        if (!string.IsNullOrWhiteSpace(author._iconPath))
                        {
                            author.icon = Utils.LoadSpriteFromFile(Path.Combine(_customLevelLoader._loadedBeatmapSaveData[beatmapLevel.levelID].customLevelFolderInfo.folderPath, author._iconPath));
                            ListData.Data.Add(new CustomCellInfo(author._name, author._role, author.icon != null ? author.icon : InfoIcon));
                        }
                        else
                        {
                            ListData.Data.Add(new CustomCellInfo(author._name, author._role, InfoIcon));
                        }
                    }
                    else
                    {
                        ListData.Data.Add(new CustomCellInfo(author._name, author._role, author.icon));
                    }
                }
            }

            //WIP Check
            if (wipFolder)
            {
                ListData.Data.Add(new CustomCellInfo("<size=70%>WIP Song. Please Play in Practice Mode", "Warning", WarningIcon));
            }

            //Additional Diff Info
            if (diffData != null)
            {
                if (Utils.DiffHasColors(diffData))
                {
                    ListData.Data.Add(new CustomCellInfo($"<size=75%>Custom Colors Available", $"Click here to preview & enable or disable it.", ColorsIcon));
                }
                string? environmentName = null;

                if (diffData._environmentNameIdx != null)
                {
                    var environmentInfoName = songData._environmentNames.ElementAtOrDefault(diffData._environmentNameIdx.Value);
                    if (environmentInfoName != null)
                    {
                        if (environmentInfoName != beatmapLevel.GetEnvironmentName(selectedKey.characteristic, selectedKey.difficulty))
                        {
                            environmentName = _environmentsListModel.GetEnvironmentInfoBySerializedNameSafe(environmentInfoName).environmentName;
                        }
                    }
                }

                if (diffData.additionalDifficultyData._warnings.Length > 0)
                {
                    foreach (var req in diffData.additionalDifficultyData._warnings)
                    {
                        ListData.Data.Add(new CustomCellInfo($"<size=75%>{req}", "Warning", WarningIcon));
                    }
                }

                if (diffData.additionalDifficultyData._information.Length > 0)
                {
                    foreach (var req in diffData.additionalDifficultyData._information)
                    {
                        ListData.Data.Add(new CustomCellInfo($"<size=75%>{req}", "Info", InfoIcon));
                    }
                }

                if (diffData.additionalDifficultyData._suggestions.Length > 0)
                {
                    foreach (var req in diffData.additionalDifficultyData._suggestions)
                    {
                        ListData.Data.Add(!Collections.capabilities.Contains(req)
                            ? new CustomCellInfo($"<size=75%>{req}", "Missing Suggestion", MissingSuggestionIcon)
                            : new CustomCellInfo($"<size=75%>{req}", "Suggestion", HaveSuggestionIcon));
                    }
                }

                if (diffData._oneSaber != null)
                {
                    var enabledText = _config.DisableOneSaberOverride ? "[<color=#ff5072>Disabled</color>]" : "[<color=#89ff89>Enabled</color>]";
                    var enabledSubtext = _config.DisableOneSaberOverride ? "enable" : "disable";
                    var saberCountText = diffData._oneSaber.Value ? "Forced One Saber" : "Forced Standard";
                    ListData.Data.Add(new CustomCellInfo($"<size=75%>{saberCountText} {enabledText}", $"Map changes saber count, click here to {enabledSubtext}.", diffData._oneSaber.Value ? OneSaberIcon : StandardIcon));
                }

                if (ListData.Data.Count > 0)
                {
                    if (environmentName == null && beatmapLevel != null)
                        environmentName = beatmapLevel.GetEnvironmentName(selectedKey.characteristic, selectedKey.difficulty);
                    ListData.Data.Add(new CustomCellInfo("<size=75%>Environment Info", $"This Map uses the Environment: {environmentName}", EnvironmentIcon));

                }
            }

            ListData.TableView.ReloadData();
            ListData.TableView.ScrollToCellWithIdx(0, TableView.ScrollPositionType.Beginning, false);
        }

        [UIAction("list-select")]
        private void Select(TableView _, int index)
        {
            ListData.TableView.ClearSelection();
            if (diffData != null)
            {
                var iconSelected = ListData.Data[index].Icon;
                if (iconSelected == ColorsIcon)
                {
                    modal.Hide(false, () => _colorsUI.ShowColors(diffData));
                }
                else if (iconSelected == StandardIcon || iconSelected == OneSaberIcon)
                {
                    _config.DisableOneSaberOverride = !_config.DisableOneSaberOverride;
                    modal.Hide(true);
                }
            }
        }

        internal void SetRainbowColors(bool shouldSet, bool firstPulse = true)
        {
            _tweeningManager.KillAllTweens(buttonBG);
            if (shouldSet)
            {
                var tween = new FloatTween(firstPulse ? 0 : 1, firstPulse ? 1 : 0, val =>
                {
                    buttonBG.color0 = new Color(1 - val, val, 0);
                    buttonBG.color1 = new Color(0, 1 - val, val);
                    buttonBG.SetAllDirty();
                }, 5f, EaseType.InOutSine);
                _tweeningManager.AddTween(tween, buttonBG);
                tween.onCompleted = delegate ()
                { SetRainbowColors(true, !firstPulse); };
            }
            else
            {
                buttonBG.color0 = originalColor0;
                buttonBG.color1 = originalColor1;
            }
        }
    }
}
