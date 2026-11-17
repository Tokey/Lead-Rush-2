using TMPro;
using UnityEngine;

namespace LR2.UI
{
    /// <summary>Colours and fonts of the LR2 interface (Resources/LR2/UITheme).</summary>
    [CreateAssetMenu(menuName = "LR2/UI/Theme", fileName = "UITheme")]
    public class UITheme : ScriptableObject
    {
        public Color accent = new Color(1f, 0.71f, 0.28f);
        public Color friendly = new Color(0.3f, 0.66f, 1f);
        public Color enemy = new Color(1f, 0.3f, 0.27f);
        public Color text = new Color(1f, 1f, 1f, 0.92f);
        public Color textDim = new Color(1f, 1f, 1f, 0.55f);
        public Color panel = new Color(0.03f, 0.04f, 0.05f, 0.62f);
        public Color warning = new Color(1f, 0.55f, 0.1f);
        public TMP_FontAsset headingFont;
        public TMP_FontAsset bodyFont;
        public TMP_FontAsset numberFont;
    }
}
