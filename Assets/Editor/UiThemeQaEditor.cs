using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Bpsim.UI;
using Bpsim.Parts;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace Bpsim.QA
{
    /// <summary>
    /// Invoke RunBaseline/Run in Play Mode via Unity MCP; results: Temp/UiThemeQA/*.json.
    /// PASS: zero failed assertions (color tolerance .003; identity checks exact).
    /// Covers palette cycling, synthetic uGUI plates and live toolbar/layer objects.
    /// Does not cover OS dialogs or third-party canvases; requires a live build UI.
    /// Restores the original palette and its preference; never saves/changes scenes.
    /// </summary>
    public static partial class UiThemeQaEditor
    {
        [Serializable] private sealed class Result
        {
            public string mode;
            public int passed;
            public int failed;
            public List<string> checks = new List<string>();
        }

        private static IEnumerator s_work;
        private static Result s_result;
        private static string s_palette;
        private static string s_pref;
        private static bool s_hadPref;
        private static GameObject s_fixture;
        private static GameObject s_settings;
        private static float s_timeScale;
        private static PartScene s_tabTestScene;
        private static PartScene s_previousScene;
        private static readonly MethodInfo s_apply = typeof(UiThemeApplicator).GetMethod(
            "ApplyTo", BindingFlags.Static | BindingFlags.NonPublic);

        public static string RunBaseline() => Start("baseline");
        public static string Run() => Start("regression");
        public static string RunSceneTabs() => Start("scene-tabs");

        private static string Start(string mode)
        {
            if (!Application.isPlaying) return "requires Play Mode";
            if (s_work != null) return "already running";
            s_result = new Result { mode = mode };
            s_palette = UiTheme.Current.Id;
            s_hadPref = PlayerPrefs.HasKey(UiTheme.PrefKey);
            s_pref = PlayerPrefs.GetString(UiTheme.PrefKey);
            s_timeScale = Time.timeScale;
            s_work = mode == "scene-tabs" ? ExerciseSceneTabs() : mode == "sai-layout" ? ExerciseSaiLayout() : Exercise();
            EditorApplication.update += Tick;
            return "started " + mode;
        }

        private static void Tick()
        {
            try { if (s_work.MoveNext()) return; }
            catch (Exception error) { Check("exception", false, error.ToString()); }
            EditorApplication.update -= Tick;
            s_work = null;
            Time.timeScale = s_timeScale;
            if (s_previousScene != null) PartManager.Instance.SelectScene(s_previousScene.SceneID);
            if (s_tabTestScene != null) PartManager.Instance.UnloadScene(s_tabTestScene.SceneID);
            s_previousScene = null;
            s_tabTestScene = null;
            if (s_fixture != null) Object.DestroyImmediate(s_fixture);
            if (s_settings != null) Object.DestroyImmediate(s_settings);
            UiTheme.Set(s_palette);
            if (s_hadPref) PlayerPrefs.SetString(UiTheme.PrefKey, s_pref);
            else PlayerPrefs.DeleteKey(UiTheme.PrefKey);
            PlayerPrefs.Save();
            string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/UiThemeQA"));
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, s_result.mode + ".json"), JsonUtility.ToJson(s_result, true));
            Debug.Log("[UiThemeQA] " + s_result.mode + " passed=" + s_result.passed + " failed=" + s_result.failed);
        }

        private static IEnumerator Exercise()
        {
            var toolbar = Object.FindObjectsOfType<Button>(true)
                .Where(b => b.name.StartsWith("Build") && b.transform.Find("Icon") != null)
                .ToDictionary(b => b.name, b => b.transform.Find("Icon").GetComponent<Image>());
            var icons = toolbar.ToDictionary(p => p.Key, p => p.Value.sprite);
            var enabled = toolbar.ToDictionary(p => p.Key, p => p.Value.enabled);
            Check("live toolbar exists", toolbar.Count >= 6, "count=" + toolbar.Count);
            var strip = Object.FindObjectsOfType<RectTransform>(true).FirstOrDefault(t => t.name == "LayerStrip");
            Check("live layer strip exists", strip != null);
            var layers = strip != null ? strip.GetComponentsInChildren<Button>(true)
                .ToDictionary(b => b.name, b => b.GetInstanceID()) : new Dictionary<string, int>();
            Check("layer identity check is non-vacuous", layers.Count > 0, "count=" + layers.Count);
            UiTheme.Set(UiTheme.Current.Id == "red-green" ? "blue-orange" : "red-green");
            double until = EditorApplication.timeSinceStartup + .45;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            CheckIcons(toolbar, icons, enabled, "first switch");
            if (strip != null)
                Check("layer identities survive switch", strip.GetComponentsInChildren<Button>(true)
                    .All(b => layers.TryGetValue(b.name, out int id) && id == b.GetInstanceID()));

            s_fixture = new GameObject("UiThemeQA_Fixture", typeof(RectTransform), typeof(Canvas));
            s_fixture.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            ((RectTransform)s_fixture.transform).anchoredPosition = new Vector2(-10000, -10000);
            var plain = Plate("Button_Plain", new Color(.2f, .25f, .4f, 1));
            var sliced = Plate("Button_Sliced", plain.color);
            Texture2D texture = new Texture2D(4, 4);
            texture.SetPixels(Enumerable.Repeat(Color.white, 16).ToArray());
            texture.Apply();
            Sprite sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f), 100, 0,
                SpriteMeshType.FullRect, new Vector4(1, 1, 1, 1));
            sprite.name = "QA_RoundedPlate";
			Sprite glyphSprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), new Vector2(.5f, .5f));
			glyphSprite.name = "QA_ContentGlyph";
            sliced.sprite = sprite;
            sliced.type = Image.Type.Sliced;
            var colorBlock = Plate("Button_ColorBlock", Color.white);
            var block = colorBlock.GetComponent<Button>().colors;
            block.normalColor = new Color(0, .196875f, .4f, 1);
            block.highlightedColor = new Color(0, .2657812f, .54f, 1);
            colorBlock.GetComponent<Button>().colors = block;
            Color initialPlain = plain.color;
            Color initialSliced = sliced.color;
            Color initialBlock = colorBlock.color;
            ColorBlock initialColors = colorBlock.GetComponent<Button>().colors;
            var hidden = Plate("Button_Hidden", initialPlain);
            hidden.enabled = false;
            var template = Plate("Button_Template", initialPlain);
            template.gameObject.SetActive(false);
            var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
            iconGo.transform.SetParent(plain.transform, false);
            var contentIcon = iconGo.GetComponent<Image>();
            contentIcon.sprite = sprite;
            var glyphButton = Plate("GlyphButton", Color.white);
            glyphButton.sprite = glyphSprite;
            var transparent = Plate("Button_TransparentHit", Color.white);
            var transparentColors = transparent.GetComponent<Button>().colors;
            transparentColors.normalColor = Color.clear;
            transparent.GetComponent<Button>().colors = transparentColors;
            s_apply.Invoke(null, new object[] { s_fixture });
            if (s_fixture.GetComponent<UiThemeApplicator>() == null) s_fixture.AddComponent<UiThemeApplicator>();
            UiTheme.Set("yellow-purple");
            UiTheme.Set("pink-green");
            UiTheme.Set("blue-orange");
            until = EditorApplication.timeSinceStartup + .6;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check("plain plate uses palette", Near(plain.color, UiTheme.Current.Button), plain.color.ToString());
            Check("sliced plate uses palette", Near(sliced.color, UiTheme.Current.Button), sliced.color.ToString());
            Check("white ColorBlock plate uses palette", Near(colorBlock.color, UiTheme.Current.Button), colorBlock.color.ToString());
            Check("renderer contains interaction multiplier only", Near(plain.canvasRenderer.GetColor(), Color.white),
                plain.canvasRenderer.GetColor().ToString());
            Check("theme keeps plate sprite", sliced.sprite == sprite);
            Check("theme keeps content icon white", Near(contentIcon.color, Color.white) && contentIcon.sprite == sprite);
            Check("icon-only button keeps content white", Near(glyphButton.color, Color.white) && glyphButton.sprite == glyphSprite);
            Check("transparent hit area stays transparent", Near(transparent.color, Color.white)
                && transparent.GetComponent<Button>().colors.normalColor.a == 0f);
            Check("disabled Graphic follows theme", Near(hidden.color, UiTheme.Current.Button));
            Check("inactive template stays native", Near(template.color, initialPlain));
            var clone = Object.Instantiate(template.gameObject, s_fixture.transform).GetComponent<Image>();
            clone.gameObject.SetActive(true);
            s_apply.Invoke(null, new object[] { clone.gameObject });
            Check("new panel uses current palette", Near(clone.color, UiTheme.Current.Button));
            Check("new panel inherits theme observer", clone.GetComponent<UiThemeApplicator>() == null);
            var sliderRoot = new GameObject("QA_Slider", typeof(RectTransform), typeof(Slider));
            sliderRoot.transform.SetParent(s_fixture.transform, false);
            var sliderTrack = ChildPlate(sliderRoot.transform, "Background", new Color(.2f, .2f, .2f, 1));
            var sliderFill = ChildPlate(sliderRoot.transform, "Fill", Color.white);
            var sliderHandle = ChildPlate(sliderRoot.transform, "Handle", Color.white);
            var slider = sliderRoot.GetComponent<Slider>();
            slider.fillRect = sliderFill.rectTransform;
            slider.handleRect = sliderHandle.rectTransform;
            slider.targetGraphic = sliderHandle;
            var scrollRoot = new GameObject("QA_Scrollbar", typeof(RectTransform), typeof(Image), typeof(Scrollbar));
            scrollRoot.transform.SetParent(s_fixture.transform, false);
            var scrollTrack = scrollRoot.GetComponent<Image>();
            var scrollHandle = ChildPlate(scrollRoot.transform, "Handle", Color.white);
            var scrollbar = scrollRoot.GetComponent<Scrollbar>();
            scrollbar.handleRect = scrollHandle.rectTransform;
            scrollbar.targetGraphic = scrollHandle;
            s_apply.Invoke(null, new object[] { s_fixture });
            Check("slider track themed", Near(sliderTrack.color, UiTheme.Current.SliderTrack));
            Check("slider fill themed", Near(sliderFill.color, UiTheme.Current.SliderFill));
            Check("slider handle themed", Near(sliderHandle.color, UiTheme.Current.Handle));
            Check("scrollbar track themed", Near(scrollTrack.color, UiTheme.Current.ScrollTrack));
            Check("scrollbar handle themed", Near(scrollHandle.color, UiTheme.Current.ScrollHandle));
            var plainButton = plain.GetComponent<Button>();
            var pointer = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current);
            plainButton.OnPointerEnter(pointer);
            until = EditorApplication.timeSinceStartup + .2;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check("hover is relative multiplier", Near(plain.canvasRenderer.GetColor(), plainButton.colors.highlightedColor));
            Check("hover preserves palette base", Near(plain.color, UiTheme.Current.Button));
            plainButton.OnPointerExit(pointer);
            plainButton.interactable = false;
            UiTheme.Set("yellow-blue");
            until = EditorApplication.timeSinceStartup + .4;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check("disabled state survives theme switch", Near(plain.canvasRenderer.GetColor(), plainButton.colors.disabledColor));
            Check("disabled button keeps palette base", Near(plain.color, UiTheme.Current.Button));
            plainButton.interactable = true;
            Time.timeScale = 0f;
            UiTheme.Set("orange-purple");
            until = EditorApplication.timeSinceStartup + .4;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check("palette transition works while paused", Near(plain.color, UiTheme.Current.Button));
            Time.timeScale = s_timeScale;
            UiTheme.Set("red-blue");
            sliced.gameObject.SetActive(false);
            until = EditorApplication.timeSinceStartup + .4;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            sliced.gameObject.SetActive(true);
            yield return null;
            yield return null;
            Check("hidden panel does not restart stale tween", Near(sliced.color, UiTheme.Current.Button));

            foreach (var palette in UiTheme.Palettes)
            {
                UiTheme.Set(palette.Id);
                until = EditorApplication.timeSinceStartup + .35;
                while (EditorApplication.timeSinceStartup < until) yield return null;
                CheckIcons(toolbar, icons, enabled, palette.Id);
                if (strip != null)
                    Check(palette.Id + " layer strip color", Near(strip.GetComponent<Image>().color, palette.Panel));
                var cardType = typeof(UiTheme).Assembly.GetType("Bpsim.UI.UiThemeToggleCard");
                var cards = Object.FindObjectsOfType<Toggle>(true).Where(t => t.GetComponent(cardType) != null).ToArray();
                Check(palette.Id + " card check is non-vacuous", cards.Length >= 50, "count=" + cards.Length);
                foreach (var card in cards)
                    Check(palette.Id + " card " + card.name, Near(card.targetGraphic.color,
                        card.isOn ? palette.Selection : palette.Entry));
            }
            UiTheme.Set(UiTheme.DefaultId);
            until = EditorApplication.timeSinceStartup + .4;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check("default palette registered first", UiTheme.Palettes[0].Id == UiTheme.DefaultId);
            Check("default restores plain birth color", Near(plain.color, initialPlain));
            Check("default restores sliced birth color", Near(sliced.color, initialSliced));
            Check("default restores white birth color", Near(colorBlock.color, initialBlock));
            Check("default restores ColorBlock", colorBlock.GetComponent<Button>().colors.Equals(initialColors));
            Check("new panel restores native template color", Near(clone.color, initialPlain));
			Object.DestroyImmediate(s_fixture);
			s_fixture = null;

            var settingsButton = Object.FindObjectsOfType<Button>(true).FirstOrDefault(b => b.name == "SettingsButton");
            if (settingsButton != null)
            {
                var existingPanels = Object.FindObjectsOfType<MonoBehaviour>(true)
                    .Where(c => c.GetType().Name == "UserSettingsPanel").Select(c => c.GetInstanceID()).ToArray();
                settingsButton.onClick.Invoke();
                until = EditorApplication.timeSinceStartup + .4;
                while (EditorApplication.timeSinceStartup < until) yield return null;
                s_settings = Object.FindObjectsOfType<MonoBehaviour>(true)
                    .FirstOrDefault(c => c.GetType().Name == "UserSettingsPanel" && !existingPanels.Contains(c.GetInstanceID()))?.gameObject;
                var reset = s_settings != null ? s_settings.GetComponentsInChildren<Button>(true)
                    .FirstOrDefault(b => b.name == "ResetPaletteButton") : null;
                Check("palette-only reset button exists", reset != null);
                var picker = s_settings != null ? s_settings.GetComponentsInChildren<GridLayoutGroup>(true)
                    .FirstOrDefault(g => g.name == "PalettePicker") : null;
                Check("palette picker is a two-row grid", picker != null && picker.constraintCount == 5);
                if (picker != null)
                {
                    Check("default swatch is visible", picker.transform.Find("Swatch_default") != null);
                    Check("all swatches are present", picker.GetComponentsInChildren<Button>(true).Length == UiTheme.Palettes.Count);
                }
                UiTheme.Set("red-green");
                string backgroundPath = UserSettings.Instance.CustomBackground.Path;
                if (reset != null) reset.onClick.Invoke();
                Check("palette-only reset selects default", UiTheme.Current.Id == UiTheme.DefaultId);
                Check("palette reset preserves custom background", UserSettings.Instance.CustomBackground.Path == backgroundPath);
                if (s_settings != null)
                {
                    var scroll = s_settings.GetComponentInChildren<ScrollRect>(true);
                    if (scroll != null) scroll.verticalNormalizedPosition = 0f;
                    until = EditorApplication.timeSinceStartup + .4;
                    while (EditorApplication.timeSinceStartup < until) yield return null;
                    string shot = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/UiThemeQA/settings-palette.png"));
                    EditorWindow.GetWindow(typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView")).Focus();
                    ScreenCapture.CaptureScreenshot(shot, 2);
                    until = EditorApplication.timeSinceStartup + .2;
                    while (EditorApplication.timeSinceStartup < until) yield return null;
                }
            }
            else Check("live settings opener exists", false);
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(glyphSprite);
            Object.DestroyImmediate(texture);
        }

        private static Image Plate(string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(s_fixture.transform, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            go.GetComponent<Button>().targetGraphic = image;
            return image;
        }

        // A separately invoked contract: creates only a temporary scene and restores the prior scene.
        private static IEnumerator ExerciseSceneTabs()
        {
            var manager = PartManager.Instance;
            s_previousScene = manager.ActiveScene;
            UiTheme.Set("pink-green");
            Action<PartScene> capture = scene => s_tabTestScene = scene;
            manager.SceneLoaded += capture;
            try { manager.LoadEmptyScene("UiThemeQA_TemporaryTab"); }
            finally { manager.SceneLoaded -= capture; }
            yield return null;
            Check("temporary scene exists", s_tabTestScene != null);
            var tab = Object.FindObjectsOfType<Toggle>(true)
                .FirstOrDefault(t => t.name == "SceneTab_" + s_tabTestScene.SceneID);
            Check("new scene tab exists", tab != null);
            Check("new scene tab immediately uses palette", tab != null && Near(tab.targetGraphic.color, UiTheme.Current.Selection));
            Check("new scene tab is selected", tab != null && tab.isOn);
            manager.SelectScene(s_previousScene.SceneID);
            Check("external scene selection deselects previous tab", tab != null && !tab.isOn);
            Check("external scene selection refreshes tab color", tab != null && Near(tab.targetGraphic.color, UiTheme.Current.Entry));
            manager.UnloadScene(s_tabTestScene.SceneID);
            s_tabTestScene = null;
            double until = EditorApplication.timeSinceStartup + .25;
            while (EditorApplication.timeSinceStartup < until) yield return null;
            Check("unloaded tab is removed", tab == null);
            Check("original scene restored", manager.ActiveScene == s_previousScene);
        }

        private static Image ChildPlate(Transform parent, string name, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        public static string Capture()
        {
            if (!Application.isPlaying) return "requires Play Mode";
            var type = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");
            EditorWindow.GetWindow(type).Focus();
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../Temp/UiThemeQA/build-polish.png"));
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            ScreenCapture.CaptureScreenshot(path, 2);
            return path;
        }

        private static void CheckIcons(Dictionary<string, Image> live, Dictionary<string, Sprite> sprites,
            Dictionary<string, bool> enabled, string phase)
        {
            foreach (var pair in live)
                Check(phase + " icon " + pair.Key, pair.Value != null && pair.Value.sprite == sprites[pair.Key]
                    && pair.Value.enabled == enabled[pair.Key]);
        }

        private static bool Near(Color a, Color b) => Mathf.Abs(a.r - b.r) < .003f
            && Mathf.Abs(a.g - b.g) < .003f && Mathf.Abs(a.b - b.b) < .003f && Mathf.Abs(a.a - b.a) < .003f;

        private static void Check(string name, bool pass, string detail = "")
        {
            if (pass) s_result.passed++; else s_result.failed++;
            s_result.checks.Add((pass ? "PASS " : "FAIL ") + name + (detail.Length > 0 ? " : " + detail : ""));
        }
    }
}
