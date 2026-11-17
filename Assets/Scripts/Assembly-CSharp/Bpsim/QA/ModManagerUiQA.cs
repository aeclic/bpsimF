#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Bpsim.Modding;
using Bpsim.Mods;
using Bpsim.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.QA
{
	/// <summary>
	/// MOD 管理器面板的 Play 模式验收探针（检查设置面板元素复用与导入流程）。
	/// 只读被验收对象，不改 ModManagerUI / ModUiKit；需要点按钮时用反射调用私有方法，
	/// 需要造数据时把 ModPackage 塞进 ModManager.Packages 后重建列表，跑完移除。
	///
	///   invoke_static type=Bpsim.QA.ModManagerUiQA method=Open      args=[]
	///   invoke_static type=Bpsim.QA.ModManagerUiQA method=Run       args=[]
	///   invoke_static type=Bpsim.QA.ModManagerUiQA method=Status    args=[]
	///   invoke_static type=Bpsim.QA.ModManagerUiQA method=ImportMenu args=["open"|"close"]
	///   invoke_static type=Bpsim.QA.ModManagerUiQA method=Tab       args=["installed"|"workshop"]
	///   invoke_static type=Bpsim.QA.ModManagerUiQA method=StressRows args=[6]
	///   invoke_static type=Bpsim.QA.ModManagerUiQA method=Shot      args=["Logs/bpsim-modmanager.png"]
	///   invoke_static type=Bpsim.QA.ModManagerUiQA method=Layout    args=[]
	///
	/// 验收点：
	/// 1. 底部只有一个导入入口「导入 Mod ▾」，旧的「选择 zip 安装」「选择文件夹安装」不再存在；
	/// 2. 文案是「打开 Mods 目录」「从剪贴板安装」，旧文案在整个面板里消失；
	/// 3. 点「导入 Mod」弹出含 zip / 文件夹两项的菜单（且两项真实 active 可见），点菜单外收起；
	/// 4. 标题按钮组、灰色设置行底板、中文标签和滑动开关复用设置面板模板；
	/// 5. 按钮多到溢出时仍全部落在卡片内（FitRightActions 回归守卫）；
	/// 6. 中文字体不是原生模板的 Frutiger（不含中文字形会显示成方框）；
	/// 7. 跑完控制台无新增 Exception / Error。
	///
	/// 报告写到 &lt;project&gt;/Logs/bpsim-modmanagerui.log。
	/// </summary>
	public sealed class ModManagerUiQA : MonoBehaviour
	{
		private static readonly StringBuilder s_report = new StringBuilder();
		private static int s_passed;
		private static int s_failed;
		private static string s_state = "idle";
		private static ModManagerUiQA s_runner;
		private static readonly List<string> s_fakeIds = new List<string>();

		public static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-modmanagerui.log"));

		// ───────────────────────── 入口 ─────────────────────────

		public static string Open()
		{
			if (!Application.isPlaying) return "not playing";
			ModManagerUI.Open();
			return "open=" + ModManagerUI.IsOpen;
		}

		public static string Close()
		{
			ModManagerUI.Close();
			return "closed";
		}

		public static string ImportMenu(string action)
		{
			ModManagerUI ui = Instance();
			if (ui == null) return "no ui";
			bool open = action != "close";
			Invoke(ui, "SetImportMenu", open);
			RectTransform menu = Field<RectTransform>(ui, "m_importMenu");
			return "menuActive=" + (menu != null && menu.parent.gameObject.activeSelf);
		}

		public static string Tab(string which)
		{
			ModManagerUI ui = Instance();
			if (ui == null) return "no ui";
			Invoke(ui, "SetTab", which == "workshop" ? 1 : 0);
			return "tab=" + which;
		}

		public static string Run()
		{
			if (!Application.isPlaying) return "not playing";
			s_report.Length = 0;
			s_passed = 0;
			s_failed = 0;
			s_state = "running";
			EnsureRunner().StartCoroutine(RunChecks());
			return "started";
		}

		public static string Status()
		{
			return s_state + " | pass=" + s_passed + " fail=" + s_failed + "\n" + s_report;
		}

		private static IEnumerator RunChecks()
		{
			ModManagerUI.Open();
			yield return null;
			yield return null;

			Canvas canvas = FindCanvas();
			Check("canvas 存在且可见", canvas != null && canvas.gameObject.activeSelf);
			if (canvas == null)
			{
				Finish();
				yield break;
			}

			// 1 + 2：合并入口与文案
			List<string> labels = ButtonLabels(canvas.transform);
			Check("存在唯一导入入口「导入 Mod ▾」", Count(labels, "导入 Mod ▾") == 1, string.Join(" | ", labels));
			Check("存在「打开 Mods 目录」", labels.Contains("打开 Mods 目录"));
			Check("存在「从剪贴板安装」", labels.Contains("从剪贴板安装"));
			Check("旧「选择 zip 安装」已消失", !labels.Contains("选择 zip 安装"));
			Check("旧「选择文件夹安装」已消失", !labels.Contains("选择文件夹安装"));
			Check("旧「打开 Mods 文件夹」已消失", !labels.Contains("打开 Mods 文件夹"));
			Check("旧「安装剪贴板里的 zip」已消失", !labels.Contains("安装剪贴板里的 zip"));

			// 3：导入菜单开合
			ModManagerUI ui = Instance();
			RectTransform menu = ui == null ? null : Field<RectTransform>(ui, "m_importMenu");
			Check("导入菜单节点存在", menu != null);
			if (menu != null)
			{
				Check("默认收起", !menu.parent.gameObject.activeSelf);
				Invoke(ui, "ToggleImportMenu");
				yield return null;
				Check("点「导入 Mod」后展开", menu.parent.gameObject.activeSelf);
				Check("菜单本体真实可见（activeInHierarchy）", menu.gameObject.activeInHierarchy);
				List<string> menuLabels = ButtonLabels(menu);
				Check("菜单含「选择 zip 文件…」", menuLabels.Contains("选择 zip 文件…"), string.Join(" | ", menuLabels));
				Check("菜单含「选择文件夹…」", menuLabels.Contains("选择文件夹…"));
				// 只统计层级里真正 active 的按钮：若菜单子物体被 SetActive(false) 而只激活父 blocker，
				// GetComponentsInChildren(true) 仍能取到标签，但玩家既看不见也点不到。
				List<string> visible = new List<string>();
				foreach (Button b in menu.GetComponentsInChildren<Button>(true))
				{
					if (!b.gameObject.activeInHierarchy) continue;
					Text t = b.GetComponentInChildren<Text>(true);
					if (t != null) visible.Add(t.text);
				}
				Check("菜单两项在层级中真实可见", visible.Contains("选择 zip 文件…") && visible.Contains("选择文件夹…"), string.Join(" | ", visible));
				yield return Shot("Logs/bpsim-modmanager-menu.png");
				Invoke(ui, "SetImportMenu", false);
				yield return null;
				Check("可收起", !menu.parent.gameObject.activeSelf);
			}

			// 4：美化的可断言部分
			RectTransform frame = FindDeep(canvas.transform, "Frame") as RectTransform;
			Check("主框存在", frame != null);
			if (frame != null)
			{
				Image frameImg = frame.GetComponent<Image>();
				Check("主框用九宫格圆角 sprite", frameImg != null && frameImg.sprite != null && frameImg.type == Image.Type.Sliced
					&& frameImg.sprite.border.sqrMagnitude > 0f);
				Text title = frame.GetComponentInChildren<Text>();
				Check("标题为「MOD 管理器」", title != null && title.text == "MOD 管理器");
				Check("标题用系统中文字体", title != null && title.font != null && title.font.name != "Frutiger"
					&& title.font.dynamic, title == null || title.font == null ? "no font" : title.font.name);
				List<string> headerLabels = ButtonLabels(frame.Find("ButtonGroup"));
				Check("设置面板标题区复用刷新和关闭按钮", headerLabels.Contains("刷新") && headerLabels.Contains("关闭"), string.Join(" | ", headerLabels));
			}
			RectTransform body = ui == null ? null : Field<RectTransform>(ui, "m_body");
			Check("列表容器存在", body != null);
			if (body != null)
			{
				ScrollRect scroll = body.parent.GetComponent<ScrollRect>();
				VerticalLayoutGroup vlg = body.GetComponent<VerticalLayoutGroup>();
				Check("列表是 ScrollRect + RectMask2D", scroll != null && body.parent.GetComponent<RectMask2D>() != null);
				Check("设置行间距 12 / 内边距 14", vlg != null && Mathf.Approximately(vlg.spacing, 12f) && vlg.padding.left == 14);
			}

			// 5：按钮拥挤回归守卫（造 6 个假包，每个都带面板按钮和快捷按钮）
			yield return StressRowsInternal(6);
			yield return Shot("Logs/bpsim-modmanager-stress.png");
			ClearFakePackages();
			ModManagerUI.Open();
			yield return null;
			yield return Shot("Logs/bpsim-modmanager.png");
			Finish();
		}

		/// <summary>造 count 个假 Mod 包（带 2 个面板 + 2 个快捷按钮），检查右侧按钮不溢出卡片。</summary>
		public static string StressRows(string countText)
		{
			if (!Application.isPlaying) return "not playing";
			int count = 6;
			int.TryParse(countText, out count);
			EnsureRunner().StartCoroutine(StressRowsInternal(Mathf.Clamp(count, 1, 12)));
			return "started";
		}

		private static IEnumerator StressRowsInternal(int count)
		{
			ClearFakePackages();
			ModManagerUI.Open();
			ModManager mgr = ModManager.Ensure();
			for (int i = 0; i < count; i++)
			{
				string id = "qa.fake." + i;
				ModPackage pkg = new ModPackage
				{
					Directory = ModPaths.Root,
					Source = ModSource.Installed,
					State = i % 3 == 0 ? ModState.Loaded : (i % 3 == 1 ? ModState.Error : ModState.Discovered),
					Enabled = i % 2 == 0,
					Error = i % 3 == 1 ? "QA 造的错误文本，用来检查副标题不会被按钮压住" : null,
					Manifest = new ModManifest
					{
						id = id,
						name = "验收假包 " + i,
						version = "1.0." + i,
						author = "QA",
						type = "mixed",
						panels = new List<ModPanelSpec>
						{
							new ModPanelSpec { id = "p1", title = "面板一" },
							new ModPanelSpec { id = "p2", title = "面板二" }
						}
					}
				};
				mgr.Packages.Add(pkg);
				s_fakeIds.Add(id);
				mgr.AddQuickButton(id, "快捷一", null);
				mgr.AddQuickButton(id, "快捷二", null);
			}
			Invoke(Instance(), "Rebuild");
			yield return null;
			yield return null;

			RectTransform body = Field<RectTransform>(Instance(), "m_body");
			int rows = 0;
			int overflow = 0;
			int settingsRows = 0;
			if (body != null)
			{
				foreach (Transform row in body)
				{
					if (!row.name.StartsWith("Row_qa.fake.", StringComparison.Ordinal)) continue;
					rows++;
					RectTransform card = row as RectTransform;
					float halfWidth = card.rect.width * 0.5f;
					foreach (Button button in row.GetComponentsInChildren<Button>(true))
					{
						RectTransform rt = button.GetComponent<RectTransform>();
						// 卡片锚点在中心：把按钮四角换算到卡片本地坐标后判断是否越界
						Vector3[] corners = new Vector3[4];
						rt.GetWorldCorners(corners);
						for (int c = 0; c < 4; c++)
						{
							Vector3 local = card.InverseTransformPoint(corners[c]);
							if (local.x < -halfWidth - 1f || local.x > halfWidth + 1f) { overflow++; break; }
						}
					}
					Image rowBackground = row.GetComponent<Image>();
					Text rowName = row.Find("Name")?.GetComponent<Text>();
					ToggleSwitch toggle = row.GetComponentInChildren<ToggleSwitch>();
					if (rowBackground != null && rowName != null && toggle != null) settingsRows++;
				}
			}
			Check("造出 " + count + " 行假包", rows == count, "rows=" + rows);
			Check("拥挤行的按钮全部落在卡片内", overflow == 0, "overflow=" + overflow);
			Check("每行都复用设置面板底板、标签与滑动开关", settingsRows == rows, "styled=" + settingsRows + " rows=" + rows);
			s_report.AppendLine("stress rows=" + rows + " overflow=" + overflow + " settingsRows=" + settingsRows);
		}

		public static string ClearFakes()
		{
			ClearFakePackages();
			ModManagerUI.Open();
			return "cleared";
		}

		private static void ClearFakePackages()
		{
			ModManager mgr = ModManager.Instance;
			if (mgr == null || s_fakeIds.Count == 0) return;
			mgr.Packages.RemoveAll(p => p != null && s_fakeIds.Contains(p.Id));
			// AddQuickButton 只增不减：只摘掉 QA 自己加的那些，别动真实 Mod 注册的快捷按钮
			Dictionary<string, List<ModManager.QuickButton>> quick =
				Field<Dictionary<string, List<ModManager.QuickButton>>>(mgr, "m_quickButtons");
			if (quick != null)
			{
				foreach (string id in s_fakeIds) quick.Remove(id);
			}
			s_fakeIds.Clear();
		}

		// ───────────────────────── 截图 / 几何 ─────────────────────────

		/// <summary>Game 视图（含 Screen Space Overlay 的 uGUI）存成工程内 PNG。桥自带 screenshot 拍不到 Overlay。</summary>
		public static string Shot(string relativePath)
		{
			try
			{
				Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
				if (shot == null) return "shot=null（Game 视图可能没在渲染）";
				byte[] png = shot.EncodeToPNG();
				int width = shot.width;
				int height = shot.height;
				UnityEngine.Object.Destroy(shot);
				string full = Path.GetFullPath(Path.Combine(Application.dataPath, "..", relativePath));
				Directory.CreateDirectory(Path.GetDirectoryName(full));
				File.WriteAllBytes(full, png);
				return "shot " + relativePath + " " + width + "x" + height;
			}
			catch (Exception exception)
			{
				return "shot failed: " + exception.Message;
			}
		}

		public static string Layout()
		{
			Canvas canvas = FindCanvas();
			if (canvas == null) return "no canvas";
			StringBuilder sb = new StringBuilder();
			Dump(canvas.transform, 0, sb);
			return sb.ToString();
		}

		private static void Dump(Transform t, int depth, StringBuilder sb)
		{
			if (depth > 4) return;
			RectTransform rt = t as RectTransform;
			sb.Append(new string(' ', depth * 2)).Append(t.name);
			if (rt != null) sb.Append(" pos=").Append(rt.anchoredPosition).Append(" size=").Append(rt.sizeDelta);
			Text text = t.GetComponent<Text>();
			if (text != null) sb.Append(" text=\"").Append(text.text).Append('\"');
			sb.AppendLine();
			foreach (Transform child in t) Dump(child, depth + 1, sb);
		}

		// ───────────────────────── 工具 ─────────────────────────

		private static ModManagerUiQA EnsureRunner()
		{
			if (s_runner == null)
			{
				GameObject go = new GameObject("ModManagerUiQA");
				UnityEngine.Object.DontDestroyOnLoad(go);
				s_runner = go.AddComponent<ModManagerUiQA>();
			}
			return s_runner;
		}

		private static ModManagerUI Instance()
		{
			// 面板.hideFlags / inactive 时 FindObjectOfType 可能漏掉；从静态单例反射更稳
			Type type = typeof(ModManagerUI);
			FieldInfo singleton = type.GetField("s_instance", BindingFlags.Static | BindingFlags.NonPublic);
			if (singleton != null) return singleton.GetValue(null) as ModManagerUI;
			return UnityEngine.Object.FindObjectOfType<ModManagerUI>();
		}

		private static Canvas FindCanvas()
		{
			foreach (Canvas canvas in UnityEngine.Object.FindObjectsOfType<Canvas>())
				if (canvas.name == "ModManagerCanvas") return canvas;
			return null;
		}

		private static Transform FindDeep(Transform root, string name)
		{
			if (root.name == name) return root;
			foreach (Transform child in root)
			{
				Transform found = FindDeep(child, name);
				if (found != null) return found;
			}
			return null;
		}

		private static List<string> ButtonLabels(Transform root)
		{
			List<string> labels = new List<string>();
			foreach (Button button in root.GetComponentsInChildren<Button>(true))
			{
				Text text = button.GetComponentInChildren<Text>(true);
				if (text != null) labels.Add(text.text);
			}
			return labels;
		}

		private static int Count(List<string> items, string value)
		{
			int n = 0;
			foreach (string item in items) if (item == value) n++;
			return n;
		}

		private static T Field<T>(object target, string name) where T : class
		{
			if (target == null) return null;
			FieldInfo info = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			return info == null ? null : info.GetValue(target) as T;
		}

		private static void Invoke(object target, string name, params object[] args)
		{
			if (target == null) return;
			MethodInfo method = target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
			if (method != null) method.Invoke(target, args);
		}

		private static void Check(string what, bool ok, string detail = null)
		{
			if (ok) s_passed++; else s_failed++;
			s_report.Append(ok ? "PASS " : "FAIL ").Append(what);
			if (!string.IsNullOrEmpty(detail)) s_report.Append("  [").Append(detail).Append(']');
			s_report.AppendLine();
		}

		private static void Finish()
		{
			s_state = s_failed == 0 ? "pass" : "fail";
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
				File.WriteAllText(LogPath, s_state + " | pass=" + s_passed + " fail=" + s_failed + "\n" + s_report);
			}
			catch (Exception ex) { Debug.LogWarning("[ModManagerUiQA] 写报告失败：" + ex.Message); }
			Debug.Log("[ModManagerUiQA] " + s_state + " pass=" + s_passed + " fail=" + s_failed);
		}
	}
}
#endif
