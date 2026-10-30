using System.Collections.Generic;
using System.Text;
using Bpsim.Parts;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bpsim.UI
{
    /// <summary>
    /// 建造页左侧边栏里的图层条（对齐 BPIR 的 <c>LayerPanelUI</c>，但走 bpsim 自己的 ECS 数据）。
    ///
    /// 位置：夹在左侧工具栏（4 颗工具按钮 + 放大镜 = 5 颗）与部件列表之间，横向铺满部件列表宽度（450），
    /// 与上下两者之间没有间隙。图层条是 LeftSidebar 的子物体，不是独立浮窗。
    /// 形状：每个图层是一颗正方形按钮，向右横向堆叠；层数多到超出宽度时按住左右拖动平移。
    /// 数据：层数 / 活动层仍然只存在 ECS 侧的 <see cref="PartSceneUnmanaged" /> 里，本类只做视图与输入，
    /// 不持有任何层状态副本 —— 与 <c>PartBuildLayerDimSystem</c> 读的是同一份数据。
    /// </summary>
    [DefaultExecutionOrder(-20)]
    internal sealed class LayerPanelUI : MonoBehaviour
    {
        /// <summary>刷新间隔（秒）。图层计数变化不频繁，0.2s 足够。</summary>
        private const float RefreshInterval = 0.2f;

        /// <summary>图层条高度。</summary>
        private const float StripHeight = 92f;

        /// <summary>层按钮正方形边长（对齐 BPIR 的 88，这里按 450 宽度略收）。</summary>
        private const float SquareSize = 76f;

        /// <summary>层按钮之间的横向间距。</summary>
        private const float SquareSpacing = 8f;

        /// <summary>层按钮圆角半径（像素，作用在 <see cref="RoundedSprite" /> 的九宫格边框上）。</summary>
        private const int CornerRadius = 12;

        /// <summary>层条左右内边距。</summary>
        private const float StripPadding = 8f;

        /// <summary>末尾「＋ / －」小按钮边长。</summary>
        private const float SmallSize = 48f;

        // 取自 UserInterface.prefab 的原生配色：工具按钮底 0.35 灰、选中前景深蓝 (0, 0.196, 0.4)。
        private static readonly Color s_stripColor = new Color(0.145f, 0.145f, 0.145f, 1f);
        private static readonly Color s_buttonColor = new Color(0.35f, 0.35f, 0.35f, 1f);
        private static readonly Color s_smallColor = new Color(0.26f, 0.26f, 0.26f, 1f);
        private static readonly Color s_activeColor = new Color(0f, 0.19607843f, 0.4f, 1f);
        private static readonly Color s_textColor = new Color(0.93f, 0.93f, 0.93f, 1f);
        private static readonly Color s_mutedColor = new Color(0.72f, 0.75f, 0.8f, 1f);

        /// <summary>圆角九宫格 sprite 的缓存（所有层按钮共用一张）。</summary>
        private static Sprite s_roundedSprite;

        private sealed class Entry
        {
            public int Layer;
            public GameObject Root;
            public Image Foreground;
            public Text Label;
            public Text Count;
        }

        private readonly List<Entry> m_entries = new List<Entry>(20);
        private readonly StringBuilder m_signatureBuilder = new StringBuilder(160);

        private BuildToolController m_controller;
        private RectTransform m_strip;
        private RectTransform m_content;
        private LayerStripDrag m_drag;
        private Font m_font;
        private string m_signature;
        private float m_nextRefresh;
        private bool m_panelVisible = true;
        private int m_builtLayerCount = -1;

        /// <summary>由 <see cref="BuildToolController" /> 在 AddComponent 之后调用。</summary>
        public void Initialize(BuildToolController controller)
        {
            m_controller = controller;
            m_font = LoadFont();
            Build();
            Refresh(force: true);
        }

        /// <summary>显隐图层条。QA（<c>LayerConnectionQA.ShowLayerPanel/HideLayerPanel</c>）依赖此接口。</summary>
        public void SetPanelVisible(bool visible)
        {
            m_panelVisible = visible;
            if (m_strip != null)
            {
                m_strip.gameObject.SetActive(visible);
            }

            Refresh(force: true);
        }

        private static Font LoadFont()
        {
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
            {
                font = Font.CreateDynamicFontFromOSFont("Segoe UI", 14);
            }

            return font;
        }

        /// <summary>左侧边栏的 RectTransform：部件列表的直接父物体。</summary>
        private RectTransform ResolveSidebar()
        {
            UserInterface ui = UserInterface.Instance;
            if (ui != null && ui.PartSelector != null)
            {
                return ui.PartSelector.transform.parent as RectTransform;
            }

            Transform found = transform.Find("Panel/LeftSidebar");
            return found as RectTransform;
        }

        private void Build()
        {
            RectTransform sidebar = ResolveSidebar();
            if (sidebar == null)
            {
                return;
            }

            UserInterface ui = UserInterface.Instance;
            RectTransform selector = null;
            if (ui != null && ui.PartSelector != null)
            {
                selector = ui.PartSelector.transform as RectTransform;
            }

            // 部件列表紧贴工具栏下沿、铺满整个左侧边栏剩余高度（两者之间没有间隙）。
            float toolbarHeight = 90f;
            if (sidebar.childCount > 0 && sidebar.GetChild(0) is RectTransform toolbar && toolbar.rect.height > 1f)
            {
                toolbarHeight = toolbar.rect.height;
            }

            if (selector != null)
            {
                selector.anchorMin = new Vector2(0f, 1f);
                selector.anchorMax = new Vector2(1f, 1f);
                selector.pivot = new Vector2(0f, 1f);
                selector.anchoredPosition = new Vector2(0f, -toolbarHeight);
                selector.sizeDelta = new Vector2(0f, sidebar.rect.height - toolbarHeight);
            }

            // 图层条是部件列表内部顶端的一条覆盖带：挂在 PartSelector 下面，
            // 视觉上属于同一个面板（而不是夹在工具栏与列表之间把列表挤下去）。
            RectTransform stripParent = selector != null ? selector : sidebar;
            var stripObject = new GameObject("LayerStrip", typeof(RectTransform));
            m_strip = (RectTransform)stripObject.transform;
            m_strip.SetParent(stripParent, false);
            m_strip.anchorMin = new Vector2(0f, 1f);
            m_strip.anchorMax = new Vector2(1f, 1f);
            m_strip.pivot = new Vector2(0f, 1f);
            m_strip.anchoredPosition = Vector2.zero;
            m_strip.sizeDelta = new Vector2(0f, StripHeight);
            // 排在 Scroll View 之后，保证覆盖带压在部件网格之上。
            m_strip.SetSiblingIndex(Mathf.Max(0, stripParent.childCount - 1));

            Image background = stripObject.AddComponent<Image>();
            background.color = s_stripColor;
            background.raycastTarget = true;

            // 部件网格让出图层条的高度，避免第一行部件被盖住。
            if (selector != null)
            {
                Transform scroll = selector.Find("Scroll View");
                if (scroll is RectTransform scrollRect)
                {
                    scrollRect.anchorMin = Vector2.zero;
                    scrollRect.anchorMax = Vector2.one;
                    scrollRect.offsetMin = Vector2.zero;
                    scrollRect.offsetMax = new Vector2(0f, -StripHeight);
                }
            }

            var viewportObject = new GameObject("Viewport", typeof(RectTransform));
            var viewport = (RectTransform)viewportObject.transform;
            viewport.SetParent(m_strip, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            viewportObject.AddComponent<RectMask2D>();

            var contentObject = new GameObject("Content", typeof(RectTransform));
            m_content = (RectTransform)contentObject.transform;
            m_content.SetParent(viewport, false);
            m_content.anchorMin = new Vector2(0f, 0.5f);
            m_content.anchorMax = new Vector2(0f, 0.5f);
            m_content.pivot = new Vector2(0f, 0.5f);
            m_content.anchoredPosition = Vector2.zero;
            m_content.sizeDelta = new Vector2(StripPadding * 2f, StripHeight - 12f);

            // 拖动挂在图层条本身（它有 Image，是射线目标），按钮上的拖动会冒泡到这里。
            m_drag = stripObject.AddComponent<LayerStripDrag>();
            m_drag.Bind(m_content, viewport);
        }

        private void Update()
        {
            if (m_strip == null)
            {
                return;
            }

            PartManager manager = PartManager.Instance;
            bool visible = manager != null && !manager.IsSimulating && CurrentScene() != null;
            bool wanted = visible && m_panelVisible;
            if (m_strip.gameObject.activeSelf != wanted)
            {
                m_strip.gameObject.SetActive(wanted);
            }

            if (!wanted)
            {
                return;
            }

            if (Time.unscaledTime >= m_nextRefresh)
            {
                m_nextRefresh = Time.unscaledTime + RefreshInterval;
                Refresh(force: false);
            }
        }

        private PartScene CurrentScene()
        {
            return m_controller != null ? m_controller.CurrentScene : null;
        }

        private void Refresh(bool force)
        {
            if (m_strip == null)
            {
                return;
            }

            PartScene scene = CurrentScene();
            if (scene == null)
            {
                return;
            }

            ref PartSceneUnmanaged unmanaged = ref scene.Unmanaged;
            int layerCount = Mathf.Max(PartBuildLayer.MinLayer, unmanaged.LayerCount);
            int activeLayer = unmanaged.ActiveLayer;

            m_signatureBuilder.Clear();
            m_signatureBuilder.Append(activeLayer).Append('|').Append(layerCount).Append('|');
            for (int layer = PartBuildLayer.MinLayer; layer <= layerCount; layer++)
            {
                m_signatureBuilder.Append(unmanaged.CountPartsOnLayer(layer)).Append(',');
            }

            string signature = m_signatureBuilder.ToString();
            bool structureChanged = layerCount != m_builtLayerCount;
            if (!force && !structureChanged && signature == m_signature)
            {
                return;
            }

            m_signature = signature;
            if (structureChanged || m_entries.Count == 0)
            {
                RebuildEntries(ref unmanaged, layerCount);
            }

            for (int i = 0; i < m_entries.Count; i++)
            {
                Entry entry = m_entries[i];
                if (entry.Root == null || entry.Layer < 0)
                {
                    continue;
                }

                bool active = entry.Layer == activeLayer;
                entry.Foreground.enabled = active;
                entry.Label.color = active ? Color.white : s_textColor;
                if (entry.Count != null)
                {
                    int count = entry.Layer == 0
                        ? TotalParts(ref unmanaged, layerCount)
                        : unmanaged.CountPartsOnLayer(entry.Layer);
                    entry.Count.text = count > 0 ? count + " 件" : "空";
                    entry.Count.color = active ? new Color(0.85f, 0.9f, 1f, 1f) : s_mutedColor;
                }
            }
        }

        private static int TotalParts(ref PartSceneUnmanaged unmanaged, int layerCount)
        {
            int total = 0;
            for (int layer = PartBuildLayer.MinLayer; layer <= layerCount; layer++)
            {
                total += unmanaged.CountPartsOnLayer(layer);
            }

            return total;
        }

        private void RebuildEntries(ref PartSceneUnmanaged unmanaged, int layerCount)
        {
            for (int i = 0; i < m_entries.Count; i++)
            {
                if (m_entries[i].Root != null)
                {
                    Destroy(m_entries[i].Root);
                }
            }

            m_entries.Clear();
            m_builtLayerCount = layerCount;

            float x = StripPadding;
            CreateEntry(0, "全部", x);
            x += SquareSize + SquareSpacing;

            for (int layer = PartBuildLayer.MinLayer; layer <= layerCount; layer++)
            {
                CreateEntry(layer, layer.ToString(), x);
                x += SquareSize + SquareSpacing;
            }

            // 末尾的 ＋ / －：层数仍可增删（BPIR 固定 6 层，这里保留可扩展，但默认 6 层）。
            x += 4f;
            CreateSmallEntry(x, "＋", AddLayer);
            x += SmallSize + SquareSpacing;
            CreateSmallEntry(x, "－", RemoveLayer);
            x += SmallSize;

            m_content.sizeDelta = new Vector2(x + StripPadding, StripHeight - 12f);
            if (m_drag != null)
            {
                m_drag.Clamp();
            }
        }

        private void CreateEntry(int layer, string label, float x)
        {
            var entryObject = new GameObject("Layer" + layer, typeof(RectTransform));
            var rect = (RectTransform)entryObject.transform;
            rect.SetParent(m_content, false);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(SquareSize, SquareSize);

            Image background = entryObject.AddComponent<Image>();
            background.color = s_buttonColor;
            ApplyRounded(background);

            var foregroundObject = new GameObject("Foreground", typeof(RectTransform));
            var foregroundRect = (RectTransform)foregroundObject.transform;
            foregroundRect.SetParent(rect, false);
            foregroundRect.anchorMin = Vector2.zero;
            foregroundRect.anchorMax = Vector2.one;
            foregroundRect.offsetMin = Vector2.zero;
            foregroundRect.offsetMax = Vector2.zero;
            var foreground = foregroundObject.AddComponent<Image>();
            foreground.color = s_activeColor;
            ApplyRounded(foreground);
            foreground.raycastTarget = false;
            foreground.enabled = false;

            var button = entryObject.AddComponent<Button>();
            button.targetGraphic = background;
            ConfigureButtonColors(button);
            int capturedLayer = layer;
            button.onClick.AddListener(() =>
            {
                if (m_drag != null && m_drag.SuppressClick)
                {
                    return;
                }

                ApplyLayer(capturedLayer);
            });

            Text main = CreateLabel(rect, "Label", label, layer == 0 ? 20 : 26, FontStyle.Bold, s_textColor);
            SetRect(main.rectTransform, new Vector2(0f, 0.42f), Vector2.one);
            Text count = CreateLabel(rect, "Count", "空", 12, FontStyle.Normal, s_mutedColor);
            SetRect(count.rectTransform, Vector2.zero, new Vector2(1f, 0.46f));

            m_entries.Add(new Entry
            {
                Layer = layer,
                Root = entryObject,
                Foreground = foreground,
                Label = main,
                Count = count
            });
        }

        private void CreateSmallEntry(float x, string label, UnityEngine.Events.UnityAction action)
        {
            var entryObject = new GameObject("Small" + label, typeof(RectTransform));
            var rect = (RectTransform)entryObject.transform;
            rect.SetParent(m_content, false);
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = new Vector2(x, 0f);
            rect.sizeDelta = new Vector2(SmallSize, SmallSize);

            Image background = entryObject.AddComponent<Image>();
            background.color = s_smallColor;
            ApplyRounded(background);

            var button = entryObject.AddComponent<Button>();
            button.targetGraphic = background;
            ConfigureButtonColors(button);
            button.onClick.AddListener(() =>
            {
                if (m_drag != null && m_drag.SuppressClick)
                {
                    return;
                }

                action();
            });

            Text main = CreateLabel(rect, "Label", label, 22, FontStyle.Bold, s_textColor);
            SetRect(main.rectTransform, Vector2.zero, Vector2.one);

            m_entries.Add(new Entry
            {
                Layer = -1,
                Root = entryObject,
                Foreground = null,
                Label = main,
                Count = null
            });
        }

        private Text CreateLabel(RectTransform parent, string name, string text, int size, FontStyle style, Color color)
        {
            var labelObject = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)labelObject.transform;
            rect.SetParent(parent, false);

            var label = labelObject.AddComponent<Text>();
            label.font = m_font;
            label.text = text;
            label.fontSize = size;
            label.fontStyle = style;
            label.color = color;
            label.alignment = TextAnchor.MiddleCenter;
            label.raycastTarget = false;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            return label;
        }

        private static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void ConfigureButtonColors(Button button)
        {
            ColorBlock colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.18f, 1.18f, 1.18f, 1f);
            colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(0.78f, 0.78f, 0.78f, 0.5f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.1f;
            button.colors = colors;
        }

        /// <summary>把 Image 换成圆角九宫格（sprite 程序生成，缓存一份复用）。</summary>
        private static void ApplyRounded(Image image)
        {
            if (image == null)
            {
                return;
            }

            Sprite sprite = GetRoundedSprite();
            if (sprite == null)
            {
                return;
            }

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 1f;
        }

        /// <summary>
        /// 生成 24x24 的圆角矩形 sprite：四角按 <see cref="CornerRadius" /> 做抗锯齿圆弧，
        /// 中间 1px 实心区域留给九宫格拉伸（border 全部等于圆角半径）。
        /// </summary>
        private static Sprite GetRoundedSprite()
        {
            if (s_roundedSprite != null)
            {
                return s_roundedSprite;
            }

            const int size = CornerRadius * 2;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "LayerPanelRounded",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            float radius = CornerRadius;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // 像素中心到最近圆角圆心的距离（内部像素距离为负 → 全覆盖）。
                    float px = x + 0.5f;
                    float py = y + 0.5f;
                    float cx = Mathf.Clamp(px, radius, size - radius);
                    float cy = Mathf.Clamp(py, radius, size - radius);
                    float distance = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                    float alpha = Mathf.Clamp01(radius - distance + 0.5f);
                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);

            s_roundedSprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(CornerRadius, CornerRadius, CornerRadius, CornerRadius));
            s_roundedSprite.name = "LayerPanelRounded";
            s_roundedSprite.hideFlags = HideFlags.HideAndDontSave;
            return s_roundedSprite;
        }

        private void ApplyLayer(int layer)
        {
            if (m_controller == null)
            {
                return;
            }

            m_controller.SetActiveLayer(layer);
            Refresh(force: true);
        }

        private void AddLayer()
        {
            PartScene scene = CurrentScene();
            if (scene == null)
            {
                return;
            }

            ref PartSceneUnmanaged unmanaged = ref scene.Unmanaged;
            unmanaged.SetLayerCount(unmanaged.LayerCount + 1);
            Refresh(force: true);
        }

        private void RemoveLayer()
        {
            PartScene scene = CurrentScene();
            if (scene == null)
            {
                return;
            }

            ref PartSceneUnmanaged unmanaged = ref scene.Unmanaged;
            if (!unmanaged.RemoveTopLayer())
            {
                unmanaged.SetActiveLayer(0);
            }

            Refresh(force: true);
        }
    }

    /// <summary>
    /// 图层条的横向拖动：层数多到超出可视宽度时，按住左右拖动平移整条。
    /// 拖动过的这一次抬起不触发按钮点击（<see cref="SuppressClick" />），避免「想拖动结果切了层」。
    /// </summary>
    internal sealed class LayerStripDrag : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform m_content;
        private RectTransform m_viewport;
        private Vector2 m_startPointer;
        private float m_startX;
        private bool m_dragging;
        private bool m_suppressClick;

        /// <summary>本次指针抬起是否应该吞掉按钮点击（刚拖过层条）。</summary>
        public bool SuppressClick => m_suppressClick;

        public void Bind(RectTransform content, RectTransform viewport)
        {
            m_content = content;
            m_viewport = viewport;
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (m_content == null || Overflow() <= 0.5f)
            {
                m_dragging = false;
                return;
            }

            m_dragging = true;
            m_startPointer = eventData.position;
            m_startX = m_content.anchoredPosition.x;
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!m_dragging || m_content == null)
            {
                return;
            }

            float scale = 1f;
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.scaleFactor > 0f)
            {
                scale = canvas.scaleFactor;
            }

            float delta = (eventData.position.x - m_startPointer.x) / scale;
            SetX(m_startX + delta);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (m_dragging)
            {
                m_suppressClick = true;
            }

            m_dragging = false;
        }

        private void LateUpdate()
        {
            // 点击在同一帧的 ProcessMousePress 里已经处理完，这里清掉抑制标记。
            if (m_suppressClick)
            {
                m_suppressClick = false;
            }
        }

        /// <summary>内容比视口宽出的像素数；&lt;= 0 表示不需要拖动。</summary>
        private float Overflow()
        {
            if (m_content == null || m_viewport == null)
            {
                return 0f;
            }

            return m_content.rect.width - m_viewport.rect.width;
        }

        /// <summary>把内容夹回视口内（层数变化后由面板调用）。</summary>
        public void Clamp()
        {
            if (m_content == null)
            {
                return;
            }

            SetX(m_content.anchoredPosition.x);
        }

        private void SetX(float x)
        {
            float overflow = Overflow();
            if (overflow <= 0f)
            {
                m_content.anchoredPosition = new Vector2(0f, m_content.anchoredPosition.y);
                return;
            }

            float clamped = Mathf.Clamp(x, -overflow, 0f);
            m_content.anchoredPosition = new Vector2(clamped, m_content.anchoredPosition.y);
        }
    }
}
