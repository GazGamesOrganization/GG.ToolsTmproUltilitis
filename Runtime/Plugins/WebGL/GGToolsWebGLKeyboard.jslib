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

        // "Has a touch digitizer" is NOT the same as "touch is how this thing is used".
        // navigator.maxTouchPoints alone reports 10 on plenty of Windows desktops and laptops that
        // have a touch screen or merely expose a HID digitizer, and forcing mobile there pops the
        // soft keyboard bar on a machine driven by a mouse.
        //
        // The media queries below are the standard touch-primary test:
        //   (pointer: coarse) - the PRIMARY pointing device is a finger, not a mouse
        //   (hover: none)     - the PRIMARY pointing device cannot hover
        // A desktop with a touch screen still has a mouse as primary, so it reports
        // (pointer: fine) and (hover: hover) and is correctly left alone. An iPad sending a
        // desktop user agent reports coarse and no-hover, and is correctly picked up.
        var probe = { maxTouch: 0, coarse: 0, noHover: 0, forced: 0 };
        if (typeof window !== "undefined") {
            window["__ggtoolsMobileProbe"] = probe;
        }

        if (typeof navigator === "undefined" || typeof window === "undefined" || !window.matchMedia) {
            return 0;
        }

        probe.maxTouch = navigator.maxTouchPoints || 0;
        probe.coarse = window.matchMedia("(pointer: coarse)").matches ? 1 : 0;
        probe.noHover = window.matchMedia("(hover: none)").matches ? 1 : 0;

        if (probe.maxTouch <= 1 || !probe.coarse || !probe.noHover) {
            return 0;
        }

        probe.forced = 1;
        Module.SystemInfo.mobile = true;
        return 2;
    },

    // Diagnostics only: what the browser reports, without changing anything.
    // Returns: 0 = not mobile and not touch primary, 1 = mobile, 2 = not mobile but touch primary.
    GGToolsWebGL_GetMobileState: function()
    {
        if (typeof Module === "undefined" || !Module.SystemInfo) {
            return 0;
        }

        if (Module.SystemInfo.mobile) {
            return 1;
        }

        if (typeof navigator === "undefined" || typeof window === "undefined" || !window.matchMedia) {
            return 0;
        }

        var touchPrimary = navigator.maxTouchPoints > 1 &&
                           window.matchMedia("(pointer: coarse)").matches &&
                           window.matchMedia("(hover: none)").matches;

        return touchPrimary ? 2 : 0;
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
            fraction: 0,
            bar: null,
            innerHeight: 0,
            layoutHeight: 0,
            visualHeight: 0,
            offsetTop: 0,
            barHeight: 0,
            appliedShift: -1,
            found: 0,
            baselineLayout: 0,
            baselineInner: 0,
            byLayout: 0,
            byInner: 0,
            byVisual: 0,
            hideNative: 0,
            keepFocus: 0,
            blurCount: 0,
            removeCount: 0,
            refocusCount: 0,
            burstCount: 0,
            burstStart: 0
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

        // Browsers disagree wildly on which of these shrinks when the keyboard opens, so measure
        // all three and trust the largest. Same idea as the Android sensor: take a baseline while
        // nothing is focused, then diff against it.
        //
        //   byLayout  documentElement.clientHeight against its baseline
        //   byInner   window.innerHeight against its baseline
        //   byVisual  gap left below the visual viewport, needs no baseline
        function measure() {
            var vv = window.visualViewport;

            var layoutHeight = document.documentElement.clientHeight;
            var innerHeight = window.innerHeight;

            state.layoutHeight = layoutHeight;
            state.innerHeight = innerHeight;
            state.visualHeight = vv.height;
            state.offsetTop = vv.offsetTop;

            state.byLayout = state.baselineLayout > 0 ? state.baselineLayout - layoutHeight : 0;
            state.byInner = state.baselineInner > 0 ? state.baselineInner - innerHeight : 0;
            state.byVisual = layoutHeight - (vv.offsetTop + vv.height);

            var best = 0;
            if (state.byLayout > best) { best = state.byLayout; }
            if (state.byInner > best) { best = state.byInner; }
            if (state.byVisual > best) { best = state.byVisual; }

            state.height = best > 1 ? best : 0;

            // Unity gets the height as a fraction of the unfocused viewport, not in pixels: CSS
            // pixels and Unity's Screen pixels differ by devicePixelRatio and by whatever the page
            // template does to the canvas. A fraction survives all of that.
            var reference = state.baselineLayout > 0 ? state.baselineLayout : layoutHeight;
            state.fraction = reference > 0 ? state.height / reference : 0;
        }

        function apply() {
            measure();

            var bar = findBar();
            state.bar = bar;
            state.found = bar ? 1 : 0;
            if (!bar) {
                state.appliedShift = -1;
                return;
            }

            state.barHeight = bar.offsetHeight;

            if (state.hideNative) {
                // Visually gone, but still focused and still receiving the typing. Both properties
                // are compositor and hit-testing only: no reflow, so no scroll-into-view, so no
                // blur - and Unity destroys the bar on blur.
                bar.style.opacity = "0";
                bar.style.pointerEvents = "none";
                bar.style.transform = "";
                state.appliedShift = 0;
                return;
            }

            bar.style.opacity = "";
            bar.style.pointerEvents = "";

            // Unity's inline style has no z-index, so a page overlay could still cover it.
            if (!bar.style.zIndex) {
                bar.style.zIndex = "2147483647";
            }

            // Move with `transform`, never with top/bottom. Those are layout properties: writing
            // them while the inner <input> holds focus forces a reflow and can trigger a
            // scroll-into-view, which fires blur, which makes Unity destroy the bar.
            bar.style.transform = state.height > 0 ? "translateY(-" + state.height + "px)" : "";
            state.appliedShift = state.height;
        }

        // Coalesce bursts of resize and scroll events into one write per frame. Without this a
        // style write that nudges the viewport can feed the next event and spin.
        var scheduled = false;
        function schedule() {
            if (scheduled) {
                return;
            }
            scheduled = true;
            window.requestAnimationFrame(function() {
                scheduled = false;
                apply();
            });
        }

        // Unity tears the whole bar down on any blur of its hidden input:
        //
        //     input.addEventListener("blur", function(e) { _JS_MobileKeyboard_Hide(true); ... });
        //
        // While a Unity side field is still attached and the bar is hidden, the user cannot reach
        // that input at all (pointer-events: none), so every blur it gets is spurious - the IME
        // moving focus during a clipboard paste, or after the text is cleared. Those blurs closed
        // the keyboard and forced the player to tap the field again.
        //
        // A capture phase listener on `document` runs BEFORE listeners on the target itself, so
        // stopPropagation here keeps Unity's handler from ever seeing it. blur does not bubble, but
        // it does capture.
        document.addEventListener("blur", function(e) {
            var target = e.target;
            if (!target || (target.tagName !== "INPUT" && target.tagName !== "TEXTAREA")) {
                return;
            }

            var container = target.parentNode;
            if (!container || !container.style || container.style.position !== "fixed") {
                return;
            }

            state.blurCount++;

            if (!state.hideNative || !state.keepFocus) {
                return;
            }

            // Runaway guard: if focus keeps bouncing, stop fighting and let the keyboard close
            // rather than trapping the player with a keyboard that will not go away.
            var now = Date.now();
            if (now - state.burstStart > 1000) {
                state.burstStart = now;
                state.burstCount = 0;
            }
            state.burstCount++;
            if (state.burstCount > 5) {
                state.keepFocus = 0;
                return;
            }

            e.stopPropagation();
            state.refocusCount++;

            setTimeout(function() {
                if (state.keepFocus && document.body.contains(target)) {
                    target.focus();
                }
            }, 0);
        }, true);

        window.visualViewport.addEventListener("resize", schedule);
        window.visualViewport.addEventListener("scroll", schedule);
        window.addEventListener("orientationchange", schedule);

        // The bar is destroyed and rebuilt on every hide/show, so reposition it the moment
        // it reappears instead of waiting for the next viewport event.
        var observer = new MutationObserver(function(records) {
            var added = false;
            for (var i = 0; i < records.length; i++) {
                if (records[i].removedNodes.length > 0) {
                    state.removeCount++;
                }
                if (records[i].addedNodes.length > 0) {
                    added = true;
                }
            }
            if (added) {
                schedule();
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

    // Keyboard height as a fraction of the unfocused viewport, times 10000. -1 when unavailable.
    // Unit independent, so Unity can just multiply it by Screen.height.
    GGToolsWebGL_GetKeyboardFraction: function()
    {
        var state = (typeof window !== "undefined") ? window["__ggtoolsKeyboardBarFix"] : null;
        return state ? Math.round(state.fraction * 10000) : -1;
    },

    // Records the viewport size while nothing is focused, so later shrinking can be diffed
    // against it. Call whenever no input field is focused. Ignored once the keyboard is open.
    GGToolsWebGL_CaptureKeyboardBaseline: function()
    {
        var state = (typeof window !== "undefined") ? window["__ggtoolsKeyboardBarFix"] : null;
        if (!state) {
            return 0;
        }

        var layoutHeight = document.documentElement.clientHeight;
        var innerHeight = window.innerHeight;

        // Only ever grow the baseline. A baseline captured while the keyboard was still closing
        // would be too small and would permanently understate every later measurement.
        if (layoutHeight > state.baselineLayout) { state.baselineLayout = layoutHeight; }
        if (innerHeight > state.baselineInner) { state.baselineInner = innerHeight; }

        return 1;
    },

    // Hides Unity's own HTML bar while keeping its input focused, so a Unity drawn bar can take
    // over. Pass 0 to restore it.
    GGToolsWebGL_SetNativeBarHidden: function(hidden)
    {
        var state = (typeof window !== "undefined") ? window["__ggtoolsKeyboardBarFix"] : null;
        if (!state) {
            return 0;
        }

        state.hideNative = hidden ? 1 : 0;
        return 1;
    },

    // While set, spurious blurs on Unity's hidden input are swallowed and focus is taken back, so
    // the keyboard survives a clipboard paste or clearing the whole field. Unity must clear this
    // before it genuinely wants the keyboard closed, otherwise the keyboard would be trapped open.
    GGToolsWebGL_SetKeepFocus: function(keep)
    {
        var state = (typeof window !== "undefined") ? window["__ggtoolsKeyboardBarFix"] : null;
        if (!state) {
            return 0;
        }

        state.keepFocus = keep ? 1 : 0;

        if (state.keepFocus) {
            state.burstCount = 0;
            state.burstStart = Date.now();
        }

        return 1;
    },

    // Diagnostics: individual measurements, so the numbers can be read on the device.
    //   0 window.innerHeight           1 visualViewport.height   2 visualViewport.offsetTop
    //   3 bar found (0/1)              4 bar height              5 applied translateY, px
    //   6 documentElement.clientHeight 7 baseline layout         8 baseline inner
    //   9 byLayout                    10 byInner                11 byVisual
    //  12 chosen height               13 native bar hidden (0/1)
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
            case 5: return Math.round(state.appliedShift);
            case 6: return Math.round(state.layoutHeight);
            case 7: return Math.round(state.baselineLayout);
            case 8: return Math.round(state.baselineInner);
            case 9: return Math.round(state.byLayout);
            case 10: return Math.round(state.byInner);
            case 11: return Math.round(state.byVisual);
            case 12: return Math.round(state.height);
            case 13: return state.hideNative;
            case 14: return state.keepFocus;
            case 15: return state.blurCount;
            case 16: return state.refocusCount;
            case 17: return state.removeCount;
            default: return -1;
        }
    },

    // Diagnostics for the touch-primary probe, which runs before the watcher is installed.
    //   0 navigator.maxTouchPoints   1 (pointer: coarse)   2 (hover: none)   3 forced mobile
    GGToolsWebGL_GetProbeValue: function(index)
    {
        var probe = (typeof window !== "undefined") ? window["__ggtoolsMobileProbe"] : null;
        if (!probe) {
            return -1;
        }

        switch (index) {
            case 0: return probe.maxTouch;
            case 1: return probe.coarse;
            case 2: return probe.noHover;
            case 3: return probe.forced;
            default: return -1;
        }
    }
};

mergeInto(LibraryManager.library, GGToolsWebGLKeyboardLib);
