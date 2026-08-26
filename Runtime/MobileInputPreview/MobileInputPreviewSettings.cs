using System;
using UnityEngine;

namespace GGTools.TMProUltilitis
{
    /// <summary>
    /// Decides when the mobile input preview view is allowed to show up.
    /// </summary>
    public enum PreviewActivationMode
    {
        /// <summary>Shows on devices with a native touch keyboard, and in the Editor when simulation is on.</summary>
        Auto = 0,
        /// <summary>Always shows, on any platform, even desktop with a physical keyboard.</summary>
        Always = 1,
        /// <summary>Never shows. Useful to disable the feature without removing the component.</summary>
        Never = 2
    }

    /// <summary>
    /// Behaviour configuration for <see cref="MobileInputPreview"/>.
    /// Nothing visual lives here: colors, fonts, sprites and layout belong to the authored
    /// <see cref="MobileInputPreviewView"/> prefab.
    /// </summary>
    [Serializable]
    public class MobileInputPreviewSettings
    {
        [Header("Activation")]
        [Tooltip("When the preview view is allowed to show up.")]
        public PreviewActivationMode activationMode = PreviewActivationMode.Auto;

        [Tooltip("In Auto mode, also show the view inside the Editor using a fake keyboard height, so the layout can be tested without a device build.")]
        public bool simulateInEditor = true;

        [Header("Keyboard height")]
        [Tooltip("How the keyboard height is measured. Auto picks the Android window frame on device, the simulation in the Editor, and TouchScreenKeyboard.area everywhere else. Only override this to debug.")]
        public KeyboardHeightSource keyboardHeightSource = KeyboardHeightSource.Auto;

        [Range(0f, 0.9f)]
        [Tooltip("Fraction of the screen height used as the fake keyboard when simulating in the Editor.")]
        public float simulatedKeyboardHeightPercent = 0.45f;

        [Range(0f, 0.9f)]
        [Tooltip("Last resort fraction of the screen height, used only when no measurement is available.")]
        public float fallbackKeyboardHeightPercent = 0.45f;

        [Tooltip("Extra offset applied above the keyboard, in real pixels. Negative values overlap the keyboard.")]
        public float extraKeyboardMargin = 0f;

        [Tooltip("Never let the bar fall below Screen.safeArea, so notches and gesture bars do not clip it.")]
        public bool respectSafeArea = true;

        [Header("Text")]
        [Tooltip("Seconds between caret blinks. Zero or less keeps the caret always visible.")]
        public float caretBlinkRate = 0.53f;

        [Tooltip("Glyph used as the caret. Leave empty to show no caret at all.")]
        public string caretGlyph = "|";

        [Min(8)]
        [Tooltip("Maximum characters shown at once. Longer text is windowed around the caret so the caret stays visible.")]
        public int charWindow = 60;

        [Header("WebGL")]
        [Tooltip("Hide the HTML input bar Unity draws over the canvas on WebGL, so this preview takes over. The hidden input stays focused and keeps receiving the typing. Turn off to keep Unity's own bar and disable this preview on the web.")]
        public bool hideNativeWebBar = true;

        [Header("Debug")]
        [Tooltip("Writes every measured value into the view's diagnosticsLabel, so the numbers can be read straight from the device.")]
        public bool showDiagnostics = false;
    }
}
