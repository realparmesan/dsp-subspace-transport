using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SubspaceTransport
{
    internal static class BatterySphereSiloUI
    {
        private const string ButtonObjectName = "wormhole-battery-sphere-rocket-toggle";
        private static UIButton _button;
        private static Text _buttonText;

        internal static void Initialize()
        {
            UIGame uiGame = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
            UISiloWindow siloWindow = uiGame != null ? uiGame.siloWindow : null;
            UIAssemblerWindow assemblerWindow = uiGame != null ? uiGame.assemblerWindow : null;
            if (siloWindow == null || siloWindow.windowTrans == null
                || assemblerWindow == null || assemblerWindow.copyButton == null)
            {
                return;
            }

            Transform existingButton = siloWindow.windowTrans.Find(ButtonObjectName);
            if (existingButton != null)
            {
                _button = existingButton.GetComponent<UIButton>();
                _buttonText = existingButton.Find("Text")?.GetComponent<Text>();
                if (_button != null && _buttonText != null)
                {
                    _button.onClick -= OnToggleClicked;
                    _button.onClick += OnToggleClicked;
                    Refresh(siloWindow);
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
                Plugin.ModLogger.LogError("The Battery Rocket silo toggle template is missing its button or text component.");
                return;
            }

            Localizer localizer = textTransform.GetComponent<Localizer>();
            if (localizer != null)
            {
                Object.DestroyImmediate(localizer);
            }

            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.SetParent(siloWindow.windowTrans, false);
            rect.anchorMin = Vector2.up;
            rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.up;
            rect.sizeDelta = new Vector2(174f, 22f);
            rect.anchoredPosition = new Vector2(-18f, -48f);

            _button.tips.tipTitle = "Battery Sphere";
            _button.tips.tipText =
                "Load Battery Rockets in this silo. They launch only at a star with a Battery Sphere planned in the " +
                "Dyson Sphere editor. Empty the silo before switching rocket types.";
            _button.onClick -= OnToggleClicked;
            _button.onClick += OnToggleClicked;
            Refresh(siloWindow);
        }

        internal static void Refresh(UISiloWindow siloWindow)
        {
            if (_button == null)
            {
                Initialize();
                if (_button == null)
                {
                    return;
                }
            }

            SiloComponent silo;
            if (!TryGetSilo(siloWindow, out silo) || !BatterySphereContent.IsUnlocked)
            {
                _button.gameObject.SetActive(false);
                return;
            }

            _button.gameObject.SetActive(true);
            string label = silo.bulletId == BatterySphereContent.ItemId
                ? "Rocket: Battery Rocket"
                : "Rocket: Carrier Rocket";
            if (_buttonText != null && _buttonText.text != label)
            {
                _buttonText.text = label;
            }
        }

        private static void OnToggleClicked(int _)
        {
            UIGame uiGame = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
            UISiloWindow siloWindow = uiGame != null ? uiGame.siloWindow : null;
            SiloComponent silo;
            if (!TryGetSilo(siloWindow, out silo) || !BatterySphereContent.IsUnlocked)
            {
                Refresh(siloWindow);
                return;
            }

            if (silo.bulletCount > 0 || silo.time > 0)
            {
                _button.tips.tipText = "The silo must be empty and idle before switching rocket types.";
                return;
            }

            silo.bulletId = silo.bulletId == BatterySphereContent.ItemId
                ? ItemProto.kRocketId
                : BatterySphereContent.ItemId;
            siloWindow.factorySystem.siloPool[siloWindow.siloId] = silo;
            siloWindow.SyncServingStorage();
            _button.tips.tipText =
                "Load Battery Rockets in this silo. They launch only at a star with a Battery Sphere planned in the " +
                "Dyson Sphere editor. Empty the silo before switching rocket types.";
            Refresh(siloWindow);
        }

        private static bool TryGetSilo(UISiloWindow siloWindow, out SiloComponent silo)
        {
            silo = default(SiloComponent);
            if (siloWindow == null
                || !siloWindow.active
                || siloWindow.factorySystem == null
                || siloWindow.factorySystem.siloPool == null
                || siloWindow.siloId <= 0
                || siloWindow.siloId >= siloWindow.factorySystem.siloPool.Length)
            {
                return false;
            }

            silo = siloWindow.factorySystem.siloPool[siloWindow.siloId];
            return silo.id == siloWindow.siloId;
        }
    }

    [HarmonyPatch(typeof(UISiloWindow), "OnSiloIdChange")]
    internal static class BatterySphereSiloChangePatch
    {
        private static void Postfix(UISiloWindow __instance)
        {
            BatterySphereSiloUI.Refresh(__instance);
        }
    }

    [HarmonyPatch(typeof(UISiloWindow), "_OnUpdate")]
    internal static class BatterySphereSiloUpdatePatch
    {
        private static void Postfix(UISiloWindow __instance)
        {
            BatterySphereSiloUI.Refresh(__instance);
        }
    }
}
