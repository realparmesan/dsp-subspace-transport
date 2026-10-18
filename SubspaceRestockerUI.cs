using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SubspaceTransport
{
    [HarmonyPatch(typeof(UIStorageWindow), "OnStorageIdChange")]
    internal static class SubspaceRestockerWindowOpenPatch
    {
        private static void Postfix(UIStorageWindow __instance)
        {
            SubspaceRestockerUI.Refresh(__instance);
        }
    }

    // Also re-locks the reserved slots straight after the filter menu's "clear filters" resets them.
    [HarmonyPatch(typeof(UIStorageWindow), "_OnUpdate")]
    internal static class SubspaceRestockerWindowUpdatePatch
    {
        private static void Postfix(UIStorageWindow __instance)
        {
            SubspaceRestockerUI.Refresh(__instance);
        }
    }

    // A status line in the depot window's title bar shows a Subspace Restocker's idle drones and Subspace Warpers.
    internal static class SubspaceRestockerUI
    {
        private const string StatusObjectName = "subspace-restocker-status";

        private static Text _status;

        internal static void Refresh(UIStorageWindow window)
        {
            if (window == null || window.titleText == null)
            {
                return;
            }

            StorageComponent storage = GetStorage(window);
            bool show = window.active && SubspaceRestockerManager.IsRestocker(window.factory, storage);
            if (!show)
            {
                if (_status != null)
                {
                    _status.gameObject.SetActive(false);
                }

                return;
            }

            if (_status == null && !CreateStatus(window))
            {
                return;
            }

            SubspaceRestockerManager.EnsureReservedSlots(storage);
            int drones = storage.GetItemCount(SubspaceRestockerContent.DroneItemId);
            int idle = Mathf.Max(0, drones - SubspaceRestockerManager.CountBusyDrones(window.factory, storage));
            int warpers = storage.GetItemCount(SubspaceWarperContent.ItemId);
            string text = "Subspace Drones " + idle + "/" + drones + " idle    Subspace Warpers " + warpers;
            if (_status.text != text)
            {
                _status.text = text;
            }

            _status.gameObject.SetActive(true);
        }

        private static bool CreateStatus(UIStorageWindow window)
        {
            Transform existing = window.titleText.transform.parent.Find(StatusObjectName);
            if (existing != null)
            {
                Object.Destroy(existing.gameObject);
            }

            GameObject statusObject = Object.Instantiate(window.titleText.gameObject, window.titleText.transform.parent);
            statusObject.name = StatusObjectName;
            Localizer localizer = statusObject.GetComponent<Localizer>();
            if (localizer != null)
            {
                Object.DestroyImmediate(localizer);
            }

            _status = statusObject.GetComponent<Text>();
            if (_status == null)
            {
                Object.Destroy(statusObject);
                Plugin.ModLogger.LogError("The depot window title has no Text component to copy.");
                return false;
            }

            // Right-aligned in the title bar, clear of the window's close button.
            RectTransform rect = (RectTransform)statusObject.transform;
            RectTransform title = (RectTransform)window.titleText.transform;
            rect.anchorMin = new Vector2(1f, title.anchorMin.y);
            rect.anchorMax = new Vector2(1f, title.anchorMax.y);
            rect.pivot = new Vector2(1f, title.pivot.y);
            rect.sizeDelta = new Vector2(360f, title.sizeDelta.y);
            rect.anchoredPosition = new Vector2(-50f, title.anchoredPosition.y);
            _status.alignment = TextAnchor.MiddleRight;
            _status.fontSize = Mathf.Max(12, _status.fontSize - 2);
            _status.horizontalOverflow = HorizontalWrapMode.Overflow;
            _status.color = new Color(0.78f, 0.6f, 1f, 0.9f);
            _status.raycastTarget = false;
            return true;
        }

        private static StorageComponent GetStorage(UIStorageWindow window)
        {
            StorageComponent[] pool = window.factoryStorage != null ? window.factoryStorage.storagePool : null;
            int id = window.storageId;
            if (pool == null || id <= 0 || id >= pool.Length || pool[id] == null || pool[id].id != id)
            {
                return null;
            }

            return pool[id];
        }
    }
}
