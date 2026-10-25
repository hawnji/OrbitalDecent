using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace OrbitalDescent
{
    /// <summary>Colour palette shared by the HUD and menus.</summary>
    public static class Palette
    {
        public static readonly Color Hud = new Color(0.62f, 0.94f, 1f, 1f);
        public static readonly Color HudDim = new Color(0.62f, 0.94f, 1f, 0.45f);
        public static readonly Color Panel = new Color(0.02f, 0.05f, 0.08f, 0.55f);
        public static readonly Color PanelSolid = new Color(0.03f, 0.06f, 0.1f, 0.92f);
        public static readonly Color Good = new Color(0.45f, 1f, 0.55f, 1f);
        public static readonly Color Caution = new Color(1f, 0.78f, 0.2f, 1f);
        public static readonly Color Warning = new Color(1f, 0.3f, 0.25f, 1f);
        public static readonly Color Impact = new Color(1f, 0.55f, 0.95f, 1f);
        public static readonly Color Zone = new Color(0.35f, 0.95f, 1f, 1f);
        public static readonly Color Button = new Color(0.08f, 0.16f, 0.22f, 0.75f);
        public static readonly Color ButtonActive = new Color(0.15f, 0.55f, 0.7f, 0.9f);
        public static readonly Color Text = new Color(0.92f, 0.97f, 1f, 1f);
        public static readonly Color TextDim = new Color(0.7f, 0.78f, 0.84f, 1f);
    }

    /// <summary>Small helper layer for building uGUI hierarchies from code (no prefabs or assets).</summary>
    public static class Ui
    {
        public static Font Font { get; private set; }
        public static Sprite White { get; private set; }
        public static Sprite Disc { get; private set; }
        public static Sprite Ring { get; private set; }
        public static Sprite Rounded { get; private set; }

        public static void Init()
        {
            if (Font != null) return;
            Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            White = MakeSprite(4, (x, y) => 1f, 0);
            Disc = MakeSprite(128, (x, y) => Mathf.Clamp01((1f - Mathf.Sqrt(x * x + y * y)) * 64f), 0);
            Ring = MakeSprite(128, (x, y) => { float r = Mathf.Sqrt(x * x + y * y); return Mathf.Clamp01((1f - Mathf.Abs(r - 0.95f) / 0.04f)); }, 0);
            Rounded = MakeSprite(64, (x, y) =>
            {
                // Rounded square with a 9-slice border.
                float dx = Mathf.Max(0, Mathf.Abs(x) - 0.5f) / 0.5f, dy = Mathf.Max(0, Mathf.Abs(y) - 0.5f) / 0.5f;
                return Mathf.Clamp01((1f - Mathf.Sqrt(dx * dx + dy * dy)) * 16f);
            }, 16);
        }

        static Sprite MakeSprite(int n, Func<float, float, float> alpha, int border)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float fx = (x + 0.5f) / n * 2f - 1f, fy = (y + 0.5f) / n * 2f - 1f;
                    px[x + y * n] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(alpha(fx, fy)) * 255));
                }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(border, border, border, border));
        }

        public static Canvas Canvas(string name, int order)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var c = go.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = order;
            var s = go.AddComponent<CanvasScaler>();
            s.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            s.referenceResolution = new Vector2(1920, 1080);
            s.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            s.matchWidthOrHeight = 1f;   // landscape phones: scale with height so controls keep their size
            go.AddComponent<GraphicRaycaster>();
            return c;
        }

        public static void EnsureEventSystem()
        {
            if (EventSystem.current != null) return;
            var go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        public static RectTransform Rect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>Sets anchors to a single point and places a fixed-size rect relative to it.</summary>
        public static RectTransform Place(this RectTransform rt, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size)
        {
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = pivot;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return rt;
        }

        public static RectTransform Stretch(this RectTransform rt, float left = 0, float right = 0, float top = 0, float bottom = 0)
        {
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Image Image(Transform parent, string name, Color c, Sprite sprite = null)
        {
            var rt = Rect(name, parent);
            var img = rt.gameObject.AddComponent<Image>();
            img.sprite = sprite ?? White;
            if (sprite == Rounded) img.type = UnityEngine.UI.Image.Type.Sliced;
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        public static Text Label(Transform parent, string text, int size, Color c, TextAnchor align = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal)
        {
            var rt = Rect("Label", parent);
            var t = rt.gameObject.AddComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.color = c;
            t.alignment = align;
            t.fontStyle = style;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button Button(Transform parent, string label, UnityAction onClick, int fontSize = 34)
        {
            var img = Image(parent, "Button:" + label, Palette.Button, Rounded);
            img.raycastTarget = true;
            var b = img.gameObject.AddComponent<Button>();
            var colors = b.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f);
            colors.pressedColor = new Color(0.7f, 1.3f, 1.5f);
            colors.disabledColor = new Color(1, 1, 1, 0.35f);
            b.colors = colors;
            b.targetGraphic = img;
            if (onClick != null) b.onClick.AddListener(onClick);
            var t = Label(img.transform, label, fontSize, Palette.Text, TextAnchor.MiddleCenter, FontStyle.Bold);
            t.rectTransform.Stretch(8, 8, 4, 4);
            return b;
        }

        public static Slider Slider(Transform parent, float min, float max, float value, UnityAction<float> onChange, bool whole = false)
        {
            var root = Rect("Slider", parent);
            var bg = Image(root, "Track", new Color(1, 1, 1, 0.15f), Rounded);
            bg.rectTransform.anchorMin = new Vector2(0, 0.4f); bg.rectTransform.anchorMax = new Vector2(1, 0.6f);
            bg.rectTransform.offsetMin = bg.rectTransform.offsetMax = Vector2.zero;
            var fillArea = Rect("FillArea", root);
            fillArea.anchorMin = new Vector2(0, 0.4f); fillArea.anchorMax = new Vector2(1, 0.6f);
            fillArea.offsetMin = fillArea.offsetMax = Vector2.zero;
            var fill = Image(fillArea, "Fill", Palette.ButtonActive, Rounded);
            fill.rectTransform.Stretch();
            var handleArea = Rect("HandleArea", root).Stretch(20, 20);
            var handle = Image(handleArea, "Handle", Palette.Text, Disc);
            handle.rectTransform.sizeDelta = new Vector2(44, 0);
            handle.rectTransform.anchorMin = new Vector2(0, 0); handle.rectTransform.anchorMax = new Vector2(0, 1);
            var s = root.gameObject.AddComponent<Slider>();
            s.fillRect = fill.rectTransform;
            s.handleRect = handle.rectTransform;
            s.targetGraphic = handle;
            s.minValue = min; s.maxValue = max; s.wholeNumbers = whole;
            s.value = value;
            bg.raycastTarget = true;
            if (onChange != null) s.onValueChanged.AddListener(onChange);
            return s;
        }

        public static Toggle Toggle(Transform parent, string label, bool value, UnityAction<bool> onChange)
        {
            var root = Rect("Toggle:" + label, parent);
            var box = Image(root, "Box", Palette.Button, Rounded);
            box.raycastTarget = true;
            box.rectTransform.Place(new Vector2(0, 0.5f), new Vector2(0, 0.5f), Vector2.zero, new Vector2(56, 56));
            var check = Image(box.transform, "Check", Palette.Good, Rounded);
            check.rectTransform.Stretch(10, 10, 10, 10);
            var t = Label(root, label, 30, Palette.Text);
            t.rectTransform.Stretch(72, 0, 0, 0);
            var tg = root.gameObject.AddComponent<Toggle>();
            tg.targetGraphic = box;
            tg.graphic = check;
            tg.isOn = value;
            if (onChange != null) tg.onValueChanged.AddListener(onChange);
            return tg;
        }

        /// <summary>Vertical stack layout helper: places children top-down with a fixed row height.</summary>
        public static RectTransform Row(Transform parent, ref float y, float height, float pad = 12f)
        {
            var rt = Rect("Row", parent);
            rt.anchorMin = new Vector2(0, 1); rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = new Vector2(0, -y);
            rt.sizeDelta = new Vector2(0, height);
            y += height + pad;
            return rt;
        }
    }

    /// <summary>Polyline drawn as a uGUI graphic (graphs, nav scope paths, tick marks).</summary>
    public sealed class UiPolyline : MaskableGraphic
    {
        public readonly List<Vector2> Points = new List<Vector2>();
        public float Thickness = 3f;
        public bool Closed;

        public void SetDirty() => SetVerticesDirty();

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            int n = Points.Count;
            if (n < 2) return;
            int segs = Closed ? n : n - 1;
            for (int i = 0; i < segs; i++)
            {
                Vector2 a = Points[i], b = Points[(i + 1) % n];
                Vector2 d = b - a;
                if (d.sqrMagnitude < 1e-6f) continue;
                Vector2 nrm = new Vector2(-d.y, d.x).normalized * Thickness * 0.5f;
                // Extend each segment by half the thickness so joints overlap without gaps.
                Vector2 ext = d.normalized * Thickness * 0.5f;
                int k = vh.currentVertCount;
                var v = UIVertex.simpleVert;
                v.color = color;
                v.position = a - ext - nrm; vh.AddVert(v);
                v.position = a - ext + nrm; vh.AddVert(v);
                v.position = b + ext + nrm; vh.AddVert(v);
                v.position = b + ext - nrm; vh.AddVert(v);
                vh.AddTriangle(k, k + 1, k + 2);
                vh.AddTriangle(k, k + 2, k + 3);
            }
        }
    }

    /// <summary>A button that reports press-and-hold (BURN, STAB, BOOST) and taps.</summary>
    public sealed class HoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public bool Held { get; private set; }
        public bool Toggled;          // for toggle-style buttons
        public bool IsToggle;
        public event Action Tapped;
        public Image Background;
        public Text Caption;
        public Color ActiveColor = Palette.ButtonActive;
        public Color IdleColor = Palette.Button;
        int pointer = int.MinValue;

        public void OnPointerDown(PointerEventData e)
        {
            Held = true;
            pointer = e.pointerId;
            if (IsToggle) Toggled = !Toggled;
            Tapped?.Invoke();
        }

        public void OnPointerUp(PointerEventData e) { if (e.pointerId == pointer) { Held = false; pointer = int.MinValue; } }
        public void OnPointerExit(PointerEventData e) { /* keep holding while the finger slides off */ }

        public void Release() { Held = false; pointer = int.MinValue; }

        public void Refresh(bool active)
        {
            if (Background != null) Background.color = active ? ActiveColor : IdleColor;
        }
    }

    /// <summary>Floating virtual joystick: the stick centre appears where the finger lands.</summary>
    public sealed class VirtualStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        public RectTransform Base, Knob;
        public float Radius = 150f;
        public Vector2 Value { get; private set; }
        public bool Active { get; private set; }
        RectTransform area;
        Vector2 origin;
        Vector2 restPos;

        void Awake()
        {
            area = (RectTransform)transform;
        }

        /// <summary>Resting position relative to the lower-left corner of the touch area.</summary>
        public Vector2 RestOffset = new Vector2(300f, 260f);

        void LateUpdate()
        {
            // Recomputed every frame: the area's size is only final after the canvas has been scaled.
            Rect r = area.rect;
            restPos = new Vector2(r.xMin + RestOffset.x, r.yMin + RestOffset.y);
            if (!Active) { Base.anchoredPosition = restPos; Knob.anchoredPosition = restPos; }
        }

        Vector2 Local(PointerEventData e)
        {
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, e.position, e.pressEventCamera, out var p);
            return p;
        }

        public void OnPointerDown(PointerEventData e)
        {
            Active = true;
            origin = Local(e);
            Base.anchoredPosition = origin;
            Knob.anchoredPosition = origin;
            Value = Vector2.zero;
        }

        public void OnDrag(PointerEventData e)
        {
            Vector2 d = Local(e) - origin;
            d = Vector2.ClampMagnitude(d, Radius);
            Knob.anchoredPosition = origin + d;
            // Small dead zone, then a gentle response curve for fine corrections.
            Vector2 v = d / Radius;
            float m = v.magnitude;
            float dz = 0.08f;
            Value = m < dz ? Vector2.zero : v.normalized * Mathf.Pow((m - dz) / (1f - dz), 1.4f);
        }

        public void OnPointerUp(PointerEventData e)
        {
            Active = false;
            Value = Vector2.zero;
            Base.anchoredPosition = restPos;
            Knob.anchoredPosition = restPos;
        }
    }
}
