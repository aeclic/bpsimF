using Bpsim.ComponentModel;
using Bpsim.Parts;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class PartSelectionPanel : SwitchableInterface
	{
		private class ViewModel : DependencyObject
		{
			public DependencyProperty<RectInt> Selection { get; private set; }

			public DependencyProperty<Vector2Int> TargetPoint { get; private set; }

			public DependencyProperty<bool> UseAbsoluteCoord { get; private set; }

			public DependencyProperty<bool> Overlay { get; private set; }

			public ViewModel()
			{
				Selection = new DependencyProperty<RectInt>();
				TargetPoint = new DependencyProperty<Vector2Int>();
				UseAbsoluteCoord = new DependencyProperty<bool>();
				Overlay = new DependencyProperty<bool>();
			}
		}

		[SerializeField]
		private Button m_switchToButton;

		[SerializeField]
		private Button m_switchBackButton;

		[SerializeField]
		private InputField m_selectionXMin;

		[SerializeField]
		private InputField m_selectionYMin;

		[SerializeField]
		private InputField m_selectionWidth;

		[SerializeField]
		private InputField m_selectionHeight;

		[SerializeField]
		private InputField m_targetPointX;

		[SerializeField]
		private InputField m_targetPointY;

		[SerializeField]
		private ToggleSwitch m_useAbsoluteCoord;

		[SerializeField]
		private ToggleSwitch m_overlay;

		[SerializeField]
		private Button m_copyButton;

		[SerializeField]
		private Button m_pasteButton;

		[SerializeField]
		private Button m_moveButton;

		[SerializeField]
		private Button m_eraseButton;

		[SerializeField]
		private Button m_deselectButton;

		[SerializeField]
		private Button m_closeButton;

		[SerializeField]
		private Text m_state;

		private ViewModel m_viewModel;

		private PartOperator m_partOperator;

		private PartDrawer m_partDrawer;

		protected override void Awake()
		{
			base.Awake();
			m_spacing = 20f;
			m_padding = new Vector2(30f, 0f);
		}

		private void Start()
		{
			m_partOperator = UserInterface.Instance.PartOperator;
			m_partDrawer = PartManager.Instance.PartDrawer;
			Bind();
		}

		private void Update()
		{
			m_switchToButton.gameObject.SetActive(m_inSidebar);
			m_switchBackButton.gameObject.SetActive(!m_inSidebar);
			NativeArray<Schematics.Unit> copiedSchematics = UserInterface.Instance.PartOperator.CopiedSchematics;
			int num = (copiedSchematics.IsCreated ? copiedSchematics.Length : 0);
			m_state.text = $"已复制 {num} 个部件";
		}

		public void UpdateLayout(bool sidebar)
		{
			UserInterface instance = UserInterface.Instance;
			Transform parent = (sidebar ? instance.RightSidebar.Content.transform : instance.SubCanvas.transform);
			base.transform.SetParent(parent, worldPositionStays: false);
			UpdateLayout(sidebar, instance.RightSidebar.Size);
		}

		private void Bind()
		{
			m_viewModel = new ViewModel();
			m_viewModel.Selection.Bind(m_partDrawer.Selection);
			m_selectionXMin.Bind(m_viewModel.Selection, Parser<int>.Default, (RectInt rect) => rect.xMin, (RectInt rect, int xMin) => new RectInt(xMin, rect.yMin, rect.width, rect.height));
			m_selectionYMin.Bind(m_viewModel.Selection, Parser<int>.Default, (RectInt rect) => rect.yMin, (RectInt rect, int yMin) => new RectInt(rect.xMin, yMin, rect.width, rect.height));
			m_selectionWidth.Bind(m_viewModel.Selection, Parser<int>.Default, (RectInt rect) => rect.width, (RectInt rect, int width) => new RectInt(rect.xMin, rect.yMin, width, rect.height));
			m_selectionHeight.Bind(m_viewModel.Selection, Parser<int>.Default, (RectInt rect) => rect.height, (RectInt rect, int height) => new RectInt(rect.xMin, rect.yMin, rect.width, height));
			m_targetPointX.Bind(m_viewModel.TargetPoint, Parser<int>.Default, (Vector2Int vector) => vector.x, (Vector2Int vector, int x) => new Vector2Int(x, vector.y));
			m_targetPointY.Bind(m_viewModel.TargetPoint, Parser<int>.Default, (Vector2Int vector) => vector.y, (Vector2Int vector, int y) => new Vector2Int(vector.x, y));
			m_useAbsoluteCoord.Bind(m_viewModel.UseAbsoluteCoord);
			m_overlay.Bind(m_viewModel.Overlay);
			m_switchToButton.onClick.AddListener(delegate
			{
				UpdateLayout(sidebar: false);
			});
			m_switchBackButton.onClick.AddListener(delegate
			{
				UpdateLayout(sidebar: true);
			});
			m_copyButton.onClick.AddListener(Copy);
			m_pasteButton.onClick.AddListener(Paste);
			m_moveButton.onClick.AddListener(Move);
			m_eraseButton.onClick.AddListener(Erase);
			m_deselectButton.onClick.AddListener(Deselect);
			m_closeButton.onClick.AddListener(base.Close);
		}

		private void Move()
		{
			if (!PartManager.Instance.HasActiveScene())
			{
				return;
			}
			PartScene activeScene = PartManager.Instance.ActiveScene;
			int4 selection = m_viewModel.Selection.Value.ToInt4();
			int2 targetPoint = m_viewModel.TargetPoint.Value.ToInt2();
			using NativeArray<Schematics.Unit> schematics = SelectParts(activeScene, selection);
			IJobExtensions.Run(PartSceneJobs.MoveSchematics(targetPoint, m_viewModel.UseAbsoluteCoord.Value, schematics));
			activeScene.Unmanaged.EntityManager.CompleteAllTrackedJobs();
			IJobExtensions.Run(PartSceneJobs.RemoveParts(activeScene, selection));
			IJobExtensions.Run(PartSceneJobs.PlaceParts(activeScene, m_viewModel.Overlay.Value, schematics));
		}

		private void Copy()
		{
			if (PartManager.Instance.HasActiveScene())
			{
				PartScene activeScene = PartManager.Instance.ActiveScene;
				NativeArray<Schematics.Unit> copiedSchematics = SelectParts(activeScene, m_viewModel.Selection.Value.ToInt4());
				m_partOperator.CopiedSchematics = copiedSchematics;
			}
		}

		private void Paste()
		{
			if (!PartManager.Instance.HasActiveScene())
			{
				return;
			}
			PartScene activeScene = PartManager.Instance.ActiveScene;
			int2 targetPoint = m_viewModel.TargetPoint.Value.ToInt2();
			NativeArray<Schematics.Unit> copiedSchematics = UserInterface.Instance.PartOperator.CopiedSchematics;
			using NativeArray<Schematics.Unit> schematics = new NativeArray<Schematics.Unit>(copiedSchematics, Allocator.TempJob);
			IJobExtensions.Run(PartSceneJobs.MoveSchematics(targetPoint, m_viewModel.UseAbsoluteCoord.Value, schematics));
			activeScene.Unmanaged.EntityManager.CompleteAllTrackedJobs();
			IJobExtensions.Run(PartSceneJobs.PlaceParts(activeScene, m_viewModel.Overlay.Value, schematics));
		}

		private void Erase()
		{
			if (PartManager.Instance.HasActiveScene())
			{
				PartScene activeScene = PartManager.Instance.ActiveScene;
				int4 selection = m_viewModel.Selection.Value.ToInt4();
				activeScene.Unmanaged.EntityManager.CompleteAllTrackedJobs();
				IJobExtensions.Run(PartSceneJobs.RemoveParts(activeScene, selection));
			}
		}

		private NativeArray<Schematics.Unit> SelectParts(PartScene partScene, int4 selection)
		{
			using NativeList<Entity> nativeList = new NativeList<Entity>(256, Allocator.TempJob);
			IJobExtensions.Run(PartSceneJobs.SelectParts(partScene, selection, nativeList));
			NativeArray<Schematics.Unit> nativeArray = new NativeArray<Schematics.Unit>(nativeList.Length, Allocator.Persistent);
			IJobExtensions.Run(PartSceneJobs.SaveParts(nativeList, nativeArray));
			return nativeArray;
		}

		private void Deselect()
		{
			if (PartManager.Instance.HasActiveScene())
			{
				m_viewModel.Selection.Value = default(RectInt);
			}
		}
	}
}
