using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace GGTools.TMProUltilitis
{
    /// <summary>
    /// Authored art for the mobile input preview. Put this component on the root of your own UI
    /// hierarchy and drop that hierarchy as a child of <see cref="MobileInputPreview"/>.
    ///
    /// The manager never creates or styles anything: it only asks this view to show/hide, to move to a
    /// given height above the keyboard, and to display a string. Everything visual (sprites, colors,
    /// fonts, layout, animation) belongs to the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("GGTools/TMPro Ultilitis/Mobile Input Preview View")]
    public class MobileInputPreviewView : MonoBehaviour
    {
        [Header("Required")]
        [Tooltip("The bar itself. Must be anchored to the bottom edge: the manager only writes its anchoredPosition.y.")]
        [SerializeField] private RectTransform barRect;

        [Tooltip("Label that mirrors what is being typed, caret included.")]
        [SerializeField] private TMP_Text previewLabel;

        [Tooltip("Align the bottom of everything inside barRect, children included, against the keyboard. Turn off to align barRect's own rect instead, ignoring children that overflow it. Keep it off if barRect contains a full screen backdrop, which would push the bar way up.")]
        [SerializeField] private bool alignWholeContent = true;

        [Header("Optional")]
        [Tooltip("Canvas of this view. Used to convert pixels into reference units and to switch the whole overlay off. Auto-filled from the parents when left empty.")]
        [SerializeField] private Canvas canvas;

        [Tooltip("Full screen backdrop shown behind the bar. Toggled together with the bar.")]
        [SerializeField] private GameObject background;

        [Tooltip("Closes the keyboard. Leave empty if the art has no confirm button.")]
        [SerializeField] private Button doneButton;

        [Tooltip("Button on the backdrop. Tapping it closes the keyboard as well.")]
        [SerializeField] private Button backgroundButton;

        [Header("Debug")]
        [Tooltip("Optional label that prints the measured keyboard values on device. Only filled when showDiagnostics is on.")]
        [SerializeField] private TMP_Text diagnosticsLabel;

        private bool listenersBound;

        /// <summary>Raised when the player asks to close the keyboard, from the Done button or the backdrop.</summary>
        public event Action DismissRequested;

        /// <summary>True when the mandatory references are wired.</summary>
        public bool IsReady => barRect != null && previewLabel != null;

        /// <summary>Description of what is missing, for the warning the manager logs.</summary>
        public string MissingReferencesMessage
        {
            get
            {
                if (barRect == null && previewLabel == null)
                {
                    return "barRect and previewLabel are not assigned";
                }

                if (barRect == null)
                {
                    return "barRect is not assigned";
                }

                return previewLabel == null ? "previewLabel is not assigned" : string.Empty;
            }
        }

        /// <summary>
        /// Describes why <see cref="barRect"/> cannot be the bar, or null when it looks sane.
        /// The usual mistake is pointing it at the canvas root instead of the bar inside it: the whole
        /// canvas then gets moved, and since a canvas root has pivot 0.5 the bar ends up cut in half.
        /// </summary>
        public string BarRectProblem
        {
            get
            {
                if (barRect == null)
                {
                    return "barRect is not assigned";
                }

                if (barRect.GetComponent<Canvas>() != null)
                {
                    return "barRect points at a Canvas. Assign the bar INSIDE the canvas, not the canvas root";
                }

                if (!(barRect.parent is RectTransform))
                {
                    return "barRect has no RectTransform parent, so it is a canvas root. Assign the bar INSIDE the canvas";
                }

                if (canvas != null && canvas.transform is RectTransform canvasRect)
                {
                    float canvasHeight = canvasRect.rect.height;
                    if (canvasHeight > 1f && barRect.rect.height > canvasHeight * 0.7f)
                    {
                        return "barRect is " + Mathf.RoundToInt(barRect.rect.height) + " tall against a canvas of " +
                               Mathf.RoundToInt(canvasHeight) + ". That is a full screen container, not a bar";
                    }
                }

                return null;
            }
        }

        private void Reset()
        {
            canvas = GetComponentInParent<Canvas>();
            previewLabel = GetComponentInChildren<TMP_Text>(true);
            doneButton = GetComponentInChildren<Button>(true);

            // The bar is the label's container, never this component's own transform: putting the view on a
            // Canvas and pointing barRect at it would move the whole canvas root instead of the bar.
            if (previewLabel != null && previewLabel.transform.parent is RectTransform labelParent && labelParent != transform)
            {
                barRect = labelParent;
            }
        }

        private void Awake()
        {
            if (canvas == null)
            {
                canvas = GetComponentInParent<Canvas>();
            }

            BindListeners();
        }

        private void OnDestroy()
        {
            UnbindListeners();
        }

        private void BindListeners()
        {
            if (listenersBound)
            {
                return;
            }

            listenersBound = true;

            if (doneButton != null)
            {
                doneButton.onClick.AddListener(RaiseDismiss);
            }

            if (backgroundButton != null)
            {
                backgroundButton.onClick.AddListener(RaiseDismiss);
            }
        }

        private void UnbindListeners()
        {
            if (!listenersBound)
            {
                return;
            }

            listenersBound = false;

            if (doneButton != null)
            {
                doneButton.onClick.RemoveListener(RaiseDismiss);
            }

            if (backgroundButton != null)
            {
                backgroundButton.onClick.RemoveListener(RaiseDismiss);
            }
        }

        private void RaiseDismiss()
        {
            DismissRequested?.Invoke();
        }

        /// <summary>Shows or hides the bar and its backdrop.</summary>
        public void SetVisible(bool visible)
        {
            if (canvas != null)
            {
                canvas.enabled = visible;
            }

            if (background != null && background.activeSelf != visible)
            {
                background.SetActive(visible);
            }

            if (barRect != null && barRect.gameObject.activeSelf != visible)
            {
                barRect.gameObject.SetActive(visible);
            }
        }

        /// <summary>Writes the mirrored text, caret included.</summary>
        public void SetText(string text)
        {
            if (previewLabel != null)
            {
                previewLabel.text = text;
            }
        }

        /// <summary>Canvas scale factor, or 1 when there is no canvas. Exposed for the diagnostics dump.</summary>
        public float CanvasScaleFactor => canvas != null && canvas.scaleFactor > 0.0001f ? canvas.scaleFactor : 1f;

        /// <summary>True when a label was wired to receive the diagnostics dump.</summary>
        public bool HasDiagnosticsLabel => diagnosticsLabel != null;

        /// <summary>Where the bottom edge of the bar actually ended up, in screen pixels. For diagnostics.</summary>
        public float LastMeasuredBottomPixels { get; private set; }

        /// <summary>Correction applied on the last call, in screen pixels. Settles at ~0. For diagnostics.</summary>
        public float LastCorrectionPixels { get; private set; }

        /// <summary>
        /// Puts the BOTTOM EDGE of the bar at <paramref name="bottomOffsetPixels"/> above the bottom of the
        /// screen.
        ///
        /// <para>
        /// Works by measuring where the bar currently is on screen and nudging it by the difference, instead
        /// of computing an absolute position from pivots and anchors. That makes it immune to how the art is
        /// authored: any pivot, any anchor, stretched rects, layout groups, children that overflow the rect,
        /// letterboxing, Screen Space - Overlay or Camera. It also self-corrects, so a wrong frame fixes
        /// itself on the next one.
        /// </para>
        /// </summary>
        /// <param name="bottomOffsetPixels">Distance from the bottom of the screen, in real pixels.</param>
        public void SetBottomOffset(float bottomOffsetPixels)
        {
            if (barRect == null)
            {
                return;
            }

            if (!(barRect.parent is RectTransform parent))
            {
                // barRect is the canvas root: nothing to measure against, fall back to the direct math.
                SetBottomOffsetDirect(bottomOffsetPixels);
                return;
            }

            Camera eventCamera = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            // Where the content sits right now, in the parent's local space.
            Bounds bounds = alignWholeContent
                ? RectTransformUtility.CalculateRelativeRectTransformBounds(parent, barRect)
                : GetOwnBoundsInParent(parent);

            float currentBottomLocal = bounds.min.y;

            // Where it should sit: the same screen height, expressed in that same local space.
            Vector2 screenPoint = new Vector2(Screen.width * 0.5f, bottomOffsetPixels);
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenPoint, eventCamera, out Vector2 localPoint))
            {
                SetBottomOffsetDirect(bottomOffsetPixels);
                return;
            }

            float delta = localPoint.y - currentBottomLocal;

            LastMeasuredBottomPixels = bottomOffsetPixels - delta * CanvasScaleFactor;
            LastCorrectionPixels = delta * CanvasScaleFactor;

            if (Mathf.Abs(delta) > 0.01f)
            {
                barRect.anchoredPosition += new Vector2(0f, delta);
            }
        }

        /// <summary>
        /// Where the code believes the content is, in screen pixels with the origin at the bottom left.
        /// Returns false when it cannot be computed. Used to draw the debug outline.
        /// </summary>
        public bool TryGetContentScreenRect(bool includeChildren, out Rect screenRect)
        {
            screenRect = default;

            if (barRect == null || !(barRect.parent is RectTransform parent))
            {
                return false;
            }

            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? canvas.worldCamera
                : null;

            Bounds bounds = includeChildren
                ? RectTransformUtility.CalculateRelativeRectTransformBounds(parent, barRect)
                : GetOwnBoundsInParent(parent);

            Vector2 min = RectTransformUtility.WorldToScreenPoint(cam, parent.TransformPoint(bounds.min));
            Vector2 max = RectTransformUtility.WorldToScreenPoint(cam, parent.TransformPoint(bounds.max));

            screenRect = Rect.MinMaxRect(
                Mathf.Min(min.x, max.x),
                Mathf.Min(min.y, max.y),
                Mathf.Max(min.x, max.x),
                Mathf.Max(min.y, max.y));

            return true;
        }

        /// <summary>Raw RectTransform geometry, for the diagnostics dump.</summary>
        public string BuildGeometryDump()
        {
            if (barRect == null)
            {
                return "barRect=null";
            }

            string problem = BarRectProblem;
            string prefix = problem == null ? string.Empty : ">>> " + problem + " <<<\n";

            string dump = prefix +
                "rect=" + Mathf.RoundToInt(barRect.rect.width) + "x" + Mathf.RoundToInt(barRect.rect.height) +
                " pv=" + barRect.pivot.y.ToString("0.00") +
                " anc=" + barRect.anchorMin.y.ToString("0.00") + "/" + barRect.anchorMax.y.ToString("0.00") +
                " ap=" + Mathf.RoundToInt(barRect.anchoredPosition.y) +
                " kids=" + barRect.childCount +
                " whole=" + (alignWholeContent ? 1 : 0);

            if (TryGetContentScreenRect(true, out Rect withKids))
            {
                dump += " all=" + Mathf.RoundToInt(withKids.yMin) + ".." + Mathf.RoundToInt(withKids.yMax);
            }

            if (TryGetContentScreenRect(false, out Rect ownOnly))
            {
                dump += " own=" + Mathf.RoundToInt(ownOnly.yMin) + ".." + Mathf.RoundToInt(ownOnly.yMax);
            }

            if (previewLabel != null)
            {
                dump += " lbl=" + Mathf.RoundToInt(previewLabel.rectTransform.rect.height);
            }

            return dump;
        }

        /// <summary>Bounds of barRect alone, ignoring children, in the parent's local space.</summary>
        private Bounds GetOwnBoundsInParent(RectTransform parent)
        {
            Rect rect = barRect.rect;
            Vector3 bottomLeft = parent.InverseTransformPoint(barRect.TransformPoint(new Vector3(rect.xMin, rect.yMin, 0f)));
            Vector3 topRight = parent.InverseTransformPoint(barRect.TransformPoint(new Vector3(rect.xMax, rect.yMax, 0f)));

            Bounds bounds = new Bounds(bottomLeft, Vector3.zero);
            bounds.Encapsulate(topRight);
            return bounds;
        }

        /// <summary>
        /// Absolute placement from pivot and anchors. Only used when the measured path is unavailable.
        /// </summary>
        private void SetBottomOffsetDirect(float bottomOffsetPixels)
        {
            float canvasBottomPixels = 0f;
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay && canvas.worldCamera != null)
            {
                canvasBottomPixels = canvas.worldCamera.pixelRect.yMin;
            }

            float offsetInUnits = (bottomOffsetPixels - canvasBottomPixels) / CanvasScaleFactor;
            float pivotCompensation = barRect.pivot.y * barRect.rect.height;

            Vector2 position = barRect.anchoredPosition;
            position.y = offsetInUnits + pivotCompensation;
            barRect.anchoredPosition = position;

            LastMeasuredBottomPixels = bottomOffsetPixels;
            LastCorrectionPixels = 0f;
        }

        /// <summary>Writes the diagnostics dump, or clears the label when diagnostics are off.</summary>
        public void SetDiagnostics(string text)
        {
            if (diagnosticsLabel == null)
            {
                return;
            }

            if (diagnosticsLabel.gameObject.activeSelf != (text != null))
            {
                diagnosticsLabel.gameObject.SetActive(text != null);
            }

            if (text != null)
            {
                diagnosticsLabel.text = text;
            }
        }

    }
}
