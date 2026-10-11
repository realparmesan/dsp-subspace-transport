using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SubspaceTransport
{
    [HarmonyPatch(typeof(UIGame), "_OnCreate")]
    internal static class StationToggleUICreatePatch
    {
        private static void Postfix()
        {
            StationToggleUI.Initialize();
            BatterySphereSiloUI.Initialize();
        }
    }

    [HarmonyPatch(typeof(UIStationWindow), "OnStationIdChange")]
    internal static class StationToggleUIStationChangePatch
    {
        private static void Postfix(UIStationWindow __instance)
        {
            StationToggleUI.Refresh(__instance);
        }
    }

    [HarmonyPatch(typeof(UIStationWindow), "_OnUpdate")]
    internal static class StationToggleUIUpdatePatch
    {
        private static void Postfix(UIStationWindow __instance)
        {
            StationToggleUI.Refresh(__instance);
        }
    }

    internal static class StationToggleUI
    {
        private const string ButtonObjectName = "subspace-transport-station-toggle";
        private static UIButton _button;
        private static Text _buttonText;
        private static bool _initializationWarningLogged;

        internal static void Initialize()
        {
            UIGame uiGame = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
            UIStationWindow stationWindow = uiGame != null ? uiGame.stationWindow : null;
            UIAssemblerWindow assemblerWindow = uiGame != null ? uiGame.assemblerWindow : null;
            if (stationWindow == null || stationWindow.windowTrans == null
                || assemblerWindow == null || assemblerWindow.copyButton == null)
            {
                if (!_initializationWarningLogged)
                {
                    Plugin.ModLogger.LogWarning("Could not initialize the Wormhole Logistics station toggle yet.");
                    _initializationWarningLogged = true;
                }

                return;
            }

            Transform existingButton = stationWindow.windowTrans.Find(ButtonObjectName);
            if (existingButton != null)
            {
                _button = existingButton.GetComponent<UIButton>();
                _buttonText = existingButton.Find("Text")?.GetComponent<Text>();
                if (_button != null && _buttonText != null)
                {
                    _button.onClick -= OnToggleClicked;
                    _button.onClick += OnToggleClicked;
                    _initializationWarningLogged = false;
                    Refresh(stationWindow);
                    return;
                }
            }

            GameObject buttonObject = Object.Instantiate(assemblerWindow.copyButton.gameObject);
            buttonObject.name = ButtonObjectName;
            _button = buttonObject.GetComponent<UIButton>();
            Transform textTransform = buttonObject.transform.Find("Text");
            _buttonText = textTransform != null ? textTransform.GetComponent<Text>() : null;
            if (_button == null || _buttonText == null)
            {
                Object.Destroy(buttonObject);
                _button = null;
                _buttonText = null;
                Plugin.ModLogger.LogError("The station toggle button template is missing its button or text component.");
                return;
            }

            Localizer localizer = textTransform.GetComponent<Localizer>();
            if (localizer != null)
            {
                Object.DestroyImmediate(localizer);
            }

            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.SetParent(stationWindow.windowTrans, false);
            rect.anchorMax = Vector2.up;
            rect.anchorMin = Vector2.up;
            rect.pivot = Vector2.up;
            rect.sizeDelta = new Vector2(104f, 22f);
            rect.anchoredPosition = new Vector2(414f, -60f);

            _button.tips.tipTitle = "Wormhole Logistics";
            _button.tips.tipText = "Enable instant transfers at this station. Both endpoint stations must be enabled.";
            _button.onClick -= OnToggleClicked;
            _button.onClick += OnToggleClicked;
            _initializationWarningLogged = false;

            Refresh(stationWindow);
        }

        internal static void Refresh(UIStationWindow stationWindow)
        {
            if (_button == null)
            {
                Initialize();
                if (_button == null)
                {
                    return;
                }
            }

            StationComponent station = GetCurrentStation(stationWindow);
            bool available = station != null
                && station.isStellar
                && SubspaceLogisticsTechnology.CurrentLevel > 0;
            _button.gameObject.SetActive(available);
            if (!available)
            {
                return;
            }

            bool enabled = StationSettings.IsEnabled(station);
            string label = enabled ? "Wormhole: ON" : "Wormhole: OFF";
            if (_buttonText != null && _buttonText.text != label)
            {
                _buttonText.text = label;
            }
        }

        private static void OnToggleClicked(int _)
        {
            UIGame uiGame = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
            UIStationWindow stationWindow = uiGame != null ? uiGame.stationWindow : null;
            StationComponent station = GetCurrentStation(stationWindow);
            if (station == null || !station.isStellar || SubspaceLogisticsTechnology.CurrentLevel <= 0)
            {
                Refresh(stationWindow);
                return;
            }

            StationSettings.Toggle(station);
            Refresh(stationWindow);
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

            return stationWindow.transport.stationPool[stationWindow.stationId];
        }
    }
}
