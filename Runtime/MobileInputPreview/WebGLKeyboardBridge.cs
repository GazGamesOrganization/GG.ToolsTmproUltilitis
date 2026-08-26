using UnityEngine;

#if UNITY_WEBGL && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace GGTools.TMProUltilitis
{
    /// <summary>
    /// What the browser reports about being a mobile device.
    /// </summary>
    public enum WebGLMobileState
    {
        /// <summary>Not a WebGL build, or the bridge could not run.</summary>
        Unavailable = -1,
        /// <summary>Desktop browser with no touch. Left alone.</summary>
        Desktop = 0,
        /// <summary>Browser already reported as mobile. Nothing to fix.</summary>
        AlreadyMobile = 1,
        /// <summary>Desktop user agent on a touch device. This is the case that gets forced.</summary>
        TouchWithDesktopUserAgent = 2
    }

    /// <summary>
    /// Makes the native browser keyboard work on touch devices that report a desktop user agent.
    ///
    /// <para>
    /// Unity flags a browser as mobile with <c>/Mobile|Android|iP(ad|hone)/.test(navigator.appVersion)</c>
    /// inside <c>UnityLoader.js</c>. iPadOS 13+ Safari and Android tablets set to "Desktop site" fail that
    /// test, so <see cref="TouchScreenKeyboard.isSupported"/> comes back false and <c>TMP_InputField</c>
    /// never opens a keyboard. This flips the flag back on when the browser reports real touch points.
    /// </para>
    ///
    /// <para>
    /// Runs by itself before the first scene loads. Define <c>GGTOOLS_NO_WEBGL_KEYBOARD_FIX</c> to opt out,
    /// or call <see cref="ForceMobileKeyboard"/> manually.
    /// </para>
    ///
    /// <para>
    /// On WebGL, Unity draws its own HTML input bar over the canvas, so
    /// <see cref="MobileInputPreview"/> stands down there and this bridge is the whole web story.
    /// </para>
    /// </summary>
    public static class WebGLKeyboardBridge
    {
        private const string LogPrefix = "[MobileInputPreview] ";

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern int GGToolsWebGL_ForceMobileKeyboard();

        [DllImport("__Internal")]
        private static extern int GGToolsWebGL_GetMobileState();

        [DllImport("__Internal")]
        private static extern int GGToolsWebGL_InstallKeyboardBarFix();

        [DllImport("__Internal")]
        private static extern int GGToolsWebGL_GetKeyboardHeight();

        [DllImport("__Internal")]
        private static extern int GGToolsWebGL_GetDebugValue(int index);
#endif

        /// <summary>
        /// One line with every browser measurement behind the keyboard bar positioning.
        /// <c>inner</c> window.innerHeight, <c>vv</c> visualViewport.height, <c>off</c> its offsetTop,
        /// <c>found</c> whether the bar was located, <c>barh</c> its height, <c>top</c> the applied top.
        /// </summary>
        public static string BuildDebugDump()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                return "inner=" + GGToolsWebGL_GetDebugValue(0) +
                       " vv=" + GGToolsWebGL_GetDebugValue(1) +
                       " off=" + GGToolsWebGL_GetDebugValue(2) +
                       " found=" + GGToolsWebGL_GetDebugValue(3) +
                       " barh=" + GGToolsWebGL_GetDebugValue(4) +
                       " top=" + GGToolsWebGL_GetDebugValue(5);
            }
            catch (System.Exception)
            {
                return "webgl bridge unavailable";
            }
#else
            return string.Empty;
#endif
        }

        /// <summary>Result of the last <see cref="ForceMobileKeyboard"/> call.</summary>
        public static WebGLMobileState LastState { get; private set; } = WebGLMobileState.Unavailable;

        /// <summary>True once the keyboard bar repositioning is watching the visual viewport.</summary>
        public static bool BarFixInstalled { get; private set; }

        /// <summary>
        /// Keyboard height in CSS pixels as measured by the browser, or -1 when unavailable.
        /// Diagnostics only: nothing in the package positions anything from it on WebGL.
        /// </summary>
        public static int KeyboardHeightCssPixels
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                try
                {
                    return GGToolsWebGL_GetKeyboardHeight();
                }
                catch (System.Exception)
                {
                    return -1;
                }
#else
                return -1;
#endif
            }
        }

#if !GGTOOLS_NO_WEBGL_KEYBOARD_FIX
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoApply()
        {
            ForceMobileKeyboard();
            InstallKeyboardBarFix();
        }
#endif

        /// <summary>
        /// Keeps Unity's own HTML keyboard bar above the soft keyboard.
        ///
        /// <para>
        /// Unity anchors that bar with <c>position: fixed; bottom: 0</c>, which resolves against the layout
        /// viewport. A soft keyboard shrinks the visual viewport, not the layout one, and Unity's page
        /// template pins the layout viewport with <c>height=device-height</c> on top of that. The bar
        /// therefore renders behind the keyboard. This watches <c>window.visualViewport</c> and lifts the
        /// bar by the measured keyboard height.
        /// </para>
        ///
        /// <para>Safe to call more than once. Does nothing outside a WebGL player.</para>
        /// </summary>
        public static bool InstallKeyboardBarFix()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            int result;

            try
            {
                result = GGToolsWebGL_InstallKeyboardBarFix();
            }
            catch (System.Exception e)
            {
                BarFixInstalled = false;
                Debug.LogWarning(LogPrefix + "WebGL keyboard bar fix unavailable. " + e.Message);
                return false;
            }

            BarFixInstalled = result > 0;

            if (result == 0)
            {
                Debug.LogWarning(LogPrefix + "Browser has no visualViewport support, so the native keyboard bar " +
                                 "may render behind the keyboard.");
            }

            return BarFixInstalled;
#else
            BarFixInstalled = false;
            return false;
#endif
        }

        /// <summary>
        /// Turns the browser keyboard back on when the device has touch but reports a desktop user agent.
        /// Does nothing outside a WebGL player. Safe to call more than once.
        /// </summary>
        public static WebGLMobileState ForceMobileKeyboard()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                LastState = (WebGLMobileState)GGToolsWebGL_ForceMobileKeyboard();
            }
            catch (System.Exception e)
            {
                LastState = WebGLMobileState.Unavailable;
                Debug.LogWarning(LogPrefix + "WebGL keyboard bridge unavailable. " + e.Message);
                return LastState;
            }

            if (LastState == WebGLMobileState.TouchWithDesktopUserAgent)
            {
                Debug.Log(LogPrefix + "Browser reports a desktop user agent on a touch device. " +
                          "Forced Module.SystemInfo.mobile so the native keyboard opens.");
            }
#else
            LastState = WebGLMobileState.Unavailable;
#endif
            return LastState;
        }

        /// <summary>Reads what the browser reports without changing anything.</summary>
        public static WebGLMobileState PeekState()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            try
            {
                return (WebGLMobileState)GGToolsWebGL_GetMobileState();
            }
            catch (System.Exception)
            {
                return WebGLMobileState.Unavailable;
            }
#else
            return WebGLMobileState.Unavailable;
#endif
        }
    }
}
