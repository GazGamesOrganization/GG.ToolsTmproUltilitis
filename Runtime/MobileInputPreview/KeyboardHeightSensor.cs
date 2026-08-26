using System;
using System.Text;
using UnityEngine;

namespace GGTools.TMProUltilitis
{
    /// <summary>
    /// How the keyboard height is measured.
    /// </summary>
    public enum KeyboardHeightSource
    {
        /// <summary>Picks the best source for the current platform.</summary>
        Auto = 0,
        /// <summary>Android only: measures the window visible frame through JNI. Exact on every device.</summary>
        AndroidVisibleFrame = 1,
        /// <summary>Uses TouchScreenKeyboard.area. Unreliable on many Android devices, absent on WebGL.</summary>
        TouchScreenKeyboardArea = 2,
        /// <summary>Ignores every measurement and uses fallbackKeyboardHeightPercent.</summary>
        FixedPercent = 3,
        /// <summary>Editor only: fakes a keyboard with simulatedKeyboardHeightPercent.</summary>
        EditorSimulated = 4,
        /// <summary>WebGL only: asks the browser through the viewport bridge.</summary>
        WebGLViewport = 5
    }

    /// <summary>
    /// Measures how many screen pixels the soft keyboard covers at the bottom of the screen.
    ///
    /// <para>
    /// <see cref="TouchScreenKeyboard.area"/> is not trustworthy: several Android devices, tablets in
    /// particular, report a zero rect or a stale value. On Android this sensor instead asks the decor view
    /// for its visible display frame, which is the same measurement the Android framework itself uses and
    /// is exact on every device.
    /// </para>
    /// </summary>
    internal sealed class KeyboardHeightSensor : IDisposable
    {
        private const float PollInterval = 0.05f;

        private MobileInputPreviewSettings settings;

        private float nextPollTime;
        private float cachedHeight;

        // Bottom inset present while the keyboard is closed: navigation bar, gesture bar, etc.
        // Subtracted from the measurement so only the keyboard is left.
        private float baselineInset;
        private bool hasBaseline;

        // Explicit initializers: on non-Android builds these are only read, never written.
        private float lastRawInset = 0f;
        private float lastAreaHeight = 0f;

#if UNITY_ANDROID && !UNITY_EDITOR
        private bool androidBridgeFailed;
        private AndroidJavaObject decorView;
        private AndroidJavaObject visibleFrame;
#endif

        /// <summary>The source actually used on the last measurement.</summary>
        public KeyboardHeightSource ResolvedSource { get; private set; }

        public void Configure(MobileInputPreviewSettings config)
        {
            settings = config;
            nextPollTime = 0f;
        }

        /// <summary>
        /// Records the bottom inset that exists while the keyboard is closed, so navigation bars do not get
        /// mistaken for a keyboard. Call this whenever no input field is focused.
        /// </summary>
        public void CaptureBaseline()
        {
            KeyboardHeightSource source = ResolveSource();

            if (source == KeyboardHeightSource.WebGLViewport)
            {
                WebGLKeyboardBridge.CaptureBaseline();
                return;
            }

            if (source != KeyboardHeightSource.AndroidVisibleFrame)
            {
                return;
            }

            float inset = ReadAndroidBottomInset();
            if (inset < 0f)
            {
                return;
            }

            baselineInset = inset;
            hasBaseline = true;
            nextPollTime = 0f;
        }

        /// <summary>
        /// Pixels covered by the keyboard at the bottom of the screen. Throttled internally, so it is cheap
        /// to call every frame.
        /// </summary>
        public float GetHeightPixels()
        {
            if (Time.unscaledTime < nextPollTime)
            {
                return cachedHeight;
            }

            nextPollTime = Time.unscaledTime + PollInterval;
            cachedHeight = Measure();
            return cachedHeight;
        }

