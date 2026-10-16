using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace SubspaceTransport
{
    // Megastructure type selector in the Dyson Sphere editor (Y): a drop-down at the top centre of the editor,
    // clear of the left-edge panel More Megastructures draws. Below the status is the Hyper Mass Injector's
    // hyperinjection button, shown while that type is selected.
    [HarmonyPatch(typeof(UIDysonEditor), "_OnUpdate")]
    internal static class BatterySphereEditorUI
    {
        private const string PanelObjectName = "wormhole-megastructure-selector";
        private const float PanelWidth = 200f;
        private const float ButtonWidth = 200f;
        private const float ButtonHeight = 24f;
        private const float RowSpacing = 28f;
        private const float StatusHeight = 110f;
        private const int TypeRows = 3;
        private const float StatusTop = TypeRows * RowSpacing;
        private const float InjectTop = StatusTop + StatusHeight + 4f;
        private const float BodyTop = 30f;
        private const float TopOffset = 50f;

        private const int LabelFontSize = 14;
        private const int StatusFontSize = 14;
        private const float StatusPadding = 8f;

        // Accumulator counts for a Battery Sphere, in a panel to the right of the selector.
        private const float CountsPanelWidth = 170f;
        private const float CountsPanelGap = 8f;
        private const float CountsHeaderHeight = 26f;
        private const float CountsRowHeight = 36f;
        private const float CountsIconSize = 32f;

        private static readonly Color SelectedTextColor = new Color(1f, 0.82f, 0.25f, 1f);
        private static readonly Color ButtonPlateColor = new Color(0.05f, 0.09f, 0.13f, 0.94f);
        private static readonly Color StatusPlateColor = new Color(0.05f, 0.09f, 0.13f, 0.8f);
        private static readonly Color NormalTextColor = new Color(0.92f, 0.95f, 0.98f, 1f);
        private static readonly Color UnavailableTextColor = new Color(0.55f, 0.58f, 0.62f, 1f);
        private static readonly Color InjectTextColor = new Color(0.78f, 0.62f, 1f, 1f);

        private static UIDysonEditor _editor;

        // The game's UI font, taken from the copied button; Arial until a button has been created.
        private static Font _font;
        private static RectTransform _panel;
        private static RectTransform _body;
        private static Text _handleText;
        private static bool _expanded = true;
        private static UIButton _dysonButton;
        private static Text _dysonText;
        private static UIButton _batteryButton;
        private static Text _batteryText;
        private static UIButton _hyperButton;
        private static Text _hyperText;
        private static UIButton _injectButton;
        private static Text _injectText;
        private static Text _status;
        private static string _lastStatus;
        private static RectTransform _countsPanel;
        private static Text _emptyCountText;
        private static Text _chargedCountText;
        private static int _lastEmpty = -1;
        private static int _lastCharged = -1;

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
            bool hyperPlanned = HyperMassManager.IsPlanned(star.index);
            SetButtonState(_dysonButton, _dysonText, "Dyson Sphere", !planned && !hyperPlanned);
            SetButtonState(_batteryButton, _batteryText, "Battery Sphere", planned);
            SetButtonState(_hyperButton, _hyperText, "Hyper Mass Injector", hyperPlanned);
            UpdateInjectButton(star, hyperPlanned);
            UpdateCounts(star, planned);

            string status;
            if (hyperPlanned)
            {
                status = GetHyperMassStatus(star);
            }
            else if (!BatterySphereContent.IsUnlocked)
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
                    "Battery Sphere Project\nSphere output: {0:N0} MW",
                    power / 1000000L);
            }

            if (_lastStatus != status)
            {
                _status.text = status;
                _lastStatus = status;
            }
        }

        private static void UpdateCounts(StarData star, bool planned)
        {
            bool visible = planned && _expanded;
            _countsPanel.gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            int rockets;
            long power;
            int empty;
            int charged;
            BatterySphereManager.GetProjectStatus(star.index, out rockets, out power, out empty, out charged);
            if (empty != _lastEmpty)
            {
                _emptyCountText.text = empty.ToString("N0");
                _lastEmpty = empty;
            }

            if (charged != _lastCharged)
            {
                _chargedCountText.text = charged.ToString("N0");
                _lastCharged = charged;
            }
        }

        private static string GetHyperMassStatus(StarData star)
        {
            long watts = HyperMassManager.GetSphereWatts(star.index);
            float injected = HyperMassManager.GetInjectedLuminosity(star.index);
            string result = string.Format(
                "Hyper Mass Injector Project\nSphere output: {0} / {1}\n" +
                "Star: {2:0.000} L, {3:0.00} R, {4:N0} K",
                HyperMassManager.FormatPower(watts),
                HyperMassManager.ActivationPower,
                star.dysonLumino,
                star.radius,
                star.temperature);
            float target = HyperMassContent.TargetLuminosity;
            if (target > 0f)
            {
                result += string.Format("\nResearch target: {0:0.0#} L", target);
            }

            if (injected > 0f)
            {
                result += string.Format("\nHyperinjected: {0}", star.typeString);
            }

            return result;
        }

        private static void UpdateInjectButton(StarData star, bool hyperPlanned)
        {
            _injectButton.gameObject.SetActive(hyperPlanned);
            if (!hyperPlanned)
            {
                return;
            }

            bool available = HyperMassManager.GetInjectionBlocker(star) == null;
            _injectText.color = available ? InjectTextColor : UnavailableTextColor;
            _injectButton.highlighted = false;
        }

        private static void OnInjectClicked(int _)
        {
            StarData star = GetViewStar();
            string blocker = HyperMassManager.GetInjectionBlocker(star);
            if (blocker != null)
            {
                UIRealtimeTip.Popup(blocker);
                return;
            }

            int starIndex = star.index;
            UIMessageBox.Show(
                "Hyperinject Mass into the Star",
                "Inject matter from other galaxies into " + star.displayName + ", raising it to " +
                HyperMassContent.TargetLuminosity.ToString("0.0#") + " L. The star grows into a hypergiant, " +
                "and the system's solar power gets stronger. The Hyper Mass Injector sphere is consumed and destroyed, " +
                "and the change cannot be undone.",
                "取消".Translate(),
                "确定".Translate(),
                UIMessageBox.QUESTION,
                null,
                () =>
                {
                    StarData current = GetViewStar();
                    if (current != null && current.index == starIndex)
                    {
                        HyperMassManager.Inject(current);

                        // The editor still points at the destroyed sphere.
                        _editor.selection.SetViewStar(current);
                    }
                });
        }

        private static void OnDysonClicked(int _)
        {
            StarData star = GetViewStar();
            if (star == null
                || (!BatterySphereManager.IsPlanned(star.index) && !HyperMassManager.IsPlanned(star.index))
                || !CheckNoStructures(star, "switching to a Dyson Sphere"))
            {
                return;
            }

            BatterySphereManager.SetPlanned(star.index, false);
            HyperMassManager.SetPlanned(star.index, false);
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

            if (!CheckNoMegastructure(star) || !CheckNoStructures(star, "planning a Battery Sphere"))
            {
                return;
            }

            HyperMassManager.SetPlanned(star.index, false);
            BatterySphereManager.SetPlanned(star.index, true);
        }

        private static void OnHyperClicked(int _)
        {
            StarData star = GetViewStar();
            if (star == null || HyperMassManager.IsPlanned(star.index))
            {
                return;
            }

            if (!HyperMassContent.IsUnlocked)
            {
                UIRealtimeTip.Popup("Research Stellar Hyper Mass Inducer first.");
                return;
            }

            if (!StarConversion.CanConvert(star))
            {
                UIRealtimeTip.Popup("A Hyper Mass Injector needs a main-sequence or giant star.");
                return;
            }

            if (!CheckNoMegastructure(star) || !CheckNoStructures(star, "planning a Hyper Mass Injector"))
            {
                return;
            }

            BatterySphereManager.SetPlanned(star.index, false);
            HyperMassManager.SetPlanned(star.index, true);
        }

        private static bool CheckNoMegastructure(StarData star)
        {
            if (MoreMegaStructureCompat.HasMegastructure(star.index))
            {
                UIRealtimeTip.Popup(
                    "This star has a More Megastructures structure planned. Switch it to a Dyson Sphere first.");
                return false;
            }

            return true;
        }

        private static bool CheckNoStructures(StarData star, string action)
        {
            DysonSphere sphere = HyperMassManager.GetSphere(star.index);
            bool sandbox = GameMain.data.gameDesc != null && GameMain.data.gameDesc.isSandboxMode;
            if (sphere != null && sphere.totalNodeCount > 0 && !sandbox)
            {
                UIRealtimeTip.Popup("Remove this star's Dyson Sphere structures before " + action + ".");
                return false;
            }

            return true;
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
            handle.tips.tipText = "Show or hide the Dyson Sphere / Battery Sphere / Hyper Mass Injector selector.";
            handle.onClick += OnHandleClicked;

            GameObject bodyObject = new GameObject("body", typeof(RectTransform));
            _body = (RectTransform)bodyObject.transform;
            _body.SetParent(_panel, false);
            _body.anchorMin = Vector2.up;
            _body.anchorMax = Vector2.up;
            _body.pivot = Vector2.up;
            _body.anchoredPosition = new Vector2(0f, -BodyTop);
            _body.sizeDelta = new Vector2(PanelWidth, InjectTop + ButtonHeight);

            _dysonButton = CreateButton(assemblerWindow, _body, "dyson", 0f, out _dysonText);
            _dysonButton.tips.tipTitle = "Dyson Sphere";
            _dysonButton.tips.tipText =
                "Use this star for an ordinary Dyson Sphere. Battery and Hyper Mass Rockets stay in the silo.";
            _dysonButton.onClick += OnDysonClicked;

            _batteryButton = CreateButton(assemblerWindow, _body, "battery", RowSpacing, out _batteryText);
            _batteryButton.tips.tipTitle = "Battery Sphere";
            _batteryButton.tips.tipText =
                "Use this star for a Battery Sphere. Battery Rockets launched here charge accumulators for " +
                "subspace logistics. The star's Dyson Sphere must have no structures.";
            _batteryButton.onClick += OnBatteryClicked;

            _hyperButton = CreateButton(assemblerWindow, _body, "hyper-mass", 2f * RowSpacing, out _hyperText);
            _hyperButton.tips.tipTitle = "Hyper Mass Injector";
            _hyperButton.tips.tipText =
                "Use this star for a Hyper Mass Injector. Only Hyper Mass Rockets build it. At " +
                HyperMassManager.ActivationPower + " it can hyperinject the star into a hypergiant. " +
                "The star's Dyson Sphere must have no structures.";
            _hyperButton.onClick += OnHyperClicked;

            _injectButton = CreateButton(assemblerWindow, _body, "hyperinject", InjectTop, out _injectText);
            _injectText.text = "Hyperinject Mass into the Star";
            _injectButton.tips.tipTitle = "Hyperinject Mass into the Star";
            _injectButton.tips.tipText =
                "Available once this Hyper Mass Injector's sphere generates " + HyperMassManager.ActivationPower +
                ". Turns the star into a hypergiant at the luminosity your Stellar Hyper Mass research allows. " +
                "Hyperinject again after more Amplification research to grow it further.";
            _injectButton.onClick += OnInjectClicked;

            AddPlate(_body, "status-backdrop", new Vector2(0f, -StatusTop), new Vector2(PanelWidth, StatusHeight), StatusPlateColor);
            _status = CreateText(
                _body,
                "status",
                new Vector2(StatusPadding, -StatusTop - StatusPadding),
                new Vector2(PanelWidth - 2f * StatusPadding, StatusHeight - 2f * StatusPadding));
            CreateCountsPanel(_panel);
            ApplyExpanded();
            _editor = editor;
            _lastStatus = null;
            _lastEmpty = -1;
            _lastCharged = -1;

            // The editor treats clicks outside its GUI rects as clicks on the sphere view.
            if (editor.guiRects != null && Array.IndexOf(editor.guiRects, _panel) < 0)
            {
                RectTransform[] rects = new RectTransform[editor.guiRects.Length + 2];
                Array.Copy(editor.guiRects, rects, editor.guiRects.Length);
                rects[rects.Length - 2] = _panel;
                rects[rects.Length - 1] = _countsPanel;
                editor.guiRects = rects;
            }
        }

        private static void CreateCountsPanel(RectTransform parent)
        {
            float height = CountsHeaderHeight + 2f * CountsRowHeight + StatusPadding;
            GameObject panelObject = new GameObject(
                "battery-counts",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            _countsPanel = (RectTransform)panelObject.transform;
            _countsPanel.SetParent(parent, false);
            _countsPanel.anchorMin = Vector2.up;
            _countsPanel.anchorMax = Vector2.up;
            _countsPanel.pivot = Vector2.up;
            _countsPanel.anchoredPosition = new Vector2(PanelWidth + CountsPanelGap, -BodyTop);
            _countsPanel.sizeDelta = new Vector2(CountsPanelWidth, height);
            Image plate = panelObject.GetComponent<Image>();
            plate.color = StatusPlateColor;
            plate.raycastTarget = false;

            Text header = CreateText(
                _countsPanel,
                "header",
                new Vector2(StatusPadding, -StatusPadding),
                new Vector2(CountsPanelWidth - 2f * StatusPadding, CountsHeaderHeight - StatusPadding));
            header.text = "Accumulators";

            _emptyCountText = CreateCountRow(0, ItemProto.kAccumulator);
            _chargedCountText = CreateCountRow(1, ItemProto.kAccumulatorFull);
            _countsPanel.gameObject.SetActive(false);
        }

        private static Text CreateCountRow(int row, int itemId)
        {
            float top = -CountsHeaderHeight - row * CountsRowHeight;
            ItemProto proto = LDB.items.Select(itemId);
            GameObject iconObject = new GameObject(
                "icon-" + itemId,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            RectTransform iconRect = (RectTransform)iconObject.transform;
            iconRect.SetParent(_countsPanel, false);
            iconRect.anchorMin = Vector2.up;
            iconRect.anchorMax = Vector2.up;
            iconRect.pivot = Vector2.up;
            iconRect.anchoredPosition = new Vector2(StatusPadding, top - (CountsRowHeight - CountsIconSize) / 2f);
            iconRect.sizeDelta = new Vector2(CountsIconSize, CountsIconSize);
            Image icon = iconObject.GetComponent<Image>();
            icon.sprite = proto != null ? proto.iconSprite : null;
            icon.enabled = icon.sprite != null;
            icon.raycastTarget = false;

            float textLeft = StatusPadding + CountsIconSize + 8f;
            Text count = CreateText(
                _countsPanel,
                "count-" + itemId,
                new Vector2(textLeft, top),
                new Vector2(CountsPanelWidth - textLeft - StatusPadding, CountsRowHeight));
            count.alignment = TextAnchor.MiddleLeft;
            count.fontSize = 18;
            count.text = "0";
            return count;
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
            StripTextStyling(text);
            if (_font == null)
            {
                _font = text.font;
            }

            text.fontSize = LabelFontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            AddContrastShadow(text);

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

        // Children draw over their parent, so a plate for a Text is a sibling created before it.
        private static void AddPlate(Transform parent, string name, Vector2 position, Vector2 size, Color color)
        {
            GameObject plateObject = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform plate = (RectTransform)plateObject.transform;
            plate.SetParent(parent, false);
            plate.anchorMin = Vector2.up;
            plate.anchorMax = Vector2.up;
            plate.pivot = Vector2.up;
            plate.anchoredPosition = position;
            plate.sizeDelta = size;
            Image image = plateObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        // The copied button label carries the game's glow effects, glow material, localizer and best-fit sizing,
        // which blur and squash custom labels on the dark plates.
        private static void StripTextStyling(Text text)
        {
            foreach (Component component in text.GetComponents<Component>())
            {
                if (!(component is RectTransform) && !(component is CanvasRenderer) && !(component is Text))
                {
                    UnityEngine.Object.DestroyImmediate(component);
                }
            }

            text.material = null;
            text.resizeTextForBestFit = false;
            text.fontStyle = FontStyle.Normal;
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
            text.font = _font != null ? _font : Resources.GetBuiltinResource<Font>("Arial.ttf");
            text.fontSize = StatusFontSize;
            text.lineSpacing = 1.1f;
            text.alignment = TextAnchor.UpperLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            text.supportRichText = false;
            text.color = NormalTextColor;
            AddContrastShadow(text);
            return text;
        }
    }
}
