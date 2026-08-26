// GGTools - TMProUltilitis / Mobile Input Preview
//
// Unity decides whether the browser is "mobile" in UnityLoader.js with:
//
//     mobile: /Mobile|Android|iP(ad|hone)/.test(navigator.appVersion)
//
// That flag reaches the runtime through JS_SystemInfo_IsMobile and is what makes
// TouchScreenKeyboard.isSupported true, which is what makes TMP_InputField open the
// keyboard at all.
//
// The regex fails on real touch devices that report a desktop user agent:
//   - iPad on iPadOS 13+, where Safari sends a macOS user agent by default
//   - Android tablets with "Desktop site" enabled, which drops the Android token
//   - in-app WebViews with a custom user agent
//
// On those devices no keyboard ever opens and text fields are unusable.
//
// Module.SystemInfo.mobile is read at call time, not cached on the C# side, so overriding
// it after load works. navigator.maxTouchPoints > 1 is the same fallback Unity itself uses
// to detect iPad (see the Safari version warning in UnityLoader.js), and it keeps desktop
// browsers with a mouse untouched.

var GGToolsWebGLKeyboardLib = {

    // Returns: 0 = not applied, 1 = browser already reported as mobile, 2 = forced on.
    GGToolsWebGL_ForceMobileKeyboard: function()
    {
        if (typeof Module === "undefined" || !Module.SystemInfo) {
            return 0;
        }

        if (Module.SystemInfo.mobile) {
            return 1;
        }

        if (typeof navigator === "undefined" || !(navigator.maxTouchPoints > 1)) {
            return 0;
        }

        Module.SystemInfo.mobile = true;
        return 2;
    },

    // Diagnostics only: what the browser reports, without changing anything.
    // Returns: 0 = not mobile and not touch, 1 = mobile, 2 = not mobile but touch capable.
    GGToolsWebGL_GetMobileState: function()
    {
        if (typeof Module === "undefined" || !Module.SystemInfo) {
            return 0;
        }

        if (Module.SystemInfo.mobile) {
            return 1;
        }

        return (typeof navigator !== "undefined" && navigator.maxTouchPoints > 1) ? 2 : 0;
    }
};

mergeInto(LibraryManager.library, GGToolsWebGLKeyboardLib);
