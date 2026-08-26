using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GGTools.TMProUltilitis
{
    /// <summary>
    /// Drives an authored <see cref="MobileInputPreviewView"/> so it floats above the native touch keyboard
    /// whenever a <see cref="TMP_InputField"/> gets selected, letting the player read what is being typed
    /// even when the keyboard covers the field.
    ///
    /// The manager owns behaviour only: when to show, how high to sit, and what text to mirror.
    /// The art is a child of this GameObject and is entirely yours.
    ///
    /// No per-field setup is needed: every <see cref="TMP_InputField"/> in the project is covered, unless
    /// a <see cref="MobileInputPreviewTarget"/> opts it out.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("GGTools/TMPro Ultilitis/Mobile Input Preview")]
    public class MobileInputPreview : MonoBehaviour
    {
        private const string LogPrefix = "[MobileInputPreview] ";
        private const float BaselineInterval = 0.5f;

        /// <summary>
        /// Optional prefab name under any Resources folder. When present, the manager spawns itself after the
        /// first scene loads, so the feature works without placing anything in a scene by hand.
        /// </summary>
        public const string AutoSpawnResourcePath = "GGToolsMobileInputPreview";

        [Header("View")]
        [Tooltip("Authored art driven by this manager. Auto-filled from the children when left empty.")]
        [SerializeField] private MobileInputPreviewView view;

        [Header("Lifetime")]
        [Tooltip("Keep the manager and its art alive across scene loads.")]
        [SerializeField] private bool dontDestroyOnLoad = true;

        [Header("Behaviour")]
        [SerializeField] private MobileInputPreviewSettings settings = new MobileInputPreviewSettings();

        private readonly KeyboardHeightSensor keyboardSensor = new KeyboardHeightSensor();

        private GameObject lastSelected;
        private TMP_InputField attachedField;
        private bool hasField;
        private bool warnedMissingEventSystem;
        private bool warnedMissingView;
        private bool warnedAboutBarRect;
        private float nextBaselineTime;

        private string diagnostics = string.Empty;
        private float nextDiagnosticsLogTime;
        private GUIStyle diagnosticsStyle;

        /// <summary>The live instance, or null while none exists.</summary>
        public static MobileInputPreview Instance { get; private set; }

        /// <summary>Runtime behaviour configuration.</summary>
        public MobileInputPreviewSettings Settings => settings;

        /// <summary>The authored art this manager drives.</summary>
        public MobileInputPreviewView View => view;

        /// <summary>The field currently being previewed, or null when the view is hidden.</summary>
        public TMP_InputField CurrentField => attachedField;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoSpawn()
        {
            if (Instance != null || FindAnyObjectByType<MobileInputPreview>() != null)
            {
                return;
            }

            GameObject prefab = Resources.Load<GameObject>(AutoSpawnResourcePath);
            if (prefab == null)
            {
                // No prefab, no art. The manager is expected to be authored in a scene instead.
                return;
            }

            Instantiate(prefab).name = prefab.name;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            keyboardSensor.Configure(settings);

            if (view == null)
            {
                view = GetComponentInChildren<MobileInputPreviewView>(true);
            }

            if (view != null)
            {
                view.DismissRequested += OnDismissRequested;
            }

            if (dontDestroyOnLoad)
            {
                DontDestroyOnLoad(gameObject);
            }
        }

        private void OnEnable()
        {
            if (Instance != this)
            {
                return;
            }

            HideView();
        }

        private void OnDisable()
        {
            if (Instance != this)
            {
                return;
            }

            Detach();
            HideView();
        }

        private void OnDestroy()
        {
            if (Instance != this)
            {
                return;
            }

            Detach();
            keyboardSensor.Dispose();

            if (view != null)
            {
                view.DismissRequested -= OnDismissRequested;
            }

            Instance = null;
        }

        private void Update()
        {
            if (Instance != this)
            {
                return;
            }

            // Before any early return: when something is misconfigured, that is exactly when the dump matters.
            UpdateDiagnostics();

            if (!HasUsableView() || !IsActivationAllowed())
            {
                Detach();
                lastSelected = null;
                return;
            }

            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                if (!warnedMissingEventSystem)
                {
                    warnedMissingEventSystem = true;
                    Debug.LogWarning(LogPrefix + "No EventSystem in the scene, so no TMP_InputField can be selected. The preview stays hidden.");
                }

                Detach();
                lastSelected = null;
                return;
            }

            warnedMissingEventSystem = false;

            GameObject selected = eventSystem.currentSelectedGameObject;
            if (selected != lastSelected)
            {
                lastSelected = selected;
                Detach();

                if (selected != null && selected.TryGetComponent(out TMP_InputField field) && ShouldPreview(field))
                {
                    Attach(field);
                }
            }
            else if (hasField && attachedField == null)
            {
                // The attached field was destroyed. Unity's fake-null makes the comparison above miss it,
                // because a destroyed GameObject compares equal to a plain null selection.
                Detach();
                lastSelected = null;
            }

            if (!hasField && Time.unscaledTime >= nextBaselineTime)
            {
                // With no field focused the keyboard is closed, so whatever covers the bottom of the window
                // right now is the navigation bar. Remember it, to subtract it from later measurements.
                nextBaselineTime = Time.unscaledTime + BaselineInterval;
                keyboardSensor.CaptureBaseline();
            }
        }

        private void LateUpdate()
        {
            if (Instance != this || !hasField)
            {
                return;
            }

            if (attachedField == null || !attachedField.isActiveAndEnabled)
            {
                Detach();
                return;
            }

            Refresh();
        }

        /// <summary>Pushes the current keyboard height and text into the view.</summary>
        private void Refresh()
        {
            view.SetBottomOffset(GetBottomOffset());
            view.SetText(BuildPreviewString(attachedField));
        }

        /// <summary>
        /// Rebuilds the diagnostics dump. Runs whether or not a field is focused, so the values can be read
        /// before, during and after the keyboard opens.
        /// </summary>
        private void UpdateDiagnostics()
        {
            if (!settings.showDiagnostics)
            {
                if (diagnostics.Length > 0)
                {
                    diagnostics = string.Empty;

                    if (view != null)
                    {
                        view.SetDiagnostics(null);
                    }
                }

                return;
            }

            float scaleFactor = view != null ? view.CanvasScaleFactor : 1f;
            diagnostics = keyboardSensor.BuildDiagnostics(GetBottomOffset(), scaleFactor)
                          + " field=" + (hasField ? 1 : 0)
                          + " act=" + (IsActivationAllowed() ? 1 : 0)
                          + " view=" + (view != null && view.IsReady ? 1 : 0)
                          + " tsk=" + (TouchScreenKeyboard.isSupported ? 1 : 0)
                          + " bot=" + (view != null ? Mathf.RoundToInt(view.LastMeasuredBottomPixels) : 0)
                          + " corr=" + (view != null ? Mathf.RoundToInt(view.LastCorrectionPixels) : 0);

            if (view != null && view.HasDiagnosticsLabel)
            {
                view.SetDiagnostics(diagnostics);
            }

            // Also goes to the console, so it shows up in `adb logcat -s Unity` on a development build.
            if (Time.unscaledTime >= nextDiagnosticsLogTime)
            {
                nextDiagnosticsLogTime = Time.unscaledTime + 1f;
                Debug.Log(LogPrefix + diagnostics);
            }
        }

        /// <summary>
        /// Draws the diagnostics dump straight on screen when no <c>diagnosticsLabel</c> was wired.
        /// IMGUI, so it needs no prefab and no canvas, and it works in any build.
        /// </summary>
        private void OnGUI()
        {
            if (Instance != this || !settings.showDiagnostics || diagnostics.Length == 0)
            {
                return;
            }

            // The geometry overlay always draws: it is the whole point of the debug mode.
            DrawGeometryOverlay();

            if (view != null && view.HasDiagnosticsLabel)
            {
                return;
            }

            if (diagnosticsStyle == null)
            {
                diagnosticsStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = Mathf.Max(14, Screen.height / 45),
                    wordWrap = true,
                    alignment = TextAnchor.UpperLeft
                };
                diagnosticsStyle.normal.textColor = Color.white;
            }

            string text = diagnostics;
            if (view != null)
            {
                text += "\n" + view.BuildGeometryDump();
            }

            float width = Screen.width - 20f;
            float height = diagnosticsStyle.CalcHeight(new GUIContent(text), width) + 10f;
            Rect box = new Rect(10f, 10f, width, height);

            Color previous = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.7f);
            GUI.DrawTexture(box, Texture2D.whiteTexture);
            GUI.color = previous;

            GUI.Label(new Rect(box.x + 5f, box.y + 5f, width - 10f, height), text, diagnosticsStyle);
        }

        /// <summary>
        /// Draws what the code believes the geometry is, straight over the real pixels.
        ///
        /// <para>
        /// Red line: where the top of the keyboard was measured.
        /// Green box: the content bounds the positioning code is aligning, children included.
        /// Yellow box: barRect's own rect, children excluded.
        /// </para>
        /// If the green box does not hug the visible bar, the bounds are not seeing the real art.
        /// </summary>
        private void DrawGeometryOverlay()
        {
            if (view == null)
            {
                return;
            }

            float keyboardTop = GetBottomOffset();
            DrawScreenLine(new Rect(0f, Screen.height - keyboardTop - 1f, Screen.width, 3f), Color.red);

            if (view.TryGetContentScreenRect(true, out Rect withKids))
            {
                DrawScreenOutline(withKids, Color.green);
            }

            if (view.TryGetContentScreenRect(false, out Rect ownOnly))
            {
                DrawScreenOutline(ownOnly, Color.yellow);
            }
        }

        /// <summary>Outlines a rect given in screen pixels with the origin at the bottom left.</summary>
        private static void DrawScreenOutline(Rect screenRect, Color color)
        {
            // Screen space has y up, IMGUI has y down.
            float top = Screen.height - screenRect.yMax;
            float height = screenRect.height;

            DrawScreenLine(new Rect(screenRect.xMin, top, screenRect.width, 2f), color);
            DrawScreenLine(new Rect(screenRect.xMin, top + height - 2f, screenRect.width, 2f), color);
            DrawScreenLine(new Rect(screenRect.xMin, top, 2f, height), color);
            DrawScreenLine(new Rect(screenRect.xMax - 2f, top, 2f, height), color);
        }

        private static void DrawScreenLine(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private bool HasUsableView()
        {
            if (view != null && view.IsReady)
            {
                WarnAboutBarRectOnce();
                warnedMissingView = false;
                return true;
            }

            if (!warnedMissingView)
            {
                warnedMissingView = true;

                string reason = view == null
                    ? "no MobileInputPreviewView found among the children"
                    : view.MissingReferencesMessage;

                Debug.LogWarning(LogPrefix + "Preview disabled: " + reason + ".", this);
            }

            return false;
        }

        /// <summary>
        /// The references are all wired, but barRect may still point at the wrong object. Pointing it at the
        /// canvas root is the common mistake and it makes the bar land half under the keyboard.
        /// </summary>
        private void WarnAboutBarRectOnce()
        {
            if (warnedAboutBarRect)
            {
                return;
            }

            string problem = view.BarRectProblem;
            if (problem == null)
            {
                return;
            }

            warnedAboutBarRect = true;
            Debug.LogWarning(LogPrefix + problem + ".", view);
        }

        private bool IsActivationAllowed()
        {
            switch (settings.activationMode)
            {
                case PreviewActivationMode.Never:
                    return false;
                case PreviewActivationMode.Always:
                    return true;
                default:
                    return TouchScreenKeyboard.isSupported || (Application.isEditor && settings.simulateInEditor);
            }
        }

        private bool ShouldPreview(TMP_InputField field)
        {
            if (!field.isActiveAndEnabled || !field.interactable)
            {
                return false;
            }

            if (field.TryGetComponent(out MobileInputPreviewTarget target) && !target.enablePreview)
            {
                return false;
            }

            return true;
        }

        private void Attach(TMP_InputField field)
        {
            attachedField = field;
            hasField = true;
            attachedField.onSubmit.AddListener(OnFieldSubmitted);

            Refresh();
            view.SetVisible(true);
        }

        private void Detach()
        {
            if (!hasField)
            {
                return;
            }

            if (attachedField != null)
            {
                attachedField.onSubmit.RemoveListener(OnFieldSubmitted);
            }

            attachedField = null;
            hasField = false;
            HideView();
        }

        private void HideView()
        {
            if (view != null)
            {
                view.SetVisible(false);
            }
        }

        private void OnFieldSubmitted(string _)
        {
            Detach();
        }

        private void OnDismissRequested()
        {
            TMP_InputField field = attachedField;
            Detach();

            if (field != null)
            {
                field.DeactivateInputField();
            }

            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }

            lastSelected = null;
        }

        /// <summary>
        /// Distance from the bottom of the screen where the bar should sit, in real pixels.
        /// </summary>
        private float GetBottomOffset()
        {
            float offset = keyboardSensor.GetHeightPixels() + settings.extraKeyboardMargin;

            if (settings.respectSafeArea)
            {
                // Never stack the two offsets: the keyboard already covers the bottom inset when it is open.
                offset = Mathf.Max(offset, Screen.safeArea.yMin);
            }

            return offset;
        }

        /// <summary>
        /// Builds the string shown in the view: masked when the field is a password, windowed around the
        /// caret when it is longer than the bar can fit, and with a blinking caret glyph inserted.
        /// </summary>
        private string BuildPreviewString(TMP_InputField field)
        {
            string raw = field.text ?? string.Empty;

            if (field.contentType == TMP_InputField.ContentType.Password)
            {
                raw = new string(field.asteriskChar, raw.Length);
            }

            int caret = Mathf.Clamp(field.caretPosition, 0, raw.Length);

            // The native mobile keyboard does not always sync the caret back, so assume end of text.
            if (caret == 0 && raw.Length > 0)
            {
                caret = raw.Length;
            }

            int window = Mathf.Max(8, settings.charWindow);
            if (raw.Length > window)
            {
                int start = Mathf.Clamp(caret - window / 2, 0, raw.Length - window);
                string windowed = raw.Substring(start, window);
                caret -= start;

                if (start + window < raw.Length)
                {
                    windowed += "…";
                }

                if (start > 0)
                {
                    windowed = "…" + windowed;
                    caret++;
                }

                raw = windowed;
            }

            if (string.IsNullOrEmpty(settings.caretGlyph))
            {
                return raw;
            }

            string glyph = IsCaretVisible() ? settings.caretGlyph : new string(' ', settings.caretGlyph.Length);
            return raw.Insert(Mathf.Clamp(caret, 0, raw.Length), glyph);
        }

        private bool IsCaretVisible()
        {
            if (settings.caretBlinkRate <= 0f)
            {
                return true;
            }

            return Mathf.Repeat(Time.unscaledTime, settings.caretBlinkRate * 2f) < settings.caretBlinkRate;
        }
    }
}
