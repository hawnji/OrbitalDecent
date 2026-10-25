// Minimal signatures of the uGUI package (com.unity.ugui) used by the runtime scripts, so the
// compile check can run without the Unity editor. Bodies are empty; Unity uses the real package.
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace UnityEngine.UI
{
    public class VertexHelper
    {
        public int currentVertCount => 0;
        public void Clear() { }
        public void AddVert(Vector3 position, Color32 color, Vector2 uv0) { }
        public void AddVert(UIVertex v) { }
        public void AddTriangle(int idx0, int idx1, int idx2) { }
        public void AddUIVertexQuad(UIVertex[] verts) { }
    }

    public abstract class UIBehaviour : MonoBehaviour
    {
        protected virtual void Awake() { }
        protected virtual void OnEnable() { }
        protected virtual void OnDisable() { }
        protected virtual void Start() { }
        protected virtual void OnRectTransformDimensionsChange() { }
    }

    public abstract class Graphic : UIBehaviour
    {
        public virtual Color color { get; set; }
        public virtual bool raycastTarget { get; set; }
        public virtual Material material { get; set; }
        public RectTransform rectTransform => null;
        public virtual Texture mainTexture => null;
        public virtual void SetVerticesDirty() { }
        public virtual void SetAllDirty() { }
        public virtual void SetMaterialDirty() { }
        protected virtual void OnPopulateMesh(VertexHelper vh) { }
        public void CrossFadeAlpha(float alpha, float duration, bool ignoreTimeScale) { }
    }

    public abstract class MaskableGraphic : Graphic { public bool maskable { get; set; } }

    public class Image : MaskableGraphic
    {
        public enum Type { Simple, Sliced, Tiled, Filled }
        public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 }
        public Sprite sprite { get; set; }
        public Type type { get; set; }
        public FillMethod fillMethod { get; set; }
        public float fillAmount { get; set; }
        public int fillOrigin { get; set; }
        public bool preserveAspect { get; set; }
        public float pixelsPerUnitMultiplier { get; set; }
    }

    public class RawImage : MaskableGraphic
    {
        public Texture texture { get; set; }
        public Rect uvRect { get; set; }
    }

    public class Text : MaskableGraphic
    {
        public string text { get; set; }
        public Font font { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public bool supportRichText { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
        public float lineSpacing { get; set; }
        public bool resizeTextForBestFit { get; set; }
        public float preferredWidth => 0;
        public float preferredHeight => 0;
    }

    public class CanvasScaler : UIBehaviour
    {
        public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize, ConstantPhysicalSize }
        public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink }
        public ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public ScreenMatchMode screenMatchMode { get; set; }
        public float matchWidthOrHeight { get; set; }
        public float scaleFactor { get; set; }
    }

    public class GraphicRaycaster : MonoBehaviour { }

    public abstract class BaseMeshEffect : UIBehaviour { public abstract void ModifyMesh(VertexHelper vh); }

    public class Shadow : BaseMeshEffect
    {
        public Color effectColor { get; set; }
        public Vector2 effectDistance { get; set; }
        public override void ModifyMesh(VertexHelper vh) { }
    }

    public class Outline : Shadow { }

    public struct ColorBlock
    {
        public Color normalColor { get; set; }
        public Color highlightedColor { get; set; }
        public Color pressedColor { get; set; }
        public Color selectedColor { get; set; }
        public Color disabledColor { get; set; }
        public float colorMultiplier { get; set; }
        public float fadeDuration { get; set; }
    }

    public class Selectable : UIBehaviour
    {
        public Graphic targetGraphic { get; set; }
        public bool interactable { get; set; }
        public ColorBlock colors { get; set; }
    }

    public class Button : Selectable
    {
        public class ButtonClickedEvent : UnityEvent { }
        public ButtonClickedEvent onClick { get; set; } = new ButtonClickedEvent();
    }

    public class Slider : Selectable
    {
        public class SliderEvent : UnityEvent<float> { }
        public RectTransform fillRect { get; set; }
        public RectTransform handleRect { get; set; }
        public float minValue { get; set; }
        public float maxValue { get; set; }
        public bool wholeNumbers { get; set; }
        public float value { get; set; }
        public SliderEvent onValueChanged { get; set; } = new SliderEvent();
    }

    public class Toggle : Selectable
    {
        public class ToggleEvent : UnityEvent<bool> { }
        public Graphic graphic { get; set; }
        public bool isOn { get; set; }
        public ToggleEvent onValueChanged { get; set; } = new ToggleEvent();
    }
}

namespace UnityEngine.EventSystems
{
    public class UIBehaviourBase : MonoBehaviour { }
    public class EventSystem : MonoBehaviour { public static EventSystem current { get; set; } }
    public abstract class BaseInputModule : MonoBehaviour { }
    public class StandaloneInputModule : BaseInputModule { }
    public class BaseEventData { }
    public class PointerEventData : BaseEventData
    {
        public Vector2 position { get; set; }
        public int pointerId { get; set; }
        public Camera pressEventCamera => null;
    }
    public interface IEventSystemHandler { }
    public interface IPointerDownHandler : IEventSystemHandler { void OnPointerDown(PointerEventData eventData); }
    public interface IPointerUpHandler : IEventSystemHandler { void OnPointerUp(PointerEventData eventData); }
    public interface IPointerExitHandler : IEventSystemHandler { void OnPointerExit(PointerEventData eventData); }
    public interface IDragHandler : IEventSystemHandler { void OnDrag(PointerEventData eventData); }
}
