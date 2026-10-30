#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Bpsim.Parts;
using Bpsim.UI;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace Bpsim.QA
{
	public sealed class BuildUiInteractionQA : MonoBehaviour
	{
		private static string s_status = "idle";
		private static readonly StringBuilder Report = new StringBuilder();
		private static int s_pass, s_fail;
		private Mouse m_mouse;
		private Mouse m_previousMouse;
		private readonly List<Mouse> m_disabledMice = new List<Mouse>();
		private InputSettings.BackgroundBehavior m_background;
		private PartScene m_testScene;
		private PartScene m_previousScene;
		public static string Status() => s_status + "\n" + Report;
		public static string Run()
		{
			if (!Application.isPlaying) return "requires play mode";
			if (s_status == "running") return s_status;
			Report.Clear(); s_pass = s_fail = 0; s_status = "running";
			new GameObject("BuildUiInteractionQA").AddComponent<BuildUiInteractionQA>().StartCoroutine(RunChecks());
			return s_status;
		}
		private static void Check(string label, bool pass)
		{
			if (pass) s_pass++; else s_fail++;
			Report.AppendLine((pass ? "PASS " : "FAIL ") + label);
		}
		private static Vector2 Center(RectTransform rect) => RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
		private void MouseAt(Vector2 point, bool held)
		{
			m_mouse.MakeCurrent();
			var state = new MouseState { position = point };
			if (held) state = state.WithButton(MouseButton.Left);
			InputSystem.QueueStateEvent(m_mouse, state);
			InputSystem.Update();
			// Drive the same SceneScreen polling entry used each frame; world drawing runs
			// normally on the following frame. No OS input or user device is changed.
			typeof(SceneScreen).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(UserInterface.Instance.SceneScreen, null);
		}
		private static IEnumerator RunChecks()
		{
			var runner = FindObjectOfType<BuildUiInteractionQA>();
			var checks = runner.Checks();
			while (true)
			{
				bool more;
				try { more = checks.MoveNext(); }
				catch (Exception e) { Check("exception: " + e, false); runner.Finish(); yield break; }
				if (!more) yield break;
				yield return checks.Current;
			}
		}
		private IEnumerator Checks()
		{
			var ui = UserInterface.Instance; var manager = PartManager.Instance;
			if (ui == null || manager == null) { Check("UI loaded", false); Finish(); yield break; }
			m_previousScene = manager.ActiveScene;
			Action<PartScene> loaded = scene => m_testScene = scene;
			manager.SceneLoaded += loaded;
			manager.LoadEmptyScene("UI 回归检查");
			manager.SceneLoaded -= loaded;
			manager.SelectScene(m_testScene.SceneID);
			yield return null; yield return null;
			Check("isolated test scene is active", manager.ActiveScene == m_testScene);
			if (manager.ActiveScene != m_testScene) { Finish(); yield break; }
			var em = m_testScene.System.EntityManager;
			ui.BuildTool.SetTool(BuildTool.None);
			m_background = InputSystem.settings.backgroundBehavior;
			InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
			m_previousMouse = Mouse.current; m_mouse = InputSystem.AddDevice<Mouse>();
			foreach (var mouse in InputSystem.devices.OfType<Mouse>().ToArray())
				if (mouse != m_mouse && mouse.enabled) { m_disabledMice.Add(mouse); InputSystem.DisableDevice(mouse); }
			var destination = new Vector2(Screen.width * .55f, Screen.height * .45f);
			var origins = new[] {
				ui.BuildHud.SliderRect,
				ui.PartSelector.GetComponentInChildren<Scrollbar>(true).handleRect,
				ui.PartSelector.transform.Find("LayerStrip") as RectTransform
			};
			for (int mode = 0; mode < 2; mode++)
			{
				m_testScene.GridlessEnabled = mode == 1;
				for (int i = 0; i < origins.Length; i++)
				{
					int count = m_testScene.Unmanaged.Parts.Length;
					var origin = Center(origins[i]); MouseAt(origin, false); yield return null;
					MouseAt(origin, true); yield return null;
					MouseAt(destination, true); yield return null; yield return null;
					Check($"UI drag {i} mode {mode} suppresses canvas press", !ui.SceneScreen.Pointer.IsPressed);
					MouseAt(destination + new Vector2(50, 30), true); yield return null;
					MouseAt(destination, false); yield return null;
					Check($"UI drag {i} mode {mode} creates no parts", m_testScene.Unmanaged.Parts.Length == count);
				}
			}
			MouseAt(destination, true); yield return null;
			Check("fresh canvas press works after UI release: " + ui.SceneScreen.Pointer.HasValue + "/" + Mouse.current?.deviceId + "/" + Mouse.current?.leftButton.isPressed, ui.SceneScreen.Pointer.IsPressed);
			MouseAt(destination, false); yield return null;
			m_testScene.Unmanaged.ClearParts();
			var info = new PartTypeInfo(PartType.MetalFrame, 0);
			var world = SceneCamera.Instance.Camera.ScreenToWorldPoint(destination);
			int x = Mathf.RoundToInt(world.x), y = Mathf.RoundToInt(world.y);
			var request = new PartCreationRequest(manager.EntitySpawner.FindPrefab(info), m_testScene.SceneID, info, x, y, 0, false);
			m_testScene.Unmanaged.SetActiveLayer(1);
			m_testScene.Unmanaged.PlacePartAt(request, false);
			m_testScene.Unmanaged.FindPartAt(x, y, out var part);
			yield return null;
			// Occupy the sidebar first, reproducing the formerly ignored request.
			typeof(PartOperator).GetMethod("OpenPartDrawingPanel", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ui.PartOperator, null);
			yield return null;
			var point = SceneCamera.Instance.Camera.WorldToScreenPoint(new Vector3(x, y, 0));
			var click = new PointerEventData(EventSystem.current) { position = point, button = PointerEventData.InputButton.Left };
			ui.SceneScreen.OnPointerClick(click); ui.SceneScreen.OnPointerClick(click);
			yield return null;
			var panel = ui.PartOperator.PropertyPanel;
			Check("double click opens properties with occupied sidebar", panel != null && panel.gameObject.activeInHierarchy);
			if (panel == null) { Finish(); yield break; }
			ui.BuildTool.SetActiveLayer(0);
			ui.SceneScreen.OnPointerClick(click); ui.SceneScreen.OnPointerClick(click);
			yield return null;
			Check("repeated double click inspects all-layers view", ui.PartOperator.PropertyPanel != null);
			Check("all-layers properties are read-only", !panel.GetComponentInChildren<InputField>().interactable);
			ui.BuildTool.SetActiveLayer(1); yield return null;
			var content = panel.transform.Find("Properties/Scroll View/Viewport/Content");
			InputField Input(string key) => content.GetComponentsInChildren<InputField>(true).First(field => field.transform.parent.name == key);
			var header = panel.transform.Find("Properties/Header");
			Check("taffy themed blue header has original 60-unit height", header.GetComponent<RawImage>().color == new Color(44f / 255, 89f / 255, 149f / 255, 1) && ((RectTransform)header).rect.height == 60);
			Check("taffy core fields use 24-point bold white type", Input("GridX").textComponent.fontSize == 24 && Input("GridX").textComponent.fontStyle == FontStyle.Bold && Input("GridX").textComponent.color == Color.white);
			Check("taffy core fields have 60-unit gray plates", ((RectTransform)Input("GridX").transform).rect.height == 60 && Input("GridX").targetGraphic.color == new Color(.2f, .2f, .2f, 1));
			var compactSlider = Input("ScaleX").transform.parent.Find("Slider").GetComponent<Slider>();
			Check("taffy compact slider row is 22 units with a 12-unit track", ((RectTransform)Input("ScaleX").transform.parent).rect.height == 22 && ((RectTransform)compactSlider.transform).rect.height == 12);
			Check("taffy slider uses blue fill and 14-unit pale handle", compactSlider.fillRect.GetComponent<Image>().color == new Color(47f / 255, 128f / 255, 237f / 255, 1) && compactSlider.handleRect.sizeDelta.x == 14 && ((Image)compactSlider.targetGraphic).sprite == null);
			var scrollBar = panel.GetComponentInChildren<Scrollbar>(true);
			Check("taffy scrollbar uses subtle track and blue-gray handle", scrollBar.GetComponent<Image>().color == new Color(1, 1, 1, .05f) && scrollBar.targetGraphic.color == Color.Lerp(new Color(.16f, .16f, .16f, 1), new Color(47f / 255, 128f / 255, 237f / 255, 1), .35f));
			var scrollView = panel.GetComponentInChildren<ScrollRect>();
			Canvas.ForceUpdateCanvases();
			Check("scrollbar thumb follows viewport/content ratio: " + scrollBar.size + "/" + scrollView.viewport.rect.height + "/" + scrollView.content.rect.height + "/" + ((RectTransform)scrollBar.transform).rect.height + "/" + scrollBar.handleRect.rect.height, Mathf.Abs(scrollBar.size - Mathf.Clamp01(scrollView.viewport.rect.height / scrollView.content.rect.height)) < .02f);
			Check("taffy original white close button is retained", panel.transform.Find("Properties/Actions/Close").GetComponent<Image>().color == Color.white && panel.transform.Find("Properties/Actions/Close").GetComponentInChildren<Text>().color == Color.black);
			Input("OffsetX").onEndEdit.Invoke("1.25");
			Input("OffsetY").onEndEdit.Invoke("-0.75");
			Input("FineAngle").onEndEdit.Invoke("32.5");
			Input("ScaleX").onEndEdit.Invoke("2");
			Input("ScaleY").onEndEdit.Invoke("0.5");
			var free = m_testScene.Unmanaged.GetPartFreeTransform(part);
			Check("property fields write ECS transform", free.Offset.x == 1.25f && free.Offset.y == -.75f && free.RotationDegrees == 32.5f && free.Scale.x == 2 && free.Scale.y == .5f);
			Input("ScaleX").onEndEdit.Invoke("NaN");
			Check("invalid number cannot poison transform", m_testScene.Unmanaged.GetPartFreeTransform(part).Scale.x == 2);
			Input("Layer").onEndEdit.Invoke("2");
			Check("layer field moves occupancy", m_testScene.Unmanaged.GetPartLayer(part) == 2 && m_testScene.Unmanaged.FindPartAt(x, y, 2, out var layerPart) && layerPart == part);
			Input("GridX").onEndEdit.Invoke((x + 2).ToString());
			Check("grid edit works on nonactive layer", em.GetComponentData<PartTransform>(part).X == x + 2 && m_testScene.Unmanaged.ActiveLayer == 1);
			int history = ui.BuildTool.History.Count;
			var slider = Input("OffsetX").transform.parent.Find("Slider").GetComponent<Slider>();
			slider.OnPointerDown(new PointerEventData(EventSystem.current) { position = Center((RectTransform)slider.transform), button = PointerEventData.InputButton.Left });
			slider.value = 3; slider.value = 4; slider.value = 5;
			Check("slider gesture records a single undo", ui.BuildTool.History.Count == history + 1);
			ui.BuildTool.Undo(); yield return null;
			m_testScene.Unmanaged.FindPartAt(x + 2, y, 2, out part);
			Check("undo restores property value", part != Entity.Null && m_testScene.Unmanaged.GetPartFreeTransform(part).Offset.x == 1.25f);
			ui.PartOperator.OpenPartPropertyPanel(m_testScene, part); yield return null;
			panel = ui.PartOperator.PropertyPanel;
			content = panel.transform.Find("Properties/Scroll View/Viewport/Content");
			Input("ScaleX").onEndEdit.Invoke("5"); Input("ScaleY").onEndEdit.Invoke("3");
			Input("FineAngle").onEndEdit.Invoke("0"); Input("GridX").onEndEdit.Invoke("1"); Input("GridY").onEndEdit.Invoke("-9");
			ui.PartOperator.OpenPartPropertyPanel(m_testScene, part);
			panel.UpdateLayout(false); yield return null;
			Check("floating property panel fits canvas", ((RectTransform)panel.transform).rect.height <= ((RectTransform)ui.SubCanvas.transform).rect.height);
			var floatingContent = panel.transform.Find("Properties/Scroll View/Viewport/Content");
			Check("taffy floating panel uses wide two-column layout", ((RectTransform)panel.transform).rect.width >= 1150 && ((RectTransform)floatingContent.Find("Rotation")).anchoredPosition.x > ((RectTransform)floatingContent.Find("Position")).anchoredPosition.x);
			BuildToolbarQA.Capture("Logs/build-ui-properties-floating.png");
			yield return new WaitForSecondsRealtime(.3f);
			panel.UpdateLayout(true); yield return null;
			Check("panel docks back into sidebar", panel.transform.parent == ui.RightSidebar.Content.transform);
			var tabs = ui.Canvas.transform.Find("Panel/SceneTabView");
			Check("scene tabs have no backdrop", tabs != null && tabs.GetComponent<Graphic>() != null && !tabs.GetComponent<Graphic>().enabled);
			BuildToolbarQA.Capture("Logs/build-ui-properties.png");
			yield return new WaitForSecondsRealtime(.3f);
			Finish();
		}
		private void Finish()
		{
			s_status = $"{(s_fail == 0 ? "PASS" : "FAIL")} pass={s_pass} fail={s_fail}";
			Report.AppendLine(s_status); File.WriteAllText(Path.Combine(Application.dataPath, "../Logs/build-ui-interaction-qa.log"), Report.ToString());
			if (m_previousScene != null) PartManager.Instance.SelectScene(m_previousScene.SceneID);
			if (m_testScene != null) PartManager.Instance.UnloadScene(m_testScene.SceneID);
			Debug.Log(s_status); Cleanup(); Destroy(gameObject);
		}
		private void Cleanup()
		{
			if (m_mouse != null) { InputSystem.RemoveDevice(m_mouse); m_mouse = null; InputSystem.settings.backgroundBehavior = m_background; }
			foreach (var mouse in m_disabledMice) if (mouse.added) InputSystem.EnableDevice(mouse);
			m_disabledMice.Clear();
			if (m_previousMouse != null && m_previousMouse.added) m_previousMouse.MakeCurrent();
			m_previousMouse = null;
		}
		private void OnDestroy() => Cleanup();
	}
}
#endif
