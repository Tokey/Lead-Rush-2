using LR2.Audio;
using UnityEngine;
using UnityEngine.EventSystems;

namespace LR2.UI
{
    /// <summary>Hover/click sound + subtle scale and accent slide for menu buttons.</summary>
    public class UIButtonFX : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerClickHandler
    {
        public RectTransform accentBar;
        public float hoverScale = 1.03f;
        private bool _hover;

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hover = true;
            var s = LR2CommonSounds.Instance;
            if (s != null) LR2Audio.Play2D(s.uiHover);
        }

        public void OnPointerExit(PointerEventData eventData) => _hover = false;

        public void OnPointerClick(PointerEventData eventData)
        {
            var s = LR2CommonSounds.Instance;
            if (s != null) LR2Audio.Play2D(s.uiClick);
        }

        private void Update()
        {
            float target = _hover ? hoverScale : 1f;
            transform.localScale = Vector3.one * Mathf.Lerp(transform.localScale.x, target, Time.unscaledDeltaTime * 14f);
            if (accentBar != null)
                accentBar.localScale = new Vector3(Mathf.Lerp(accentBar.localScale.x, _hover ? 1f : 0f, Time.unscaledDeltaTime * 14f), 1f, 1f);
        }
    }
}
