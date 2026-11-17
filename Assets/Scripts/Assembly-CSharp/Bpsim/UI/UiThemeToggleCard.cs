using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
    /// <summary>只为原版 Toggle 底板/选中层换色，不创建图形、替换目标或调整布局。</summary>
    internal sealed class UiThemeToggleCard : MonoBehaviour
    {
        private Toggle m_toggle;
        private Graphic m_plate;
        private Graphic m_selection;
        private ColorBlock m_originalColors;

        internal static void Attach(Toggle toggle)
        {
            if (toggle != null && toggle.targetGraphic != null && toggle.GetComponent<UiThemeToggleCard>() == null)
                toggle.gameObject.AddComponent<UiThemeToggleCard>();
        }

        private void Awake()
        {
            m_toggle = GetComponent<Toggle>();
            m_plate = m_toggle.targetGraphic;
            m_selection = m_toggle.graphic;
            m_originalColors = m_toggle.colors;
            if (GetComponent<UiThemeOwned>() == null) gameObject.AddComponent<UiThemeOwned>();
        }

        private void OnEnable()
        {
            m_toggle.onValueChanged.AddListener(OnSelection);
            UiTheme.Changed += ApplyPalette;
            ApplyPalette(UiTheme.Current);
        }

        private void OnDisable()
        {
            if (m_toggle != null) m_toggle.onValueChanged.RemoveListener(OnSelection);
            UiTheme.Changed -= ApplyPalette;
        }

        private void OnSelection(bool selected) => ApplyPalette(UiTheme.Current);
        internal void RefreshPalette() => ApplyPalette(UiTheme.Current);

        private void ApplyPalette(UiPalette palette)
        {
            if (m_plate == null || m_toggle == null) return;
            // 原版 targetGraphic 与 Toggle.graphic 保持原位、原组件与原射线目标。
            m_plate.color = m_toggle.isOn ? palette.Selection : palette.Entry;
            if (m_selection != null) m_selection.color = palette.Selection;
            ColorBlock colors = m_originalColors;
            if (palette.Id != UiTheme.DefaultId)
            {
                colors.normalColor = colors.selectedColor = Color.white;
                colors.highlightedColor = palette.IsSai ? Color.white : new Color(1.12f, 1.12f, 1.12f, 1f);
                colors.pressedColor = new Color(.86f, .86f, .86f, 1f);
                colors.disabledColor = new Color(.75f, .75f, .75f, .5f);
                colors.colorMultiplier = 1f;
            }
            m_toggle.colors = colors;
        }
    }
}
