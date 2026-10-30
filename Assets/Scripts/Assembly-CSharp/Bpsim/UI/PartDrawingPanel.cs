using Bpsim.ComponentModel;
using Bpsim.Parts;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class PartDrawingPanel : SwitchableInterface
	{
		[SerializeField]
		private Button m_switchToButton;

		[SerializeField]
		private Button m_switchBackButton;

		[SerializeField]
		private InputField m_brushSizeX;

		[SerializeField]
		private InputField m_brushSizeY;

		[SerializeField]
		private Dropdown m_brushShape;

		[SerializeField]
		private ToggleSwitch m_overlay;

		[SerializeField]
		private InputField m_partType;

		[SerializeField]
		private InputField m_partIndex;

		[SerializeField]
		private Button m_resetButton;

		[SerializeField]
		private Button m_closeButton;

		private DependencyProxy<PartDrawingSettings> m_viewModel;

		protected override void Awake()
		{
			base.Awake();
			m_spacing = 20f;
			m_padding = new Vector2(30f, 0f);
		}

		private void Start()
		{
			PartDrawingSettings settings = PartManager.Instance.PartDrawer.Settings;
			m_viewModel = new DependencyProxy<PartDrawingSettings>(settings);
			m_brushSizeX.Bind(m_viewModel.GetProperty<Vector2>("BrushSize"), Parser<float>.Default, (Vector2 vector) => vector.x, (Vector2 vector, float x) => vector.WithX(x));
			m_brushSizeY.Bind(m_viewModel.GetProperty<Vector2>("BrushSize"), Parser<float>.Default, (Vector2 vector) => vector.y, (Vector2 vector, float y) => vector.WithY(y));
			m_brushShape.Bind(m_viewModel.GetProperty<PartDrawer.Shape>("BrushShape"));
			m_overlay.Bind(m_viewModel.GetProperty<bool>("Overlay"));
			m_partType.Bind(m_viewModel.GetProperty<PartType>("PartType"), EnumNumberParser<PartType>.Default);
			m_partIndex.Bind(m_viewModel.GetProperty<int>("PartIndex"));
			m_switchToButton.onClick.AddListener(delegate
			{
				UpdateLayout(sidebar: false);
			});
			m_switchBackButton.onClick.AddListener(delegate
			{
				UpdateLayout(sidebar: true);
			});
			m_closeButton.onClick.AddListener(base.Close);
			m_resetButton.onClick.AddListener(Reset);
		}

		private void Update()
		{
			m_switchToButton.gameObject.SetActive(m_inSidebar);
			m_switchBackButton.gameObject.SetActive(!m_inSidebar);
			m_partType.transform.parent.Find("Text").GetComponent<Text>().text = m_viewModel.Source.PartType.GetAliasName();
		}

		public void UpdateLayout(bool sidebar)
		{
			UserInterface instance = UserInterface.Instance;
			Transform parent = (sidebar ? instance.RightSidebar.Content.transform : instance.SubCanvas.transform);
			base.transform.SetParent(parent, worldPositionStays: false);
			UpdateLayout(sidebar, instance.RightSidebar.Size);
		}

		private void Reset()
		{
			m_viewModel.Source.Reset();
		}
	}
}
