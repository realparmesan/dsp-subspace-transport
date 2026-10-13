using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SubspaceTransport
{
    // The station list entries are instantiated from templates whose heights are read in _OnCreate,
    // so the Subspace Warper row is added to the templates before that.
    [HarmonyPatch(typeof(UIControlPanelWindow), "_OnCreate")]
    internal static class ControlPanelWindowCreatePatch
    {
        private static void Prefix(UIControlPanelWindow __instance)
        {
            if (__instance.objectEntryPrefabs == null)
            {
                return;
            }

            foreach (UIControlPanelObjectEntry prefab in __instance.objectEntryPrefabs)
            {
                UIControlPanelStationEntry stationEntry = prefab as UIControlPanelStationEntry;
                if (stationEntry != null)
                {
                    ControlPanelUI.AddEntryRow(stationEntry);
                }
            }
        }
    }

    [HarmonyPatch(typeof(UIControlPanelStationEntry), "_OnUpdate")]
    internal static class ControlPanelStationEntryUpdatePatch
    {
        private static void Postfix(UIControlPanelStationEntry __instance)
        {
            ControlPanelUI.UpdateEntry(__instance);
        }
    }

    [HarmonyPatch(typeof(UIControlPanelStationInspector), "_OnCreate")]
    internal static class ControlPanelInspectorCreatePatch
    {
        private static void Postfix(UIControlPanelStationInspector __instance)
        {
            ControlPanelUI.CreateInspectorControls(__instance);
        }
    }

    [HarmonyPatch(typeof(UIControlPanelStationInspector), "OnStationIdChange")]
    internal static class ControlPanelInspectorStationChangePatch
    {
        private static void Postfix(UIControlPanelStationInspector __instance)
        {
            ControlPanelUI.RefreshInspector(__instance);
        }
    }

    [HarmonyPatch(typeof(UIControlPanelStationInspector), "_OnUpdate")]
    internal static class ControlPanelInspectorUpdatePatch
    {
        private static void Postfix(UIControlPanelStationInspector __instance)
        {
            ControlPanelUI.RefreshInspector(__instance);
        }
    }

    // Adds to the Control Panel, for Interstellar Logistics Stations only:
    // - a Subspace Warper row beneath the drone, vessel and Space Warper rows of each station list entry;
    // - the same row beneath the delivery icons in the station inspector's info tab;
    // - the Wormhole Transport checkbox under the outgoing integration count row of the settings tab.
    // The checkbox reads and writes the same StationSettings value as the station window's row.
    internal static class ControlPanelUI
    {
        private const string EntryRowName = "subspace-warper-group";
        private const string InspectorRowName = "subspace-warper-delivery";
        private const string InspectorSettingName = "wormhole-transport-setting";

        // Matches the vanilla settings rows, which are 20 px apart.
        private const float SettingsRowHeight = 20f;
        private const float RowSpacing = 4f;

        private sealed class DeliveryRow
        {
            internal GameObject Root;
            internal UIButton Button;
            internal Image Icon;
            internal Text Count;
        }

        private static readonly ConditionalWeakTable<UIControlPanelStationEntry, DeliveryRow> EntryRows =
            new ConditionalWeakTable<UIControlPanelStationEntry, DeliveryRow>();

        // Paths from the warper group to its icon, count text and button, found on the template.
        private static string _entryIconPath;
        private static string _entryCountPath;
        private static string _entryButtonPath;

        private static UIControlPanelStationInspector _inspector;
        private static DeliveryRow _inspectorRow;
        private static RectTransform _settingRow;
        private static Image _settingCheck;

        internal static void AddEntryRow(UIControlPanelStationEntry template)
        {
            if (template.warperGroupGo == null
                || template.shipGroupGo == null
                || template.droneGroupGo == null
                || template.warperIconImage == null
                || template.warperCountText == null
                || template.warperIconButton == null)
            {
                Plugin.ModLogger.LogWarning("The Control Panel station entry is missing its delivery rows.");
                return;
            }

            Transform group = template.warperGroupGo.transform;
            if (group.parent.Find(EntryRowName) != null)
            {
                return;
            }

            _entryIconPath = GetRelativePath(group, template.warperIconImage.transform);
            _entryCountPath = GetRelativePath(group, template.warperCountText.transform);
            _entryButtonPath = GetRelativePath(group, template.warperIconButton.transform);
            if (_entryIconPath == null || _entryCountPath == null || _entryButtonPath == null)
            {
                Plugin.ModLogger.LogWarning(
                    "The Control Panel Space Warper icon is not inside its row, so no Subspace Warper row was added.");
                return;
            }

            GameObject row = Object.Instantiate(template.warperGroupGo, group.parent, false);
            row.name = EntryRowName;
            PlaceAfter(
                (RectTransform)row.transform,
                (RectTransform)template.droneGroupGo.transform,
                (RectTransform)template.shipGroupGo.transform,
                (RectTransform)group);
            GrowToFit(
                template.rectTransform,
                (RectTransform)row.transform,
                (RectTransform)template.droneGroupGo.transform);
        }

        internal static void UpdateEntry(UIControlPanelStationEntry entry)
        {
            DeliveryRow row = GetEntryRow(entry);
            if (row == null)
            {
                return;
            }

            StationComponent station = entry.isTargetDataValid ? entry.station : null;
            bool show = SubspaceLogisticsTechnology.IsUnlocked && StationSettings.IsWormholeStation(station);
            if (row.Root.activeSelf != show)
            {
                row.Root.SetActive(show);
            }

            if (show)
            {
                row.Icon.color = entry.warperIconImage.color;
                row.Count.color = entry.warperCountText.color;
                UpdateDeliveryRow(row, station);
            }
        }

        internal static void CreateInspectorControls(UIControlPanelStationInspector inspector)
        {
            _inspector = inspector;
            _inspectorRow = null;
            _settingRow = null;
            _settingCheck = null;

            if (inspector.warperGo != null
                && inspector.shipGo != null
                && inspector.droneGo != null
                && inspector.warperIconImage != null
                && inspector.warperCountText != null
                && inspector.warperIconButton != null)
            {
                _inspectorRow = CreateInspectorRow(inspector);
            }
            else
            {
                Plugin.ModLogger.LogWarning("The Control Panel station inspector is missing its delivery icons.");
            }

            if (inspector.warperNecessaryGroup != null
                && inspector.warperNecessaryButton != null
                && inspector.warperNecessaryCheck != null
                && inspector.minPilerGroup != null)
            {
                CreateInspectorSetting(inspector);
            }
            else
            {
                Plugin.ModLogger.LogWarning("The Control Panel station inspector is missing its settings rows.");
            }
        }

        internal static void RefreshInspector(UIControlPanelStationInspector inspector)
        {
            if (inspector != _inspector)
            {
                return;
            }

            StationComponent station = GetInspectorStation(inspector);
            bool show = inspector.active
                && SubspaceLogisticsTechnology.IsUnlocked
                && StationSettings.IsWormholeStation(station);

            if (_inspectorRow != null)
            {
                if (_inspectorRow.Root.activeSelf != show)
                {
                    _inspectorRow.Root.SetActive(show);
                }

                if (show)
                {
                    UpdateDeliveryRow(_inspectorRow, station);
                }
            }

            if (_settingRow != null)
            {
                if (_settingRow.gameObject.activeSelf != show)
                {
                    _settingRow.gameObject.SetActive(show);
                }

                if (show)
                {
                    // The outgoing integration count row is only present once its research is unlocked.
                    RectTransform piler = inspector.minPilerGroup;
                    float y = piler.gameObject.activeSelf
                        ? piler.anchoredPosition.y - SettingsRowHeight
                        : piler.anchoredPosition.y;
                    _settingRow.anchoredPosition = new Vector2(piler.anchoredPosition.x, y);
                    _settingCheck.enabled = StationSettings.IsEnabled(station);
                }
            }
        }

        private static DeliveryRow CreateInspectorRow(UIControlPanelStationInspector inspector)
        {
            Transform group = inspector.warperGo.transform;
            Transform existing = group.parent.Find(InspectorRowName);
            if (existing != null)
            {
                Object.Destroy(existing.gameObject);
            }

            string iconPath = GetRelativePath(group, inspector.warperIconImage.transform);
            string countPath = GetRelativePath(group, inspector.warperCountText.transform);
            string buttonPath = GetRelativePath(group, inspector.warperIconButton.transform);
            if (iconPath == null || countPath == null || buttonPath == null)
            {
                Plugin.ModLogger.LogWarning(
                    "The Control Panel inspector's Space Warper icon is not inside its group, " +
                    "so no Subspace Warper row was added.");
                return null;
            }

            GameObject clone = Object.Instantiate(inspector.warperGo, group.parent, false);
            clone.name = InspectorRowName;
            PlaceAfter(
                (RectTransform)clone.transform,
                (RectTransform)inspector.droneGo.transform,
                (RectTransform)inspector.shipGo.transform,
                (RectTransform)group);

            DeliveryRow row = BindDeliveryRow(clone, iconPath, countPath, buttonPath);
            if (row == null)
            {
                Object.Destroy(clone);
                return null;
            }

            // Vanilla hides the Space Warper button until warp drive research; this row has its own condition.
            row.Button.gameObject.SetActive(true);
            clone.SetActive(false);
            return row;
        }

        private static void CreateInspectorSetting(UIControlPanelStationInspector inspector)
        {
            Transform template = inspector.warperNecessaryGroup;
            Transform existing = template.parent.Find(InspectorSettingName);
            if (existing != null)
            {
                Object.Destroy(existing.gameObject);
            }

            string buttonPath = GetRelativePath(template, inspector.warperNecessaryButton.transform);
            string checkPath = GetRelativePath(template, inspector.warperNecessaryCheck.transform);
            if (buttonPath == null || checkPath == null)
            {
                Plugin.ModLogger.LogWarning(
                    "The Control Panel's Warpers Required checkbox is not inside its row, " +
                    "so no Wormhole Transport row was added.");
                return;
            }

            GameObject rowObject = Object.Instantiate(template.gameObject, template.parent, false);
            rowObject.name = InspectorSettingName;
            RectTransform row = (RectTransform)rowObject.transform;
            RectTransform piler = inspector.minPilerGroup;
            row.anchorMin = piler.anchorMin;
            row.anchorMax = piler.anchorMax;
            row.pivot = piler.pivot;

            UIButton button = FindComponent<UIButton>(row, buttonPath);
            Image check = FindComponent<Image>(row, checkPath);
            Text[] labels = rowObject.GetComponentsInChildren<Text>(true);
            if (button == null || check == null || labels.Length == 0)
            {
                Object.Destroy(rowObject);
                Plugin.ModLogger.LogError("The Control Panel checkbox template is missing its button, check, or label.");
                return;
            }

            foreach (Text label in labels)
            {
                Localizer localizer = label.GetComponent<Localizer>();
                if (localizer != null)
                {
                    Object.DestroyImmediate(localizer);
                }
            }

            labels[0].text = "Wormhole Transport";
            labels[0].horizontalOverflow = HorizontalWrapMode.Overflow;

            button.tips.tipTitle = "Wormhole Transport";
            button.tips.tipText =
                "Transfer cargo instantly with other enabled Interstellar Logistics Stations. " +
                "Each transfer uses one Subspace Warper from the sending station, or from the receiving station " +
                "if the sender has none.";
            button.onClick += OnSettingClicked;

            _settingRow = row;
            _settingCheck = check;
            rowObject.SetActive(false);
        }

        private static void OnSettingClicked(int _)
        {
            StationComponent station = GetInspectorStation(_inspector);
            if (!StationSettings.IsWormholeStation(station) || !SubspaceLogisticsTechnology.IsUnlocked)
            {
                return;
            }

            bool enabled = !StationSettings.IsEnabled(station);
            StationSettings.SetEnabled(station, enabled);
            _settingCheck.enabled = enabled;
        }

        private static DeliveryRow GetEntryRow(UIControlPanelStationEntry entry)
        {
            DeliveryRow row;
            if (EntryRows.TryGetValue(entry, out row))
            {
                return row.Root != null ? row : null;
            }

            Transform rowTransform = entry.warperGroupGo != null
                ? entry.warperGroupGo.transform.parent.Find(EntryRowName)
                : null;
            row = rowTransform != null
                ? BindDeliveryRow(rowTransform.gameObject, _entryIconPath, _entryCountPath, _entryButtonPath)
                : null;
            if (row == null)
            {
                // Remember the failure so the lookup isn't repeated every frame.
                row = new DeliveryRow();
            }

            EntryRows.Add(entry, row);
            return row.Root != null ? row : null;
        }

        private static DeliveryRow BindDeliveryRow(GameObject root, string iconPath, string countPath, string buttonPath)
        {
            if (iconPath == null || countPath == null || buttonPath == null)
            {
                return null;
            }

            DeliveryRow row = new DeliveryRow
            {
                Root = root,
                Icon = FindComponent<Image>(root.transform, iconPath),
                Count = FindComponent<Text>(root.transform, countPath),
                Button = FindComponent<UIButton>(root.transform, buttonPath)
            };
            if (row.Icon == null || row.Count == null || row.Button == null)
            {
                Plugin.ModLogger.LogWarning("Could not find the icon, count, or button in a Subspace Warper row.");
                return null;
            }

            row.Icon.sprite = null;
            row.Button.tips.itemId = SubspaceWarperContent.ItemId;
            row.Button.tips.itemInc = 0;
            row.Button.tips.itemCount = 0;
            row.Button.tips.type = UIButton.ItemTipType.Other;
            return row;
        }

        private static void UpdateDeliveryRow(DeliveryRow row, StationComponent station)
        {
            if (row.Icon.sprite == null)
            {
                ItemProto item = LDB.items.Select(SubspaceWarperContent.ItemId);
                row.Icon.sprite = item != null ? item.iconSprite : null;
            }

            string count = StationSettings.GetSubspaceWarperCount(station).ToString();
            if (row.Count.text != count)
            {
                row.Count.text = count;
            }
        }

        // Places row as the next item after drone, ship and warper. Vertically stacked rows continue the stack;
        // side-by-side icons get a new row beneath the first one.
        private static void PlaceAfter(RectTransform row, RectTransform drone, RectTransform ship, RectTransform warper)
        {
            LayoutElement layout = row.GetComponent<LayoutElement>();
            if (layout == null)
            {
                layout = row.gameObject.AddComponent<LayoutElement>();
            }

            layout.ignoreLayout = true;

            Vector2 step = warper.anchoredPosition - ship.anchoredPosition;
            if (Mathf.Abs(step.y) >= 1f && Mathf.Abs(step.y) >= Mathf.Abs(step.x))
            {
                row.anchoredPosition = warper.anchoredPosition + new Vector2(0f, step.y);
            }
            else
            {
                float height = Mathf.Max(drone.rect.height, warper.rect.height);
                row.anchoredPosition = new Vector2(
                    drone.anchoredPosition.x,
                    drone.anchoredPosition.y - height - RowSpacing);
            }
        }

        // Extends the entry downward by the distance the new row sits below the drone row. The template's world
        // corners aren't laid out yet when this runs, so this uses the rows' own positions and heights.
        private static void GrowToFit(RectTransform entry, RectTransform row, RectTransform drone)
        {
            float extra = drone.anchoredPosition.y - row.anchoredPosition.y;
            if (extra > 0f)
            {
                entry.sizeDelta = new Vector2(entry.sizeDelta.x, entry.sizeDelta.y + Mathf.Ceil(extra));
            }
        }

        private static StationComponent GetInspectorStation(UIControlPanelStationInspector inspector)
        {
            PlanetTransport transport = inspector != null ? inspector.transport : null;
            if (transport?.stationPool == null
                || inspector.stationId <= 0
                || inspector.stationId >= transport.stationPool.Length)
            {
                return null;
            }

            StationComponent station = transport.stationPool[inspector.stationId];
            return station != null && station.id == inspector.stationId ? station : null;
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (target == root)
            {
                return string.Empty;
            }

            string path = target.name;
            for (Transform current = target.parent; current != null; current = current.parent)
            {
                if (current == root)
                {
                    return path;
                }

                path = current.name + "/" + path;
            }

            return null;
        }

        private static T FindComponent<T>(Transform root, string path) where T : Component
        {
            Transform target = path.Length == 0 ? root : root.Find(path);
            return target != null ? target.GetComponent<T>() : null;
        }
    }
}
