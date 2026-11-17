using System.Collections;
using System.Linq;
using System.Reflection;
using Bpsim.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Bpsim.QA
{
    public static partial class UiThemeQaEditor
    {
        // RunSai -> Temp/UiThemeQA/sai-layout.json. Checks the original prefab geometry
        // and graphic identities with SAI colors; never changes/saves source scenes.
        public static string RunSai() => Start("sai-layout");
        public static string SelectSai()
        {
            UiTheme.Set(UiTheme.SaiId);
            return "selected SAI 深灰";
        }

        private static IEnumerator ExerciseSaiLayout()
        {
            UiTheme.Set(UiTheme.SaiId);
            double until = EditorApplication.timeSinceStartup + .5;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check("SAI palette is registered", UiTheme.Current.IsSai && UiTheme.Palettes.Count == 10);
            Check("SAI neutral panel token", Near(UiTheme.Current.Panel, new Color(40f / 255, 40f / 255, 40f / 255, 1)));
            Check("SAI sunken field token", Near(UiTheme.Current.Field, new Color(32f / 255, 32f / 255, 32f / 255, 1)));
            Check("SAI purple selection token", Near(UiTheme.Current.Selection, new Color(96f / 255, 96f / 255, 128f / 255, 1)));
            var all = Object.FindObjectsOfType<RectTransform>(true);
            var sidebar = all.First(t => t.name == "LeftSidebar");
            var selector = all.First(t => t.name == "PartSelector");
            var strip = all.First(t => t.name == "LayerStrip");
            var play = Object.FindObjectsOfType<Button>(true).First(b => b.name == "SimulationButton");
            var firstFile = Object.FindObjectsOfType<Button>(true).First(b => b.name == "LoadButton");
            var top = (RectTransform)firstFile.transform.parent;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameObject/UserInterface.prefab");
            var originalSidebar = prefab.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "LeftSidebar");
            var originalTop = prefab.GetComponentsInChildren<RectTransform>(true)
                .First(t => t.name == "ActionBar" && t.GetComponentInChildren<Button>(true) != null);
            Check("original topbar height", Mathf.Abs(top.rect.height - originalTop.rect.height) < .1f);
            Check("original playback button size", Mathf.Abs(((RectTransform)play.transform).rect.width - 76) < .1f);
            Check("original sidebar width", Mathf.Abs(sidebar.rect.width - originalSidebar.rect.width) < .1f);
            Check("original horizontal layer strip height", Mathf.Abs(strip.rect.height - 92) < .1f);
            Check("no redesign desktop component", typeof(UiTheme).Assembly.GetType("Bpsim.UI.UiBuildDesktop") == null);
            Check("no redesign status/header objects", !all.Any(t => t.name == "DesktopStatusBar" || t.name == "LayersHeader"));
            Check("no replacement card graphics", !all.Any(t => t.name == "CardSurface" || t.name == "SelectedAccent"));
            var scroll = selector.Find("Scroll View") as RectTransform;
            Check("original full-width parts scroll", scroll.offsetMin.x == 0 && scroll.offsetMax.x == 0 && scroll.offsetMax.y == -92);
            var layer0 = strip.GetComponentsInChildren<Button>(true).First(t => t.name == "Layer0");
            var layer1 = strip.GetComponentsInChildren<Button>(true).First(t => t.name == "Layer1");
            var rect0 = (RectTransform)layer0.transform;
            var rect1 = (RectTransform)layer1.transform;
            Check("original square layer buttons", rect0.rect.size == new Vector2(76, 76));
            Check("layers stack horizontally", rect0.anchoredPosition.y == rect1.anchoredPosition.y
                && rect0.anchoredPosition.x < rect1.anchoredPosition.x);
            Check("original full-size selection foreground", ((RectTransform)rect0.Find("Foreground")).rect.size == rect0.rect.size);
            var grid = scroll.GetComponentInChildren<GridLayoutGroup>(true);
            var originalSelector = prefab.GetComponentsInChildren<Component>(true).First(c => c != null && c.GetType().Name == "PartSelector");
            var originalGrid = originalSelector.GetComponentInChildren<GridLayoutGroup>(true);
            Check("original part grid", grid.cellSize == originalGrid.cellSize && grid.spacing == originalGrid.spacing
                && grid.constraintCount == originalGrid.constraintCount);
            var template = (GameObject)new SerializedObject(originalSelector).FindProperty("m_itemTemplate").objectReferenceValue;
            var originalToggle = template.GetComponent<Toggle>();
            var originalIcon = (RectTransform)template.transform.Find("PartIcon");
            var cards = Object.FindObjectsOfType<Toggle>(true).Where(t => t.name.StartsWith("Item_")).ToArray();
            Check("part buttons exist", cards.Length > 40);
            foreach (var card in cards)
            {
                Check("original graphic type " + card.name, card.targetGraphic.GetType() == originalToggle.targetGraphic.GetType());
                Check("original graphic path " + card.name, Relative(card.targetGraphic.transform, card.transform)
                    == Relative(originalToggle.targetGraphic.transform, template.transform));
                var icon = (RectTransform)card.transform.Find("PartIcon");
                Check("original icon geometry " + card.name, icon.sizeDelta == originalIcon.sizeDelta && icon.anchoredPosition == originalIcon.anchoredPosition);
                Check("SAI colors on original button " + card.name, Near(card.targetGraphic.color,
                    card.isOn ? UiTheme.Current.Selection : UiTheme.Current.Entry));
            }
            var toolbar = Object.FindObjectsOfType<Button>(true).Where(b => b.transform.parent == top && b.gameObject.activeSelf).ToArray();
            foreach (var button in toolbar)
                Check("original tool sizing " + button.name, ((RectTransform)button.transform).rect.width <= 64.1f
                    && ((RectTransform)button.transform).rect.width >= 32);
            var sliderRect = all.First(t => t.name == "HudSlider");
            Check("HUD and file controls do not overlap", MaxX(sliderRect) < MinX((RectTransform)firstFile.transform));
            var toolbarType = typeof(UiTheme).Assembly.GetType("Bpsim.UI.BuildToolbar");
            var toolbarComponent = top.GetComponent(toolbarType);
            var reflow = toolbarType.GetMethod("Reflow", BindingFlags.Instance | BindingFlags.NonPublic);
            var savedDelta = top.sizeDelta;
            try
            {
                foreach (float width in new[] { 1440f, 1920f })
                {
                    top.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                    reflow.Invoke(toolbarComponent, null);
                    Check("original toolbar fits " + width, toolbar.All(b =>
                        ((RectTransform)b.transform).anchoredPosition.x + ((RectTransform)b.transform).rect.width / 2 < width - 104));
                }
            }
            finally { top.sizeDelta = savedDelta; reflow.Invoke(toolbarComponent, null); }
            var tabs = all.First(t => t.name == "SceneTabView");
            var originalTabs = prefab.GetComponentsInChildren<RectTransform>(true).First(t => t.name == "SceneTabView");
            Check("original scene tab placement", tabs.anchoredPosition == originalTabs.anchoredPosition && tabs.sizeDelta == originalTabs.sizeDelta);

            // Preserve the old horizontal layer drag, including click suppression/clamping.
            s_fixture = new GameObject("SaiScrollFixture", typeof(RectTransform), typeof(Canvas));
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(s_fixture.transform, false);
            ((RectTransform)viewport.transform).sizeDelta = new Vector2(196, 92);
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            ((RectTransform)content.transform).sizeDelta = new Vector2(600, 92);
            var dragType = typeof(UiTheme).Assembly.GetType("Bpsim.UI.LayerStripDrag");
            var drag = viewport.AddComponent(dragType);
            dragType.GetMethod("Bind").Invoke(drag, new object[] { content.transform, viewport.transform });
            var pointer = new PointerEventData(EventSystem.current) { position = Vector2.zero };
            ((IBeginDragHandler)drag).OnBeginDrag(pointer);
            pointer.position = new Vector2(-1000, 0);
            ((IDragHandler)drag).OnDrag(pointer);
            ((IEndDragHandler)drag).OnEndDrag(pointer);
            Check("horizontal drag clamps overflow", Mathf.Abs(((RectTransform)content.transform).anchoredPosition.x + 404) < .01f);
            Check("drag suppresses layer click", (bool)dragType.GetProperty("SuppressClick").GetValue(drag));
        }

        private static string Relative(Transform child, Transform root)
        {
            string path = child.name;
            while (child.parent != null && child.parent != root) { child = child.parent; path = child.name + "/" + path; }
            return path;
        }
        private static float MinX(RectTransform rect) { var c = new Vector3[4]; rect.GetWorldCorners(c); return c[0].x; }
        private static float MaxX(RectTransform rect) { var c = new Vector3[4]; rect.GetWorldCorners(c); return c[2].x; }
    }
}