        private float Measure()
        {
            KeyboardHeightSource source = ResolveSource();
            ResolvedSource = source;

            switch (source)
            {
                case KeyboardHeightSource.EditorSimulated:
                    return Screen.height * settings.simulatedKeyboardHeightPercent;

                case KeyboardHeightSource.FixedPercent:
                    return Screen.height * settings.fallbackKeyboardHeightPercent;

                case KeyboardHeightSource.WebGLViewport:
                {
                    // The browser reports a fraction of the viewport, not pixels, so it converts
                    // straight into Unity units without worrying about devicePixelRatio.
                    float fraction = WebGLKeyboardBridge.KeyboardHeightFraction;
                    if (fraction >= 0f)
                    {
                        return Screen.height * fraction;
                    }

                    ResolvedSource = KeyboardHeightSource.FixedPercent;
                    return Screen.height * settings.fallbackKeyboardHeightPercent;
                }

                case KeyboardHeightSource.AndroidVisibleFrame:
                {
                    float inset = ReadAndroidBottomInset();
                    if (inset >= 0f)
                    {
                        float height = inset - (hasBaseline ? baselineInset : 0f);

                        // Anything smaller than this is a navigation bar, not a keyboard.
                        if (height > Screen.height * 0.08f)
                        {
                            return height;
                        }

                        return 0f;
                    }

                    // JNI bridge unavailable: degrade to the area, then to the percent.
                    goto case KeyboardHeightSource.TouchScreenKeyboardArea;
                }

                case KeyboardHeightSource.TouchScreenKeyboardArea:
                {
                    lastAreaHeight = TouchScreenKeyboard.isSupported ? TouchScreenKeyboard.area.height : 0f;
                    ResolvedSource = KeyboardHeightSource.TouchScreenKeyboardArea;

                    if (lastAreaHeight > 1f)
                    {
                        return lastAreaHeight;
                    }

                    ResolvedSource = KeyboardHeightSource.FixedPercent;
                    return Screen.height * settings.fallbackKeyboardHeightPercent;
                }

                default:
                    return 0f;
            }
        }

        private KeyboardHeightSource ResolveSource()
        {
            if (settings == null)
            {
                return KeyboardHeightSource.FixedPercent;
            }

            if (settings.keyboardHeightSource != KeyboardHeightSource.Auto)
            {
                return settings.keyboardHeightSource;
            }

            if (Application.isEditor && settings.simulateInEditor)
            {
                return KeyboardHeightSource.EditorSimulated;
            }

#if UNITY_ANDROID && !UNITY_EDITOR
            if (!androidBridgeFailed)
            {
                return KeyboardHeightSource.AndroidVisibleFrame;
            }
#endif

#if UNITY_WEBGL && !UNITY_EDITOR
            // TouchScreenKeyboard.area does not exist on WebGL: the WebGL MobileKeyboard.js exports
            // no GetRect at all, so it is always Rect.zero. The browser viewport is the only source.
            return KeyboardHeightSource.WebGLViewport;
#else
            return KeyboardHeightSource.TouchScreenKeyboardArea;
#endif
        }

        /// <summary>
        /// Pixels covered at the bottom of the window, keyboard plus navigation bar.
        /// Returns -1 when the Android bridge is not usable.
        /// </summary>
        private float ReadAndroidBottomInset()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (androidBridgeFailed)
            {
                return -1f;
            }

            try
            {
                if (decorView == null)
                {
                    using (AndroidJavaClass player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (AndroidJavaObject window = activity.Call<AndroidJavaObject>("getWindow"))
                    {
                        decorView = window.Call<AndroidJavaObject>("getDecorView");
                    }

                    visibleFrame = new AndroidJavaObject("android.graphics.Rect");
                }

                decorView.Call("getWindowVisibleDisplayFrame", visibleFrame);

                int visibleBottom = visibleFrame.Get<int>("bottom");
                int decorHeight = decorView.Call<int>("getHeight");

                if (decorHeight <= 0)
                {
                    return -1f;
                }

                float insetInDecorPixels = decorHeight - visibleBottom;

                // The decor view and the Unity surface can differ, for instance under multi-window.
                float toScreenPixels = Screen.height / (float)decorHeight;
                lastRawInset = insetInDecorPixels * toScreenPixels;

                return Mathf.Max(0f, lastRawInset);
            }
            catch (Exception e)
            {
                androidBridgeFailed = true;
                Debug.LogWarning("[MobileInputPreview] Android window bridge failed, falling back to TouchScreenKeyboard.area. " + e.Message);
                return -1f;
            }
#else
            return -1f;
#endif
        }

        /// <summary>One-line dump of every intermediate value, for the on-device diagnostics label.</summary>
        public string BuildDiagnostics(float finalOffsetPixels, float canvasScaleFactor)
        {
            StringBuilder sb = new StringBuilder(160);
            sb.Append("src=").Append(ResolvedSource);
            sb.Append(" kb=").Append(Mathf.RoundToInt(cachedHeight));
            sb.Append(" raw=").Append(Mathf.RoundToInt(lastRawInset));
            sb.Append(" base=").Append(hasBaseline ? Mathf.RoundToInt(baselineInset) : -1);
            sb.Append(" area=").Append(Mathf.RoundToInt(lastAreaHeight));
            sb.Append(" off=").Append(Mathf.RoundToInt(finalOffsetPixels));
            sb.Append(" scr=").Append(Screen.width).Append('x').Append(Screen.height);
            sb.Append(" safe=").Append(Mathf.RoundToInt(Screen.safeArea.yMin));
            sb.Append(" sf=").Append(canvasScaleFactor.ToString("0.00"));
            return sb.ToString();
        }

        public void Dispose()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            visibleFrame?.Dispose();
            decorView?.Dispose();
            visibleFrame = null;
            decorView = null;
#endif
        }
    }
}
