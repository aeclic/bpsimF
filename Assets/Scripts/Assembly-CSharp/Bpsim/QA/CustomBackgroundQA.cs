using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Bpsim.Background;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>
	/// 自定义背景的运行时自检（不依赖编辑器、不依赖关卡）。
	///
	/// 分三段，全部在 Play Mode 下跑：
	/// <list type="number">
	/// <item><b>公式</b>：直接调 <see cref="CustomBackgroundSystem.EvaluateSurface"/>，用一台参数已知的正交相机
	/// 核对 BPSS 那套 <c>viewH = 2*orthographicSize</c> 的尺寸/位置折算（含绝对坐标、固定尺寸分支）。</item>
	/// <item><b>持久化</b>：把 CustomBackgroundSettings 塞进 UserSettings，走一遍 Bpsim.Serialization.Json 往返，
	/// 确认 19 个参数都能原样回来（System.Text.Json + camelCase + 字段序列化）。</item>
	/// <item><b>渲染</b>：现场生成一张 PNG 当背景 → 等系统加载 → 断言 <see cref="CustomBackgroundSystem.LastDrawn"/> 为 1
	/// 且回读到正的尺寸；再关掉开关断言不再绘制。视频分支用「路径不存在」验错误处理。</item>
	/// </list>
	///
	/// 用法（DshBridge）：
	/// <code>
	/// invoke_static type=Bpsim.QA.CustomBackgroundQA method=Run      args=[]   → "started"
	/// invoke_static type=Bpsim.QA.CustomBackgroundQA method=Status   args=[]   → "done ok=6 fail=0 ..."
	/// </code>
	/// 报告也会写到 <see cref="LogPath"/>。
	/// </summary>
	public class CustomBackgroundQA : MonoBehaviour
	{
		private const string Tag = "[BgQA]";

		private static CustomBackgroundQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static int s_pass;

		private static int s_fail;

		private static bool s_running;

		public static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "custom-background-qa.log"));

		/// <summary>启动一次自检；返回立即生效的状态串（真正的结果用 <see cref="Status"/> 轮询）。</summary>
		public static string Run(string unused)
		{
			if (s_running)
			{
				return "busy: " + s_status;
			}
			if (!Application.isPlaying)
			{
				return "error: 需要 Play Mode（公式段可以，但渲染段需要每帧更新）";
			}
			s_report.Length = 0;
			s_pass = 0;
			s_fail = 0;
			s_running = true;
			s_status = "running";
			s_instance = Ensure();
			s_instance.StartCoroutine(s_instance.RunWrapped());
			return "started";
		}

		public static string Status()
		{
			return s_status + " pass=" + s_pass + " fail=" + s_fail + " log=" + LogPath;
		}

		/// <summary>最近一次失败的明细（给 CI/人工看）。</summary>
		public static string Report()
		{
			return s_report.ToString();
		}

		// ---------------------------------------------------------------- 演示 / 截图

		/// <summary>
		/// 演示用：现场画一张 64×32 的洋红/青色测试图（半透明棋盘格），返回绝对路径。
		/// 之所以不用 Assets 里的美术资源，是为了让"背景真的在画"这件事不依赖任何外部文件。
		/// </summary>
		public static string MakeDemoImage()
		{
			Directory.CreateDirectory(CustomBackgroundMediaLoader.StorageDirectory);
			int width = 256;
			int height = 128;
			Texture2D texture = new Texture2D(width, height, TextureFormat.RGBA32, mipChain: false);
			Color[] pixels = new Color[width * height];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					bool first = ((x / 16) + (y / 16)) % 2 == 0;
					Color color = first ? new Color(0.85f, 0.15f, 0.55f, 1f) : new Color(0.15f, 0.75f, 0.85f, 1f);
					// 画一圈不透明边框，方便目视确认四条边都落在视野里。
					if (x < 3 || y < 3 || x >= width - 3 || y >= height - 3)
					{
						color = new Color(1f, 1f, 0.2f, 1f);
					}
					pixels[y * width + x] = color;
				}
			}
			texture.SetPixels(pixels);
			texture.Apply();
			byte[] png = texture.EncodeToPNG();
			UnityEngine.Object.Destroy(texture);
			string path = Path.Combine(CustomBackgroundMediaLoader.StorageDirectory, "demo-background.png");
			File.WriteAllBytes(path, png);
			return path;
		}

		/// <summary>
		/// 演示用：把给定图片设为背景并打开设置面板，好让截图里同时出现「背景」和「设置里的那一组控件」。
		/// 参数为空时自动用 <see cref="MakeDemoImage"/> 生成的图。
		/// </summary>
		public static string Demo(string imagePath)
		{
			if (!Application.isPlaying)
			{
				return "error: 需要 Play Mode";
			}
			CustomBackgroundSettings settings = UserSettings.Instance?.CustomBackground;
			if (settings == null)
			{
				return "error: UserSettings.CustomBackground 不存在";
			}
			if (string.IsNullOrEmpty(imagePath))
			{
				imagePath = MakeDemoImage();
			}
			settings.Path = imagePath;
			settings.Enabled = true;
			settings.Opacity = 1f;
			settings.CameraAdaptive = true;
			settings.CustomScale = true;
			settings.ScaleX = 1f;
			settings.ScaleY = 1f;
			settings.OffsetX = 0f;
			settings.OffsetY = 0f;
			settings.Rotation = 0f;
			settings.UseAbsolute = false;
			settings.Distance = 6f;
			UserSettings.Save();

			Bpsim.UI.UserInterface ui = Bpsim.UI.UserInterface.Instance;
			if (ui != null)
			{
				System.Reflection.MethodInfo open = typeof(Bpsim.UI.UserInterface.ActionBarView).GetMethod(
					"OpenSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				open?.Invoke(ui.ActionBar, null);
			}
			return "ok path=" + settings.Path + " enabled=" + settings.Enabled;
		}

		/// <summary>
		/// 一行式现状摘要（不依赖截图，纯文本硬证据）：系统画了几张、画到哪儿、媒体加载状态、
		/// 以及设置面板是否真的开着、里面有多少行。
		/// </summary>
		public static string Probe(string unused)
		{
			if (!Application.isPlaying)
			{
				return "error: 需要 Play Mode";
			}
			StringBuilder b = new StringBuilder();
			b.Append("drawn=").Append(CustomBackgroundSystem.LastDrawn)
			 .Append(" frame=").Append(CustomBackgroundSystem.LastDrawnFrame)
			 .Append(" now=").Append(UnityEngine.Time.frameCount)
			 .Append(" err='").Append(CustomBackgroundSystem.LastError).Append('\'');
			if (CustomBackgroundSystem.TryGetSurface(out Vector3 position, out Vector2 size, out float rotation))
			{
				b.Append(" pos=(").Append(F(position.x)).Append(',').Append(F(position.y)).Append(',').Append(F(position.z)).Append(')')
				 .Append(" size=(").Append(F(size.x)).Append(',').Append(F(size.y)).Append(')')
				 .Append(" rot=").Append(F(rotation));
			}
			CustomBackgroundSettings settings = UserSettings.Instance?.CustomBackground;
			if (settings != null)
			{
				b.Append(" enabled=").Append(settings.Enabled).Append(" path='").Append(settings.Path).Append('\'');
			}
			Bpsim.UI.UserSettingsPanel panel = UnityEngine.Object.FindObjectOfType<Bpsim.UI.UserSettingsPanel>();
			if (panel == null)
			{
				b.Append(" panel=none");
			}
			else
			{
				System.Reflection.FieldInfo contentField = typeof(Bpsim.UI.UserSettingsPanel).GetField(
					"m_content", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				Transform content = (contentField != null) ? ((GameObject)contentField.GetValue(panel))?.transform : null;
				b.Append(" panel=open rows=").Append(content != null ? content.childCount : -1);
				System.Reflection.FieldInfo drawerField = typeof(Bpsim.UI.UserSettingsPanel).GetField(
					"m_backgroundDrawer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				GameObject drawer = (drawerField != null) ? drawerField.GetValue(panel) as GameObject : null;
				b.Append(" drawer=").Append(drawer == null ? "none" : (drawer.activeSelf ? "open" : "closed"));
				b.Append(" drawerRows=").Append(drawer != null ? drawer.transform.childCount : -1);
			}
			return b.ToString();
		}

		/// <summary>抓一帧 Game 视图（含 UI）写到文件，供人工/自动化看图。</summary>
		public static string Capture(string filePath)
		{
			if (!Application.isPlaying)
			{
				return "error: 需要 Play Mode（本方法抓的是 Game 视图）";
			}
			if (string.IsNullOrEmpty(filePath))
			{
				filePath = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "custom-background-demo.png"));
			}
			Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
			try
			{
				string directory = Path.GetDirectoryName(filePath);
				if (!string.IsNullOrEmpty(directory))
				{
					Directory.CreateDirectory(directory);
				}
				File.WriteAllBytes(filePath, shot.EncodeToPNG());
			}
			finally
			{
				UnityEngine.Object.Destroy(shot);
			}
			return "ok " + filePath + " " + Screen.width + "x" + Screen.height;
		}

		private static CustomBackgroundQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject go = new GameObject("[CustomBackgroundQA]");
			go.hideFlags = HideFlags.DontSave;
			UnityEngine.Object.DontDestroyOnLoad(go);
			s_instance = go.AddComponent<CustomBackgroundQA>();
			return s_instance;
		}

		/// <summary>
		/// 迭代器本体：<c>yield</c> 不能出现在带 catch 的 try 里（CS1626），
		/// 所以异常捕获放到 <see cref="RunWrapped"/>，这里只用 try/finally 做清理。
		/// </summary>
		private IEnumerator Routine()
		{
			CustomBackgroundSettings settings = null;
			CustomBackgroundSettings original = null;
			string testImage = string.Empty;
			try
			{
				UserSettings userSettings = UserSettings.Instance;
				Check(userSettings != null, "UserSettings.Instance 存在");
				if (userSettings == null)
				{
					yield break;
				}
				settings = userSettings.CustomBackground;
				Check(settings != null, "UserSettings.CustomBackground 存在");
				if (settings == null)
				{
					yield break;
				}

				Section("1/5 公式（EvaluateSurface，正交相机 size=10, aspect=2, 距离 4）");
				RunFormulaChecks();

				// 整段自检会反复改这份配置，先在入口留一份完整快照，收尾时原样还原，
				// 免得 QA 把玩家真正在用的背景设置改掉了。
				original = Bpsim.Serialization.Json.Deserialize<CustomBackgroundSettings>(
					Bpsim.Serialization.Json.Serialize(settings));

				Section("2/5 持久化往返");
				RunSerializationChecks(userSettings);

				Section("3/5 渲染：图片背景");
				testImage = CreateTestImage();
				settings.Path = testImage;
				settings.Enabled = true;
				settings.Distance = 4f;
				settings.Opacity = 1f;
				settings.CameraAdaptive = true;
				settings.CustomScale = true;
				settings.ScaleX = 1f;
				settings.ScaleY = 1f;
				settings.UseAbsolute = false;
				settings.OffsetX = 0f;
				settings.OffsetY = 0f;
				settings.Rotation = 0f;
				settings.AbsoluteX = 0f;
				settings.AbsoluteY = 0f;
				// 系统按路径指纹重载，改完等几帧让它把 PNG 解出来。
				yield return null;
				yield return null;
				yield return null;
				yield return null;
				Check(CustomBackgroundSystem.LastError == string.Empty, "加载无错误（LastError 为空，实际='" + CustomBackgroundSystem.LastError + "'）");
				Check(CustomBackgroundSystem.LastDrawn == 1, "图片背景已提交绘制（LastDrawn=" + CustomBackgroundSystem.LastDrawn + "）");
				bool hasSurface = CustomBackgroundSystem.TryGetSurface(out Vector3 position, out Vector2 size, out float rotation);
				Check(hasSurface, "TryGetSurface 能回读姿态（快速填充依赖它）");
				if (hasSurface)
				{
					// 视野高度直接取当前相机，别写死 20：场景相机进 Play 后会被
					// PartScene 的 CameraSize / 缩放弹簧改掉（10 或 20 都可能）。
					Camera cam = ResolveCamera();
					float viewHeight = (cam != null && cam.orthographic) ? 2f * cam.orthographicSize : 20f;
					Check(Mathf.Abs(size.y - viewHeight) < 0.01f, "高度跟随视野（期望 " + F(viewHeight) + "，实际 " + F(size.y) + "）");
					// 图片是 64x32 → 宽高比 2；相机 aspect 由 Game 视图决定，所以只校验「宽 = 高 * 宽高比 * ScaleX」这一关系。
					float expectedWidth = size.y * 2f;
					Check(Mathf.Abs(size.x - expectedWidth) < 0.05f, "宽度 = 高 × 媒体宽高比（期望 " + F(expectedWidth) + "，实际 " + F(size.x) + "）");
					// 背景是世界里的一块板：Z = 网格平面 z + Distance（不再是 Distance 本身），
					// 这样它永远落在网格和部件后面，不会像贴纸一样浮在部件上面。
					float expectedZ = CustomBackgroundSystem.ResolveGridAnchor().z + 4f;
					Check(Mathf.Abs(position.z - expectedZ) < 0.005f, "Z = 网格平面 + Distance（期望 " + F(expectedZ) + "，实际 " + F(position.z) + "）");
					Check(Mathf.Abs(rotation) < 0.001f, "无旋转时旋转角为 0");
				}

				Section("4/5 渲染：关闭开关 / 视频错误处理");
				settings.Enabled = false;
				yield return null;
				yield return null;
				Check(CustomBackgroundSystem.LastDrawn == 0, "关闭开关后不再绘制（LastDrawn=" + CustomBackgroundSystem.LastDrawn + "）");

				settings.Path = Path.Combine(Application.persistentDataPath, "CustomBackground", "definitely-missing.mp4");
				settings.Enabled = true;
				yield return null;
				yield return null;
				Check(CustomBackgroundSystem.LastDrawn == 0, "视频文件不存在时不绘制");
				Check(!string.IsNullOrEmpty(CustomBackgroundSystem.LastError), "视频文件缺失时给出 LastError（实际='" + CustomBackgroundSystem.LastError + "'）");

				Section("5/5 设置面板（真实 UI：打开设置 → 数行 → 双向绑定）");
				yield return CheckPanel(settings);
			}
			finally
			{
				if (settings != null && original != null)
				{
					settings.Update(original);
				}
				if (!string.IsNullOrEmpty(testImage))
				{
					try
					{
						File.Delete(testImage);
					}
					catch (Exception)
					{
					}
				}
			}
		}

		/// <summary>包一层：逐帧推进迭代器并兜住异常，最后统一收尾。</summary>
		private IEnumerator RunWrapped()
		{
			IEnumerator inner = Routine();
			while (true)
			{
				object current;
				try
				{
					if (!inner.MoveNext())
					{
						break;
					}
					current = inner.Current;
				}
				catch (Exception ex)
				{
					s_fail++;
					s_report.Append(Tag + " 异常: ").Append(ex).Append('\n');
					Debug.LogException(ex);
					break;
				}
				yield return current;
			}
			Finish();
		}

		// ---------------------------------------------------------------- 1. 公式

	private void RunFormulaChecks()
	{
		Camera camera = CreateProbeCamera();
		if (camera == null)
		{
			Check(false, "能建出探针相机");
			return;
		}
		// 锚点固定传 (0,0,5)，等价于 PartScene.prefab 里 Grid 的世界位置，
		// 这样公式断言不依赖当前是否有活动场景。
		Vector3 anchor = new Vector3(0f, 0f, 5f);
		try
		{
			CustomBackgroundSettings s = new CustomBackgroundSettings();
			s.Enabled = true;
			s.Distance = 4f;
			s.CameraAdaptive = true;
			s.CustomScale = true;
			s.ScaleX = 1f;
			s.ScaleY = 1f;
			s.Opacity = 1f;

			// 媒体宽高比 2 → 尺寸应为 (20 * 2, 20)。
			CustomBackgroundSurface surface = CustomBackgroundSystem.EvaluateSurface(s, 2f, camera, anchor);
			Check(surface.Visible, "开启时 Visible=true");
			Check(Near(surface.Size.x, 40f), "自适应+自定义缩放：宽 = viewH(20) × 宽高比(2) = 40，实际 " + F(surface.Size.x));
			Check(Near(surface.Size.y, 20f), "自适应+自定义缩放：高 = viewH = 20，实际 " + F(surface.Size.y));
			// 背景是世界里的一块板：Z = 网格平面(5) + Distance(4)，永远在网格和部件后面。
			Check(Near(surface.Position.z, 9f), "Z = 网格平面(5) + Distance(4) = 9，实际 " + F(surface.Position.z));
			Check(Near(surface.Opacity, 1f), "透明度 = 1");

			// 媒体宽高比未知(0) → 退回相机宽高比 2（探针相机 aspect 设成 2）。
			surface = CustomBackgroundSystem.EvaluateSurface(s, 0f, camera, anchor);
			Check(Near(surface.Size.x, 40f), "宽高比未知时退回相机宽高比，宽 = 40，实际 " + F(surface.Size.x));

			// 拉伸 2 倍。
			s.ScaleX = 2f;
			surface = CustomBackgroundSystem.EvaluateSurface(s, 2f, camera, anchor);
			Check(Near(surface.Size.x, 80f), "X 拉伸 2 → 宽 80，实际 " + F(surface.Size.x));
			s.ScaleX = 1f;

			// 关闭摄像机自适应 → 尺寸就是 X/Y 拉伸本身。
			s.CameraAdaptive = false;
			s.ScaleX = 3f;
			s.ScaleY = 5f;
			surface = CustomBackgroundSystem.EvaluateSurface(s, 2f, camera, anchor);
			Check(Near(surface.Size.x, 3f) && Near(surface.Size.y, 5f), "关闭自适应：尺寸 = (3,5)，实际 " + F(surface.Size.x) + "x" + F(surface.Size.y));
			s.CameraAdaptive = true;
			s.ScaleX = 1f;
			s.ScaleY = 1f;

			// 绝对坐标 → 位置就是 AbsX/AbsY，Z 仍是「网格平面 + Distance」。
			s.UseAbsolute = true;
			s.AbsoluteX = -7f;
			s.AbsoluteY = 3.5f;
			s.Rotation = 30f;
			surface = CustomBackgroundSystem.EvaluateSurface(s, 2f, camera, anchor);
			Check(Near(surface.Position.x, -7f) && Near(surface.Position.y, 3.5f), "绝对坐标生效，实际 (" + F(surface.Position.x) + "," + F(surface.Position.y) + ")");
			Check(Near(surface.Position.z, 9f), "绝对坐标下 Z 仍 = 网格平面 + Distance = 9，实际 " + F(surface.Position.z));
			Check(Near(surface.Rotation, 30f), "旋转角记录 30");

			// 关闭开关 → 不可见。
			s.Enabled = false;
			surface = CustomBackgroundSystem.EvaluateSurface(s, 2f, camera, anchor);
			Check(!surface.Visible, "关闭开关时 Visible=false");

			// 相机移动不再拖动背景：背景锚在网格平面上，X/Y 只由偏移量决定。
			s.Enabled = true;
			s.UseAbsolute = false;
			s.Rotation = 0f;
			s.OffsetX = 0f;
			s.OffsetY = 0f;
			camera.transform.position = new Vector3(11f, -4f, -10f);
			surface = CustomBackgroundSystem.EvaluateSurface(s, 2f, camera, anchor);
			Check(Near(surface.Position.x, 0f) && Near(surface.Position.y, 0f), "相机移动不影响背景 X/Y（锚在网格平面），实际 (" + F(surface.Position.x) + "," + F(surface.Position.y) + ")");

			// 偏移 0.5 个视野宽度 → 右移 0.5 * viewW = 0.5 * 40 = 20（锚点 x = 0）。
			s.OffsetX = 0.5f;
			surface = CustomBackgroundSystem.EvaluateSurface(s, 2f, camera, anchor);
			Check(Near(surface.Position.x, 20f), "X 偏移 0.5 视野宽 → 右移 20，实际 " + F(surface.Position.x));

			// 旋转只由用户角度决定，不再跟着相机朝向。
			s.OffsetX = 0f;
			s.Rotation = 25f;
			camera.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
			surface = CustomBackgroundSystem.EvaluateSurface(s, 2f, camera, anchor);
			Check(Near(surface.Rotation, 25f), "相机旋转不影响背景旋转角，实际 " + F(surface.Rotation));
			camera.transform.rotation = Quaternion.identity;
		}
		finally
		{
			UnityEngine.Object.DestroyImmediate(camera.gameObject);
		}
	}


		/// <summary>一台参数完全已知的正交相机：size=10（→ viewH=20）、aspect=2。</summary>
		private static Camera CreateProbeCamera()
		{
			GameObject go = new GameObject("[CustomBackgroundQA Camera]")
			{
				hideFlags = HideFlags.HideAndDontSave
			};
			Camera camera = go.AddComponent<Camera>();
			camera.orthographic = true;
			camera.orthographicSize = 10f;
			camera.aspect = 2f;
			camera.enabled = false;
			go.transform.position = new Vector3(0f, 0f, -10f);
			go.transform.rotation = Quaternion.identity;
			return camera;
		}

		// ---------------------------------------------------------------- 2. 持久化

		private void RunSerializationChecks(UserSettings userSettings)
		{
			// 这一段会改活的对象来构造"非默认值"样本，结束后必须还原，
			// 否则后面渲染段会带着这里的 Rotation/Absolute 去画（第一版就是这样误报的）。
			CustomBackgroundSettings snapshot = Bpsim.Serialization.Json.Deserialize<CustomBackgroundSettings>(
				Bpsim.Serialization.Json.Serialize(userSettings.CustomBackground));
			string json = Bpsim.Serialization.Json.Serialize(userSettings);
			Check(!string.IsNullOrEmpty(json), "UserSettings 能序列化成 JSON");
			Check(json != null && json.IndexOf("customBackground", StringComparison.Ordinal) >= 0, "JSON 里出现 customBackground 字段");

			UserSettings reloaded = Bpsim.Serialization.Json.Deserialize<UserSettings>(json);
			Check(reloaded != null, "JSON 能反序列化回 UserSettings");
			if (reloaded == null || reloaded.CustomBackground == null)
			{
				Check(false, "反序列化后 CustomBackground 不为空");
				return;
			}
			CustomBackgroundSettings source = userSettings.CustomBackground;
			CustomBackgroundSettings target = reloaded.CustomBackground;
			source.Path = "C:/qa/背景.png";
			source.Enabled = true;
			source.CameraAdaptive = false;
			source.UseAbsolute = true;
			source.ScaleX = 1.25f;
			source.ScaleY = 0.75f;
			source.OffsetX = -0.5f;
			source.OffsetY = 0.25f;
			source.AbsoluteX = 12.5f;
			source.AbsoluteY = -3.5f;
			source.Rotation = 45f;
			source.Distance = 7f;
			source.Opacity = 0.4f;
			source.Volume = 0.3f;
			source.PlaybackSpeed = 1.5f;
			source.SeekTime = 12.5f;
			source.Loop = false;
			source.Playing = false;
			source.CustomScale = false;

			string updated = Bpsim.Serialization.Json.Serialize(userSettings);
			target = Bpsim.Serialization.Json.Deserialize<UserSettings>(updated)?.CustomBackground;
			if (target == null)
			{
				Check(false, "第二次反序列化的 CustomBackground 不为空");
				return;
			}
			Check(target.Path == source.Path, "Path 往返一致（含非 ASCII，实际 '" + target.Path + "'）");
			Check(target.Enabled, "Enabled 往返一致");
			Check(!target.CameraAdaptive, "CameraAdaptive 往返一致");
			Check(target.UseAbsolute, "UseAbsolute 往返一致");
			Check(!target.CustomScale, "CustomScale 往返一致");
			Check(Near(target.ScaleX, 1.25f) && Near(target.ScaleY, 0.75f), "ScaleX/Y 往返一致");
			Check(Near(target.OffsetX, -0.5f) && Near(target.OffsetY, 0.25f), "OffsetX/Y 往返一致");
			Check(Near(target.AbsoluteX, 12.5f) && Near(target.AbsoluteY, -3.5f), "绝对坐标往返一致");
			Check(Near(target.Rotation, 45f) && Near(target.Distance, 7f), "旋转/Z 往返一致");
			Check(Near(target.Opacity, 0.4f) && Near(target.Volume, 0.3f), "透明度/音量往返一致");
			Check(Near(target.PlaybackSpeed, 1.5f) && Near(target.SeekTime, 12.5f), "播放速度/跳转时间往返一致");
			Check(!target.Loop && !target.Playing, "循环/播放状态往返一致");

			if (snapshot != null)
			{
				userSettings.CustomBackground.Update(snapshot);
			}
		}

		// ---------------------------------------------------------------- 5. 设置面板

		/// <summary>
		/// 走真正的 UI 路径：从 <see cref="Bpsim.UI.UserInterface.ActionBar"/> 点开设置面板
		/// （和玩家点齿轮完全同一条代码），再检查分组是否建出来、控件是否和设置双向连通。
		/// 这样测的是"玩家真能看到、真能改"，而不是我自己拼出来的对象。
		/// </summary>
		private static IEnumerator CheckPanel(CustomBackgroundSettings settings)
		{
			Bpsim.UI.UserInterface ui = Bpsim.UI.UserInterface.Instance;
			Check(ui != null, "UserInterface.Instance 存在（Play 模式 UI 已启动）");
			if (ui == null)
			{
				yield break;
			}

			System.Reflection.MethodInfo open = typeof(Bpsim.UI.UserInterface.ActionBarView).GetMethod(
				"OpenSettings", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
			Check(open != null, "找到 ActionBarView.OpenSettings");
			if (open == null)
			{
				yield break;
			}
			open.Invoke(ui.ActionBar, null);
			yield return null;
			yield return null;

			Bpsim.UI.UserSettingsPanel panel = UnityEngine.Object.FindObjectOfType<Bpsim.UI.UserSettingsPanel>();
			Check(panel != null, "设置面板被实例化到 SubCanvas");
			if (panel == null)
			{
				yield break;
			}
			try
			{
				System.Reflection.FieldInfo contentField = typeof(Bpsim.UI.UserSettingsPanel).GetField(
					"m_content", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				Transform content = (contentField != null) ? ((GameObject)contentField.GetValue(panel))?.transform : null;
				Check(content != null, "拿到面板 Content 容器");
				if (content == null)
				{
					yield break;
				}

				// 背景选项现在收在抽屉里：先展开，再在抽屉容器里找行。
				System.Reflection.FieldInfo drawerField = typeof(Bpsim.UI.UserSettingsPanel).GetField(
					"m_backgroundDrawer", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
				GameObject drawerObject = (drawerField != null) ? drawerField.GetValue(panel) as GameObject : null;
				Check(drawerObject != null, "自定义背景分组有抽屉容器");
				if (drawerObject == null)
				{
					yield break;
				}
				Transform drawer = drawerObject.transform;
				RectTransform drawerRect = (RectTransform)drawer;
				Transform header = FindChild(content, "BackgroundDrawerHeader");
				Check(header != null, "有「自定义背景」抽屉标题行（在 Content 顶层）");
				Transform arrow = (header != null) ? header.Find("Arrow") : null;
				Check(arrow != null, "标题行右侧有展开箭头");

				// 从确定的收起状态起步（跳过动画），不受上次会话记住的状态影响。
				panel.SetBackgroundDrawerOpen(false, immediate: true);
				yield return null;
				Check(!drawerObject.activeSelf, "抽屉默认是收起的");
				Check(Near(drawerRect.rect.height, 0f), "收起时抽屉高度为 0（实际 " + F(drawerRect.rect.height) + "）");
				Check(Near(EulerZ(arrow), 90f), "收起时箭头转向右侧（角度 " + F(EulerZ(arrow)) + "°）");

				// ---- 展开：高度要逐帧长起来，动画播完才是最终高度（像卷轴拉开）
				panel.SetBackgroundDrawerOpen(true);
				Check(panel.BackgroundDrawerAnimating, "展开带缓动动画（不是瞬间弹开）");
				Check(drawerObject.activeSelf, "抽屉可以展开");
				Check(Near(drawerRect.rect.height, 0f), "展开动画从 0 高度开始（实际 " + F(drawerRect.rect.height) + "）");
				float naturalHeight = UnityEngine.UI.LayoutUtility.GetPreferredHeight(drawerRect);
				Check(naturalHeight > 0f, "抽屉的自然高度为正（" + F(naturalHeight) + "）");
				yield return null;
				float midHeight = drawerRect.rect.height;
				Check(midHeight > 0f && midHeight < naturalHeight, "展开到一半时高度介于 0 和自然高度之间（" + F(midHeight) + "）");
				int frames = 0;
				while (panel.BackgroundDrawerAnimating && frames < 120)
				{
					frames++;
					yield return null;
				}
				Check(!panel.BackgroundDrawerAnimating, "抽屉展开动画会自己播完（用了 " + frames + " 帧）");
				Check(Mathf.Abs(drawerRect.rect.height - naturalHeight) <= 1f, "展开完成后高度等于自然高度（实际 " + F(drawerRect.rect.height) + " vs " + F(naturalHeight) + "）");
				Check(Near(EulerZ(arrow), 0f), "展开后箭头朝下（角度 " + F(EulerZ(arrow)) + "°）");

				int rowCount = drawer.childCount;
				Check(rowCount >= 25, "抽屉里建出足够的行（实际 " + rowCount + " 行，含 19 个参数行 + 3 个按钮 + 进度/滑条/状态）");

				// ---- 抽屉里的选项比标题行小一号：行高 90 → 72，整行等比缩到 0.8（标题大、内容行细）
				float headerHeight = ((RectTransform)header).rect.height;
				Check(Mathf.Abs(headerHeight - 90f) <= 0.5f, "标题行保持常规高度（实际 " + F(headerHeight) + "）");
				float headerWidth = ((RectTransform)header).rect.width;
				int compactRows = 0;
				float tallestRow = 0f;
				float widestRow = 0f;
				for (int i = 0; i < drawer.childCount; i++)
				{
					RectTransform rowRect = drawer.GetChild(i) as RectTransform;
					if (rowRect == null)
					{
						continue;
					}
					compactRows++;
					tallestRow = Mathf.Max(tallestRow, rowRect.rect.height);
					widestRow = Mathf.Max(widestRow, rowRect.rect.width * rowRect.localScale.x);
				}
				Check(compactRows == rowCount, "抽屉里的行都按小号样式建出来了（" + compactRows + " 行）");
				Check(tallestRow <= 72.5f, "抽屉里的行高压到 72（最高 " + F(tallestRow) + "，常规行是 90）");
				Check(widestRow <= headerWidth * 0.81f, "抽屉里的行视觉宽度也比标题行窄一档（" + F(widestRow) + " vs " + F(headerWidth) + "）");
				Transform firstRow = drawer.GetChild(0);
				Check(Mathf.Abs(firstRow.localScale.x - 0.8f) <= 0.001f, "抽屉里的行整体缩到 0.8（实际 " + F(firstRow.localScale.x) + "）");
				Check(naturalHeight <= 2500f, "小号行让抽屉自然高度变矮（" + F(naturalHeight) + " < 2750）");

				string[] labels = new string[]
				{
				"开启背景", "背景路径", "视频音量", "透明度", "自定义缩放", "摄像机自适应",
				"X 轴拉伸", "Y 轴拉伸", "X 轴偏移", "Y 轴偏移", "启用绝对坐标", "绝对坐标 X", "绝对坐标 Y",
				"旋转角度（度）", "Z 轴距离", "播放循环", "播放速度", "播放/暂停", "跳转时间（秒）",
				"播放进度", "拖动进度", "背景状态"
				};
				int missing = 0;
				StringBuilder missingList = new StringBuilder();
				for (int i = 0; i < labels.Length; i++)
				{
				if (FindRow(drawer, labels[i]) == null)
				{
					missing++;
					missingList.Append(labels[i]).Append(' ');
				}
				}
				Check(missing == 0, "22 个背景相关的行全部在抽屉里" + (missing == 0 ? string.Empty : "（缺: " + missingList + "）"));

				Check(FindChild(drawer, "Background上传背景") != null, "「上传背景」按钮存在");
				Check(FindChild(drawer, "Background清除背景") != null, "「清除背景」按钮存在");
				Check(FindChild(drawer, "Background快速填充") != null, "「快速填充」按钮存在");

				Transform progressRow = FindRow(drawer, "拖动进度");
				Check(progressRow != null && progressRow.Find("Value").GetComponentInChildren<UnityEngine.UI.Slider>(true) != null, "进度行是真正的 Slider（可拖动跳转）");

				// ---- 绑定方向 1：设置 → 控件
				float beforeDistance = settings.Distance;
				settings.Distance = 6.5f;
				yield return null;
				Transform distanceRow = FindRow(drawer, "Z 轴距离");
				UnityEngine.UI.InputField distanceField = (distanceRow != null) ? distanceRow.Find("Value").GetComponent<UnityEngine.UI.InputField>() : null;
				Check(distanceField != null, "Z 轴距离行有 InputField");
				bool pushed = false;
				if (distanceField != null)
				{
				float shown;
				pushed = float.TryParse(distanceField.text, NumberStyles.Float, CultureInfo.InvariantCulture, out shown) && Mathf.Abs(shown - 6.5f) < 0.001f;
				}
				Check(pushed, "改设置后输入框自动刷新（设置 → 控件）");

				// ---- 绑定方向 2：控件 → 设置
				if (distanceField != null)
				{
				distanceField.onEndEdit.Invoke("7.25");
				}
				Check(Near(settings.Distance, 7.25f), "输入框回车后写回设置（控件 → 设置，实际 " + F(settings.Distance) + "）");
				settings.Distance = beforeDistance;

				// ---- Toggle 类控件：点开「开启背景」应该真的改设置
				Transform enabledRow = FindRow(drawer, "开启背景");
				Bpsim.UI.ToggleSwitch enabledToggle = (enabledRow != null) ? enabledRow.Find("Value").GetComponent<Bpsim.UI.ToggleSwitch>() : null;
				Check(enabledToggle != null, "开启背景行是 ToggleSwitch");
				if (enabledToggle != null)
				{
					bool beforeEnabled = settings.Enabled;
					enabledToggle.IsOn = !beforeEnabled;
					Check(settings.Enabled == !beforeEnabled, "拨动开关写回设置（控件 → 设置）");
					enabledToggle.SetIsOnWithoutNotify(beforeEnabled);
					settings.Enabled = beforeEnabled;
				}

				// ---- 收起抽屉：高度收回 0，动画播完容器才隐藏，行和绑定都还在
				float heightBeforeClose = drawerRect.rect.height;
				panel.SetBackgroundDrawerOpen(false);
				Check(panel.BackgroundDrawerAnimating, "收起带缓动动画（不是瞬间消失）");
				yield return null;
				Check(drawerRect.rect.height < heightBeforeClose, "收起过程中高度在变小（" + F(heightBeforeClose) + " → " + F(drawerRect.rect.height) + "）");
				frames = 0;
				while (panel.BackgroundDrawerAnimating && frames < 120)
				{
					frames++;
					yield return null;
				}
				Check(!panel.BackgroundDrawerAnimating, "抽屉收起动画会自己播完（用了 " + frames + " 帧）");
				Check(!drawerObject.activeSelf, "抽屉可以收起");
				Check(Near(drawerRect.rect.height, 0f), "收起后高度归零（实际 " + F(drawerRect.rect.height) + "）");
				Check(Near(EulerZ(arrow), 90f), "收起后箭头转向右侧（角度 " + F(EulerZ(arrow)) + "°）");
				Check(FindRow(drawer, "Z 轴距离") != null, "收起后行仍然存在（只是隐藏）");

				// 再展开一次：动画从 0 起播，用来确认状态机可以反复开关。
				panel.SetBackgroundDrawerOpen(true);
				Check(panel.BackgroundDrawerAnimating, "再次展开仍有动画");
				frames = 0;
				while (panel.BackgroundDrawerAnimating && frames < 120)
				{
					frames++;
					yield return null;
				}
				Check(drawerObject.activeSelf && !panel.BackgroundDrawerAnimating, "抽屉可以重新展开");
			}
			finally
			{
				UnityEngine.Object.Destroy(panel.gameObject);
			}
		}

		private static Transform FindChild(Transform root, string name)
		{
			for (int i = 0; i < root.childCount; i++)
			{
				Transform child = root.GetChild(i);
				if (child.name == name)
				{
					return child;
				}
			}
			return null;
		}

		/// <summary>按行首的 Name 文本找行（行本身没有有意义的名字）。</summary>
		private static Transform FindRow(Transform content, string label)
		{
			for (int i = 0; i < content.childCount; i++)
			{
				Transform row = content.GetChild(i);
				Transform name = row.Find("Name");
				if (name == null)
				{
					continue;
				}
				UnityEngine.UI.Text text = name.GetComponent<UnityEngine.UI.Text>();
				if (text != null && text.text == label)
				{
					return row;
				}
			}
			return null;
		}

		// ---------------------------------------------------------------- 工具

		/// <summary>生成一张 64x32 的洋红 PNG（宽高比 2，方便断言尺寸）。</summary>
		private static string CreateTestImage()
		{
			Directory.CreateDirectory(CustomBackgroundMediaLoader.StorageDirectory);
			Texture2D texture = new Texture2D(64, 32, TextureFormat.RGBA32, mipChain: false);
			Color[] pixels = new Color[64 * 32];
			for (int i = 0; i < pixels.Length; i++)
			{
				pixels[i] = new Color(1f, 0f, 1f, 1f);
			}
			texture.SetPixels(pixels);
			texture.Apply();
			byte[] png = texture.EncodeToPNG();
			UnityEngine.Object.Destroy(texture);
			string path = Path.Combine(CustomBackgroundMediaLoader.StorageDirectory, "qa-background.png");
			File.WriteAllBytes(path, png);
			return path;
		}

		private static Camera ResolveCamera()
		{
			if (SceneCamera.Instance != null && SceneCamera.Instance.Camera != null)
			{
				return SceneCamera.Instance.Camera;
			}
			return Camera.main;
		}

		/// <summary>读一个物体的 Z 轴欧拉角，归一到 [-180, 180]（读回值可能是 270 这种写法）。</summary>
		private static float EulerZ(Transform target)
		{
			if (target == null)
			{
				return 0f;
			}
			return Mathf.Repeat(target.localEulerAngles.z + 180f, 360f) - 180f;
		}

		private static bool Near(float a, float b)
		{
			return Mathf.Abs(a - b) <= 0.001f;
		}

		private static string F(float value)
		{
			return value.ToString("0.####", CultureInfo.InvariantCulture);
		}

		private static void Section(string title)
		{
			s_report.Append('\n').Append("== ").Append(title).Append('\n');
			Debug.Log(Tag + " " + title);
		}

		private static void Check(bool condition, string what)
		{
			if (condition)
			{
				s_pass++;
				s_report.Append("  PASS  ").Append(what).Append('\n');
			}
			else
			{
				s_fail++;
				s_report.Append("  FAIL  ").Append(what).Append('\n');
				Debug.LogError(Tag + " FAIL " + what);
			}
		}

		private void Finish()
		{
			s_running = false;
			s_status = (s_fail == 0) ? "done ok" : "done FAILED";
			s_report.Append('\n').Append(Tag + " 结果: pass=").Append(s_pass).Append(" fail=").Append(s_fail).Append('\n');
			try
			{
				string directory = Path.GetDirectoryName(LogPath);
				if (!string.IsNullOrEmpty(directory))
				{
					Directory.CreateDirectory(directory);
				}
				File.WriteAllText(LogPath, s_report.ToString(), new UTF8Encoding(false));
			}
			catch (Exception ex)
			{
				Debug.LogWarning(Tag + " 写日志失败: " + ex.Message);
			}
			Debug.Log(Tag + " " + s_status + " pass=" + s_pass + " fail=" + s_fail + " → " + LogPath);
		}
	}
}
