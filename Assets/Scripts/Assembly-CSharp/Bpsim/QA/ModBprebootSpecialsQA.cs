#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bpsim.Mods;
using Bpsim.Parts;
using Bpsim.Parts.Gameplay;
using Bpsim.Rendering;
using Bpsim.UI;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.QA
{
	/// <summary>
	/// 「bpreboot 特殊部件」Mod 的 Play 模式验收探针。
	///
	///   invoke_static type=Bpsim.QA.ModBprebootSpecialsQA method=Run        args=[]
	///   invoke_static type=Bpsim.QA.ModBprebootSpecialsQA method=Status     args=[]
	///   invoke_static type=Bpsim.QA.ModBprebootSpecialsQA method=Capture    args=[]
	///   invoke_static type=Bpsim.QA.ModBprebootSpecialsQA method=ShowAll    args=[]
	///
	/// 验收点：
	/// 1. 建造列表里出现 9000+ 的 Mod 变体，且**原版索引一个都没被动过**；
	/// 2. 每个变体都能在 <c>PartEntitySpawner</c> 里查到预制体（否则「看得见放不出来」）；
	/// 3. 变体的精灵实体带 <see cref="PartAtlasIndex" /> 且序号不等于 0（说明贴图走的是 Mod 图集）；
	/// 4. <see cref="PartAtlasRegistry" /> 里有 Mod 图集材质，且材质贴图不是原版图集；
	/// 5. 变体实际放到场景里能被 <c>PlacePartAt</c> 接受，物理参数按 Mod 覆写生效
	///    （异形木框发动机功率 77、异形大型引擎 10000）。
	///
	/// 报告写到 &lt;project&gt;/Logs/bpsim-bprebootspecialsqa.log。
	/// </summary>
	public sealed class ModBprebootSpecialsQA : MonoBehaviour
	{
		private static ModBprebootSpecialsQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static int s_passed;

		private static int s_failed;

		private const int CustomIndexBase = 9000;

		/// <summary>Mod 登记的 17 件变体。</summary>
		private static readonly PartTypeInfo[] s_variants =
		{
			new PartTypeInfo(PartType.WoodenFrame, 9000),
			new PartTypeInfo(PartType.WoodenFrame, 9001),
			new PartTypeInfo(PartType.MetalFrame, 9000),
			new PartTypeInfo(PartType.MetalFrame, 9001),
			new PartTypeInfo(PartType.MetalFrame, 9002),
			new PartTypeInfo(PartType.MetalFrame, 9003),
			new PartTypeInfo(PartType.Engine, 9000),
			new PartTypeInfo(PartType.EngineBig, 9000),
			new PartTypeInfo(PartType.SpringBoxingGlove, 9000),
			new PartTypeInfo(PartType.StickyWheel, 9000),
			new PartTypeInfo(PartType.GrapplingHook, 9000),
			new PartTypeInfo(PartType.GrapplingHook, 9001),
			new PartTypeInfo(PartType.GrapplingHook, 9002),
			new PartTypeInfo(PartType.GrapplingHook, 9003),
			new PartTypeInfo(PartType.GrapplingHook, 9004),
			new PartTypeInfo(PartType.Propeller, 9000),
			new PartTypeInfo(PartType.Spring, 9000),
			new PartTypeInfo(PartType.TNT, 9000)
		};

		public static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-bprebootspecialsqa.log"));

		public static string Run()
		{
			ModBprebootSpecialsQA runner = Ensure();
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
		/// 列出每个变体模板上所有 <c>SpriteBase</c> 的键名，用来核对皮肤是否按预期
		/// 落在正确的子物体上（光看截图容易把「没换」看成「换了」）。
		/// </summary>
		public static string DumpSkins()
		{
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.Factory == null)
			{
				return "PartManager 未就绪";
			}
			StringBuilder builder = new StringBuilder();
			PartCollection<ManagedPart> collection = manager.Factory.PartCollection;
			foreach (PartTypeInfo info in s_variants)
			{
				if (!collection.TryFindPart(info, out ManagedPart part) || part == null)
				{
					builder.AppendLine(info.PartType + "#" + info.PartIndex + " : <missing>");
					continue;
				}
				builder.AppendLine(info.PartType + "#" + info.PartIndex + " :");
				SpriteBase[] sprites = part.GetComponentsInChildren<SpriteBase>(includeInactive: true);
				foreach (SpriteBase sprite in sprites)
				{
					builder.AppendLine("    " + sprite.gameObject.name + " = " + sprite.SpriteName + "  [" + sprite.GetType().Name + "]");
				}
			}
			return builder.ToString();
		}

		/// <summary>
		/// 实测建造列表 UI 到底有没有把 Mod 变体展示出来。
		///
		/// 打印三件事：
		/// 1. 左侧主列表里每种 PartType 有几行（<c>PartSelector</c> 的 item 数）；
		/// 2. 某个家族的变体弹窗（<c>CustomPartSelector</c>）里生成多少个条目、名字是什么；
		/// 3. <c>PartCollection.FindParts</c> 的键集合，用来和 UI 对照。
		/// </summary>
		public static string DumpUi()
		{
			StringBuilder builder = new StringBuilder();
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.Factory == null)
			{
				return "PartManager 未就绪";
			}
			PartCollection<ManagedPart> collection = manager.Factory.PartCollection;

			builder.AppendLine("== PartCollection（每族的索引键）==");
			foreach (PartType type in new PartType[]
			{
				PartType.WoodenFrame, PartType.MetalFrame, PartType.Engine, PartType.EngineBig,
				PartType.SpringBoxingGlove, PartType.StickyWheel, PartType.GrapplingHook,
				PartType.Propeller, PartType.Spring, PartType.TNT
			})
			{
				if (!collection.TryFindParts(type, out PartCollection<ManagedPart>.Entry entry) || entry == null)
				{
					builder.AppendLine("  " + type + " : <no entry>");
					continue;
				}
				List<int> keys = new List<int>(entry.Data.Keys);
				builder.AppendLine("  " + type + " (" + keys.Count + ") : " + string.Join(", ", keys));
			}

			builder.AppendLine("== 左侧主列表 PartSelector ==");
			PartSelector selector = Object.FindObjectOfType<PartSelector>();
			if (selector == null)
			{
				builder.AppendLine("  找不到 PartSelector（可能不在建造界面）");
			}
			else
			{
				GameObject contentObject = GetPrivateField(selector, "m_content") as GameObject;
				Transform content = contentObject != null ? contentObject.transform : null;
				if (content == null)
				{
					builder.AppendLine("  m_content 为空");
				}
				else
				{
					builder.AppendLine("  Content 子物体数 = " + content.childCount);
					PartSelector live = Object.FindObjectOfType<PartSelector>();
					if (live != null)
					{
						for (int i = 0; i < content.childCount; i++)
						{
							Transform child = content.GetChild(i);
							Transform name = child.Find("Name");
							if (name == null) continue;
							UnityEngine.UI.Text text = name.GetComponent<UnityEngine.UI.Text>();
							if (text == null) continue;
							if (text.text.IndexOf("（+") >= 0)
								builder.AppendLine("  带变体行: " + child.name + " = " + text.text);
						}
					}
					StringBuilder names = new StringBuilder();
					for (int i = 0; i < content.childCount; i++)
					{
						if (i > 0)
						{
							names.Append(", ");
						}
						names.Append(content.GetChild(i).name);
					}
					builder.AppendLine("  子物体：" + names);
				}
			}

			builder.AppendLine("== 变体弹窗 CustomPartSelector（模拟打开 WoodenFrame）==");
			if (selector == null)
			{
				builder.AppendLine("  跳过：没有 PartSelector");
			}
			else
			{
				CustomPartSelector template = (CustomPartSelector)GetPrivateField(selector, "m_customPartSelectorTemplate");
				if (template == null)
				{
					builder.AppendLine("  m_customPartSelectorTemplate 为空");
				}
				else
				{
					CustomPartSelector popup = Object.Instantiate(template, UserInterface.Instance.SubCanvas.transform);
					try
					{
						popup.Initialize(PartType.WoodenFrame, 0, 0, false);
						GameObject listObject = GetPrivateField(popup, "m_listContent") as GameObject;
						Transform listContent = listObject != null ? listObject.transform : null;
						if (listContent == null)
						{
							builder.AppendLine("  m_listContent 为空");
						}
						else
						{
							builder.AppendLine("  变体条目数 = " + listContent.childCount);
							for (int i = 0; i < listContent.childCount; i++)
							{
								Transform child = listContent.GetChild(i);
								Transform label = child.Find("Name");
								string text = label != null ? label.GetComponent<UnityEngine.UI.Text>().text : "?";
								Transform icon = child.Find("PartIcon");
								SpriteBase iconSprite = icon != null ? icon.GetComponent<SpriteBase>() : null;
								string iconName = iconSprite != null ? iconSprite.SpriteName : "<no sprite>";
								RawImage rawImage = icon != null ? icon.GetComponent<RawImage>() : null;
								string rect = rawImage != null ? rawImage.uvRect.ToString() : "<no rawimage>";
								string texInfo = "<no rawimage>";
								if (rawImage != null)
								{
									Texture tex = rawImage.texture;
									Material mat = rawImage.material;
									SpriteManager sm = SpriteManager.Instance;
									int sid = iconSprite != null ? iconSprite.SpriteID : -1;
									string atlas = sm != null ? sm.GetSpriteAtlas(sid) : null;
									int ai = PartAtlasRegistry.IndexOf(atlas);
									Texture2D regTex = PartAtlasRegistry.GetTexture(ai);
									texInfo = "tex=" + (tex != null ? tex.name + "(" + tex.width + "x" + tex.height + ")" : "null")
										+ " mat=" + (mat != null ? mat.name : "null")
										+ " matTex=" + (mat != null && mat.mainTexture != null ? mat.mainTexture.name : "null")
										+ " sid=" + sid + " atlas=" + (atlas == null ? "null" : atlas)
										+ " ai=" + ai + " regTex=" + (regTex != null ? regTex.name : "null")
										+ " size=" + rawImage.rectTransform.sizeDelta;
								}
								builder.AppendLine("    " + child.name + "  文本=" + text
									+ "  图标=" + iconName + "  uv=" + rect + "  " + texInfo);
							}
						}
					}
					finally
					{
						Object.DestroyImmediate(popup.gameObject);
					}
				}
			}
			return builder.ToString();
		}

		private static object GetPrivateField(object target, string fieldName)
		{
			System.Reflection.FieldInfo field = target.GetType().GetField(
				fieldName,
				System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
			return field == null ? null : field.GetValue(target);
		}

		/// <summary>把游戏相机（正交，2D）当前的画面存成 PNG。</summary>
		public static string Capture()
		{
			SceneCamera sceneCamera = SceneCamera.Instance;
			if (sceneCamera == null || sceneCamera.Camera == null)
			{
				return "no camera";
			}
			Camera cam = sceneCamera.Camera;
			RenderTexture previous = cam.targetTexture;
			RenderTexture rt = new RenderTexture(960, 720, 24, RenderTextureFormat.ARGB32);
			Texture2D texture = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
			try
			{
				cam.targetTexture = rt;
				cam.Render();
				RenderTexture.active = rt;
				texture.ReadPixels(new Rect(0f, 0f, rt.width, rt.height), 0, 0);
				texture.Apply();
				string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-bprebootspecials.png"));
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllBytes(path, texture.EncodeToPNG());
				return path;
			}
			finally
			{
				cam.targetTexture = previous;
				RenderTexture.active = null;
				Object.Destroy(rt);
				Object.Destroy(texture);
			}
		}

		/// <summary>
		/// 打开「木框」的变体弹窗并截图，用来肉眼确认 9000+ 的条目真的在列表里。
		/// </summary>
		public static string ShotPopup()
		{
			return ShotPopupOf((int)PartType.WoodenFrame);
		}

		/// <summary>按部件族打开变体弹窗并截图（参数：PartType 的枚举名或整数）。</summary>
		public static string ShotPopupOf(int partType)
		{
			ModBprebootSpecialsQA runner = Ensure();
			runner.StartCoroutine(runner.ShotPopupRoutine((PartType)partType));
			return "started";
		}

		private IEnumerator ShotPopupRoutine()
		{
			yield return ShotPopupBody(PartType.WoodenFrame);
		}

		private IEnumerator ShotPopupRoutine(PartType partType)
		{
			yield return ShotPopupBody(partType);
		}

		private IEnumerator ShotPopupBody(PartType partType)
		{
			PartSelector selector = Object.FindObjectOfType<PartSelector>();
			if (selector == null)
			{
				s_status = "shot-popup: 没有 PartSelector";
				yield break;
			}
			System.Reflection.MethodInfo method = typeof(PartSelector).GetMethod(
				"CreateCustomPartSelector",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			if (method == null)
			{
				s_status = "shot-popup: 找不到 CreateCustomPartSelector";
				yield break;
			}
			method.Invoke(selector, new object[] { partType });
			yield return null;
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();
			string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-bprebootspecials-popup-" + (int)partType + ".png"));
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			ScreenCapture.CaptureScreenshot(path);
			s_status = "shot-popup: " + path;
		}

		/// <summary>按部件族打开变体弹窗、滚到列表末尾再截图（看 134 项的铁框等大族）。</summary>
		public static string ShotPopupBottom(int partType)
		{
			ModBprebootSpecialsQA runner = Ensure();
			runner.StartCoroutine(runner.ShotPopupBottomRoutine((PartType)partType));
			return "started";
		}

		private IEnumerator ShotPopupBottomRoutine(PartType partType)
		{
			PartSelector selector = Object.FindObjectOfType<PartSelector>();
			if (selector == null)
			{
				s_status = "shot-popup-bottom: 没有 PartSelector";
				yield break;
			}
			System.Reflection.MethodInfo method = typeof(PartSelector).GetMethod(
				"CreateCustomPartSelector",
				System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			if (method == null)
			{
				s_status = "shot-popup-bottom: 找不到 CreateCustomPartSelector";
				yield break;
			}
			method.Invoke(selector, new object[] { partType });
			yield return null;
			yield return new WaitForEndOfFrame();
			ScrollRect scroll = Object.FindObjectOfType<ScrollRect>();
			if (scroll != null)
			{
				Canvas.ForceUpdateCanvases();
				scroll.verticalNormalizedPosition = 0f;
				Canvas.ForceUpdateCanvases();
			}
			yield return new WaitForEndOfFrame();
			yield return new WaitForEndOfFrame();
			string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-bprebootspecials-popup-bottom-" + (int)partType + ".png"));
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			ScreenCapture.CaptureScreenshot(path);
			s_status = "shot-popup-bottom: " + path + (scroll == null ? " (没有 ScrollRect)" : "");
		}

		/// <summary>
		/// 截当前 Game 视图（含 UGUI 建造列表），存成 PNG。等一帧到渲染结束再抓，
		/// 否则 Canvas 还没重建，抓到的是上一帧。
		/// </summary>
		public static string Shot()
		{
			ModBprebootSpecialsQA runner = Ensure();
			runner.StartCoroutine(runner.ShotRoutine());
			return "started";
		}

		private IEnumerator ShotRoutine()
		{
			yield return new WaitForEndOfFrame();
			string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-bprebootspecials-screen.png"));
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			ScreenCapture.CaptureScreenshot(path);
			s_status = "shot: " + path;
		}

		/// <summary>
		/// 把 17 件变体摆成一个网格放进空场景（不开始模拟），方便截图肉眼看皮肤。
		/// </summary>
		public static string ShowAll()		{
			ModBprebootSpecialsQA runner = Ensure();
			runner.StartCoroutine(runner.ShowAllRoutine());
			return "started";
		}

		private static ModBprebootSpecialsQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[ModBprebootSpecialsQA]");
			gameObject.hideFlags = HideFlags.HideAndDontSave;
			Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<ModBprebootSpecialsQA>();
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

		private static void Line(string message)
		{
			s_report.Append(message).Append('\n');
		}

		private IEnumerator RunChecks()
		{
			for (int i = 0; i < 900 && PartManager.Instance == null; i++)
			{
				yield return null;
			}
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				Check("PartManager", false, "未就绪");
				Finish();
				yield break;
			}
			Check("PartManager", true);

			// Mod 在 AfterSceneLoad 之后加载，Bootstrapper 每 0.5 秒重试一次。
			int found = 0;
			for (int i = 0; i < 600; i++)
			{
				found = 0;
				PartCollection<ManagedPart> collection = manager.Factory.PartCollection;
				foreach (PartTypeInfo info in s_variants)
				{
					if (collection.TryFindPart(info, out ManagedPart part) && part != null)
					{
						found++;
					}
				}
				if (found == s_variants.Length)
				{
					break;
				}
				yield return null;
			}
			Check("17 件变体都在 PartCollection 里", found == s_variants.Length, "found=" + found + "/" + s_variants.Length);

			CheckSpawner(manager);
			CheckAtlases(manager);
			CheckExtensions(manager);
			CheckOriginalsUntouched(manager);
			yield return CheckPlacement(manager);

			Finish();
		}

		// ------------------------------------------------------------------
		// 1. 预制体表：变体必须能 FindPrefab
		// ------------------------------------------------------------------
		private static void CheckSpawner(PartManager manager)
		{
			int missing = 0;
			foreach (PartTypeInfo info in s_variants)
			{
				Entity prefab = manager.EntitySpawner.FindPrefab(info);
				if (prefab == Entity.Null)
				{
					missing++;
					Line("   missing prefab: " + info.PartType + "#" + info.PartIndex);
				}
			}
			Check("变体预制体都在 PartEntitySpawner 里", missing == 0, "missing=" + missing);
		}

		// ------------------------------------------------------------------
		// 2. 图集：材质已注册、贴图不是原版、精灵实体带 PartAtlasIndex
		// ------------------------------------------------------------------
		private static void CheckAtlases(PartManager manager)
		{
			Check("PartAtlasRegistry 至少有 3 张图集（原版 + 皮肤 + 图标）", PartAtlasRegistry.Count >= 3, "count=" + PartAtlasRegistry.Count);

			int skin = PartAtlasRegistry.IndexOf("Bpreboot_Skins");
			int icons = PartAtlasRegistry.IndexOf("Bpreboot_Icons");
			Check("皮肤图集已登记", skin > 0, "index=" + skin);
			Check("图标图集已登记", icons > 0, "index=" + icons);

			Material baseMaterial = null;
			CoreManager core = CoreManager.Instance;
			if (core != null)
			{
				core.Resources.TryLoadAsset<Material>("Part_Texture_0", out baseMaterial);
			}
			Material skinMaterial = PartAtlasRegistry.GetMaterial(skin, null);
			Check("皮肤图集材质有独立贴图", skinMaterial != null && skinMaterial.mainTexture != null
				&& (baseMaterial == null || skinMaterial.mainTexture != baseMaterial.mainTexture));

			// 抽一件变体，看它的精灵实体有没有 PartAtlasIndex（且非 0）。
			EntityManager entityManager = PartGameplayUtil.Em;
			int checkedSprites = 0;
			int tagged = 0;
			foreach (PartTypeInfo info in s_variants)
			{
				Entity prefab = manager.EntitySpawner.FindPrefab(info);
				if (prefab == Entity.Null || !entityManager.Exists(prefab))
				{
					continue;
				}
				DynamicBuffer<LinkedEntityGroup> group = entityManager.GetBuffer<LinkedEntityGroup>(prefab, true);
				for (int i = 0; i < group.Length; i++)
				{
					Entity child = group[i].Value;
					if (!entityManager.HasComponent<SpriteID>(child))
					{
						continue;
					}
					checkedSprites++;
					if (entityManager.HasComponent<PartAtlasIndex>(child)
						&& entityManager.GetComponentData<PartAtlasIndex>(child).Value > 0)
					{
						tagged++;
					}
				}
			}
			Check("变体精灵实体带 Mod 图集序号", checkedSprites > 0 && tagged > 0,
				"sprites=" + checkedSprites + " tagged=" + tagged);

			SpriteManager spriteManager = SpriteManager.Instance;
			if (spriteManager != null)
			{
				Check("皮肤精灵键能在 SpriteManager 里查到", spriteManager.HasID("bpreboot/AlienWoodenFrame"));
				Check("变体图标键能在 SpriteManager 里查到", spriteManager.HasID("Part_5_9000_Rendered"));
			}
		}

		// ------------------------------------------------------------------
		// 3. 物理参数按 Mod 覆写
		// ------------------------------------------------------------------
		private static void CheckExtensions(PartManager manager)
		{
			Dictionary<PartTypeInfo, PartExtensionData> map = manager.Factory.PartExtensionMap;
			if (map == null)
			{
				Check("PartExtensionMap 可读", false);
				return;
			}
			bool wooden = map.TryGetValue(new PartTypeInfo(PartType.WoodenFrame, 9000), out PartExtensionData woodenData);
			Check("异形木框的发动机功率 = 77", wooden && Mathf.Approximately(woodenData.EnginePower, 77f),
				wooden ? ("power=" + woodenData.EnginePower) : "缺条目");

			bool engine = map.TryGetValue(new PartTypeInfo(PartType.Engine, 9000), out PartExtensionData engineData);
			Check("异形引擎的发动机功率 = 7500", engine && Mathf.Approximately(engineData.EnginePower, 7500f),
				engine ? ("power=" + engineData.EnginePower) : "缺条目");

			bool big = map.TryGetValue(new PartTypeInfo(PartType.EngineBig, 9000), out PartExtensionData bigData);
			Check("异形大型引擎的发动机功率 = 10000", big && Mathf.Approximately(bigData.EnginePower, 10000f),
				big ? ("power=" + bigData.EnginePower) : "缺条目");

			bool tungsten = map.TryGetValue(new PartTypeInfo(PartType.MetalFrame, 9003), out PartExtensionData tungstenData);
			Check("钨方块的质量 = 5", tungsten && Mathf.Approximately(tungstenData.Mass, 5f),
				tungsten ? ("mass=" + tungstenData.Mass) : "缺条目");
		}

		// ------------------------------------------------------------------
		// 4. 原版索引没被替换/覆盖
		// ------------------------------------------------------------------
		private static void CheckOriginalsUntouched(PartManager manager)
		{
			PartCollection<ManagedPart> collection = manager.Factory.PartCollection;
			int[] expected = { 11, 134, 10, 7, 7, 5, 4, 10 };
			PartType[] types =
			{
				PartType.WoodenFrame, PartType.MetalFrame, PartType.EngineSmall, PartType.Engine,
				PartType.EngineBig, PartType.SpringBoxingGlove, PartType.StickyWheel, PartType.GrapplingHook
			};
			for (int i = 0; i < types.Length; i++)
			{
				if (!collection.TryFindParts(types[i], out var entry))
				{
					Check("原版 " + types[i] + " 仍在", false, "缺 PartType");
					continue;
				}
				int original = 0;
				foreach (int key in entry.Data.Keys)
				{
					if (key < CustomIndexBase)
					{
						original++;
					}
				}
				Check("原版 " + types[i] + " 的索引数不变", original == expected[i], "got=" + original + " want=" + expected[i]);
			}
		}

		// ------------------------------------------------------------------
		// 5. 真摆一次：变体必须能被 PlacePartAt 接受
		// ------------------------------------------------------------------
		private IEnumerator CheckPlacement(PartManager manager)
		{
			if (manager.IsSimulating)
			{
				manager.EndSimulation();
				yield return null;
			}
			manager.LoadEmptyScene("BprebootSpecialsQA");
			yield return null;
			PartScene scene = manager.ActiveScene;
			if (scene == null)
			{
				Check("空场景可用", false);
				yield break;
			}

			int placed = 0;
			int index = 0;
			foreach (PartTypeInfo info in s_variants)
			{
				Entity prefab = manager.EntitySpawner.FindPrefab(info);
				if (prefab == Entity.Null)
				{
					continue;
				}
				int x = (index % 5) * 2;
				int y = (index / 5) * 2;
				index++;
				if (scene.Unmanaged.PlacePartAt(new PartCreationRequest(prefab, scene.SceneID, info, x, y, 0, false), false))
				{
					placed++;
				}
				else
				{
					Line("   rejected: " + info.PartType + "#" + info.PartIndex + " at " + x + "," + y);
				}
			}
			scene.Unmanaged.Update();
			yield return null;
			Check("17 件变体都能放进场景", placed == s_variants.Length, "placed=" + placed + "/" + s_variants.Length);

			SceneCamera camera = SceneCamera.Instance;
			if (camera != null)
			{
				// 排成 5 列、间距 2.2，镜头拉近到 9 —— 之前用 14 太小，肉眼看不出皮肤。
				camera.MoveTo(new Vector2(4.4f, 3.3f));
				camera.ScaleTo(9f);
				camera.ZoomTo(9f);
			}
		}

		private IEnumerator ShowAllRoutine()
		{
			for (int i = 0; i < 900 && PartManager.Instance == null; i++)
			{
				yield return null;
			}
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				yield break;
			}
			for (int i = 0; i < 600; i++)
			{
				if (manager.Factory.PartCollection.TryFindPart(new PartTypeInfo(PartType.WoodenFrame, 9000), out ManagedPart part) && part != null)
				{
					break;
				}
				yield return null;
			}
			yield return CheckPlacement(manager);
			s_status = "shown";
		}

		private void Finish()
		{
			s_status = s_failed == 0 ? "pass" : "fail";
			try
			{
				string path = LogPath;
				Directory.CreateDirectory(Path.GetDirectoryName(path));
				File.WriteAllText(path, Status());
			}
			catch (System.Exception exception)
			{
				Debug.LogWarning("[ModBprebootSpecialsQA] 写日志失败：" + exception.Message);
			}
			Debug.Log("[ModBprebootSpecialsQA] " + s_status + " pass=" + s_passed + " fail=" + s_failed);
		}
	}
}
#endif
