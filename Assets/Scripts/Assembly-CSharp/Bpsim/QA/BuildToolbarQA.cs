#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Bpsim.Parts;
using Bpsim.UI;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.QA
{
	/// <summary>
	/// 顶部工具栏的 Play 模式验收探针。和 <see cref="T:Bpsim.QA.BpsimPlayQA" /> 一样由 DshBridge 驱动：
	///
	///   invoke_static type=Bpsim.QA.BuildToolbarQA method=Run    args=[]
	///   invoke_static type=Bpsim.QA.BuildToolbarQA method=Status args=[]
	///
	/// 它检查的是「工具栏真的接进了 prefab 场景」这件事：按钮是否克隆出来并排好、工具切换是否让
	/// 左侧栏进入中性、网格开关是否落到 <see cref="P:Bpsim.Parts.PartScene.GridEnabled" />、
	/// 以及 <see cref="T:Bpsim.Parts.PartSceneUnmanaged" /> 上新增的移动/旋转/整车平移/撤销是否真的改了网格与部件表。
	/// 报告同时写到 &lt;project&gt;/Logs/bpsim-toolbarqa.log。
	/// </summary>
	public sealed class BuildToolbarQA : MonoBehaviour
	{
		private static BuildToolbarQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static int s_passed;

		private static int s_failed;

		public static string Run()
		{
			BuildToolbarQA runner = Ensure();
			s_report.Length = 0;
			s_passed = 0;
			s_failed = 0;
			s_status = "running";
			runner.StartCoroutine(runner.RunChecks());
			return "started";
		}

		public static string Status()
		{
			return s_status + " | pass=" + s_passed + " fail=" + s_failed + "\n" + s_report;
		}

		/// <summary>
		/// 把当前 Game 视图（含 Screen Space Overlay 的 uGUI，也就是 HUD 播放键和镜头滑条）
		/// 截一张 PNG。overlay canvas 不进相机的 RenderTexture，所以只能走
		/// <see cref="M:UnityEngine.ScreenCapture.CaptureScreenshot(System.String)" />：
		///
		///   invoke_static type=Bpsim.QA.BuildToolbarQA method=Capture args=["Logs/hud.png"]
		///
		/// 相对路径按项目根解析（和报告日志同一套约定）。
		/// </summary>
		public static string Capture(string path)
		{
			BuildToolbarQA runner = Ensure();
			string full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", path ?? "Logs/hud.png"));
			string dir = System.IO.Path.GetDirectoryName(full);
			if (!string.IsNullOrEmpty(dir))
			{
				System.IO.Directory.CreateDirectory(dir);
			}
			runner.StartCoroutine(CaptureRoutine(full));
			return full;
		}

		private static IEnumerator CaptureRoutine(string path)
		{
			// CaptureScreenshot 在本帧末读取后备缓冲，必须等一帧界面才画完。
			yield return new WaitForEndOfFrame();
			ScreenCapture.CaptureScreenshot(path);
		}

		/// <summary>
		/// 手动目视验收用：等价于用鼠标点一下 HUD 左上角那颗播放键。
		/// 配合 <see cref="M:Bpsim.QA.BuildToolbarQA.Capture(System.String)" /> 就能截到
		/// 「模拟中」的红色停止键与贴左缘的滑条：
		///
		///   invoke_static type=Bpsim.QA.BuildToolbarQA method=Simulate args=[true]
		///
		/// 注意它走的是 <see cref="P:UnityEngine.UI.Button.onClick" />，也就是真实的点击路径，
		/// 不是直接调 <see cref="T:Bpsim.Parts.PartManager" /> 的开关。
		/// </summary>
		public static string Simulate(bool on)
		{
			UserInterface ui = UserInterface.Instance;
			if (ui == null || ui.BuildHud == null || ui.BuildHud.PlayButton == null)
			{
				return "hud not ready";
			}
			if (on && PartManager.Instance != null && PartManager.Instance.IsSimulating)
			{
				return "already simulating";
			}
			if (!on && (PartManager.Instance == null || !PartManager.Instance.IsSimulating))
			{
				return "not simulating";
			}
			ui.BuildHud.PlayButton.onClick.Invoke();
			return on ? "simulate" : "stop";
		}

		/// <summary>
		/// 手动目视验收用：等价于点一下左侧栏「框选」右边那颗放大镜。
		///
		///   invoke_static type=Bpsim.QA.BuildToolbarQA method=Magnify args=[true]
		///
		/// 返回 saved / overview / bounds 这些中间量，配合 <see cref="M:Bpsim.QA.BuildToolbarQA.Capture(System.String)" />
		/// 就能看出到底拉远了多少、包围盒是从哪儿取的。走的是
		/// <see cref="P:UnityEngine.UI.Toggle.isOn" />，也就是真实的点击路径。
		/// </summary>
		public static string Magnify(bool on)
		{
			UserInterface ui = UserInterface.Instance;
			if (ui == null || ui.Magnifier == null)
			{
				return "magnifier not ready";
			}
			ui.Magnifier.SetPreview(on);
			SceneCamera camera = SceneCamera.Instance;
			string size = (camera == null) ? "<null>" : camera.TargetSize.ToString("0.###");
			return (on ? "preview" : "restore") + " target=" + size + " | " + ui.Magnifier.Diagnostics;
		}

		private static BuildToolbarQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[BuildToolbarQA]");
			gameObject.hideFlags = HideFlags.DontSave;
			Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<BuildToolbarQA>();
			return s_instance;
		}

		private static void Check(string name, bool ok, string detail = "")
		{
			if (ok)
			{
				s_passed++;
			}
			else
			{
				s_failed++;
			}
			s_report.Append(ok ? "PASS " : "FAIL ").Append(name);
			if (!string.IsNullOrEmpty(detail))
			{
				s_report.Append(" | ").Append(detail);
			}
			s_report.Append('\n');
		}

		private IEnumerator RunChecks()
		{
			for (int i = 0; i < 600 && (UserInterface.Instance == null || PartManager.Instance == null); i++)
			{
				yield return null;
			}
			UserInterface ui = UserInterface.Instance;
			PartManager manager = PartManager.Instance;
			if (ui == null || manager == null)
			{
				Check("singletons", false, "UserInterface 或 PartManager 未就绪");
				Finish();
				yield break;
			}
			Check("singletons", true);

			BuildToolController controller = ui.BuildTool;
			Check("controller", controller != null, controller == null ? "UserInterface.BuildTool 为空" : "");
			if (controller == null)
			{
				Finish();
				yield break;
			}
			Check("build toolbar mounted", ui.ActionBar.GameObject.GetComponent<BuildToolbar>() != null);

			Transform actionBar = ui.ActionBar.GameObject.transform;
			CheckIconButton(actionBar, "BuildMoveButton", "Icon_Move");
			CheckIconButton(actionBar, "BuildMoveAllButton", "Icon_MoveAll");
			CheckIconButton(actionBar, "BuildTransformPartButton", "Icon_FreeTransformPart");
			CheckIconButton(actionBar, "BuildTransformMachineButton", "Icon_TransformMachine");
			CheckIconButton(actionBar, "BuildGridButton", "Icon_Grid");
			CheckIconButton(actionBar, "BuildUndoButton", "Icon_Undo");

			// 合并掉的按钮不能再出现：它们和留下的按钮是同一套手势的两个入口。
			// 「部件调整」由双击部件覆盖，「旋转部件/整车旋转」并进平移按钮的单击，
			// 「自由变换」并进部件变换的缩放手柄。
			string[] removed =
			{
				"BuildAdjustButton", "BuildRotateButton", "BuildRotateAllButton",
				"BuildFreeTransformPartButton", "BuildGridlessButton"
			};
			string leftovers = string.Empty;
			foreach (string name in removed)
			{
				if (actionBar.Find(name) != null)
				{
					leftovers += (name + " ");
				}
			}
			Check("merged buttons are gone", leftovers.Length == 0, (leftovers.Length == 0) ? ("checked=" + removed.Length) : ("仍在：" + leftovers));

			// 原有的文件操作按钮也换成图标，且都还是 64x64 的方形。
			CheckIconButton(actionBar, "LoadButton", "Icon_Load");
			CheckIconButton(actionBar, "SaveButton", "Icon_Save");
			CheckIconButton(actionBar, "ScreenshotButton", "Icon_Screenshot");
			CheckIconButton(actionBar, "SettingsButton", "Icon_Settings");

			// 「模拟」按钮已经搬去 HUD 画布当 Besiege 式的大播放键：ActionBar 上不能再留一份，
			// 否则模拟中它会跟着主画布一起被关掉，「运行时也能显示」就不成立了。
			RectTransform load = actionBar.Find("LoadButton") as RectTransform;
			RectTransform settings = actionBar.Find("SettingsButton") as RectTransform;
			Check("play button left the action bar", actionBar.Find("SimulationButton") == null);
			Check("file buttons kept", load != null && settings != null);
			if (load != null && settings != null)
			{
				// 文件按钮顺序是 加载/保存/截图/设置，设置在第 4 位（3 个步距）。
				float step = BuildToolbar.LastStep;
				Check("file buttons relay out", Mathf.Approximately(load.anchoredPosition.x, 428f) && Mathf.Abs(settings.anchoredPosition.x - (428f + step * 3f)) < 0.01f, "load=" + load.anchoredPosition.x + " settings=" + settings.anchoredPosition.x + " step=" + step);
			}
			CheckNoOverlap(actionBar);
			// 工具栏右端要给右侧的 FPS 文本留位置（FPS 锚在右边，宽 160）。
			RectTransform undo = actionBar.Find("BuildUndoButton") as RectTransform;
			RectTransform fpsRect = actionBar.Find("Text") as RectTransform;
			if (undo != null && fpsRect != null)
			{
				float barWidth = ((RectTransform)actionBar).rect.width;
				float toolbarRight = undo.anchoredPosition.x + undo.sizeDelta.x * 0.5f;
				float fpsLeft = barWidth + fpsRect.anchoredPosition.x - fpsRect.sizeDelta.x * 0.5f;
				Check("toolbar clears fps", toolbarRight < fpsLeft - 16f, "toolbarRight=" + toolbarRight + " fpsLeft=" + fpsLeft);
			}
			else
			{
				Check("toolbar clears fps", false, "缺少 BuildUndoButton 或 Text");
			}
			RectTransform fps = actionBar.Find("Text") as RectTransform;
			Check("fps text kept", fps != null && Mathf.Approximately(fps.anchoredPosition.x, -48f), (fps == null) ? "缺少 Text" : ("x=" + fps.anchoredPosition.x));
			// 提示条挂在 Panel 上并置为最后一个子物体（否则会被场景标签条盖住），
			// 所以这里从 actionBar 的父物体找，同时校验它的绘制顺序确实排在标签条之后。
			Transform panel = actionBar.parent;
			Transform hint = ((panel != null) ? panel.Find("Hint") : null);
			Check("hover hint exists", hint != null && hint.Find("Text") != null && hint.Find("Text").GetComponent<Text>().font != null);
			Transform tabView = ((panel != null) ? panel.Find("SceneTabView") : null);
			Check("hint draws above scene tabs", hint != null && tabView != null && hint.GetSiblingIndex() > tabView.GetSiblingIndex(), "hintIndex=" + ((hint == null) ? -1 : hint.GetSiblingIndex()) + " tabIndex=" + ((tabView == null) ? -1 : tabView.GetSiblingIndex()));

			// 悬停（桌面）/ 长按（安卓）都要能报出功能名，所以每个按钮都得有一条提示文字。
			BuildToolbar toolbar = ui.ActionBar.GameObject.GetComponent<BuildToolbar>();
			if (toolbar != null)
			{
				string[] names =
				{
					"LoadButton", "SaveButton", "ScreenshotButton", "SettingsButton",
					"BuildMoveButton", "BuildMoveAllButton", "BuildTransformPartButton",
					"BuildTransformMachineButton", "BuildGridButton", "BuildUndoButton"
				};
				string missing = string.Empty;
				foreach (string name in names)
				{
					if (string.IsNullOrEmpty(toolbar.HintOf(name)))
					{
						missing += (name + " ");
					}
				}
				Check("every button has a hint", missing.Length == 0, (missing.Length == 0) ? ("hints=" + names.Length) : ("缺少提示：" + missing));
				Check("settings hint is 设置", toolbar.HintOf("SettingsButton") == "设置", "hint=" + toolbar.HintOf("SettingsButton"));

				// 悬停命中：把每个按钮的屏幕中心喂给同一套命中逻辑，验证「屏幕坐标 → 按钮」没算错。
				string hitMissing = string.Empty;
				foreach (string name in names)
				{
					RectTransform button = actionBar.Find(name) as RectTransform;
					if (button == null || !button.gameObject.activeInHierarchy)
					{
						continue;
					}
					Vector2 screenPoint = RectTransformUtility.WorldToScreenPoint(null, button.TransformPoint(button.rect.center));
					string hit = toolbar.HintAtScreenPoint(screenPoint);
					if (hit != toolbar.HintOf(name))
					{
						hitMissing += (name + "→" + (hit ?? "null") + " ");
					}
				}
				Check("hover hit test maps every button", hitMissing.Length == 0, (hitMissing.Length == 0) ? ("hit=" + names.Length) : hitMissing);
				// 工具栏按钮之外（画布中央偏下）不该命中任何按钮，否则提示条会在场景里乱弹。
				Vector2 outside = new Vector2(Screen.width * 0.5f, Screen.height * 0.25f);
				Check("hover misses empty area", toolbar.HintAtScreenPoint(outside) == null, "hit=" + (toolbar.HintAtScreenPoint(outside) ?? "null"));
			}
			else
			{
				Check("every button has a hint", false, "缺少 BuildToolbar");
			}

			// ------------------------------------------------------------ Besiege 式 HUD
			// 播放键 + 镜头滑条住在自己的 Canvas 上（sortingOrder 5）：主画布在模拟中被关掉，
			// 它们必须还在屏幕上，这就是「运行时也能显示在左上角」的实现方式。
			BuildHud hud = ui.BuildHud;
			Check("build hud mounted", hud != null, (hud == null) ? "UserInterface.BuildHud 为空" : "");
			if (hud != null)
			{
				Check("hud canvas survives simulation", hud.Layer != null && !hud.Layer.IsChildOf(ui.Canvas.transform), "layer=" + ((hud.Layer == null) ? "<null>" : hud.Layer.name));
				Check("hud sorts under modal panels", hud.Canvas != null && hud.Canvas.sortingOrder == BuildHud.HudSortingOrder && hud.Canvas.sortingOrder < ui.SubCanvas.sortingOrder, "hud=" + ((hud.Canvas == null) ? -1 : hud.Canvas.sortingOrder) + " sub=" + ui.SubCanvas.sortingOrder);
				CheckPlayButton(hud);
				// 播放键 + 右边的滑条占掉了工具栏前面一大段，文件按钮组整体右移，两者不能压在一起。
				RectTransform playRect = hud.PlayRect;
				RectTransform sliderRect = hud.SliderRect;
				RectTransform loadRect = actionBar.Find("LoadButton") as RectTransform;
				if (playRect != null && sliderRect != null && loadRect != null)
				{
					float hudRight = sliderRect.anchoredPosition.x + sliderRect.sizeDelta.x;
					float loadLeft = loadRect.anchoredPosition.x - loadRect.sizeDelta.x * 0.5f;
					Check("hud clears file buttons", hudRight < loadLeft, "hudRight=" + hudRight + " loadLeft=" + loadLeft);
				}
				CheckZoomSlider(hud);
				CheckHudHints(hud);
				CheckRuntimeLayout(hud);
				CheckTimeScale(hud);
			}

			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			if (scene == null)
			{
				manager.LoadEmptyScene("ToolbarQA");
				yield return null;
				scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			}
			Check("active scene", scene != null);
			if (scene == null)
			{
				Finish();
				yield break;
			}
			yield return null;
			// 有场景了，播放键要从「不可用」的灰色变回绿色（UpdatePlayAvailability 每帧同步）。
			Image hudPlate = (hud == null || hud.PlayButton == null) ? null : hud.PlayButton.GetComponent<Image>();
			Check("play button green with scene", hudPlate != null && hudPlate.color.g > hudPlate.color.r + 0.2f, "color=" + ((hudPlate == null) ? "<null>" : hudPlate.color.ToString()));

			// 工具切换：激活后左侧栏进入中性（不会和放置笔刷抢指针），再点一次恢复 View。
			Check("grid defaults on", controller.GridEnabled && !controller.GridlessEnabled, "GridEnabled=" + controller.GridEnabled + " gridless=" + controller.GridlessEnabled);
			controller.ToggleGrid();
			Check("grid toggles off", !scene.GridEnabled, "scene.GridEnabled=" + scene.GridEnabled);
			controller.ToggleGrid();
			Check("grid toggles on", scene.GridEnabled, "scene.GridEnabled=" + scene.GridEnabled);

			// 网格按钮现在是三态（显示+吸附 → 隐藏 → 自由放置 → 显示+吸附），
			// 以前「网格」「无网格」两枚按钮能凑出「网格：开 + 无网格：开 + 网格不可见」
			// 这种自相矛盾的状态，这里逐态核对「按钮显示的态」与「场景真实可见性」一致。
			BuildToolbar gridToolbar = ui.ActionBar.GameObject.GetComponent<BuildToolbar>();
			FieldInfo gridField = typeof(PartScene).GetField("m_grid", BindingFlags.Instance | BindingFlags.NonPublic);
			FieldInfo largeGridField = typeof(PartScene).GetField("m_largeGrid", BindingFlags.Instance | BindingFlags.NonPublic);
			GameObject gridObject = gridField?.GetValue(scene) as GameObject;
			GameObject largeGridObject = largeGridField?.GetValue(scene) as GameObject;
			Check("grid mode starts snap", gridToolbar != null && gridToolbar.CurrentGridMode() == BuildToolbar.GridModeState.Snap, "mode=" + ((gridToolbar == null) ? "<null>" : gridToolbar.CurrentGridMode().ToString()));
			if (gridToolbar != null)
			{
				gridToolbar.CycleGridModeForTest();
				yield return null;
				yield return null;
				Check("grid mode hidden keeps snapping", gridToolbar.CurrentGridMode() == BuildToolbar.GridModeState.Hidden && !controller.GridlessEnabled && gridObject != null && !gridObject.activeSelf, "mode=" + gridToolbar.CurrentGridMode() + " gridless=" + controller.GridlessEnabled + " gridVisible=" + ((gridObject == null) ? "?" : gridObject.activeSelf.ToString()));
				gridToolbar.CycleGridModeForTest();
				yield return null;
				yield return null;
				Check("grid mode free places off-centre", gridToolbar.CurrentGridMode() == BuildToolbar.GridModeState.Free && controller.GridlessEnabled && gridObject != null && !gridObject.activeSelf, "mode=" + gridToolbar.CurrentGridMode() + " gridless=" + controller.GridlessEnabled);
				gridToolbar.CycleGridModeForTest();
				yield return null;
				yield return null;
				Check("grid mode cycles back to snap", gridToolbar.CurrentGridMode() == BuildToolbar.GridModeState.Snap && controller.GridEnabled && !controller.GridlessEnabled && gridObject != null && gridObject.activeSelf, "mode=" + gridToolbar.CurrentGridMode() + " gridEnabled=" + controller.GridEnabled + " gridless=" + controller.GridlessEnabled);
			}
			Check("grid hint tracks mode", gridToolbar != null && !string.IsNullOrEmpty(gridToolbar.HintOf("BuildGridButton")) && gridToolbar.HintOf("BuildGridButton").Contains("吸附"), "hint=" + ((gridToolbar == null) ? "<null>" : gridToolbar.HintOf("BuildGridButton")));
			Check("grid state restored", controller.GridEnabled && !controller.GridlessEnabled, "grid=" + controller.GridEnabled + " gridless=" + controller.GridlessEnabled);

			controller.SetTool(BuildTool.Move);
			Check("tool activates", controller.CurrentTool == BuildTool.Move, "tool=" + controller.CurrentTool);
			Check("tool neutralises part operator", ui.PartOperator.Mode == PartOperationMode.None, "mode=" + ui.PartOperator.Mode);
			controller.SetTool(BuildTool.Move);
			Check("tool toggles off", controller.CurrentTool == BuildTool.None, "tool=" + controller.CurrentTool);
			Check("operator back to view", ui.PartOperator.Mode == PartOperationMode.View, "mode=" + ui.PartOperator.Mode);

			// 合并掉的旧工具值不再有按钮，但外部脚本 / 旧存档逻辑仍可能传进来：
			// 必须翻译成合并后的工具（旋转→平移、自由变换→部件变换、部件调整→无），
			// 而且「再传一次同一个旧值」要能取消，说明比对发生在归一化之后。
			controller.SetTool(BuildTool.Rotate);
			Check("legacy rotate maps to move", controller.CurrentTool == BuildTool.Move, "tool=" + controller.CurrentTool);
			controller.SetTool(BuildTool.Rotate);
			Check("legacy rotate toggles off", controller.CurrentTool == BuildTool.None, "tool=" + controller.CurrentTool);
			controller.SetTool(BuildTool.FreeTransformPart);
			Check("legacy free transform maps to transform part", controller.CurrentTool == BuildTool.TransformPart, "tool=" + controller.CurrentTool);
			controller.SetTool(BuildTool.RotateAll);
			Check("legacy rotate all maps to move all", controller.CurrentTool == BuildTool.MoveAll, "tool=" + controller.CurrentTool);
			controller.SetTool(BuildTool.Adjust);
			Check("legacy adjust has no tool", controller.CurrentTool == BuildTool.None, "tool=" + controller.CurrentTool);

			// 放大镜（概览镜头）按钮：位于「框选」右边的第 5 颗，独立开关，不参与四选一的放置模式。
			CheckMagnifier(ui);

			// 下面用两个金属框验证 PartSceneUnmanaged 新增的四个入口。
			// 先清场并等到真的空：外面（别的验收探针 / 手点）可能已经在场景里留了部件，
			// 场景不干净的话「整车旋转」的包围盒中心会算偏，后面的坐标断言就全是噪声。
			bool cleared = false;
			for (int i = 0; i < 60 && !cleared; i++)
			{
				scene.Unmanaged.ClearParts();
				yield return null;
				cleared = scene.Unmanaged.Parts.Length == 0;
			}
			Check("scene starts empty", cleared, "parts=" + scene.Unmanaged.Parts.Length);
			Entity first = PlaceFrame(scene, manager, 0, 0);
			Entity second = PlaceFrame(scene, manager, 4, 0);
			yield return null;
			Check("seed parts", first != Entity.Null && second != Entity.Null, "parts=" + scene.Unmanaged.Parts.Length);
			Check("seed is exactly two parts", scene.Unmanaged.Parts.Length == 2, "parts=" + scene.Unmanaged.Parts.Length);

			bool rotated = scene.Unmanaged.TryRotatePart(first, 1);
			PartTransform rotatedTransform = manager.System.EntityManager.GetComponentData<PartTransform>(first);
			Check("rotate part", rotated && rotatedTransform.Rotation == 1, "rotation=" + rotatedTransform.Rotation);
			scene.Unmanaged.TryRotatePart(first, -1);
			yield return null;

			bool moved = scene.Unmanaged.TryMovePartContent(0, 0, 0, 2);
			PartTransform movedTransform = manager.System.EntityManager.GetComponentData<PartTransform>(first);
			bool gridAtNewCell = scene.Unmanaged.FindPartGridAt(0, 2, out var gridAtNewCellInfo) && (gridAtNewCellInfo.HasPart || gridAtNewCellInfo.HasPartContainer);
			bool gridAtOldCell = scene.Unmanaged.FindPartGridAt(0, 0, out var gridAtOldCellInfo) && (gridAtOldCellInfo.HasPart || gridAtOldCellInfo.HasPartContainer);
			Check("move part", moved && movedTransform.X == 0 && movedTransform.Y == 2, "coord=(" + movedTransform.X + "," + movedTransform.Y + ")");
			Check("move part rebuilds grid", gridAtNewCell && !gridAtOldCell, "new=" + gridAtNewCell + " old=" + gridAtOldCell);
			yield return null;

			// 整车旋转：绕包围盒中心转 90°，位置与朝向一起变。
			bool allRotated = scene.Unmanaged.TryRotateAllParts(1);
			PartTransform rotatedAll = manager.System.EntityManager.GetComponentData<PartTransform>(first);
			Check("rotate all parts", allRotated && rotatedAll.Rotation == 1, "rotation=" + rotatedAll.Rotation + " coord=(" + rotatedAll.X + "," + rotatedAll.Y + ")");
			bool rotatedBack = scene.Unmanaged.TryRotateAllParts(-1);
			PartTransform restoredAll = manager.System.EntityManager.GetComponentData<PartTransform>(first);
			Check("rotate all parts back", rotatedBack && restoredAll.Rotation == 0 && restoredAll.X == 0 && restoredAll.Y == 2, "coord=(" + restoredAll.X + "," + restoredAll.Y + ")");
			yield return null;

			bool allMoved = scene.Unmanaged.TryMoveAllParts(5, 0);
			bool firstMoved = scene.Unmanaged.FindPartGridAt(5, 2, out var firstCell) && (firstCell.HasPart || firstCell.HasPartContainer);
			bool secondMoved = scene.Unmanaged.FindPartGridAt(9, 0, out var secondCell) && (secondCell.HasPart || secondCell.HasPartContainer);
			Check("move all parts", allMoved && firstMoved && secondMoved, "first=" + firstMoved + " second=" + secondMoved);
			yield return null;

			// 撤销：整场快照，撤销后部件回到平移前的位置。
			controller.History.Clear();
			controller.Record("qa-move-all");
			scene.Unmanaged.TryMoveAllParts(-5, 0);
			yield return null;
			bool undoOk = controller.History.Undo(scene);
			yield return null;
			bool backAtFive = scene.Unmanaged.FindPartGridAt(5, 2, out var backCell) && (backCell.HasPart || backCell.HasPartContainer);
			Check("undo restores parts", undoOk && backAtFive, "undo=" + undoOk + " restored=" + backAtFive + " parts=" + scene.Unmanaged.Parts.Length);

			controller.Record("qa-depth");
			Check("undo enabled", controller.CanUndo);
			while (controller.CanUndo)
			{
				controller.History.Undo(scene);
			}
			Check("undo drains", !controller.CanUndo && controller.History.Count == 0, "count=" + controller.History.Count);
			yield return null;

			bool sceneCleared = false;
			for (int i = 0; i < 60 && !sceneCleared; i++)
			{
				scene.Unmanaged.ClearParts();
				yield return null;
				sceneCleared = scene.Unmanaged.Parts.Length == 0;
			}
			Check("scene cleared", sceneCleared, "parts=" + scene.Unmanaged.Parts.Length);
			Finish();
		}

		private static Entity PlaceFrame(PartScene scene, PartManager manager, int x, int y)
		{
			PartTypeInfo info = new PartTypeInfo(PartType.MetalFrame, 0);
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null)
			{
				return Entity.Null;
			}
			PartCreationRequest request = new PartCreationRequest(prefab, scene.SceneID, info, x, y, 0, false);
			if (!scene.Unmanaged.PlacePartAt(in request, false))
			{
				return Entity.Null;
			}
			return scene.Unmanaged.FindPartAt(x, y, out var part) ? part : Entity.Null;
		}

		/// <summary>
		/// 图标按钮的验收：底板是九宫格 sprite、文字层被隐藏、子物体 Icon 拿到真实 sprite 且尺寸为 36。
		/// </summary>
		private static void CheckIconButton(Transform actionBar, string name, string expectedIcon)
		{
			Transform child = actionBar.Find(name);
			if (child == null)
			{
				Check(name, false, "按钮不存在");
				return;
			}
			Image plate = child.GetComponent<Image>();
			Button button = child.GetComponent<Button>();
			Transform iconTransform = child.Find("Icon");
			Image icon = (iconTransform == null) ? null : iconTransform.GetComponent<Image>();
			Transform labelTransform = child.Find("Text");
			bool labelHidden = labelTransform == null || !labelTransform.gameObject.activeSelf;
			string spriteName = (icon == null || icon.sprite == null) ? "<null>" : icon.sprite.name;
			// 图标跟着按钮缩（窄窗口下 Reflow 会把按钮压小），所以按当前边长算出期望值。
			float iconSize = Mathf.Min(36f, BuildToolbar.LastButtonSize * 0.5625f);
			bool ok = plate != null && button != null && icon != null && icon.sprite != null
				&& spriteName == expectedIcon
				&& labelHidden
				&& Mathf.Abs(((RectTransform)iconTransform).sizeDelta.x - iconSize) < 0.01f;
			Check(name, ok, "plate=" + (plate != null) + " button=" + (button != null) + " icon=" + spriteName + " textHidden=" + labelHidden);
		}

		/// <summary>
		/// 顶部按钮排得下不挤：按 anchor 换算成以左边缘为原点的 x，逐个比较相邻按钮的区间，
		/// 任何一对重叠都算失败（这正是「工具栏和已有按钮合并」最容易出错的地方）。
		/// </summary>
		private static void CheckNoOverlap(Transform actionBar)
		{
			List<KeyValuePair<float, float>> spans = new List<KeyValuePair<float, float>>(16);
			foreach (RectTransform item in actionBar)
			{
				if (item == null || item.name == "Text")
				{
					continue;
				}
				if (item.GetComponent<Button>() == null || !item.gameObject.activeSelf)
				{
					continue;
				}
				// 这些按钮都是 anchorMin.x == anchorMax.x == 0，anchoredPosition.x 就是左边距 + 半宽。
				float center = item.anchoredPosition.x + (item.anchorMax.x - item.anchorMin.x) * ((RectTransform)item.parent).rect.width;
				float half = item.sizeDelta.x * 0.5f;
				spans.Add(new KeyValuePair<float, float>(center - half, center + half));
			}
			spans.Sort((a, b) => a.Key.CompareTo(b.Key));
			string overlap = null;
			for (int i = 1; i < spans.Count; i++)
			{
				if (spans[i].Key < spans[i - 1].Value - 0.5f)
				{
					overlap = spans[i - 1].Key + ".." + spans[i - 1].Value + " 与 " + spans[i].Key + ".." + spans[i].Value;
					break;
				}
			}
			// 按钮总数下限跟着工具栏收敛：4 个文件按钮 + 6 个工具按钮（平移 / 整车平移 /
			// 部件变换 / 载具变换 / 网格 / 撤销）。少了说明有按钮没克隆出来。
			Check("toolbar buttons do not overlap", overlap == null && spans.Count >= 10, (overlap == null) ? ("buttons=" + spans.Count) : ("重叠: " + overlap));
		}

		/// <summary>播放键：搬到 HUD 画布上、绿色大按钮、播放三角图标、贴在右上角。</summary>
		private static void CheckPlayButton(BuildHud hud)
		{
			Button button = hud.PlayButton;
			RectTransform rect = hud.PlayRect;
			Check("play button on hud", button != null && rect != null && rect.parent == hud.Layer, (rect == null) ? "缺少播放键" : ("parent=" + ((rect.parent == null) ? "<null>" : rect.parent.name)));
			if (button == null || rect == null)
			{
				return;
			}
			Image plate = button.GetComponent<Image>();
			Transform iconTransform = rect.Find("Icon");
			Image icon = (iconTransform == null) ? null : iconTransform.GetComponent<Image>();
			Transform label = rect.Find("Text");
			string spriteName = (icon == null || icon.sprite == null) ? "<null>" : icon.sprite.name;
			Check("play button icon", spriteName == "Icon_Simulate", "icon=" + spriteName);
			Check("play button label hidden", label == null || !label.gameObject.activeSelf);
			// Besiege 的播放键比工具栏按钮大一号（76 > 64）。
			Check("play button is big", rect.sizeDelta.x >= 75.5f, "size=" + rect.sizeDelta.x);
			// 没有场景时播放键是「不可用」的灰色底板，有场景才会变绿（见 UpdatePlayAvailability），
			// 所以这里只验它用的是播放键自己的配色，而不是工具栏那套蓝色底板。
			bool playTint = plate != null && ((plate.color.g > plate.color.r + 0.2f) || (Mathf.Abs(plate.color.r - plate.color.g) < 0.05f && plate.color.r < 0.3f));
			Check("play button uses play tint", playTint, "color=" + ((plate == null) ? "<null>" : plate.color.ToString()));
			// Besiege 的播放键在抬头左上角：锚左上、中心 56,-45、边长 76。
			Check("play button anchored top left", Mathf.Approximately(rect.anchorMin.x, 0f) && Mathf.Approximately(rect.anchorMax.x, 0f) && Mathf.Approximately(rect.anchorMin.y, 1f) && Mathf.Approximately(rect.anchorMax.y, 1f), "anchor=" + rect.anchorMin);
			Check("play button sits at left top", Mathf.Abs(rect.anchoredPosition.x - 56f) < 0.5f && Mathf.Abs(rect.anchoredPosition.y + 45f) < 0.5f, "pos=" + rect.anchoredPosition);
		}

		/// <summary>
		/// 抬头滑条（建造态 = 镜头缩放）：横条、细滑槽 + 白旋钮 + 两端图标，
		/// 而且真的在改 <see cref="T:Bpsim.SceneCamera" /> 的尺寸。
		/// 样式照 Besiege 的时间滑条：横着、没有填充条、没有底板。
		/// </summary>
		private static void CheckZoomSlider(BuildHud hud)
		{
			HudSlider slider = hud.Slider;
			RectTransform rect = hud.SliderRect;
			Check("zoom slider on hud", slider != null && rect != null && rect.parent == hud.Layer, (rect == null) ? "缺少滑条" : ("parent=" + ((rect.parent == null) ? "<null>" : rect.parent.name)));
			if (slider == null || rect == null)
			{
				return;
			}
			// Besiege 的时间滑条是横的（TimeSliderView 按 X 算百分比）。
			Check("zoom slider is horizontal", rect.sizeDelta.x > rect.sizeDelta.y * 3f, "size=" + rect.sizeDelta);
			// 播放键右侧紧邻、同一条水平中线 —— Besiege 的 TimeScale 与 PlayButton 的 y 完全一致。
			RectTransform play = hud.PlayRect;
			float playRight = (play == null) ? 94f : play.anchoredPosition.x + play.sizeDelta.x * 0.5f;
			float gap = rect.anchoredPosition.x - playRight;
			Check("zoom slider sits right of play button", play != null && Mathf.Approximately(rect.anchorMin.x, 0f) && gap >= 0f && gap <= 24f, "sliderX=" + rect.anchoredPosition.x + " playRight=" + playRight + " gap=" + gap);
			Check("zoom slider lines up with play button", play != null && Mathf.Abs(rect.anchoredPosition.y - play.anchoredPosition.y) < 1f, "sliderY=" + rect.anchoredPosition.y + " playY=" + ((play == null) ? 0f : play.anchoredPosition.y));
			// 长度要短：Besiege 的滑槽在 1080p 下只有 166px。
			Check("zoom slider is short", rect.sizeDelta.x <= 320f, "width=" + rect.sizeDelta.x);
			Transform track = rect.Find("Track");
			RectTransform trackRect = (track == null) ? null : track as RectTransform;
			Image trackImage = (trackRect == null) ? null : trackRect.GetComponent<Image>();
			Check("zoom slider parts", trackRect != null && trackRect.Find("Knob") != null && rect.Find("ModeIcon") != null && rect.Find("ZoomOut") != null && rect.Find("Value") != null, "track=" + (trackRect != null));
			// 缩放模式没有百分比文字，那一格必须让掉：否则左端放大镜和滑槽之间会空出一大块，
			// 和右端滑槽到缩小图标的空隙明显不对称（用户反馈过）。
			RectTransform modeIconRect = rect.Find("ModeIcon") as RectTransform;
			RectTransform tailRect = rect.Find("ZoomOut") as RectTransform;
			if (trackRect != null && modeIconRect != null && tailRect != null)
			{
				float leftGap = trackRect.anchoredPosition.x - (modeIconRect.anchoredPosition.x + modeIconRect.sizeDelta.x);
				float rightGap = tailRect.anchoredPosition.x - (trackRect.anchoredPosition.x + trackRect.sizeDelta.x);
				Check("slider row gaps match", leftGap > 0.5f && Mathf.Abs(leftGap - rightGap) < 0.5f, "left=" + leftGap + " right=" + rightGap);
				// 热区贴着内容收尾，空出来的那一段不再吃鼠标。
				float contentRight = tailRect.anchoredPosition.x + tailRect.sizeDelta.x;
				Check("slider row hugs content", Mathf.Abs(rect.sizeDelta.x - contentRight - 2f) < 0.5f, "width=" + rect.sizeDelta.x + " contentRight=" + contentRight);
			}
			// Besiege 的滑条就是一条细线：滑槽得比热区薄得多，也不该有填充块。
			Check("zoom slider track is a thin line", trackImage != null && trackImage.sprite == null && trackRect.sizeDelta.y <= 8f && trackRect.sizeDelta.x > trackRect.sizeDelta.y * 3f && trackRect.Find("Fill") == null, "track=" + ((trackRect == null) ? "<null>" : trackRect.sizeDelta.ToString()));
			Check("zoom slider starts as zoom", slider.Mode == HudSliderMode.Zoom, "mode=" + slider.Mode);
			SceneCamera camera = SceneCamera.Instance;
			if (camera == null)
			{
				Check("zoom slider drives camera", false, "缺少 SceneCamera");
				return;
			}
			float original = camera.TargetSize;
			slider.SetValue(0f);
			float far = camera.TargetSize;
			slider.SetValue(1f);
			float near = camera.TargetSize;
			Check("zoom slider drives camera", Mathf.Abs(far - camera.MaxSize) < 0.01f && Mathf.Abs(near - camera.MinSize) < 0.01f, "far=" + far + " near=" + near + " range=" + camera.MinSize + ".." + camera.MaxSize);
			// 指数映射：中点落在几何平均上，前半段不会「几乎看不出变化」。
			slider.SetValue(0.5f);
			float middle = camera.TargetSize;
			float expected = Mathf.Sqrt(camera.MinSize * camera.MaxSize);
			Check("zoom slider is exponential", Mathf.Abs(middle - expected) < expected * 0.02f, "middle=" + middle + " expected=" + expected);
			// 滚轮缩放之后旋钮要自己跟过去。
			camera.ScaleTo(expected);
			slider.SyncFromCamera();
			Check("zoom slider follows camera", Mathf.Abs(slider.Value - 0.5f) < 0.02f, "value=" + slider.Value);
			camera.ScaleTo(original);
		}

		/// <summary>
		/// 模拟态 = 时间倍速（Besiege 的 TimeScale 滑条）：0..200%，
		/// 写的是 <c>Time.timeScale</c>（ECS 的 world 时间跟着它走，物理步长不变）。
		/// 断言完必须把倍速还原，否则后面的检查全跑在慢放/暂停里。
		/// </summary>
		private static void CheckTimeScale(BuildHud hud)
		{
			HudSlider slider = hud.Slider;
			RectTransform rect = hud.SliderRect;
			if (slider == null || rect == null)
			{
				Check("time slider", false, "缺少滑条");
				return;
			}
			float originalTimeScale = Time.timeScale;
			hud.ApplyLayout(true);
			Check("time slider switches mode", slider.Mode == HudSliderMode.TimeScale, "mode=" + slider.Mode);
			// 顶上换成时钟，底端那个「缩小」图标收起来，百分比那行字亮出来。
			Transform modeIcon = rect.Find("ModeIcon");
			Image modeImage = (modeIcon == null) ? null : modeIcon.GetComponent<Image>();
			Sprite modeSprite = (modeImage == null) ? null : modeImage.sprite;
			Check("time slider shows clock", modeSprite != null && modeSprite.name == "Icon_Clock", "icon=" + ((modeSprite == null) ? "<null>" : modeSprite.name));
			Transform tail = rect.Find("ZoomOut");
			Check("time slider hides zoom out", tail == null || !tail.gameObject.activeSelf, "active=" + ((tail == null) ? "<null>" : tail.gameObject.activeSelf.ToString()));
			slider.SetValue(0.5f);
			Check("time slider rests at 100%", Mathf.Abs(Time.timeScale - 1f) < 0.001f && slider.LabelText == "100%", "timeScale=" + Time.timeScale + " label=" + (slider.LabelText ?? "<null>"));
			slider.SetValue(1f);
			Check("time slider reaches 200%", Mathf.Abs(Time.timeScale - 2f) < 0.001f && slider.LabelText == "200%", "timeScale=" + Time.timeScale + " label=" + (slider.LabelText ?? "<null>"));
			slider.SetValue(0f);
			Check("time slider can pause", Mathf.Abs(Time.timeScale) < 0.001f && slider.LabelText == "0%", "timeScale=" + Time.timeScale + " label=" + (slider.LabelText ?? "<null>"));
			// 回建造界面必须把全局时间还回去，否则整个游戏卡在暂停。
			hud.ApplyLayout(false);
			Time.timeScale = originalTimeScale;
			Check("time slider restores on build", Mathf.Approximately(Time.timeScale, originalTimeScale) && slider.Mode == HudSliderMode.Zoom, "timeScale=" + Time.timeScale + " mode=" + slider.Mode);
		}

		private static void CheckHudHints(BuildHud hud)
		{
			Check("hud hints registered", hud.HintOf("PlayButton") == "开始模拟" && hud.HintOf("HudSlider") == "镜头缩放", "play=" + (hud.HintOf("PlayButton") ?? "null") + " slider=" + (hud.HintOf("HudSlider") ?? "null"));
			RectTransform play = hud.PlayRect;
			RectTransform slider = hud.SliderRect;
			string playHit = (play == null) ? null : hud.HintAtScreenPoint(RectTransformUtility.WorldToScreenPoint(null, play.TransformPoint(play.rect.center)));
			string sliderHit = (slider == null) ? null : hud.HintAtScreenPoint(RectTransformUtility.WorldToScreenPoint(null, slider.TransformPoint(slider.rect.center)));
			Check("hud hover hit test", playHit == "开始模拟" && sliderHit == "镜头缩放", "play→" + (playHit ?? "null") + " slider→" + (sliderHit ?? "null"));
			Vector2 outside = new Vector2(Screen.width * 0.5f, Screen.height * 0.25f);
			Check("hud hover misses empty area", hud.HintAtScreenPoint(outside) == null, "hit=" + (hud.HintAtScreenPoint(outside) ?? "null"));
		}

		/// <summary>
		/// 运行时布局：播放键原地变成红色停止键、滑条原地不动（Besiege 的抬头在模拟里不会挪窝），
		/// 但滑条的用途从镜头缩放切成时间倍速。
		/// 直接调 <see cref="M:Bpsim.UI.BuildHud.ApplyLayout" />（同一帧内断言完再摆回去），
		/// 免得为了验收真的把物理模拟跑起来。
		/// </summary>
		private static void CheckRuntimeLayout(BuildHud hud)
		{
			RectTransform play = hud.PlayRect;
			RectTransform slider = hud.SliderRect;
			if (play == null || slider == null)
			{
				Check("runtime layout", false, "缺少播放键或滑条");
				return;
			}
			Vector2 buildPlay = play.anchoredPosition;
			Vector2 buildSlider = slider.anchoredPosition;
			float buildSliderWidth = slider.sizeDelta.x;
			hud.ApplyLayout(true);
			Image plate = hud.PlayButton.GetComponent<Image>();
			Transform iconTransform = play.Find("Icon");
			Image icon = (iconTransform == null) ? null : iconTransform.GetComponent<Image>();
			string stopIcon = (icon == null || icon.sprite == null) ? "<null>" : icon.sprite.name;
			bool stillLeftTop = Mathf.Approximately(play.anchorMin.x, 0f) && Mathf.Approximately(play.anchorMin.y, 1f) && Mathf.Approximately(play.anchoredPosition.x, buildPlay.x) && Mathf.Approximately(play.anchoredPosition.y, buildPlay.y);
			Check("runtime play button stays left top", stillLeftTop, "anchor=" + play.anchorMin + " pos=" + play.anchoredPosition);
			Check("runtime play button keeps size", Mathf.Abs(play.sizeDelta.x - 76f) < 0.5f, "size=" + play.sizeDelta);
			Check("runtime play button becomes stop", stopIcon == "Icon_Stop", "icon=" + stopIcon);
			Check("runtime play button turns red", plate != null && plate.color.r > plate.color.g + 0.2f, "color=" + ((plate == null) ? "<null>" : plate.color.ToString()));
			// 和播放键一样：模拟里不挪窝，只换用途。
			bool sliderStays = Mathf.Approximately(slider.anchoredPosition.x, buildSlider.x) && Mathf.Approximately(slider.anchoredPosition.y, buildSlider.y);
			Check("runtime slider stays put", sliderStays, "slider=" + slider.anchoredPosition + " build=" + buildSlider);
			// 倍速模式多出「200%」那一段，行会变宽；图标 / 文字 / 滑槽之间的空隙仍然一样。
			RectTransform runtimeModeIcon = slider.Find("ModeIcon") as RectTransform;
			RectTransform runtimeLabel = slider.Find("Value") as RectTransform;
			RectTransform runtimeTrack = slider.Find("Track") as RectTransform;
			if (runtimeModeIcon != null && runtimeLabel != null && runtimeTrack != null)
			{
				float gapLabel = runtimeLabel.anchoredPosition.x - (runtimeModeIcon.anchoredPosition.x + runtimeModeIcon.sizeDelta.x);
				float gapTrack = runtimeTrack.anchoredPosition.x - (runtimeLabel.anchoredPosition.x + runtimeLabel.sizeDelta.x);
				Check("runtime slider row stays symmetric", gapLabel > 0.5f && Mathf.Abs(gapLabel - gapTrack) < 0.5f, "gapLabel=" + gapLabel + " gapTrack=" + gapTrack);
				Check("runtime slider grows for percent", slider.sizeDelta.x > buildSliderWidth + 10f, "width=" + slider.sizeDelta.x + " build=" + buildSliderWidth);
			}
			Check("runtime hint says stop", hud.HintOf("PlayButton") == "停止模拟", "hint=" + (hud.HintOf("PlayButton") ?? "null"));
			Check("runtime hint says time", hud.HintOf("HudSlider") == "时间倍速", "hint=" + (hud.HintOf("HudSlider") ?? "null"));
			hud.ApplyLayout(false);
			Check("build layout restored", Mathf.Approximately(play.anchoredPosition.x, buildPlay.x) && Mathf.Approximately(play.anchoredPosition.y, buildPlay.y) && Mathf.Approximately(slider.anchoredPosition.x, buildSlider.x) && Mathf.Approximately(slider.anchoredPosition.y, buildSlider.y), "play=" + play.anchoredPosition + " slider=" + slider.anchoredPosition);
			Check("build hint says zoom", hud.HintOf("HudSlider") == "镜头缩放", "hint=" + (hud.HintOf("HudSlider") ?? "null"));
		}

		/// <summary>
		/// 放大镜按钮的验收要点：
		/// ① 它真的被克隆出来并排在「框选」右边（HorizontalLayoutGroup 靠 sibling 顺序排，顺序错了位置就错）；
		/// ② 它是<b>独立</b>开关 —— 脱离 ToggleGroup 之后既不会被四个模式按钮关掉，也不会把
		///    <see cref="P:Bpsim.UI.PartOperator.Mode" /> 抢成 None（那会让放置笔刷失效）；
		/// ③ 点开确实把镜头目标尺寸推大、再点回来能恢复原值。
		/// </summary>
		private static void CheckMagnifier(UserInterface ui)
		{
			BuildMagnifier magnifier = ui.Magnifier;
			Check("magnifier mounted", magnifier != null);
			if (magnifier == null)
			{
				return;
			}
			PartOperator partOperator = ui.PartOperator;
			Transform bar = (partOperator == null) ? null : partOperator.transform;
			Transform button = (bar == null) ? null : bar.Find("MagnifyButton");
			Check("magnifier button cloned", button != null, (bar == null) ? "找不到 PartOperator" : "");
			if (button == null)
			{
				return;
			}
			Transform select = bar.Find("SelectButton");
			Check("magnifier sits right of select", select != null && button.GetSiblingIndex() == select.GetSiblingIndex() + 1 && ((RectTransform)button).anchoredPosition.x > ((RectTransform)select).anchoredPosition.x, "index=" + button.GetSiblingIndex() + " x=" + ((RectTransform)button).anchoredPosition.x);
			Toggle toggle = button.GetComponent<Toggle>();
			Check("magnifier is an independent toggle", toggle != null && toggle.group == null, (toggle == null) ? "缺少 Toggle" : ("group=" + ((toggle.group == null) ? "<null>" : toggle.group.name)));
			Check("magnifier drops double click", button.GetComponent<DoubleClickableButton>() == null);
			Transform iconTransform = button.Find("Image");
			RawImage raw = (iconTransform == null) ? null : iconTransform.GetComponent<RawImage>();
			Texture iconTexture = (raw == null) ? null : raw.texture;
			Check("magnifier icon applied", iconTexture != null && iconTexture.name == "Icon_Magnify", "icon=" + ((iconTexture == null) ? "<null>" : iconTexture.name));

			SceneCamera camera = SceneCamera.Instance;
			if (camera == null)
			{
				Check("magnifier zooms out", false, "找不到 SceneCamera");
				return;
			}
			// 存档进来时镜头可能本来就框着整台载具（那时概览尺寸和当前尺寸几乎相等，看不出变化），
			// 所以先固定一个近景起点，让「拉远到装下整车」这件事一定是可观测的。
			float before = camera.TargetSize;
			Vector2 beforePoint = camera.TargetPoint;
			// 先把概览收起来：真机/编辑器里人造的点击可能把按钮留在开启态，那时镜头已经不是
			// 探针以为的尺寸，后面的 near/overview 断言会全是噪声。
			magnifier.SetPreview(false);
			camera.ScaleTo(camera.MinSize);
			float near = camera.TargetSize;
			magnifier.SetPreview(true);
			float overview = camera.TargetSize;
			Check("magnifier zooms out", overview > near * 1.5f && magnifier.IsPreviewing, "near=" + near.ToString("0.###") + " overview=" + overview.ToString("0.###") + " | " + magnifier.Diagnostics);
			PartManager partManager = PartManager.Instance;
			Vector2 overviewPoint = camera.TargetPoint;
			CheckMagnifierInputOwnership(ui, partManager, camera, overviewPoint);
			// 概览要把镜头挪到部件中心，否则只是「拉远但没对准」。
			bool hasParts = partManager != null && partManager.HasActiveScene() && partManager.ActiveScene != null && partManager.ActiveScene.Unmanaged.Parts.Length > 0;
			// 空场景时没有包围盒，概览只拉远、不挪镜头，中心应保持原样。
			// 有部件时镜头要「往左偏半条侧栏宽」：左侧部件栏盖住了画面左边那一条，
			// 只有把载具中心摆到剩下那条可见区的正中，载具才算真的入画。
			float occluded = magnifier.OccludedLeftFraction;
			Check("magnifier detects the sidebar", hasParts ? (occluded > 0.02f && occluded < 0.6f) : true, "left=" + occluded.ToString("0.###"));
			Vector2 center = hasParts ? partManager.ActiveScene.Unmanaged.GetPartsCenter() : beforePoint;
			if (hasParts)
			{
				float aspect = ((camera.Camera != null && camera.Camera.aspect > 0.01f) ? camera.Camera.aspect : 16f / 9f);
				center = new Vector2(center.x - 0.5f * occluded * 2f * overview * aspect, center.y);
			}
			Check("magnifier centers the build", Vector2.Distance(camera.TargetPoint, center) < 0.5f, "parts=" + (hasParts ? "yes" : "no") + " point=" + camera.TargetPoint + " center=" + center + " left=" + occluded.ToString("0.###"));
			// 打开概览不能把放置模式顶掉：ToggleGroup 没断的话 PartOperator 会判成 None。
			Check("magnifier keeps placement mode", partOperator.Mode != PartOperationMode.None, "mode=" + partOperator.Mode);
			magnifier.SetPreview(false);
			Check("magnifier restores zoom", Mathf.Abs(camera.TargetSize - near) < 0.01f && !magnifier.IsPreviewing, "target=" + camera.TargetSize.ToString("0.###") + " near=" + near.ToString("0.###"));
			Check("magnifier restores camera point", Vector2.Distance(camera.TargetPoint, beforePoint) < 0.5f, "point=" + camera.TargetPoint + " before=" + beforePoint);
			// 收拾现场：把镜头还给进入本次检查之前的尺寸，别让后面的检查看到近景。
			camera.ScaleTo(before);
		}

		private static void CheckMagnifierInputOwnership(UserInterface ui, PartManager manager, SceneCamera camera, Vector2 restorePoint)
		{
			if (manager == null || !manager.HasActiveScene() || ui.SceneScreen == null)
			{
				Check("magnifier owns build input", false, "缺少场景或画布");
				return;
			}

			InteractionSettings interaction = UserSettings.Instance.InteractionSettings;
			bool quickBuild = interaction.QuickBuild;
			PartOperationMode mode = ui.PartOperator.Mode;
			SceneScreen screen = ui.SceneScreen;
			FieldInfo pointerField = typeof(SceneScreen).GetField("m_pointer", BindingFlags.Instance | BindingFlags.NonPublic);
			FieldInfo previousPointerField = typeof(SceneScreen).GetField("m_previousPointer", BindingFlags.Instance | BindingFlags.NonPublic);
			MethodInfo cameraUpdate = typeof(SceneCamera).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);
			if (pointerField == null || previousPointerField == null || cameraUpdate == null)
			{
				Check("magnifier owns build input", false, "找不到输入探针成员");
				return;
			}

			try
			{
				interaction.QuickBuild = true;
				ui.PartOperator.SetMode(PartOperationMode.Place);
				Vector2 start = new Vector2(Screen.width * 0.75f, Screen.height * 0.5f);
				Vector2 end = start + new Vector2(48f, 0f);
				previousPointerField.SetValue(screen, new SceneScreen.PointerData(true, start, Vector2.zero, 0f, Time.unscaledTime));
				pointerField.SetValue(screen, new SceneScreen.PointerData(true, end, end - start, 0f, Time.unscaledTime));
				Vector2 beforePan = camera.TargetPoint;
				cameraUpdate.Invoke(camera, null);
				Check("magnifier pans with QuickBuild and Place active", Vector2.Distance(beforePan, camera.TargetPoint) > 0.001f, "before=" + beforePan + " after=" + camera.TargetPoint);

				pointerField.SetValue(screen, new SceneScreen.PointerData(true, end, Vector2.zero, 0f, Time.unscaledTime));
				previousPointerField.SetValue(screen, new SceneScreen.PointerData(true, end, Vector2.zero, 0f, Time.unscaledTime));
				int partCount = manager.ActiveScene.Unmanaged.Parts.Length;
				manager.PartDrawer.Update(manager);
				Check("magnifier tap does not quickbuild a part", manager.ActiveScene.Unmanaged.Parts.Length == partCount, "before=" + partCount + " after=" + manager.ActiveScene.Unmanaged.Parts.Length);
			}
			catch (System.Exception exception)
			{
				Check("magnifier owns build input", false, exception.GetType().Name + ": " + exception.Message);
			}
			finally
			{
				interaction.QuickBuild = quickBuild;
				ui.PartOperator.SetMode(mode);
				pointerField.SetValue(screen, SceneScreen.PointerData.Empty);
				previousPointerField.SetValue(screen, SceneScreen.PointerData.Empty);
				camera.MoveTo(restorePoint);
			}
		}

		private static void Finish()
		{
			s_status = (s_failed == 0) ? "done:PASS" : "done:FAIL";
			s_report.Append("=== ").Append(s_status).Append(" pass=").Append(s_passed).Append(" fail=").Append(s_failed).Append(" ===\n");
			Debug.Log("[BuildToolbarQA] " + s_status + " pass=" + s_passed + " fail=" + s_failed);
			try
			{
				System.IO.File.WriteAllText(System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "..", "Logs", "bpsim-toolbarqa.log")), s_report.ToString());
			}
			catch (System.Exception exception)
			{
				Debug.LogWarning("[BuildToolbarQA] 写日志失败: " + exception.Message);
			}
		}
	}
}
#endif
