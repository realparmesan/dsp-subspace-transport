using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SubspaceTransport
{
    [HarmonyPatch(typeof(UIStationWindow), "OnStationIdChange")]
    internal static class SubspaceRestockerWindowOpenPatch
    {
        private static void Postfix(UIStationWindow __instance)
        {
            SubspaceRestockerUI.OnStationChanged(__instance);
        }
    }

    [HarmonyPatch(typeof(UIStationWindow), "_OnUpdate")]
    internal static class SubspaceRestockerWindowUpdatePatch
    {
        private static void Postfix(UIStationWindow __instance)
        {
            SubspaceRestockerUI.Update(__instance);
        }
    }

    [HarmonyPatch(typeof(UIStationWindow), "_OnClose")]
    internal static class SubspaceRestockerWindowClosePatch
    {
        private static void Postfix(UIStationWindow __instance)
        {
            SubspaceRestockerUI.Deactivate(__instance);
        }
    }

    // In a Subspace Restocker's station window, the vessel and Space Warper boxes, which planetary stations leave
    // empty, hold Subspace Drones and Subspace Warpers instead.
    internal static class SubspaceRestockerUI
    {
        // The power group's width and energy bar length when an Interstellar Logistics Station shows its warper box.
        private const float PowerGroupWidth = 320f;
        private const float EnergyBarLength = 180f;

        private const int VesselItemId = 5002;
        private const int SpaceWarperItemId = 1210;

        private static UIStationWindow _window;
        private static bool _active;

        internal static void OnStationChanged(UIStationWindow window)
        {
            StationComponent station = window.active ? StationWindowUI.GetCurrentStation(window) : null;
            if (!SubspaceRestockerManager.IsRestocker(station))
            {
                Deactivate(window);
                return;
            }

            if (_window != window)
            {
                window.shipIconButton.onClick += OnDroneSlotClicked;
                window.warperIconButton.onClick += OnWarperSlotClicked;
                _window = window;
            }

            SetSlotIcons(window, SubspaceRestockerContent.DroneItemId, SubspaceWarperContent.ItemId);
            _active = true;
            ApplyLayout(window, station);
        }

        internal static void Update(UIStationWindow window)
        {
            if (!_active || window != _window)
            {
                return;
            }

            StationComponent station = StationWindowUI.GetCurrentStation(window);
            if (!SubspaceRestockerManager.IsRestocker(station))
            {
                Deactivate(window);
                return;
            }

            ApplyLayout(window, station);
        }

        // Vanilla sets the boxes and power group when the station changes, so the next station's are already right.
        internal static void Deactivate(UIStationWindow window)
        {
            if (!_active || window != _window)
            {
                return;
            }

            _active = false;
            SetSlotIcons(window, VesselItemId, SpaceWarperItemId);
        }

        // Vanilla sets some of these every update, so they are reapplied after it.
        private static void ApplyLayout(UIStationWindow window, StationComponent station)
        {
            window.shipIconButton.gameObject.SetActive(true);
            window.warperIconButton.gameObject.SetActive(true);
            window.shipAutoReplenishButton.gameObject.SetActive(false);
            window.powerGroupRect.sizeDelta = new Vector2(PowerGroupWidth, window.powerGroupRect.sizeDelta.y);

            float charge = station.energyMax > 0 ? (float)((double)station.energy / station.energyMax) : 0f;
            float energyX = charge > 0.7f ? EnergyBarLength * charge - 30f : EnergyBarLength * charge + 30f;
            window.energyText.rectTransform.anchoredPosition = new Vector2(Mathf.Round(energyX), 0f);

            int drones = StationSettings.GetSubspaceDroneCount(station);
            int idle = Mathf.Max(0, drones - SubspaceRestockerManager.CountBusyDrones(station));
            SetText(window.shipCountText, idle + "/" + drones);
            SetText(window.warperCountText, StationSettings.GetSubspaceWarperCount(station).ToString());
        }

        private static void SetSlotIcons(UIStationWindow window, int shipSlotItemId, int warperSlotItemId)
        {
            ItemProto shipSlotItem = LDB.items.Select(shipSlotItemId);
            ItemProto warperSlotItem = LDB.items.Select(warperSlotItemId);
            window.shipIconImage.sprite = shipSlotItem != null ? shipSlotItem.iconSprite : null;
            window.warperIconImage.sprite = warperSlotItem != null ? warperSlotItem.iconSprite : null;
            window.shipIconButton.tips.itemId = shipSlotItemId;
            window.warperIconButton.tips.itemId = warperSlotItemId;
        }

        private static void SetText(Text text, string value)
        {
            if (text.text != value)
            {
                text.text = value;
            }
        }

        // Vanilla's own handlers ignore planetary stations, so these only act on restockers.
        private static void OnDroneSlotClicked(int _)
        {
            StationComponent station = StationWindowUI.GetCurrentStation(_window);
            Player player = _window != null ? _window.player : null;
            if (!_active || player == null || !SubspaceRestockerManager.IsRestocker(station))
            {
                return;
            }

            // Only idle drones can be taken out; drones in flight still count against the slot.
            StationWindowUI.ClickSlot(
                player,
                SubspaceRestockerContent.DroneItemId,
                SubspaceRestockerContent.DroneSlotCapacity - StationSettings.GetSubspaceDroneCount(station),
                count => StationSettings.AddSubspaceDrones(station, count),
                () => StationSettings.TakeSubspaceDrones(
                    station,
                    StationSettings.GetSubspaceDroneCount(station) - SubspaceRestockerManager.CountBusyDrones(station)));
        }

        private static void OnWarperSlotClicked(int _)
        {
            StationComponent station = StationWindowUI.GetCurrentStation(_window);
            Player player = _window != null ? _window.player : null;
            if (!_active || player == null || !SubspaceRestockerManager.IsRestocker(station))
            {
                return;
            }

            StationWindowUI.ClickSlot(
                player,
                SubspaceWarperContent.ItemId,
                StationSettings.SubspaceWarperMaxCount - StationSettings.GetSubspaceWarperCount(station),
                count => StationSettings.AddSubspaceWarpers(station, count),
                () => StationSettings.TakeAllSubspaceWarpers(station));
        }
    }
}
