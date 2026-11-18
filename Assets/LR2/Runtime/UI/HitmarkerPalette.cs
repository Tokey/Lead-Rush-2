using UnityEngine;

namespace LR2.UI
{
    /// <summary>Shape-independent, luminance-separated hitmarker colours for colour-vision safety.</summary>
    public static class HitmarkerPalette
    {
        public static readonly Color Body = new Color(1f, 1f, 1f, 1f);
        public static readonly Color Head = new Color(0.78f, 0.93f, 1f, 1f);
        public static readonly Color Kill = new Color(1f, 0.3f, 0.6f, 1f);
        public static readonly Color Outline = Color.black;

        public static float RelativeLuminance(Color color)
        {
            return 0.2126f * ToLinear(color.r) + 0.7152f * ToLinear(color.g) + 0.0722f * ToLinear(color.b);
        }

        public static float Contrast(Color foreground, Color background)
        {
            float a = RelativeLuminance(foreground);
            float b = RelativeLuminance(background);
            return (Mathf.Max(a, b) + 0.05f) / (Mathf.Min(a, b) + 0.05f);
        }

        private static float ToLinear(float value) => value <= 0.04045f ? value / 12.92f : Mathf.Pow((value + 0.055f) / 1.055f, 2.4f);
    }
}
