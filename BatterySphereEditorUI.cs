using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SubspaceTransport
{
    // Megastructure type selector in the Dyson Sphere editor (Y): a drop-down at the top centre of the editor,
    // clear of the left-edge panel More Megastructures draws. The Stellar Hyper Mass sphere will be added as
    // a third button after Battery Sphere.
    [HarmonyPatch(typeof(UIDysonEditor), "_OnUpdate")]
    internal static class BatterySphereEditorUI
    {
        private const string PanelObjectName = "wormhole-megastructure-selector";
        private const float PanelWidth = 200f;
        private const float ButtonWidth = 200f;
        private const float ButtonHeight = 24f;
        private const float RowSpacing = 28f;
        private const float StatusHeight = 110f;
        private const float BodyTop = 30f;
        private const float TopOffset = 50f;

        private static readonly Color SelectedTextColor = new Color(1f, 0.82f, 0.25f, 1f);
        private static readonly Color ButtonPlateColor = new Color(0.05f, 0.09f, 0.13f, 0.94f);
        private static readonly Color StatusPlateColor = new Color(0.05f, 0.09f, 0.13f, 0.8f);
        private static readonly Color NormalTextColor = new Color(0.92f, 0.95f, 0.98f, 1f);

        private static UIDysonEditor _editor;
        private static RectTransform _panel;
        private static RectTransform _body;
        private static Text _handleText;
        private static bool _expanded = true;
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
                    "Battery Sphere Project\nSphere output: {0:N0} MW\n" +
                    "Accumulator buffer: {1:N0} empty / {2:N0} charged",
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
            if (star == null || !BatterySphereManager.IsPlanned(star.index))
            {
                return;
            }

            DysonSphere[] spheres = GameMain.data != null ? GameMain.data.dysonSpheres : null;
            DysonSphere sphere = spheres != null && star.index < spheres.Length ? spheres[star.index] : null;
            bool sandbox = GameMain.data.gameDesc != null && GameMain.data.gameDesc.isSandboxMode;
            if (sphere != null && sphere.totalNodeCount > 0 && !sandbox)
            {
                UIRealtimeTip.Popup("Remove this star's Dyson Sphere structures before switching to a Dyson Sphere.");
                return;
            }
            BatterySphereManager.SetPlanned(star.index, false);
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
            // The highlighted button background is bright and washes out the label, so the selection is shown by
            // the label colour alone.
            button.highlighted = false;
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
            _panel.anchorMin = new Vector2(0.5f, 1f);
            _panel.anchorMax = new Vector2(0.5f, 1f);
            _panel.pivot = new Vector2(0.5f, 1f);
            _panel.anchoredPosition = new Vector2(0f, -TopOffset);

            UIButton handle = CreateButton(assemblerWindow, _panel, "handle", 0f, out _handleText);
            handle.tips.tipTitle = "Megastructure type";
            handle.tips.tipText = "Show or hide the Dyson Sphere / Battery Sphere selector.";
            handle.onClick += OnHandleClicked;

            GameObject bodyObject = new GameObject("body", typeof(RectTransform));
            _body = (RectTransform)bodyObject.transform;
            _body.SetParent(_panel, false);
            _body.anchorMin = Vector2.up;
            _body.anchorMax = Vector2.up;
            _body.pivot = Vector2.up;
            _body.anchoredPosition = new Vector2(0f, -BodyTop);
            _body.sizeDelta = new Vector2(PanelWidth, 2f * RowSpacing + StatusHeight);

            _dysonButton = CreateButton(assemblerWindow, _body, "dyson", 0f, out _dysonText);
            _dysonButton.tips.tipTitle = "Dyson Sphere";
            _dysonButton.tips.tipText = "Use this star for an ordinary Dyson Sphere. Battery Rockets stay in the silo.";
            _dysonButton.onClick += OnDysonClicked;

            _batteryButton = CreateButton(assemblerWindow, _body, "battery", RowSpacing, out _batteryText);
            _batteryButton.tips.tipTitle = "Battery Sphere";
            _batteryButton.tips.tipText =
                "Use this star for a Battery Sphere. Battery Rockets launched here charge accumulators for " +
                "subspace logistics. The star's Dyson Sphere must have no structures.";
            _batteryButton.onClick += OnBatteryClicked;

            _status = CreateText(_body, "status", new Vector2(0f, -2f * RowSpacing), new Vector2(PanelWidth, StatusHeight));
            _status.fontSize = 14;
            AddPlateBehind(_status, StatusPlateColor);
            ApplyExpanded();
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

        private static void OnHandleClicked(int _)
        {
            _expanded = !_expanded;
            ApplyExpanded();
        }

        // The editor only treats the panel's own rect as GUI, so the rect must follow the pop-out.
        private static void ApplyExpanded()
        {
            _body.gameObject.SetActive(_expanded);
            _panel.sizeDelta = new Vector2(
                PanelWidth,
                _expanded ? BodyTop + _body.sizeDelta.y : ButtonHeight);
            _handleText.text = (_expanded ? "v " : "> ") + "Megastructure type";
        }

        private static UIButton CreateButton(
            UIAssemblerWindow assemblerWindow,
            Transform parent,
            string name,
            float offset,
            out Text text)
        {
            GameObject buttonObject = UnityEngine.Object.Instantiate(assemblerWindow.copyButton.gameObject);
            buttonObject.name = name;
            UIButton button = buttonObject.GetComponent<UIButton>();
            Transform textTransform = buttonObject.transform.Find("Text");
            text = textTransform.GetComponent<Text>();
            AddContrastShadow(text);
            Localizer localizer = textTransform.GetComponent<Localizer>();
            if (localizer != null)
            {
                UnityEngine.Object.DestroyImmediate(localizer);
            }

            AddBackdrop(buttonObject.transform, ButtonPlateColor);

            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.up;
            rect.anchorMax = Vector2.up;
            rect.pivot = Vector2.up;
            rect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            rect.anchoredPosition = new Vector2(0f, -offset);
            return button;
        }

        // A dark, mostly opaque plate behind a label, so it stays readable whatever the button graphic or the
        // sphere view behind it looks like.
        private static void AddBackdrop(Transform parent, Color color)
        {
            GameObject plateObject = new GameObject("backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform plate = (RectTransform)plateObject.transform;
            plate.SetParent(parent, false);
            plate.anchorMin = Vector2.zero;
            plate.anchorMax = Vector2.one;
            plate.offsetMin = Vector2.zero;
            plate.offsetMax = Vector2.zero;
            plate.SetAsFirstSibling();
            Image image = plateObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        // Children draw over their parent, so a plate for a Text sits behind it as a sibling instead.
        private static void AddPlateBehind(Text text, Color color)
        {
            RectTransform source = (RectTransform)text.transform;
            GameObject plateObject = new GameObject("status-backdrop", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform plate = (RectTransform)plateObject.transform;
            plate.SetParent(source.parent, false);
            plate.anchorMin = source.anchorMin;
            plate.anchorMax = source.anchorMax;
            plate.pivot = source.pivot;
            plate.anchoredPosition = source.anchoredPosition;
            plate.sizeDelta = source.sizeDelta;
            plate.SetSiblingIndex(source.GetSiblingIndex());
            Image image = plateObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private static void AddContrastShadow(Text text)
        {
            if (text.GetComponent<Shadow>() == null)
            {
                Shadow shadow = text.gameObject.AddComponent<Shadow>();
                shadow.effectColor = new Color(0f, 0f, 0f, 0.85f);
                shadow.effectDistance = new Vector2(1f, -1f);
            }
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
            text.color = NormalTextColor;
            AddContrastShadow(text);
            return text;
        }
    }
}
