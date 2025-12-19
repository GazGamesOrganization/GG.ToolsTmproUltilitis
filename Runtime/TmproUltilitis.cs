using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using System.Text;

namespace GGTools.TMProUltilitis
{
    public static class TMProUltils
    {
        private static readonly Dictionary<TextMeshProUGUI, Coroutine> activeCoroutines = new Dictionary<TextMeshProUGUI, Coroutine>();

        /// <summary>
        /// Starts typing text into a TextMeshProUGUI character by character,
        /// or sets the full text instantly if delay is zero or less.
        /// </summary>
        /// <param name="textbox">The TextMeshProUGUI component to display the text.</param>
        /// <param name="script">MonoBehaviour used to start the coroutine.</param>
        /// <param name="text">The text to type.</param>
        /// <param name="delay">Delay between each character in seconds. If &lt;= 0, text is set instantly.</param>
        public static void Type(this TextMeshProUGUI textbox, MonoBehaviour script, string text, float delay = 0)
        {
            if (delay <= 0)
            {
                textbox.text = text;
                return;
            }
            Coroutine co = script.StartCoroutine(CharTyper(textbox, text, delay));
            activeCoroutines[textbox] = co;
        }

        /// <summary>
        /// Checks whether the given TextMeshProUGUI is currently being typed by this helper.
        /// </summary>
        /// <param name="textbox">The TextMeshProUGUI to check.</param>
        /// <returns>True if a typing coroutine is active for this textbox; otherwise false.</returns>
        public static bool IsTyping(this TextMeshProUGUI textbox)
        {
            return (activeCoroutines.ContainsKey(textbox));
        }

        /// <summary>
        /// Stops the typing effect for the given TextMeshProUGUI if it is currently typing.
        /// </summary>
        /// <param name="textbox">The TextMeshProUGUI whose typing should be stopped.</param>
        /// <param name="script">MonoBehaviour used to stop the coroutine.</param>
        public static void StopType(this TextMeshProUGUI textbox, MonoBehaviour script)
        {
            if (activeCoroutines.ContainsKey(textbox))
            {
                script.StopCoroutine(activeCoroutines[textbox]);
                activeCoroutines.Remove(textbox);
            }
            else
            {
                Debug.LogError("Não esta digitando");
            }
        }

        /// <summary>
        /// Automatically inserts &lt;page&gt; tags into a string based on a maximum
        /// character count per page, trying to break at spaces when possible.
        /// </summary>
        /// <param name="text">The original text.</param>
        /// <param name="maxChar">Maximum number of characters per page.</param>
        /// <returns>Text with &lt;page&gt; tags inserted.</returns>
        public static string AutoPage(this string text, int maxChar)
        {
            if (string.IsNullOrEmpty(text) || maxChar <= 0)
            {
                return text;
            }

            StringBuilder sb = new StringBuilder(text);
            int lastPageInsert = 0;

            while (lastPageInsert + maxChar < sb.Length)
            {
                int pageBreakIndex = lastPageInsert + maxChar;

                // Volta até o último espaço antes do limite
                while (pageBreakIndex > lastPageInsert && sb[pageBreakIndex] != ' ')
                {
                    pageBreakIndex--;
                }

                // Se não achou espaço, quebra mesmo assim (melhor que travar)
                if (pageBreakIndex <= lastPageInsert)
                    pageBreakIndex = lastPageInsert + maxChar;
                else
                    pageBreakIndex++;

                sb.Insert(pageBreakIndex, "<page>");
                lastPageInsert = pageBreakIndex + "<page>".Length;
            }

            return sb.ToString();
        }


        public static string Colorize(string text, Color color)
        {
            string hex = ColorUtility.ToHtmlStringRGB(color);
            return $"<color=#{hex}>{text}</color>";
        }

        public static string Bold(string text)
        {
            return $"<b>{text}</b>";
        }

        public static string Italic(string text)
        {
            return $"<i>{text}</i>";
        }
        public static string Underline(string text)
        {
            return $"<u>{text}</u>";
        }

        public static string Strikethrough(string text)
        {
            return $"<s>{text}</s>";
        }

        public static string SmallCaps(string text)
        {
            return $"<smallcaps>{text}</smallcaps>";
        }

        public static string Highlight(string text, Color color)
        {
            string hex = ColorUtility.ToHtmlStringRGB(color);
            return $"<mark=#{hex}>{text}</mark>";
        }

        public static string Size(string text, int percent)
        {
            return $"<size={percent}%>{text}</size>";
        }

        public static string CharacterSpacing(string text, float spacing)
        {
            return $"<cspace={spacing}>{text}</cspace>";
        }

        public static string Font(string text, TMP_FontAsset font)
        {
            return $"<font=\"{font.name}\">{text}</font>";
        }

        private static IEnumerator CharTyper(TextMeshProUGUI textbox, string text = "", float delay = 0)
        {
            textbox.text = "";
            StringBuilder tag = new StringBuilder("");
            bool insideTag = false;
            WaitForSeconds waitSeconds = new WaitForSeconds(delay);
            StringBuilder visibleText = new StringBuilder("");
            foreach (char character in text)
            {
                if (character == '<')
                {
                    insideTag = true;
                }
                if (character == '>')
                {
                    tag.Append(character);
                    Debug.Log(tag.ToString());
                    insideTag = false;
                    if (tag.ToString() == "<page>")
                    {
                        textbox.text = text;
                        break;
                    }
                    else
                    {
                        tag.Clear();
                    }
                }
                if (insideTag)
                {
                    tag.Append(character);
                    visibleText.Append(character);
                    yield return null;
                }
                else
                {
                    visibleText.Append(character);
                    if (!insideTag) textbox.text = visibleText.ToString();
                    yield return waitSeconds;
                }
            }
        }
    }
}