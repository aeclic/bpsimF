using System;
using System.IO;
using Bpsim.Parts;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace Bpsim.Background
{
	/// <summary>
	/// 自定义背景的渲染系统（ECS 版 BPSS CustomBackgroundScale + AddonBackground）。
	///
	/// 数据流：
	/// <code>
	/// UserSettings.CustomBackground   （设置面板写，唯一的数据来源）
	///        │  每帧读参数
	///        ▼
	/// CustomBackgroundSurface         （系统算出的世界空间 TRS，单例实体上）
	///        │  Graphics.RenderMesh
	///        ▼
	/// 一张 quad 画在主相机前面
	/// </code>
	///
	/// 之所以不挂 MonoBehaviour：背景的姿态每帧都要重算（相机在动），
	/// 用 ECS 就是"参数是组件、算姿态是系统"，不新建 GameObject，也不去碰场景里的东西。
	///
	/// 放在 <see cref="PresentationSystemGroup"/> 里是为了保证在 SceneCamera.LateUpdate
	/// 之后才读相机状态（否则背景会比镜头慢一帧，拖动时会抖）。
	/// </summary>
	[UpdateInGroup(typeof(PresentationSystemGroup))]
	internal partial class CustomBackgroundSystem : SystemBase
	{
		private const string MaterialShaderName = "Bpsim/Unlit_Texture";

		private Entity m_entity;

		private Mesh m_mesh;

		private Material m_material;

		/// <summary>当前活着的系统（设置面板用它与渲染侧通信）。</summary>
		public static CustomBackgroundSystem Instance { get; private set; }

		/// <summary>上一帧是否真的画了背景（QA / 面板判断用）。</summary>
		public static int LastDrawn { get; private set; }

		public static int LastDrawnFrame { get; private set; } = -1;

		/// <summary>最近一次加载失败的原因（面板可以显示给用户）。</summary>
		public static string LastError { get; private set; } = string.Empty;

		protected override void OnCreate()
		{
			base.OnCreate();
			Instance = this;
			LastDrawn = 0;
			LastError = string.Empty;
			m_entity = EntityManager.CreateEntity(typeof(CustomBackgroundTag), typeof(CustomBackgroundSurface), typeof(CustomBackgroundMedia));
			EntityManager.SetName(m_entity, "[BpsimCustomBackground]");
			EntityManager.SetComponentData(m_entity, new CustomBackgroundSurface
			{
				Visible = false,
				Opacity = 0f,
				Matrix = float4x4.identity
			});
			EntityManager.SetComponentData(m_entity, new CustomBackgroundMedia());
			m_mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
			Shader shader = Shader.Find(MaterialShaderName);
			if (shader == null)
			{
				Debug.LogError("[BpsimBackground] 找不到着色器 " + MaterialShaderName + "，自定义背景无法渲染。");
				return;
			}
			m_material = new Material(shader)
			{
				hideFlags = HideFlags.DontSave,
				name = "[BpsimCustomBackground]"
			};
			m_material.mainTexture = Texture2D.whiteTexture;
			m_material.SetColor("_Color", Color.white);
		}

		protected override void OnDestroy()
		{
			if (m_entity != Entity.Null && EntityManager.Exists(m_entity))
			{
				CustomBackgroundMedia media = EntityManager.GetComponentObject<CustomBackgroundMedia>(m_entity);
				CustomBackgroundMediaLoader.Release(media);
			}
			if (m_material != null)
			{
				UnityEngine.Object.Destroy(m_material);
				m_material = null;
			}
			if (Instance == this)
			{
				Instance = null;
			}
			LastDrawn = 0;
			base.OnDestroy();
		}

		protected override void OnUpdate()
		{
			LastDrawn = 0;
			if (m_mesh == null || m_material == null)
			{
				return;
			}
			if (m_entity == Entity.Null || !EntityManager.Exists(m_entity))
			{
				return;
			}
			UserSettings userSettings = UserSettings.Instance;
			CustomBackgroundSettings settings = (userSettings != null) ? userSettings.CustomBackground : null;
			if (settings == null)
			{
				return;
			}
			CustomBackgroundMedia media = EntityManager.GetComponentObject<CustomBackgroundMedia>(m_entity);
			if (media == null)
			{
				return;
			}
			SyncMedia(settings, media);
			Camera camera = ResolveCamera();
			CustomBackgroundSurface surface = Evaluate(settings, media, camera);
			EntityManager.SetComponentData(m_entity, surface);
			SyncVideo(settings, media);
			Draw(surface, media, camera);
		}

		/// <summary>路径（或文件内容）变了才重新加载资源。</summary>
		private void SyncMedia(CustomBackgroundSettings settings, CustomBackgroundMedia media)
		{
			string path = settings.Path ?? string.Empty;
			string stamp = string.Empty;
			if (!string.IsNullOrEmpty(path))
			{
				if (File.Exists(path))
				{
					try
					{
						stamp = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
					}
					catch (Exception)
					{
						stamp = path;
					}
				}
				else
				{
					stamp = path + "|missing";
				}
			}
			if (stamp == media.SourceStamp)
			{
				return;
			}
			media.SourceStamp = stamp;
			CustomBackgroundMediaLoader.Release(media);
			if (string.IsNullOrEmpty(path))
			{
				LastError = string.Empty;
				return;
			}
			if (!File.Exists(path))
			{
				LastError = "背景文件不存在: " + path;
				Debug.LogWarning("[BpsimBackground] " + LastError);
				return;
			}
			try
			{
				if (CustomBackgroundMediaLoader.IsVideoFile(path))
				{
					CustomBackgroundMediaLoader.LoadVideo(path, media);
				}
				else
				{
					CustomBackgroundMediaLoader.LoadTexture(path, media);
				}
				LastError = string.Empty;
			}
			catch (Exception ex)
			{
				LastError = ex.Message;
				CustomBackgroundMediaLoader.Release(media);
				Debug.LogWarning("[BpsimBackground] 加载背景失败: " + ex.Message);
			}
		}

		private static Camera ResolveCamera()
		{
			SceneCamera sceneCamera = SceneCamera.Instance;
			if (sceneCamera != null && sceneCamera.Camera != null)
			{
				return sceneCamera.Camera;
			}
			return Camera.main;
		}

	/// <summary>没有活动场景（主菜单 / QA）时用的兜底网格平面 z，等于 PartScene.prefab 里 Grid 的 localPosition.z。</summary>
	private const float DefaultGridPlaneZ = 5f;

	/// <summary>
	/// 背景锚点：当前活动场景网格平面的世界坐标。
	/// 网格 SpriteRenderer 按 50 单位吸附跟随相机（见 <see cref="PartScene.LateUpdate" />），
	/// 把背景锚在同一个点上，背景就和网格一起走 —— 也就是玩家眼里的「贴在网格上」，
	/// 而不是像以前那样贴着镜头飘。
	/// </summary>
	internal static Vector3 ResolveGridAnchor()
	{
		PartManager manager = PartManager.Instance;
		if (manager != null)
		{
			PartScene scene = manager.ActiveScene;
			if (scene != null)
			{
				Vector2 anchor = scene.GridAnchor;
				return new Vector3(anchor.x, anchor.y, scene.GridPlaneZ);
			}
		}
		return new Vector3(0f, 0f, DefaultGridPlaneZ);
	}

	/// <summary>
	/// 把设置折算成世界空间的 TRS。
	/// </summary>
	/// <remarks>
	/// 历史包袱：这段公式原本逐条照搬 BPSS 的 <c>CustomBackgroundScale.LateUpdate</c>，
	/// 也就是把背景当成「贴在相机前的屏幕壁纸」（X/Y 取相机位置、Z 取 Distance、朝向取相机朝向）。
	/// 那个语义在 bpsim 里是错的：相机在 z = -10 朝 +Z 看，部件在 z ≈ 0、网格在 z = +5，
	/// 于是 z = Distance（默认 4）的背景落在部件<b>前面</b>，看起来像浮在部件上方的一张纸，
	/// 而且跟着镜头平移、旋转。现在的语义是「世界里的一块背景板」：
	/// <list type="bullet">
	/// <item>Z = 网格平面 z + Distance，永远在网格和部件后面。</item>
	/// <item>X/Y 锚在网格平面上（不再取相机位置），偏移量以视野为单位。</item>
	/// <item>朝向只保留用户的绕 Z 旋转角。</item>
	/// </list>
	/// 视野高度 viewH = 2*orthographicSize（正交）或 2*Distance*tan(fov/2)，尺寸规则保持不变。
	/// </remarks>
	private static CustomBackgroundSurface Evaluate(CustomBackgroundSettings settings, CustomBackgroundMedia media, Camera camera)
	{
		float mediaAspect = (media != null) ? media.Aspect : 0f;
		return EvaluateSurface(settings, mediaAspect, camera);
	}

	/// <summary>
	/// 纯函数版本（相机 + 参数 → 姿态），不依赖 ECS / 托管媒体，
	/// 因此 QA 可以在没有默认世界的情况下直接校验公式。锚点取当前活动场景的网格平面。
	/// </summary>
	internal static CustomBackgroundSurface EvaluateSurface(CustomBackgroundSettings settings, float mediaAspect, Camera camera)
	{
		return EvaluateSurface(settings, mediaAspect, camera, ResolveGridAnchor());
	}

	/// <summary>
	/// 纯函数版本的重载：锚点由调用方给出（QA 用它做确定性断言，不依赖当前场景）。
	/// </summary>
	internal static CustomBackgroundSurface EvaluateSurface(CustomBackgroundSettings settings, float mediaAspect, Camera camera, Vector3 gridAnchor)
	{
		CustomBackgroundSurface surface = new CustomBackgroundSurface
		{
			Visible = settings.Enabled,
			Opacity = Mathf.Clamp01(settings.Opacity),
			Matrix = float4x4.identity
		};
		if (!settings.Enabled)
		{
			return surface;
		}
		float distance = Mathf.Max(0.1f, settings.Distance);
		float cameraAspect = (camera != null) ? camera.aspect : 1f;
		float viewHeight;
		if (camera != null && camera.orthographic)
		{
			viewHeight = 2f * camera.orthographicSize;
		}
		else
		{
			float fieldOfView = (camera != null) ? camera.fieldOfView : 60f;
			viewHeight = 2f * distance * Mathf.Tan(fieldOfView * Mathf.Deg2Rad * 0.5f);
		}
		float viewWidth = viewHeight * cameraAspect;
		// 背景板永远落在网格平面之后：相机在 -Z 朝 +Z 看，z 越大越远，
		// 所以「网格平面 z + 距离」既保证它不被网格/部件盖住，也不会跑到部件前面。
		float planeZ = gridAnchor.z + distance;
		Vector3 position;
		// 朝向只由用户旋转角决定，不再乘相机旋转 —— 背景是世界里的一块板，不是贴在镜头前的壁纸。
		Quaternion rotation = Quaternion.Euler(0f, 0f, settings.Rotation);
		if (settings.UseAbsolute)
		{
			// 绝对坐标：X/Y 钉死在世界坐标上，Z 仍由网格平面 + 距离决定，
			// 这样无论绝对坐标填什么，背景都还在部件后面。
			position = new Vector3(settings.AbsoluteX, settings.AbsoluteY, planeZ);
		}
		else
		{
			// 相对坐标：锚在网格平面中心，偏移量以视野为单位（1 = 一个视野宽 / 高）。
			position = new Vector3(
				gridAnchor.x + settings.OffsetX * viewWidth,
				gridAnchor.y + settings.OffsetY * viewHeight,
				planeZ);
		}
		float effectiveAspect = (mediaAspect > 0f) ? mediaAspect : cameraAspect;
		Vector2 size;
		if (settings.CameraAdaptive)
		{
			if (settings.CustomScale)
			{
				// 自定义缩放：视野高度 × 媒体宽高比 × X/Y 拉伸，随相机缩放同步变化。
				size = new Vector2(viewHeight * effectiveAspect * settings.ScaleX, viewHeight * settings.ScaleY);
			}
			else
			{
				// 关闭自定义缩放：自动撑满视野（等价于 X/Y 拉伸 = 1 且宽高比取屏幕）。
				size = new Vector2(viewHeight * cameraAspect, viewHeight);
			}
		}
		else
		{
			// 固定尺寸：X/Y 拉伸就是世界尺寸。
			size = new Vector2(settings.ScaleX, settings.ScaleY);
		}
		if (size.x <= 0f || size.y <= 0f || !float.IsFinite(size.x) || !float.IsFinite(size.y))
		{
			surface.Visible = false;
			return surface;
		}
		surface.Position = new float3(position.x, position.y, position.z);
		surface.Size = new float2(size.x, size.y);
		surface.Rotation = settings.Rotation;
		surface.Distance = distance;
		surface.Matrix = float4x4.TRS(surface.Position, rotation, new float3(size.x, size.y, 1f));
		return surface;
	}

		/// <summary>把播放参数同步到 VideoPlayer（只写有变化的值，避免每帧触发内部重建）。</summary>
		private void SyncVideo(CustomBackgroundSettings settings, CustomBackgroundMedia media)
		{
			VideoPlayer player = media.Video;
			if (player == null || !player.isPrepared)
			{
				return;
			}
			if (media.AppliedLoop != settings.Loop)
			{
				media.AppliedLoop = settings.Loop;
				player.isLooping = settings.Loop;
			}
			if (!(Mathf.Abs(media.AppliedSpeed - settings.PlaybackSpeed) <= 0.0001f))
			{
				media.AppliedSpeed = settings.PlaybackSpeed;
				player.playbackSpeed = settings.PlaybackSpeed;
			}
			if (!(Mathf.Abs(media.AppliedVolume - settings.Volume) <= 0.0001f))
			{
				media.AppliedVolume = settings.Volume;
				player.SetDirectAudioVolume(0, Mathf.Clamp01(settings.Volume));
			}
			if (!(Mathf.Abs(media.AppliedSeek - settings.SeekTime) <= 0.0001f))
			{
				media.AppliedSeek = settings.SeekTime;
				CustomBackgroundMediaLoader.Seek(media, settings.SeekTime);
			}
			if (media.AppliedPlaying != settings.Playing)
			{
				media.AppliedPlaying = settings.Playing;
				if (settings.Playing)
				{
					player.Play();
				}
				else
				{
					player.Pause();
				}
			}
		}

		private void Draw(CustomBackgroundSurface surface, CustomBackgroundMedia media, Camera camera)
		{
			if (!surface.Visible)
			{
				return;
			}
			Texture texture = CustomBackgroundMediaLoader.CurrentTexture(media);
			if (texture == null)
			{
				return;
			}
			m_material.mainTexture = texture;
			m_material.SetColor("_Color", new Color(1f, 1f, 1f, surface.Opacity));
			Bounds bounds = new Bounds(
				new Vector3(surface.Position.x, surface.Position.y, surface.Position.z),
				new Vector3(Mathf.Max(0.01f, Mathf.Abs(surface.Size.x)), Mathf.Max(0.01f, Mathf.Abs(surface.Size.y)), 0.01f));
			RenderParams renderParams = new RenderParams(m_material)
			{
				worldBounds = bounds,
				shadowCastingMode = ShadowCastingMode.Off,
				receiveShadows = false,
				layer = 0,
				camera = camera
			};
			Graphics.RenderMesh(in renderParams, m_mesh, 0, surface.Matrix);
			LastDrawn = 1;
			LastDrawnFrame = UnityEngine.Time.frameCount;
		}

		/// <summary>「快速填充」用：回读背景当前真实的渲染位置与尺寸。</summary>
		public static bool TryGetSurface(out Vector3 position, out Vector2 size, out float rotation)
		{
			position = Vector3.zero;
			size = Vector2.one;
			rotation = 0f;
			CustomBackgroundSystem system = Instance;
			if (system == null || system.m_entity == Entity.Null || !system.EntityManager.Exists(system.m_entity))
			{
				return false;
			}
			CustomBackgroundSurface surface = system.EntityManager.GetComponentData<CustomBackgroundSurface>(system.m_entity);
			if (!surface.Visible)
			{
				return false;
			}
			position = new Vector3(surface.Position.x, surface.Position.y, surface.Position.z);
			size = new Vector2(surface.Size.x, surface.Size.y);
			rotation = surface.Rotation;
			return true;
		}

		/// <summary>进度显示用：视频当前时间 / 总时长。</summary>
		public static bool TryGetPlayback(out float time, out float duration)
		{
			time = 0f;
			duration = 0f;
			CustomBackgroundSystem system = Instance;
			if (system == null || system.m_entity == Entity.Null || !system.EntityManager.Exists(system.m_entity))
			{
				return false;
			}
			CustomBackgroundMedia media = system.EntityManager.GetComponentObject<CustomBackgroundMedia>(system.m_entity);
			return CustomBackgroundMediaLoader.TryGetPlayback(media, out time, out duration);
		}
	}
}
