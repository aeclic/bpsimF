using Bpsim.Parts;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	/// <summary>
	/// 建造界面左侧「光标 / 增加 / 减少 / 框选」四个模式按钮右边的第 5 颗按钮：
	/// 一枚简化的放大镜。点一下切换到预览镜头状态，不自动改变缩放或位置。
	///
	/// 行为对齐 BPSS 901-17 建造界面的 PreviewButtonRegular：那里点下去 GameMode 发
	/// UIEvent.Type.Preview，IngameCamera 从 CameraState.Building 切到 CameraState.Preview，
	/// 并允许预览期间平移和手动缩放。这里不引入新的相机状态机，只把预览状态交给 SceneCamera。
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

		private UserInterface m_root;

		private Toggle m_toggle;

		private bool m_preview;

		private string m_diagnostics = string.Empty;

		/// <summary>预览状态是否打开（QA 探针会读它）。</summary>
		internal bool IsPreviewing => m_preview;

		/// <summary>最近一次切换时的相机状态，供 QA 探针和日志定位问题。</summary>
		internal string Diagnostics => m_diagnostics;

		/// <summary>QA 用：走 <see cref="P:UnityEngine.UI.Toggle.isOn" />，也就是真实的点击路径。</summary>
		internal void SetPreview(bool on)
		{
			// Toggle.isOn 没变化时不会回调 onValueChanged；确保探针也能同步预览状态。
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
				Debug.LogWarning("[BuildMagnifier] PartManager 尚未就绪，切换场景时不会复位预览状态。");
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
			m_diagnostics = string.Format("{0} size={1:0.###} point={2}", preview ? "on" : "off", camera.TargetSize, camera.TargetPoint);
			Debug.Log("[BuildMagnifier] " + m_diagnostics);
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
			// 进入模拟后镜头由猪的跟随逻辑接管，预览状态不再适用。
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
