using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SubspaceTransport
{
    [HarmonyPatch(typeof(UIGame), "_OnCreate")]
    internal static class StationWindowUICreatePatch
    {
        private static void Postfix()
        {
            StationWindowUI.Initialize();
        }
    }

    [HarmonyPatch(typeof(UIStationWindow), "OnStationIdChange")]
    internal static class StationWindowUIStationChangePatch
    {
        private static void Postfix(UIStationWindow __instance)
        {
            StationWindowUI.Refresh(__instance);
        }
    }

    // Vanilla recomputes the window height and panel position every update, so the layout is reapplied after it.
    [HarmonyPatch(typeof(UIStationWindow), "_OnUpdate")]
    internal static class StationWindowUIUpdatePatch
    {
        private static void Postfix(UIStationWindow __instance)
        {
            StationWindowUI.Refresh(__instance);
        }
    }

    // Adds, for Interstellar Logistics Stations only:
    // - a "Subspace Transport" checkbox row in the Station Settings tab;
    // - a Subspace Warper slot on a new row beneath the drone, vessel and Space Warper slots.
    internal static class StationWindowUI
    {
        private const string RowObjectName = "wormhole-transport-setting";
        private const string SlotObjectName = "subspace-warper-box";

        // Vanilla rows in the settings tab are 20 px apart; the slot row matches the 64 px boxes plus spacing.
        private const float SettingsRowHeight = 20f;
        private const float SlotRowHeight = 70f;
        private const float LastSettingsRowTop = -156f;
        private const float SettingsRowControlHeight = 26f;

        private static UIStationWindow _window;
        private static RectTransform _row;
        private static UIButton _rowButton;
        private static Image _rowCheck;
        private static GameObject _slotBox;
        private static UIButton _slotButton;
        private static Image _slotIcon;
        private static Text _slotCountText;
        private static RectTransform[] _shifted;
        private static float[] _shiftedBaseY;
        private static bool _initializationWarningLogged;

        internal static void Initialize()
        {
            UIGame uiGame = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
            UIStationWindow window = uiGame != null ? uiGame.stationWindow : null;
            if (window == null
                || window.configGroup == null
                || window.panelDownTrans == null
                || window.warperBox == null
                || window.warperNecessaryButton == null)
            {
                if (!_initializationWarningLogged)
                {
                    Plugin.ModLogger.LogWarning("Could not initialize the Subspace Logistics station window controls yet.");
                    _initializationWarningLogged = true;
                }

                return;
            }

            if (_window == window && _row != null && _slotBox != null)
            {
                return;
            }

            DestroyChild(window.configGroup, RowObjectName);
            DestroyChild(window.panelDownTrans, SlotObjectName);

            if (!CreateRow(window) || !CreateSlot(window))
            {
                return;
            }

            // Everything in the lower panel below the slot boxes moves down to make room for the slot row.
            _shifted = new[]
            {
                (RectTransform)window.stationSettingTabBtn.transform,
                (RectTransform)window.prioritySettingTabBtn.transform,
                window.controlPanelBtnRt,
                window.configGroup,
                window.priorityGroup
            };
            _shiftedBaseY = new float[_shifted.Length];
            for (int i = 0; i < _shifted.Length; i++)
            {
                _shiftedBaseY[i] = _shifted[i].anchoredPosition.y;
            }

            _window = window;
            _initializationWarningLogged = false;
        }

        internal static void Refresh(UIStationWindow window)
        {
            if (window == null)
            {
                return;
            }

            if (_window != window || _row == null || _slotBox == null)
            {
                Initialize();
                if (_window != window || _row == null || _slotBox == null)
                {
                    return;
                }
            }

            StationComponent station = GetCurrentStation(window);
            bool show = window.active
                && StationSettings.IsWormholeStation(station)
                && SubspaceLogisticsTechnology.IsUnlocked;
            _row.gameObject.SetActive(show);
            _slotBox.SetActive(show);

            float slotShift = show ? SlotRowHeight : 0f;
            for (int i = 0; i < _shifted.Length; i++)
            {
                Vector2 position = _shifted[i].anchoredPosition;
                _shifted[i].anchoredPosition = new Vector2(position.x, _shiftedBaseY[i] - slotShift);
            }

            if (!show)
            {
                return;
            }

            bool settingsTab = window.configGroup.gameObject.activeSelf;
            float extra = slotShift + (settingsTab ? SettingsRowHeight : 0f);
            window.windowTrans.sizeDelta = new Vector2(
                window.windowTrans.sizeDelta.x,
                window.windowTrans.sizeDelta.y + extra);
            window.panelDownTrans.anchoredPosition = new Vector2(
                window.panelDownTrans.anchoredPosition.x,
                window.panelDownTrans.anchoredPosition.y + extra);

            // The pile-sorter row is only present once its research is unlocked.
            float rowTop = LastSettingsRowTop
                - (window.minPilerGroup.gameObject.activeSelf ? SettingsRowHeight : 0f);
            _row.anchoredPosition = new Vector2(35f, rowTop - SettingsRowControlHeight / 2f);
            _rowCheck.enabled = StationSettings.IsEnabled(station);

            if (_slotIcon.sprite == null)
            {
                ItemProto item = LDB.items.Select(SubspaceWarperContent.ItemId);
                _slotIcon.sprite = item != null ? item.iconSprite : null;
            }

            string count = StationSettings.GetSubspaceWarperCount(station).ToString();
            if (_slotCountText.text != count)
            {
                _slotCountText.text = count;
            }
        }

        private static bool CreateRow(UIStationWindow window)
        {
            GameObject rowObject = Object.Instantiate(window.warperNecessaryButton.gameObject);
            rowObject.name = RowObjectName;
            _row = (RectTransform)rowObject.transform;
            _row.SetParent(window.configGroup, false);
            _row.anchorMin = Vector2.up;
            _row.anchorMax = Vector2.up;
            _row.pivot = new Vector2(0.5f, 0.5f);

            _rowButton = rowObject.GetComponent<UIButton>();
            Transform checkTransform = _row.Find("checked");
            _rowCheck = checkTransform != null ? checkTransform.GetComponent<Image>() : null;
            Transform labelTransform = _row.Find("label");
            Text label = labelTransform != null ? labelTransform.GetComponent<Text>() : null;
            if (_rowButton == null || _rowCheck == null || label == null)
            {
                Object.Destroy(rowObject);
                _row = null;
                Plugin.ModLogger.LogError("The station settings checkbox template is missing its button, check, or label.");
                return false;
            }

            Localizer localizer = labelTransform.GetComponent<Localizer>();
            if (localizer != null)
            {
                Object.DestroyImmediate(localizer);
            }

            label.text = "Subspace Transport";
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            ((RectTransform)labelTransform).sizeDelta = new Vector2(200f, 20f);

            _rowButton.tips.tipTitle = "Subspace Transport";
            _rowButton.tips.tipText =
                "Transfer cargo instantly with other enabled Interstellar Logistics Stations. " +
                "Each transfer uses one Subspace Warper from the sending station, or from the receiving station " +
                "if the sender has none.";
            _rowButton.onClick += OnRowClicked;
            return true;
        }

        private static bool CreateSlot(UIStationWindow window)
        {
            _slotBox = Object.Instantiate(window.warperBox);
            _slotBox.name = SlotObjectName;
            RectTransform rect = (RectTransform)_slotBox.transform;
            rect.SetParent(window.panelDownTrans, false);
            RectTransform droneBox = (RectTransform)window.droneBox.transform;
            rect.anchoredPosition = new Vector2(droneBox.anchoredPosition.x, droneBox.anchoredPosition.y - SlotRowHeight);

            _slotButton = _slotBox.GetComponentInChildren<UIButton>(true);
            Transform buttonTransform = _slotButton != null ? _slotButton.transform : null;
            Transform iconTransform = buttonTransform != null ? buttonTransform.Find("icon") : null;
            Transform countTransform = buttonTransform != null ? buttonTransform.Find("count-text") : null;
            _slotIcon = iconTransform != null ? iconTransform.GetComponent<Image>() : null;
            _slotCountText = countTransform != null ? countTransform.GetComponent<Text>() : null;
            if (_slotButton == null || _slotIcon == null || _slotCountText == null)
            {
                Object.Destroy(_slotBox);
                _slotBox = null;
                Plugin.ModLogger.LogError("The Space Warper slot template is missing its button, icon, or count text.");
                return false;
            }

            _slotIcon.sprite = null;
            _slotButton.tips.itemId = SubspaceWarperContent.ItemId;
            _slotButton.tips.itemInc = 0;
            _slotButton.tips.itemCount = 0;
            _slotButton.tips.type = UIButton.ItemTipType.Other;
            _slotButton.onClick += OnSlotClicked;
            return true;
        }

        private static void OnRowClicked(int _)
        {
            StationComponent station = GetCurrentStation(_window);
            if (!StationSettings.IsWormholeStation(station) || !SubspaceLogisticsTechnology.IsUnlocked)
            {
                return;
            }

            bool enabled = !StationSettings.IsEnabled(station);
            StationSettings.SetEnabled(station, enabled);
            _rowCheck.enabled = enabled;
        }

        // Same hand interactions as vanilla's Space Warper slot.
        private static void OnSlotClicked(int _)
        {
            StationComponent station = GetCurrentStation(_window);
            Player player = _window != null ? _window.player : null;
            if (player == null || !StationSettings.IsWormholeStation(station))
            {
                return;
            }

            int itemId = SubspaceWarperContent.ItemId;
            if (player.inhandItemId > 0 && player.inhandItemCount == 0)
            {
                player.SetHandItems(0, 0);
            }
            else if (player.inhandItemId > 0 && player.inhandItemCount > 0)
            {
                if (player.inhandItemId != itemId)
                {
                    UIRealtimeTip.Popup("只能放入".Translate() + LDB.items.Select(itemId).name);
                    return;
                }

                int space = StationSettings.SubspaceWarperMaxCount - StationSettings.GetSubspaceWarperCount(station);
                int added = Mathf.Min(player.inhandItemCount, Mathf.Max(space, 0));
                if (added <= 0)
                {
                    UIRealtimeTip.Popup("栏位已满".Translate());
                    return;
                }

                int handCount = player.inhandItemCount;
                int handInc = player.inhandItemInc;
                int removedInc = handCount > 0 ? handInc / handCount * added : 0;
                StationSettings.AddSubspaceWarpers(station, added);
                player.AddHandItemCount_Unsafe(-added);
                player.SetHandItemInc_Unsafe(handInc - removedInc);
                if (player.inhandItemCount <= 0)
                {
                    player.SetHandItemId_Unsafe(0);
                    player.SetHandItemCount_Unsafe(0);
                    player.SetHandItemInc_Unsafe(0);
                }
            }
            else if (player.inhandItemId == 0 && player.inhandItemCount == 0)
            {
                int count = StationSettings.TakeAllSubspaceWarpers(station);
                if (count <= 0)
                {
                    return;
                }

                if (VFInput.shift || VFInput.control)
                {
                    int upCount = player.TryAddItemToPackage(itemId, count, 0, false);
                    UIItemup.Up(itemId, upCount);
                    if (upCount < count)
                    {
                        StationSettings.AddSubspaceWarpers(station, count - upCount);
                    }
                }
                else
                {
                    player.SetHandItemId_Unsafe(itemId);
                    player.SetHandItemCount_Unsafe(count);
                    player.SetHandItemInc_Unsafe(0);
                }
            }
        }

        private static void DestroyChild(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                Object.Destroy(existing.gameObject);
            }
        }

        private static StationComponent GetCurrentStation(UIStationWindow stationWindow)
        {
            if (stationWindow == null || stationWindow.transport == null
                || stationWindow.transport.stationPool == null
                || stationWindow.stationId <= 0
                || stationWindow.stationId >= stationWindow.transport.stationPool.Length)
            {
                return null;
            }

            StationComponent station = stationWindow.transport.stationPool[stationWindow.stationId];
            return station != null && station.id == stationWindow.stationId ? station : null;
        }
    }
}
