using Bpsim.Parts;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	/// <summary>
	/// 建造界面左侧「光标 / 增加 / 减少 / 框选」四个模式按钮右边的第 5 颗按钮：
	/// 一枚简化的放大镜。点一下把镜头平滑拉远到能装下整台载具的概览尺寸，再点一下回到原来的缩放。
	///
	/// 行为对齐 BPSS 901-17 建造界面的 PreviewButtonRegular：那里点下去 GameMode 发
	/// UIEvent.Type.Preview，IngameCamera 从 CameraState.Building 切到 CameraState.Preview，
	/// 并用 EnablePreviewMode 把正交尺寸指数式推向远景。这里不引入新的相机状态机，
	/// 只借用 SceneCamera 已有的那条缩放弹簧（<see cref="SceneCamera.ZoomTo"/>）。
	///
	/// 按钮是运行时克隆 ViewButton 造出来的，所以不必改 UserInterface.prefab：
	/// 克隆体只做三件事 —— 断开 ToggleGroup 归属（放大镜是独立开关，不能算进「四选一」的
	/// 放置模式，否则会污染 PartOperator 靠 ActiveToggles 做的模式判定）、去掉双击组件、
	/// 换掉图标贴图。底板 / 高亮 / 间距仍然由 HorizontalLayoutGroup 统一排。
	/// </summary>
	internal sealed class BuildMagnifier : MonoBehaviour
	{
		private const string ButtonName = "MagnifyButton";

		private const string TemplateName = "ViewButton";

		private const string IconName = "Icon_Magnify";

		/// <summary>概览时在包围盒外留的余量（LevelManager.FrameCamera 用 1.1，这里稍宽一点）。</summary>
		private const float BoundsPadding = 1.15f;

		/// <summary>场景里还没有部件时的退化拉远倍数。</summary>
		private const float EmptyZoomOut = 2.5f;

		private UserInterface m_root;

		private Toggle m_toggle;

		private float m_savedSize;

		private Vector2 m_savedPoint;

		private bool m_preview;

		private string m_diagnostics = string.Empty;

		/// <summary>概览状态是否打开（QA 探针会读它）。</summary>
		internal bool IsPreviewing => m_preview;

		/// <summary>最近一次开关时算出来的尺寸/包围盒，供 QA 探针和日志定位问题。</summary>
		internal string Diagnostics => m_diagnostics;

		/// <summary>左侧部件栏占屏宽的比例（0 = 没检测到）。概览会把载具中心往左推半条栏宽，QA 用这个值算期望中心。</summary>
		internal float OccludedLeftFraction => VisibleLeftFraction();

		/// <summary>QA 用：走 <see cref="P:UnityEngine.UI.Toggle.isOn" />，也就是真实的点击路径。</summary>
		internal void SetPreview(bool on)
		{
			// 说明：Toggle 的 isOn 没变化时不会回调 onValueChanged，而「按钮亮着但概览已经收起」
			// （模拟态复位、换场景复位的 SetIsOnWithoutNotify）会造成两者不同步。所以探针这条路径
			// 在 isOn 已经是目标值时直接跑状态机，保证镜头一定落到请求的状态。
			if (m_toggle != null && m_toggle.isOn != on)
			{
				m_toggle.isOn = on;
				return;
			}
			if (m_preview != on)
			{
				OnToggleChanged(on);
			}
		}

		internal void Initialize(UserInterface root)
		{
			m_root = root;
			if (!CreateButton())
			{
				return;
			}
			PartManager partManager = PartManager.Instance;
			if (partManager != null)
			{
				partManager.SceneSelected += OnSceneSelected;
			}
			else
			{
				Debug.LogWarning("[BuildMagnifier] PartManager 尚未就绪，切换场景时不会复位概览缩放。");
			}
		}

		private bool CreateButton()
		{
			PartOperator partOperator = m_root.PartOperator;
			if (partOperator == null)
			{
				Debug.LogWarning("[BuildMagnifier] 找不到 PartOperator，放大镜按钮不创建。");
				return false;
			}
			Transform bar = partOperator.transform;
			Transform template = bar.Find(TemplateName);
			if (template == null)
			{
				Debug.LogWarning("[BuildMagnifier] 侧边栏里找不到模板按钮 " + TemplateName + "，放大镜按钮不创建。");
				return false;
			}
			GameObject button = Object.Instantiate(template.gameObject, bar, false);
			button.name = ButtonName;
			// HorizontalLayoutGroup 按 sibling 顺序排，所以放到最后就是「框选」的右边。
			button.transform.SetAsLastSibling();
			m_toggle = button.GetComponent<Toggle>();
			if (m_toggle == null)
			{
				Debug.LogWarning("[BuildMagnifier] 模板按钮上没有 Toggle，放大镜按钮不创建。");
				Object.Destroy(button);
				return false;
			}
			// 独立开关：脱离 ToggleGroup 之后既不会被四个模式按钮关掉，也不会抢它们的模式。
			m_toggle.group = null;
			DoubleClickableButton doubleClick = button.GetComponent<DoubleClickableButton>();
			if (doubleClick != null)
			{
				Object.Destroy(doubleClick);
			}
			m_toggle.SetIsOnWithoutNotify(false);
			m_toggle.onValueChanged.AddListener(OnToggleChanged);
			ApplyIcon(button.transform);
			return true;
		}

		private static void ApplyIcon(Transform button)
		{
			Sprite sprite = Resources.Load<Sprite>("BuildToolbar/" + IconName);
			if (sprite == null)
			{
				Debug.LogWarning("[BuildMagnifier] 缺少图标资源 Resources/BuildToolbar/" + IconName);
				return;
			}
			Transform target = button.Find("Image");
			if (target == null)
			{
				target = button;
			}
			RawImage raw = target.GetComponent<RawImage>();
			if (raw != null)
			{
				// 侧边栏四个模式按钮的图标是 RawImage（没有 Image 的 m_Sprite），
				// 所以只能换 texture + uvRect，不能直接塞 Sprite。
				Texture texture = sprite.texture;
				raw.texture = texture;
				Rect rect = sprite.textureRect;
				raw.uvRect = new Rect(rect.x / texture.width, rect.y / texture.height, rect.width / texture.width, rect.height / texture.height);
				raw.color = Color.white;
				return;
			}
			Image image = target.GetComponent<Image>();
			if (image != null)
			{
				image.sprite = sprite;
				image.type = Image.Type.Simple;
				image.preserveAspect = true;
				image.color = Color.white;
			}
		}

		private void OnToggleChanged(bool preview)
		{
			m_preview = preview;
			SceneCamera camera = SceneCamera.Instance;
			if (camera == null)
			{
				return;
			}
			if (preview)
			{
				m_savedSize = camera.TargetSize;
				m_savedPoint = camera.TargetPoint;
				bool fitted = ComputeOverview(camera, out float overview, out Vector2 center);
				m_diagnostics = string.Format("on saved={0:0.###} overview={1:0.###} left={2:0.###} fitted={3} center={4} bounds={5}", m_savedSize, overview, VisibleLeftFraction(), fitted, center, BoundsDiagnostics());
				if (fitted)
				{
					camera.MoveTo(center);
				}
				camera.ZoomTo(overview);
			}
			else
			{
				m_diagnostics = string.Format("off restore={0:0.###} point={1}", m_savedSize, m_savedPoint);
				camera.MoveTo(m_savedPoint);
				camera.ZoomTo(m_savedSize);
			}
			Debug.Log("[BuildMagnifier] " + m_diagnostics);
		}

		/// <summary>场景部件包围盒的文字形式；取不到时说明是哪一个前置条件没满足。</summary>
		private static string BoundsDiagnostics()
		{
			PartManager partManager = PartManager.Instance;
			if (partManager == null)
			{
				return "no manager";
			}
			if (!partManager.HasActiveScene())
			{
				return "no active scene";
			}
			PartScene scene = partManager.ActiveScene;
			if (scene == null)
			{
				return "scene null";
			}
			if (!scene.Unmanaged.TryGetPartsBounds(out float2 min, out float2 max))
			{
				return "empty";
			}
			return "parts=" + scene.Unmanaged.Parts.Length + " " + min.ToString() + ".." + max.ToString();
		}

		/// <summary>
		/// 左侧部件栏遮盖的屏幕宽度占比（0..0.7）。找不到时返回 0，退化成「整屏都看得见」。
		/// 模式按钮行比整块部件栏窄，所以不能只看按钮行：还要把部件选择面板一起算进来。
		/// </summary>
		private float VisibleLeftFraction()
		{
			PartOperator partOperator = m_root.PartOperator;
			if (partOperator == null || Screen.width <= 0)
			{
				return 0f;
			}
			float widest = 0f;
			Transform cursor = partOperator.transform;
			for (int depth = 0; depth < 4 && cursor != null; depth++)
			{
				ConsiderOccluder(cursor, ref widest);
				cursor = cursor.parent;
			}
			PartSelector partSelector = m_root.PartSelector;
			if (partSelector != null)
			{
				ConsiderOccluder(partSelector.transform, ref widest);
			}
			return Mathf.Clamp(widest, 0f, 0.7f);
		}

		/// <summary>把 <paramref name="candidate" /> 的右边缘折算成占屏宽度，取较大者。铺满大半屏的容器不算（那是整屏而非侧栏）。</summary>
		private static void ConsiderOccluder(Transform candidate, ref float widest)
		{
			RectTransform rect = candidate as RectTransform;
			if (rect == null)
			{
				return;
			}
			Canvas canvas = rect.GetComponentInParent<Canvas>();
			Camera canvasCamera = ((canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay) ? canvas.worldCamera : null);
			Vector3[] corners = new Vector3[4];
			rect.GetWorldCorners(corners);
			float right = float.NegativeInfinity;
			for (int i = 0; i < corners.Length; i++)
			{
				right = Mathf.Max(right, RectTransformUtility.WorldToScreenPoint(canvasCamera, corners[i]).x);
			}
			float fraction = right / (float)Screen.width;
			if (fraction > widest && fraction < 0.7f)
			{
				widest = fraction;
			}
		}

		/// <summary>
		/// 装得下当前场景所有部件的正交尺寸 + 包围盒中心。场景空时退化成固定倍数的拉远，
		/// 此时中心保持不动（<paramref name="fitted" /> 返回 false，调用方据此跳过挪镜头）。
		/// </summary>
		private bool ComputeOverview(SceneCamera camera, out float size, out Vector2 center)
		{
			PartManager partManager = PartManager.Instance;
			if (partManager != null && partManager.HasActiveScene())
			{
				PartScene scene = partManager.ActiveScene;
				if (scene != null && scene.Unmanaged.TryGetPartsBounds(out float2 min, out float2 max))
				{
					float2 extents = max - min;
					float2 mid = (min + max) * 0.5f;
					float aspect = camera.Camera != null && camera.Camera.aspect > 0.01f ? camera.Camera.aspect : 16f / 9f;
					// 左侧部件栏把画面左边一条盖住了，那条不算「看得见」：
					// 既要把它的宽度从可用宽度里扣掉，也要把载具中心摆到剩下那条的正中，
					// 否则拉远之后载具会有一截藏在部件栏底下（用户视角等于没装下）。
					float left = VisibleLeftFraction();
					float usable = Mathf.Clamp(1f - left, 0.35f, 1f);
					float half = math.max(extents.y * 0.5f, extents.x * 0.5f / (aspect * usable));
					size = Mathf.Clamp(half * BoundsPadding, camera.MinSize, camera.MaxSize);
					float visibleWidth = 2f * size * aspect;
					center = new Vector2(mid.x - 0.5f * left * visibleWidth, mid.y);
					return true;
				}
			}
			size = Mathf.Clamp(camera.TargetSize * EmptyZoomOut, camera.MinSize, camera.MaxSize);
			center = camera.TargetPoint;
			return false;
		}

		private void OnSceneSelected(PartScene scene)
		{
			// 换场景时 SceneCamera 会按新场景的 State 重置尺寸，这里只把按钮状态收回去，
			// 避免「按钮亮着但镜头已经换成新场景尺寸」的错位。
			if (m_toggle != null)
			{
				m_toggle.SetIsOnWithoutNotify(false);
			}
			m_preview = false;
		}

		private void Update()
		{
			// 进入模拟后镜头由猪的跟随逻辑接管（SceneCamera.LateUpdate），概览缩放没有意义，
			// 顺手复位，别把远景带进模拟里。
			if (!m_preview)
			{
				return;
			}
			PartManager partManager = PartManager.Instance;
			if (partManager == null || !partManager.IsSimulating)
			{
				return;
			}
			if (m_toggle != null)
			{
				m_toggle.SetIsOnWithoutNotify(false);
			}
			m_preview = false;
			SceneCamera camera = SceneCamera.Instance;
			if (camera != null)
			{
				camera.ZoomTo(m_savedSize);
			}
		}

		private void OnDestroy()
		{
			PartManager partManager = PartManager.Instance;
			if (partManager != null)
			{
				partManager.SceneSelected -= OnSceneSelected;
			}
		}
	}
}
