// GGTools - TMProUltilitis / Mobile Input Preview
//
// Two separate WebGL fixes for the native mobile keyboard.
//
// ---------------------------------------------------------------------------
// FIX 1 - the keyboard never opens on some touch devices
// ---------------------------------------------------------------------------
// Unity decides whether the browser is "mobile" in UnityLoader.js with:
//
//     mobile: /Mobile|Android|iP(ad|hone)/.test(navigator.appVersion)
//
// That flag reaches the runtime through JS_SystemInfo_IsMobile and is what makes
// TouchScreenKeyboard.isSupported true, which is what makes TMP_InputField open the
// keyboard at all. The regex fails on real touch devices that report a desktop user
// agent: iPad on iPadOS 13+, Android tablets with "Desktop site", in-app WebViews.
//
// Module.SystemInfo.mobile is read at call time, so overriding it after load works.
//
// ---------------------------------------------------------------------------
// FIX 2 - the keyboard bar renders underneath the keyboard
// ---------------------------------------------------------------------------
// JS_MobileKeyboard_Show creates its bar with:
//
//     "width:100%; position:fixed; bottom:0px; ..."
//
// `position: fixed` resolves against the LAYOUT viewport. A mobile soft keyboard does
// not shrink the layout viewport, it shrinks the VISUAL viewport - and Unity's own page
// template pins the layout viewport outright with `height=device-height`. So bottom:0
// lands behind the keyboard and the bar is invisible exactly when it is needed.
//
// window.visualViewport reports the real visible area, so the keyboard height is:
//
//     innerHeight - visualViewport.height - visualViewport.offsetTop
//
// Unity writes that inline style only when it creates the container, never on reuse, so
// overriding `bottom` afterwards sticks. The container is removed and recreated on every
// hide/show, hence the MutationObserver plus a fresh lookup on each event.

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
    },

    // Keeps Unity's own keyboard bar above the soft keyboard.
    // Returns: 0 = unsupported browser, 1 = already installed, 2 = installed now.
    GGToolsWebGL_InstallKeyboardBarFix: function()
    {
        if (typeof window === "undefined" || !window.visualViewport || !window.MutationObserver) {
            return 0;
        }

        if (window["__ggtoolsKeyboardBarFix"]) {
            return 1;
        }

        var state = {
            height: 0,
            bar: null,
            innerHeight: 0,
            visualHeight: 0,
            offsetTop: 0,
            barHeight: 0,
            appliedTop: -1,
            found: 0
        };
        window["__ggtoolsKeyboardBarFix"] = state;

        // Unity creates the container without id or class, so match it by shape: a fixed
        // positioned div, direct child of body, holding an input or a textarea.
        function findBar() {
            var children = document.body.children;
            for (var i = children.length - 1; i >= 0; i--) {
                var node = children[i];
                if (node.tagName !== "DIV") {
                    continue;
                }
                if (node.style.position !== "fixed") {
                    continue;
                }
                if (node.querySelector("input, textarea")) {
                    return node;
                }
            }
            return null;
        }

        function apply() {
            var vv = window.visualViewport;

            state.innerHeight = window.innerHeight;
            state.visualHeight = vv.height;
            state.offsetTop = vv.offsetTop;

            // Reported only for diagnostics. Not used to position anything: window.innerHeight
            // also shrinks with the keyboard in some browsers, which makes this read near zero.
            var covered = window.innerHeight - vv.height - vv.offsetTop;
            state.height = covered > 1 ? covered : 0;

            var bar = findBar();
            state.bar = bar;
            state.found = bar ? 1 : 0;
            if (!bar) {
                state.appliedTop = -1;
                return;
            }

            // Unity's inline style has no z-index, so a page overlay could still cover it.
            if (!bar.style.zIndex) {
                bar.style.zIndex = "2147483647";
            }

            var barHeight = bar.offsetHeight;
            state.barHeight = barHeight;

            if (barHeight <= 0) {
                // Not laid out yet. Fall back to the bottom based formula for this pass.
                bar.style.bottom = state.height + "px";
                state.appliedTop = -1;
                return;
            }

            // Anchor to the VISUAL viewport instead of the layout one.
            //
            // A fixed element resolves `top` against the layout viewport. vv.offsetTop is where the
            // visual viewport starts inside it, and vv.height is how tall the visible area is, so
            // vv.offsetTop + vv.height is exactly the top edge of the keyboard in the same
            // coordinates. Placing the bar's bottom there needs no window.innerHeight at all.
            var top = vv.offsetTop + vv.height - barHeight;

            bar.style.top = top + "px";
            bar.style.bottom = "auto";
            state.appliedTop = top;
        }

        window.visualViewport.addEventListener("resize", apply);
        window.visualViewport.addEventListener("scroll", apply);
        window.addEventListener("orientationchange", apply);

        // The bar is destroyed and rebuilt on every hide/show, so reposition it the moment
        // it reappears instead of waiting for the next viewport event.
        var observer = new MutationObserver(function(records) {
            for (var i = 0; i < records.length; i++) {
                if (records[i].addedNodes.length > 0) {
                    apply();
                    return;
                }
            }
        });
        observer.observe(document.body, { childList: true });

        apply();
        return 2;
    },

    // Diagnostics: last measured keyboard height in CSS pixels, or -1 when the fix is off.
    GGToolsWebGL_GetKeyboardHeight: function()
    {
        var state = (typeof window !== "undefined") ? window["__ggtoolsKeyboardBarFix"] : null;
        return state ? Math.round(state.height) : -1;
    },

    // Diagnostics: individual measurements, so the numbers can be read on the device.
    //   0 window.innerHeight   1 visualViewport.height   2 visualViewport.offsetTop
    //   3 bar found (0/1)      4 bar height              5 applied top (-1 = not applied)
    GGToolsWebGL_GetDebugValue: function(index)
    {
        var state = (typeof window !== "undefined") ? window["__ggtoolsKeyboardBarFix"] : null;
        if (!state) {
            return -1;
        }

        switch (index) {
            case 0: return Math.round(state.innerHeight);
            case 1: return Math.round(state.visualHeight);
            case 2: return Math.round(state.offsetTop);
            case 3: return state.found;
            case 4: return Math.round(state.barHeight);
            case 5: return Math.round(state.appliedTop);
            default: return -1;
        }
    }
};

mergeInto(LibraryManager.library, GGToolsWebGLKeyboardLib);
