using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UnityLab.Shared
{
    /// <summary>Colours shared by every screen so the demos look like one product.</summary>
    public static class UiTheme
    {
        public static readonly Color Background = new Color(0.07f, 0.08f, 0.11f, 1f);
        public static readonly Color Panel = new Color(0.12f, 0.14f, 0.19f, 0.94f);
        public static readonly Color PanelBorder = new Color(1f, 1f, 1f, 0.06f);
        public static readonly Color Accent = new Color(0.98f, 0.62f, 0.16f, 1f);
        public static readonly Color AccentHover = new Color(1f, 0.72f, 0.33f, 1f);
        public static readonly Color Text = new Color(0.93f, 0.94f, 0.96f, 1f);
        public static readonly Color MutedText = new Color(0.62f, 0.66f, 0.74f, 1f);
        public static readonly Color Field = new Color(0.07f, 0.08f, 0.11f, 1f);
        public static readonly Color Danger = new Color(0.95f, 0.36f, 0.33f, 1f);
        public static readonly Color Success = new Color(0.42f, 0.83f, 0.47f, 1f);
    }

    /// <summary>
    /// Builds uGUI + TextMesh Pro widgets from code. Screens built this way are easy to review in a
    /// diff and need no hand-edited scene data.
    /// </summary>
    public static class UiFactory
    {
        public static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null)
                return;
            // Created in the current scene: scenes that already have one keep theirs.
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        }

        public static Canvas CreateCanvas(string name, int sortingOrder = 0)
        {
            EnsureEventSystem();
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = sortingOrder;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        public static RectTransform Panel(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = color;
            return (RectTransform)go.transform;
        }

        /// <summary>Invisible container, typically with a layout group.</summary>
        public static RectTransform Group(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        public static TMP_Text Label(Transform parent, string text, float size, Color color, TextAlignmentOptions alignment = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal)
        {
            var go = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            go.transform.SetParent(parent, false);
            var label = go.GetComponent<TextMeshProUGUI>();
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.fontStyle = style;
            label.textWrappingMode = TextWrappingModes.Normal;
            label.raycastTarget = false;
            return label;
        }

        public static Button Button(Transform parent, string text, UnityAction onClick, bool primary = true)
        {
            var go = new GameObject("Button " + text, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<LayoutElement>().minHeight = 56;

            var button = go.GetComponent<Button>();
            ColorBlock colors = button.colors;
            colors.normalColor = primary ? UiTheme.Accent : new Color(1f, 1f, 1f, 0.08f);
            colors.highlightedColor = primary ? UiTheme.AccentHover : new Color(1f, 1f, 1f, 0.16f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = primary ? new Color(0.85f, 0.52f, 0.12f) : new Color(1f, 1f, 1f, 0.22f);
            colors.disabledColor = new Color(1f, 1f, 1f, 0.05f);
            button.colors = colors;
            button.onClick.AddListener(onClick);

            TMP_Text label = Label(go.transform, text, 24, primary ? UiTheme.Background : UiTheme.Text, TextAlignmentOptions.Center, FontStyles.Bold);
            Stretch(label.rectTransform);
            return button;
        }

        public static TMP_InputField InputField(Transform parent, string placeholder, string value, TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard, int characterLimit = 32)
        {
            var go = new GameObject("Input " + placeholder, typeof(RectTransform), typeof(Image), typeof(TMP_InputField), typeof(LayoutElement));
            go.transform.SetParent(parent, false);
            go.GetComponent<Image>().color = UiTheme.Field;
            go.GetComponent<LayoutElement>().minHeight = 52;

            RectTransform area = Group(go.transform, "Text Area");
            area.gameObject.AddComponent<RectMask2D>();
            Stretch(area, 16, 8);

            TMP_Text hint = Label(area, placeholder, 24, UiTheme.MutedText, TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
            hint.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(hint.rectTransform);
            TMP_Text text = Label(area, string.Empty, 24, UiTheme.Text, TextAlignmentOptions.MidlineLeft);
            text.textWrappingMode = TextWrappingModes.NoWrap;
            Stretch(text.rectTransform);

            var field = go.GetComponent<TMP_InputField>();
            field.textViewport = area;
            field.textComponent = text;
            field.placeholder = hint;
            field.contentType = contentType;
            field.characterLimit = characterLimit;
            field.caretColor = UiTheme.Accent;
            field.selectionColor = new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.35f);
            field.text = value;
            return field;
        }

        public static VerticalLayoutGroup Vertical(RectTransform target, float spacing, int padding)
        {
            var layout = target.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            return layout;
        }

        public static HorizontalLayoutGroup Horizontal(RectTransform target, float spacing, int padding)
        {
            var layout = target.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = spacing;
            layout.padding = new RectOffset(padding, padding, padding, padding);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            return layout;
        }

        public static void Stretch(RectTransform rect, float horizontalInset = 0f, float verticalInset = 0f)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(horizontalInset, verticalInset);
            rect.offsetMax = new Vector2(-horizontalInset, -verticalInset);
        }

        /// <summary>Places a rectangle of the given size at an anchor point of its parent.</summary>
        public static void Anchor(RectTransform rect, Vector2 anchor, Vector2 size, Vector2 offset)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.sizeDelta = size;
            rect.anchoredPosition = offset;
        }

        public static LayoutElement Size(Component target, float preferredHeight = -1f, float flexibleWidth = -1f, float preferredWidth = -1f)
        {
            // Not "??": Unity objects override == null, which the null-coalescing operator ignores.
            if (!target.TryGetComponent(out LayoutElement element))
                element = target.gameObject.AddComponent<LayoutElement>();
            if (preferredHeight >= 0f)
                element.preferredHeight = preferredHeight;
            if (flexibleWidth >= 0f)
                element.flexibleWidth = flexibleWidth;
            if (preferredWidth >= 0f)
                element.preferredWidth = preferredWidth;
            return element;
        }
    }
}
