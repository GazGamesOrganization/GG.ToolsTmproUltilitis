using TMPro;
using UnityEngine;

namespace GGTools.TMProUltilitis
{
    /// <summary>
    /// Optional opt-out marker for a single <see cref="TMP_InputField"/>.
    /// <see cref="MobileInputPreview"/> previews every field by default, so this component is only needed
    /// on the fields that must NOT show the bar.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_InputField))]
    [AddComponentMenu("GGTools/TMPro Ultilitis/Mobile Input Preview Target")]
    public class MobileInputPreviewTarget : MonoBehaviour
    {
        [Tooltip("Uncheck to keep the mobile input preview bar hidden while this field is selected.")]
        public bool enablePreview = true;
    }
}
