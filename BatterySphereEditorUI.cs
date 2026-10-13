using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SubspaceTransport
{
    // Megastructure type selector in the Dyson Sphere editor (Y). The Stellar Hyper Mass sphere will be
    // added as a third button after Battery Sphere.
    [HarmonyPatch(typeof(UIDysonEditor), "_OnUpdate")]
    internal static class BatterySphereEditorUI
    {
        private const string PanelObjectName = "wormhole-megastructure-selector";
        private const float PanelWidth = 370f;
        private const float ButtonWidth = 150f;
        private const float ButtonHeight = 24f;

        private static readonly Color SelectedTextColor = new Color(1f, 0.75f, 0.1f, 1f);
        private static readonly Color NormalTextColor = Color.white;

        private static UIDysonEditor _editor;
        private static RectTransform _panel;
        private static UIButton _dysonButton;
        private static Text _dysonText;
        private static UIButton _batteryButton;
        private static Text _batteryText;
        private static Text _status;
        private static string _lastStatus;

        private static void Postfix(UIDysonEditor __instance)
        {
            EnsurePanel(__instance);
            if (_panel == null)
            {
                return;
            }

            StarData star = GetViewStar();
            _panel.gameObject.SetActive(star != null);
            if (star == null)
            {
                return;
            }

            bool planned = BatterySphereManager.IsPlanned(star.index);
            SetButtonState(_dysonButton, _dysonText, "Dyson Sphere", !planned);
            SetButtonState(_batteryButton, _batteryText, "Battery Sphere", planned);

            string status;
            if (!BatterySphereContent.IsUnlocked)
            {
                status = "Research Battery Sphere Engineering to plan a Battery Sphere.";
            }
            else if (!planned)
            {
                status = "Plan a Battery Sphere at this star to launch Battery Rockets here.";
            }
            else
            {
                int rockets;
                long power;
                int empty;
                int charged;
                BatterySphereManager.GetProjectStatus(star.index, out rockets, out power, out empty, out charged);
                status = string.Format(
                    "Battery Sphere Project\nRockets: {0:N0}\nDedicated output: {1:N0} MW\n" +
                    "Accumulator buffer: {2:N0} empty / {3:N0} charged",
                    rockets,
                    power / 1000000L,
                    empty,
                    charged);
            }

            if (_lastStatus != status)
            {
                _status.text = status;
                _lastStatus = status;
            }
        }

        private static void OnDysonClicked(int _)
        {
            StarData star = GetViewStar();
            if (star != null)
            {
                BatterySphereManager.SetPlanned(star.index, false);
            }
        }

        private static void OnBatteryClicked(int _)
        {
            StarData star = GetViewStar();
            if (star == null || BatterySphereManager.IsPlanned(star.index))
            {
                return;
            }

            if (!BatterySphereContent.IsUnlocked)
            {
                UIRealtimeTip.Popup("Research Battery Sphere Engineering first.");
                return;
            }

            if (MoreMegaStructureCompat.HasMegastructure(star.index))
            {
                UIRealtimeTip.Popup(
                    "This star has a More Megastructures structure planned. Switch it to a Dyson Sphere first.");
                return;
            }

            DysonSphere[] spheres = GameMain.data != null ? GameMain.data.dysonSpheres : null;
            DysonSphere sphere = spheres != null && star.index < spheres.Length ? spheres[star.index] : null;
            bool sandbox = GameMain.data.gameDesc != null && GameMain.data.gameDesc.isSandboxMode;
            if (sphere != null && sphere.totalNodeCount > 0 && !sandbox)
            {
                UIRealtimeTip.Popup("Remove this star's Dyson Sphere structures before planning a Battery Sphere.");
                return;
            }

            BatterySphereManager.SetPlanned(star.index, true);
        }

        private static StarData GetViewStar()
        {
            return _editor != null && _editor.selection != null ? _editor.selection.viewStar : null;
        }

        private static void SetButtonState(UIButton button, Text text, string label, bool selected)
        {
            string value = selected ? "Current: " + label : "Plan " + label;
            if (text.text != value)
            {
                text.text = value;
            }

            text.color = selected ? SelectedTextColor : NormalTextColor;
            button.highlighted = selected;
        }

        private static void EnsurePanel(UIDysonEditor editor)
        {
            if (_panel != null && _editor == editor)
            {
                return;
            }

            UIGame uiGame = UIRoot.instance != null ? UIRoot.instance.uiGame : null;
            UIAssemblerWindow assemblerWindow = uiGame != null ? uiGame.assemblerWindow : null;
            if (assemblerWindow == null || assemblerWindow.copyButton == null)
            {
                return;
            }

            Transform existing = editor.transform.Find(PanelObjectName);
            if (existing != null)
            {
                UnityEngine.Object.Destroy(existing.gameObject);
            }

            GameObject panelObject = new GameObject(PanelObjectName, typeof(RectTransform));
            _panel = (RectTransform)panelObject.transform;
            _panel.SetParent(editor.transform, false);
            _panel.anchorMin = Vector2.one;
            _panel.anchorMax = Vector2.one;
            _panel.pivot = Vector2.one;
            _panel.anchoredPosition = new Vector2(-24f, -24f);
            _panel.sizeDelta = new Vector2(PanelWidth, 150f);

            Text title = CreateText(_panel, "title", new Vector2(0f, 0f), new Vector2(PanelWidth, 20f));
            title.text = "Megastructure type";

            _dysonButton = CreateButton(assemblerWindow, _panel, "dyson", 0, out _dysonText);
            _dysonButton.tips.tipTitle = "Dyson Sphere";
            _dysonButton.tips.tipText = "Use this star for an ordinary Dyson Sphere. Battery Rockets stay in the silo.";
            _dysonButton.onClick += OnDysonClicked;

            _batteryButton = CreateButton(assemblerWindow, _panel, "battery", 1, out _batteryText);
            _batteryButton.tips.tipTitle = "Battery Sphere";
            _batteryButton.tips.tipText =
                "Use this star for a Battery Sphere. Battery Rockets launched here charge accumulators for " +
                "wormhole logistics. The star's Dyson Sphere must have no structures.";
            _batteryButton.onClick += OnBatteryClicked;

            _status = CreateText(_panel, "status", new Vector2(0f, -56f), new Vector2(PanelWidth, 90f));
            _editor = editor;
            _lastStatus = null;

            // The editor treats clicks outside its GUI rects as clicks on the sphere view.
            if (editor.guiRects != null && Array.IndexOf(editor.guiRects, _panel) < 0)
            {
                RectTransform[] rects = new RectTransform[editor.guiRects.Length + 1];
                Array.Copy(editor.guiRects, rects, editor.guiRects.Length);
                rects[rects.Length - 1] = _panel;
                editor.guiRects = rects;
            }
        }

        private static UIButton CreateButton(
            UIAssemblerWindow assemblerWindow,
            Transform parent,
            string name,
            int column,
            out Text text)
        {
            GameObject buttonObject = UnityEngine.Object.Instantiate(assemblerWindow.copyButton.gameObject);
            buttonObject.name = name;
            UIButton button = buttonObject.GetComponent<UIButton>();
            Transform textTransform = buttonObject.transform.Find("Text");
            text = textTransform.GetComponent<Text>();
            Localizer localizer = textTransform.GetComponent<Localizer>();
            if (localizer != null)
            {
                UnityEngine.Object.DestroyImmediate(localizer);
            }

            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.up;
            rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.up;
            rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            rect.anchoredPosition = new Vector2(column * (ButtonWidth + 8f), -24f);
            return button;
        }

        private static Text CreateText(Transform parent, string name, Vector2 position, Vector2 size)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            RectTransform rect = (RectTransform)textObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.up;
            rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.up;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            Text text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = 15;
            text.color = Color.white;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
