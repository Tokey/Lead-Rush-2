using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace LR2.UI
{
    /// <summary>
    /// B130 (user 11-20: "make the play settings etc buttons a bit cooler? maybe tiles?"): hover motion for a title-screen
    /// tile. The chevron slides out and brightens, the icon lights up. Runs on unscaled time because the menu plays in slow
    /// motion. <see cref="UIButtonFX"/> on the same button still does the sounds, the scale and the accent bar.
    /// </summary>
    public class TitleTileFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, ISelectHandler, IDeselectHandler
    {
        public RectTransform chevron;
        public Image chevronImage;
        public Image icon;
        public Color iconIdle = Color.white;
        public Color iconHover = Color.white;

        private bool _hover, _selected, _init;
        private float _k;
        private Vector2 _chevHome;

        public void OnPointerEnter(PointerEventData e) => _hover = true;
        public void OnPointerExit(PointerEventData e) => _hover = false;
        public void OnSelect(BaseEventData e) => _selected = true;
        public void OnDeselect(BaseEventData e) => _selected = false;

        private void OnDisable()
        {
            _hover = _selected = false;
            _k = 0f;
            Apply(0f);
        }

        private void Update()
        {
            _k = Mathf.MoveTowards(_k, _hover || _selected ? 1f : 0f, Time.unscaledDeltaTime * 7f);
            Apply(_k * _k * (3f - 2f * _k));
        }

        private void Apply(float e)
        {
            if (chevron != null)
            {
                if (!_init) { _chevHome = chevron.anchoredPosition; _init = true; }
                chevron.anchoredPosition = _chevHome + new Vector2(10f * e, 0f);
            }
            if (chevronImage != null)
            {
                var c = chevronImage.color;
                c.a = Mathf.Lerp(0.38f, 1f, e);
                chevronImage.color = c;
            }
            if (icon != null) icon.color = Color.Lerp(iconIdle, iconHover, e);
        }
    }
}
