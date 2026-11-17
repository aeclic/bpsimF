using System.Collections.Generic;
using System.IO;
using Bpsim.Levels;
using Bpsim.Parts;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Bpsim.UI
{
    /// <summary>
    /// bpsimtaffy 样式的「可视化」场景弹窗。
    ///
    /// 外观直接复刻 bpsimtaffy 的 SaveSlotPanel（其 Assembly-CSharp.dll 的 IL）：
    /// 860×600 的扁平深色面板、无蓝色标题条、22 号左对齐标题、右上角 40×40 的 ✕、
    /// 可滚动的 78 高槽位列表（每条左侧 96×54 缩略图）、底部一排 130×36 的按钮。
    /// 配色沿用 SaveSlotPanel 的 .cctor 常量，未引入 PartPropertyPanel 的蓝色标题栏。
    ///
    /// 本项目是较旧的源码，没有 SaveService / SaveSlotInfo / LocalizationService 等存档槽后端，
    /// 因此列表里只展示「当前场景」这一条，缩略图用 Screenshot.Capture 实时抓取场景相机；
    /// 导入 / 导出仍打开本项目的文件对话框 SceneLoader（与 taffy 的
    /// SaveSlotPanel → ActionBarView.OpenSceneLoader(Read/Write) 一致）。
    /// </summary>
    internal class SceneVisualPopup : MonoBehaviour
    {
        // ---- 配色走 UiTheme：默认色板下接近 taffy SaveSlotPanel 观感，切换配色即时生效 ----
        private static Color PanelColor => UiTheme.Current.Panel;
        private static Color EntryColor => UiTheme.Current.Entry;
        private static Color ButtonColor => UiTheme.Current.ToolbarButton;
        private static Color ButtonMutedColor => UiTheme.Current.ButtonMuted;
        private static Color TextColor => UiTheme.Current.Text;
        private static Color MutedColor => UiTheme.Current.TextMuted;
        private static Color ThumbColor => UiTheme.Current.Shade(0.6f);

        // ---- 直接复用 bpsimtaffy SaveSlotPanel 的尺寸 ----
        private const float PanelWidth = 860f;
        private const float PanelHeight = 600f;
        private const float EntryHeight = 78f;
        private const float ThumbWidth = 96f;
        private const float ThumbHeight = 54f;
        private const int CaptureWidth = 384;
        private const int CaptureHeight = 216;

        private UserInterface m_root;
        private SceneLoaderMode m_mode;
        private Font m_font;
        private RectTransform m_listContent;
        private readonly List<UnityEngine.Object> m_generatedAssets = new List<UnityEngine.Object>();

        /// <summary>打开（或复用已存在的）可视化弹窗。</summary>
        public static void Open(UserInterface root, SceneLoaderMode mode)
        {
            if (root == null || root.SubCanvas == null || PartManager.Instance.IsSimulating)
            {
                return;
            }

            SceneVisualPopup existing = root.SubCanvas.GetComponentInChildren<SceneVisualPopup>(true);
            if (existing != null)
            {
                existing.SetMode(mode);
                return;
            }

            GameObject overlay = new GameObject(
                "SceneVisualPopup",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image));
            overlay.transform.SetParent(root.SubCanvas.transform, false);
            overlay.transform.SetAsLastSibling();

            RectTransform backdropRect = (RectTransform)overlay.transform;
            backdropRect.anchorMin = Vector2.zero;
            backdropRect.anchorMax = Vector2.one;
            backdropRect.offsetMin = Vector2.zero;
            backdropRect.offsetMax = Vector2.zero;

            // 半透明黑底：遮住并阻断对场景的点击。关闭只走面板上的 关闭 / ✕ 按钮——
            // 不把背景做成 Button，否则点面板空白处时点击事件会冒泡给背景而误关弹窗。
            Image backdrop = overlay.GetComponent<Image>();
            backdrop.color = UiTheme.Current.Backdrop;
            backdrop.raycastTarget = true;

            SceneVisualPopup popup = overlay.AddComponent<SceneVisualPopup>();
            popup.Build(root, mode);
        }

        private void SetMode(SceneLoaderMode mode)
        {
            m_mode = mode;
            transform.SetAsLastSibling();
            Rebuild();
        }

        private void Build(UserInterface root, SceneLoaderMode mode)
        {
            m_root = root;
            m_mode = mode;
            m_font = ResolveFont(root);

            // 面板：居中 860×600，扁平深色，无标题条。
            RectTransform panel = CreateRect(
                "Panel", transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(PanelWidth, PanelHeight));
            Image panelImage = panel.gameObject.AddComponent<Image>();
            panelImage.color = PanelColor;
            panelImage.raycastTarget = true;

            // 标题：22 号、MiddleLeft、直接贴在面板上（不是蓝色 header bar）。
            CreateLabel(
                panel, "Title", "场景存档", 22, TextColor, TextAnchor.MiddleLeft,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f, -8f), new Vector2(420f, 44f));

            // 右上角 40×40 的 ✕：中性灰底，不抢主按钮的视觉。
            Button close = CreateButton(
                panel, "✕", 18, Close, ButtonMutedColor,
                40f, 40f, Vector2.one, new Vector2(-28f, -30f));

            BuildList(panel);
            BuildBottomBar(panel);

            Rebuild();
            // 复用项目里的滚动条样式助手（本弹窗无 Scrollbar 时为空操作）。
            UIExtensions.StyleScrollbars(gameObject);
            UiTheme.Changed += OnThemeChanged;
        }

        private void OnThemeChanged(UiPalette palette)
        {
            // 弹窗开着时切配色：整棵重刷 + 列表条目重建（条目里的色是建时写死的）。
            UiThemeApplicator.ApplyTo(gameObject);
            Rebuild();
        }

        /// <summary>滚动槽位列表：Viewport + RectMask2D + VerticalLayoutGroup + ContentSizeFitter。</summary>
        private void BuildList(Transform panel)
        {
            RectTransform scroll = CreateRect(
                "Scroll", panel,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(-40f, -150f));
            scroll.anchoredPosition = new Vector2(0f, -22f);
            ScrollRect scrollRect = scroll.gameObject.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.scrollSensitivity = 28f;

            RectTransform viewport = CreateRect(
                "Viewport", scroll,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
            Image viewportImage = viewport.gameObject.AddComponent<Image>();
            viewportImage.color = ThumbColor;
            viewport.gameObject.AddComponent<RectMask2D>();
            scrollRect.viewport = viewport;

            RectTransform content = CreateRect(
                "Content", viewport,
                new Vector2(0f, 1f), Vector2.one, new Vector2(0.5f, 1f), Vector2.zero);
            VerticalLayoutGroup layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.spacing = 8f;
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.childControlHeight = false;
            layout.childControlWidth = true;
            layout.childForceExpandHeight = false;
            layout.childForceExpandWidth = true;
            ContentSizeFitter fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            scrollRect.content = content;
            m_listContent = content;
        }

        /// <summary>底部按钮排：与 taffy 同样的 130×36 尺寸与配色。</summary>
        private void BuildBottomBar(Transform panel)
        {
            CreateButton(
                panel, "新建", 15, () => OpenSceneLoader(m_mode), ButtonColor,
                130f, 36f, Vector2.zero, new Vector2(28f, 26f));
            CreateButton(
                panel, "导入", 15, () => OpenSceneLoader(SceneLoaderMode.Read), ButtonMutedColor,
                130f, 36f, Vector2.zero, new Vector2(168f, 26f));
            CreateButton(
                panel, "导出", 15, () => OpenSceneLoader(SceneLoaderMode.Write), ButtonMutedColor,
                130f, 36f, Vector2.zero, new Vector2(308f, 26f));
            CreateButton(
                panel, "关闭", 15, Close, ButtonMutedColor,
                130f, 36f, new Vector2(1f, 0f), new Vector2(-28f, 26f));
        }

        /// <summary>重建列表内容：销毁旧条目与旧截图，再放一条「当前场景」。</summary>
        private void Rebuild()
        {
            if (m_listContent == null)
            {
                return;
            }

            for (int i = m_listContent.childCount - 1; i >= 0; i--)
            {
                UnityEngine.Object.Destroy(m_listContent.GetChild(i).gameObject);
            }
            ReleaseGenerated();

            if (!PartManager.Instance.HasActiveScene())
            {
                CreateLabel(
                    m_listContent, "Empty", "暂无场景预览", 15, MutedColor, TextAnchor.MiddleCenter,
                    new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                    Vector2.zero, new Vector2(0f, 60f));
                return;
            }

            CreateCurrentEntry();
        }

        /// <summary>一条 78 高的槽位：缩略图 + 名称 + 部件数/最近文件 + 加载/保存按钮。</summary>
        private void CreateCurrentEntry()
        {
            RectTransform entry = CreateRect(
                "Entry_Current", m_listContent,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                new Vector2(0f, EntryHeight));
            Image entryImage = entry.gameObject.AddComponent<Image>();
            entryImage.color = EntryColor;
            LayoutElement element = entry.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = EntryHeight;
            element.minHeight = EntryHeight;

            // 96×54 缩略图底板（taffy 同款），上层用 RawImage 显示实时截图。
            RectTransform thumb = CreateRect(
                "Thumb", entry,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(ThumbWidth, ThumbHeight));
            thumb.anchoredPosition = new Vector2(60f, 0f);
            Image thumbPlate = thumb.gameObject.AddComponent<Image>();
            thumbPlate.color = ThumbColor;
            thumbPlate.preserveAspect = true;

            RectTransform previewRect = CreateRect(
                "Preview", thumb,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero);
            RawImage preview = previewRect.gameObject.AddComponent<RawImage>();
            preview.color = TextColor;
            preview.raycastTarget = false;

            string sceneName = Truncate(PartManager.Instance.ActiveScene.SceneName, 22);
            if (string.IsNullOrEmpty(sceneName))
            {
                sceneName = "当前场景";
            }
            int partCount = PartManager.Instance.ActiveScene.Unmanaged.Parts.Length;
            string lastFile = "（无）";
            string scenePath = LevelSession.ScenePath;
            if (!string.IsNullOrEmpty(scenePath))
            {
                lastFile = Truncate(Path.GetFileName(scenePath), 30);
            }

            CreateLabel(
                entry, "Name", sceneName, 17, TextColor, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(170f, 14f), new Vector2(160f, 26f));
            CreateLabel(
                entry, "Meta", partCount + " 部件 · " + lastFile, 11, MutedColor, TextAnchor.MiddleLeft,
                new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                new Vector2(170f, -14f), new Vector2(160f, 20f));

            CreateButton(
                entry, "加载", 13, () => OpenSceneLoader(SceneLoaderMode.Read), ButtonColor,
                72f, 28f, new Vector2(0f, 0.5f), new Vector2(336f, 0f));
            CreateButton(
                entry, "保存", 13, () => OpenSceneLoader(SceneLoaderMode.Write), ButtonColor,
                72f, 28f, new Vector2(0f, 0.5f), new Vector2(414f, 0f));

            RefreshPreview(preview);
        }

        /// <summary>实时抓取场景相机作为可视化缩略图；失败时退回深色底板。</summary>
        private void RefreshPreview(RawImage target)
        {
            if (target == null)
            {
                return;
            }

            if (PartManager.Instance.HasActiveScene()
                && SceneCamera.Instance != null
                && SceneCamera.Instance.Camera != null)
            {
                try
                {
                    Texture2D texture = Screenshot.Capture(
                        SceneCamera.Instance.Camera, CaptureWidth, CaptureHeight);
                    if (texture != null)
                    {
                        m_generatedAssets.Add(texture);
                        target.texture = texture;
                        target.enabled = true;
                        return;
                    }
                }
                catch (System.Exception ex)
                {
                    // Screenshot.Capture 在无活动场景 / 非法尺寸时会抛异常，静默退回占位底板。
                    UnityEngine.Debug.LogException(ex);
                }
            }

            target.texture = null;
            target.enabled = false;
        }

        /// <summary>关闭预览，再打开原有的文件对话框（避免加载后弹窗里留着旧场景截图）。</summary>
        private void OpenSceneLoader(SceneLoaderMode mode)
        {
            UserInterface root = m_root;
            Close();
            if (root != null)
            {
                root.ActionBar.OpenSceneLoader(mode);
            }
        }

        private void Close()
        {
            UnityEngine.Object.Destroy(gameObject);
        }

        private void OnDestroy()
        {
            UiTheme.Changed -= OnThemeChanged;
            ReleaseGenerated();
        }

        private void ReleaseGenerated()
        {
            for (int i = 0; i < m_generatedAssets.Count; i++)
            {
                if (m_generatedAssets[i] != null)
                {
                    UnityEngine.Object.Destroy(m_generatedAssets[i]);
                }
            }
            m_generatedAssets.Clear();
        }

        // ---- taffy SaveSlotPanel 的 CreateRect / CreateLabel / CreateButton 等价实现 ----

        private static RectTransform CreateRect(
            string name, Transform parent,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 size)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = Vector2.zero;
            return rect;
        }

        private Text CreateLabel(
            Transform parent, string name, string content, int fontSize, Color color,
            TextAnchor alignment, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 position, Vector2 size)
        {
            RectTransform rect = CreateRect(name, parent, anchorMin, anchorMax, pivot, size);
            rect.anchoredPosition = position;
            Text text = rect.gameObject.AddComponent<Text>();
            text.font = m_font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.text = content;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            return text;
        }

        private Button CreateButton(
            Transform parent, string label, int fontSize, UnityAction onClick, Color color,
            float width, float height, Vector2 anchor, Vector2 position)
        {
            RectTransform rect = CreateRect(
                "Button_" + label, parent, anchor, anchor, anchor, new Vector2(width, height));
            rect.anchoredPosition = position;

            Image plate = rect.gameObject.AddComponent<Image>();
            plate.color = color;
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = plate;
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            button.colors = colors;
            if (onClick != null)
            {
                button.onClick.AddListener(onClick);
            }

            Text text = CreateLabel(
                rect, "Text", label, fontSize, TextColor, TextAnchor.MiddleCenter,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            RectTransform textRect = (RectTransform)text.transform;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return button;
        }

        private static Font ResolveFont(UserInterface root)
        {
            if (root != null)
            {
                Text sample = root.GetComponentInChildren<Text>(true);
                if (sample != null && sample.font != null)
                {
                    return sample.font;
                }
            }

            Font font = null;
            try
            {
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch (System.Exception)
            {
                // Unity 2022 里 Arial.ttf 已被 LegacyRuntime.ttf 取代。
            }
            if (font == null)
            {
                font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            return font;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }
            return "…" + value.Substring(value.Length - maxLength + 1);
        }
    }
}
