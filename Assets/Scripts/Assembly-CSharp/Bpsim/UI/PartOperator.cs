using System;
using System.Linq;
using Bpsim.Parts;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class PartOperator : MonoBehaviour
	{
		[SerializeField]
		private ToggleGroup m_toggleGroup;

		[SerializeField]
		private DoubleClickableButton m_viewButton;

		[SerializeField]
		private DoubleClickableButton m_placeButton;

		[SerializeField]
		private DoubleClickableButton m_eraseButton;

		[SerializeField]
		private DoubleClickableButton m_selectButton;

		[SerializeField]
		private PartPropertyPanel m_partPropertyPanelPrefab;

		[SerializeField]
		private PartDrawingPanel m_partDrawingPanelPrefab;

		[SerializeField]
		private PartSelectionPanel m_partSelectionPanelPrefab;

		private UserInterface m_root;

		private PartOperationMode m_mode;

		private PartPropertyPanel m_propertyPanel;

		private PartDrawingPanel m_drawingPanel;

		private PartSelectionPanel m_selectionPanel;

		private NativeArray<Schematics.Unit> m_copiedSchematics;

		private Toggle m_neutralToggle;

		public PartOperationMode Mode => m_mode;

		public PartPropertyPanel PropertyPanel => m_propertyPanel;

		/// <summary>点亮左侧栏对应的模式 Toggle。</summary>
		public void SetMode(PartOperationMode mode)
		{
			Toggle toggle = mode switch
			{
				PartOperationMode.View => m_viewButton.GetComponent<Toggle>(), 
				PartOperationMode.Place => m_placeButton.GetComponent<Toggle>(), 
				PartOperationMode.Erase => m_eraseButton.GetComponent<Toggle>(), 
				PartOperationMode.Select => m_selectButton.GetComponent<Toggle>(), 
				_ => null, 
			};
			if (toggle != null)
			{
				toggle.isOn = true;
			}
			m_mode = mode;
		}

		/// <summary>
		/// 让 <see cref="P:Bpsim.UI.PartOperator.Mode" /> 变成 <see cref="F:Bpsim.Parts.PartOperationMode.None" />：
		/// 顶部工具栏的工具激活时用它——放置笔刷不会抢指针，同时 <see cref="T:Bpsim.SceneCamera" /> 的
		/// <c>CanMove()</c> 会因为「非 View 模式」而暂停镜头跟随，拖动部件时画面不会跟着飘。
		/// 左侧栏的四个按钮保持不亮，避免误导玩家以为在放置。
		/// </summary>
		public void SetNeutral()
		{
			if (m_neutralToggle == null)
			{
				GameObject gameObject = new GameObject("BuildToolButton", typeof(RectTransform));
				gameObject.transform.SetParent(m_toggleGroup.transform, worldPositionStays: false);
				gameObject.SetActive(value: false);
				((RectTransform)gameObject.transform).sizeDelta = Vector2.zero;
				LayoutElement layoutElement = gameObject.AddComponent<LayoutElement>();
				layoutElement.ignoreLayout = true;
				m_neutralToggle = gameObject.AddComponent<Toggle>();
				m_neutralToggle.group = m_toggleGroup;
				m_neutralToggle.transition = Selectable.Transition.None;
				gameObject.SetActive(value: true);
			}
			m_neutralToggle.isOn = true;
			m_mode = PartOperationMode.None;
		}

		public NativeArray<Schematics.Unit> CopiedSchematics
		{
			get
			{
				return m_copiedSchematics;
			}
			set
			{
				if (m_copiedSchematics.IsCreated)
				{
					m_copiedSchematics.Dispose();
				}
				m_copiedSchematics = value;
			}
		}

		private void Awake()
		{
			m_root = UserInterface.Instance;
			m_placeButton.onDoubleClick.AddListener(OpenPartDrawingPanel);
			m_eraseButton.onDoubleClick.AddListener(OpenPartDrawingPanel);
			m_selectButton.onDoubleClick.AddListener(OpenPartSelectionPanel);
			m_root.SceneScreen.DoubleClicked += OnScreenDoubleClicked;
		}

		private void Update()
		{
			Toggle toggle = m_toggleGroup.ActiveToggles().FirstOrDefault();
			if (toggle != null)
			{
				m_mode = toggle.name switch
				{
					"ViewButton" => PartOperationMode.View, 
					"PlaceButton" => PartOperationMode.Place, 
					"EraseButton" => PartOperationMode.Erase, 
					"SelectButton" => PartOperationMode.Select, 
					_ => PartOperationMode.None, 
				};
			}
		}

		private void OnScreenDoubleClicked(PointerEventData eventData)
		{
			Vector3 vector = SceneCamera.Instance.Camera.ScreenToWorldPoint(eventData.position);
			PartManager instance = PartManager.Instance;
			if (!instance.HasActiveScene()) return;
			var scene = instance.ActiveScene;
			var point = new float2(vector.x, vector.y);
			Entity part = Entity.Null;
			if (scene.Unmanaged.ActiveLayer == 0)
			{
				// The all-layers view is read-only, but still supports inspecting visible parts.
				for (int layer = scene.Unmanaged.LayerCount; layer >= 1; layer--)
					if (scene.Unmanaged.TryFindPartAtWorld(point, layer, out part)) break;
			}
			else scene.Unmanaged.TryFindPartAtWorld(point, out part);
			if (part != Entity.Null)
			{
				OpenPartPropertyPanel(scene, part);
			}
		}

		public void OpenPartPropertyPanel(PartScene partScene, Entity part)
		{
			if (m_propertyPanel != null && !m_propertyPanel.IsClosing)
			{
				m_propertyPanel.Initialize(partScene, part);
			}
			else
			{
				// A drawing/selection panel must not silently swallow a property request.
				if (m_drawingPanel != null) { m_drawingPanel.Close(); m_drawingPanel = null; }
				if (m_selectionPanel != null) { m_selectionPanel.Close(); m_selectionPanel = null; }
				PartPropertyPanel partPropertyPanel = UnityEngine.Object.Instantiate(m_partPropertyPanelPrefab);
				partPropertyPanel.UpdateLayout(sidebar: true);
				partPropertyPanel.Initialize(partScene, part);
				m_propertyPanel = partPropertyPanel;
			}
		}

		private void OpenPartDrawingPanel()
		{
			if (!UserInterface.Instance.RightSidebar.HasContent)
			{
				PartDrawingPanel partDrawingPanel = UnityEngine.Object.Instantiate(m_partDrawingPanelPrefab);
				partDrawingPanel.UpdateLayout(sidebar: true);
				m_drawingPanel = partDrawingPanel;
			}
		}

		private void OpenPartSelectionPanel()
		{
			if (!UserInterface.Instance.RightSidebar.HasContent)
			{
				PartSelectionPanel partSelectionPanel = UnityEngine.Object.Instantiate(m_partSelectionPanelPrefab);
				partSelectionPanel.UpdateLayout(sidebar: true);
				m_selectionPanel = partSelectionPanel;
			}
		}
	}
}
