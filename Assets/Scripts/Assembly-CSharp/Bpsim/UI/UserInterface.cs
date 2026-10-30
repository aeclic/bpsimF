using System;
using System.Diagnostics;
using Bpsim.Parts;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class UserInterface : UnitySingleton<UserInterface>
	{
		[Serializable]
		public class ActionBarView
		{
			[SerializeField]
			private GameObject m_gameObject;

			[SerializeField]
			private Button m_loadButton;

			[SerializeField]
			private Button m_saveButton;

			[SerializeField]
			private Button m_screenshotButton;

			[SerializeField]
			private Button m_identifyButton;

			[SerializeField]
			private Button m_simulationButton;

			[SerializeField]
			private Button m_settingsButton;

			[SerializeField]
			private SceneLoader m_sceneLoaderPrefab;

			[SerializeField]
			private ScreenshotPanel m_screenshotPanelPrefab;

			[SerializeField]
			private UserSettingsPanel m_settingsPanelPrefab;

			private UserInterface m_root;

			private FPSCounter m_counter;

			public GameObject GameObject => m_gameObject;

			/// <summary>顶部工具栏克隆按钮用的模板（加载场景按钮），保证新按钮与既有按钮完全同款。</summary>
			public Button TemplateButton => m_loadButton;

			/// <summary>
			/// 原来的「模拟」按钮。它会被 <see cref="T:Bpsim.UI.BuildHud" /> 搬到 HUD 画布上
			/// 变成 Besiege 式的大播放键，所以这里保留引用、不再参与 ActionBar 的排版。
			/// </summary>
			public Button SimulateButton => m_simulationButton;

			public void Initialize(UserInterface root)
			{
				m_root = root;
				m_loadButton.onClick.AddListener(LoadScene);
				m_saveButton.onClick.AddListener(SaveScene);
				m_screenshotButton.onClick.AddListener(Screenshot);
				m_simulationButton.onClick.AddListener(delegate
				{
					PartManager.Instance.StartSimulation();
				});
				m_settingsButton.onClick.AddListener(OpenSettings);
				m_counter = new FPSCounter(0.5f);
				m_counter.Start();
			}

			public void Update()
			{
				m_counter.Tick();
				m_gameObject.transform.Find("Text").GetComponent<Text>().text = m_counter.FPS.ToString();
			}

			private void LoadScene()
			{
				SceneLoader sceneLoader = UnityEngine.Object.Instantiate(m_sceneLoaderPrefab);
				sceneLoader.Mode = SceneLoaderMode.Read;
				sceneLoader.transform.SetParent(m_root.SubCanvas.transform, worldPositionStays: false);
			}

			private void SaveScene()
			{
				SceneLoader sceneLoader = UnityEngine.Object.Instantiate(m_sceneLoaderPrefab);
				sceneLoader.Mode = SceneLoaderMode.Write;
				sceneLoader.transform.SetParent(m_root.SubCanvas.transform, worldPositionStays: false);
			}

			private void Screenshot()
			{
				UnityEngine.Object.Instantiate(m_screenshotPanelPrefab).transform.SetParent(m_root.SubCanvas.transform, worldPositionStays: false);
			}

			private void OpenSettings()
			{
				UnityEngine.Object.Instantiate(m_settingsPanelPrefab).transform.SetParent(m_root.SubCanvas.transform, worldPositionStays: false);
			}
		}

		public class FPSCounter
		{
			private float m_updateInterval;

			private float m_frameCount;

			private Stopwatch m_stopwatch;

			private float m_result;

			public bool IsRunning => m_stopwatch.IsRunning;

			public float FPS => m_result;

			public FPSCounter(float updateInterval)
			{
				m_updateInterval = updateInterval;
				m_frameCount = 0f;
				m_stopwatch = new Stopwatch();
			}

			public void Tick()
			{
				if (m_stopwatch.IsRunning)
				{
					m_frameCount += 1f;
					float num = (float)m_stopwatch.ElapsedMilliseconds / 1000f;
					if (num >= m_updateInterval)
					{
						m_result = m_frameCount / num;
						m_frameCount = 0f;
						m_stopwatch.Restart();
					}
				}
			}

			public void Start()
			{
				m_stopwatch.Start();
			}

			public void Stop()
			{
				m_stopwatch.Stop();
			}

			public void Reset()
			{
				m_frameCount = 0f;
				m_result = 0f;
				m_stopwatch.Reset();
			}
		}

		[Serializable]
		public class LeftSidebarView
		{
			[SerializeField]
			private GameObject m_gameObject;

			[SerializeField]
			private PartOperator m_partOperator;

			[SerializeField]
			private PartSelector m_partSelector;

			public PartOperator PartOperator => m_partOperator;

			public PartSelector PartSelector => m_partSelector;
		}

		[Serializable]
		public class RightSidebarView
		{
			[SerializeField]
			private GameObject m_gameObject;

			[SerializeField]
			private GameObject m_content;

			public GameObject GameObject => m_gameObject;

			public GameObject Content => m_content;

			public bool HasContent => m_content.transform.childCount > 0;

			public Vector2 Size => ((RectTransform)m_gameObject.transform).sizeDelta;
		}

		[SerializeField]
		private Canvas m_canvas;

		[SerializeField]
		private Canvas m_subCanvas;

		[SerializeField]
		private ActionBarView m_actionBar;

		[SerializeField]
		private LeftSidebarView m_sidebar;

		[SerializeField]
		private RightSidebarView m_rightSidebar;

		[SerializeField]
		private SceneScreen m_sceneScreen;

		[SerializeField]
		private GameObject m_mask;

		private BuildToolController m_buildTool;

		private BuildHud m_buildHud;

		private BuildMagnifier m_magnifier;

		public Canvas Canvas => m_canvas;

		public Canvas SubCanvas => m_subCanvas;

		public ActionBarView ActionBar => m_actionBar;

		public LeftSidebarView LeftSidebar => m_sidebar;

		public RightSidebarView RightSidebar => m_rightSidebar;

		public PartOperator PartOperator => m_sidebar.PartOperator;

		public PartSelector PartSelector => m_sidebar.PartSelector;

		public SceneScreen SceneScreen => m_sceneScreen;

		/// <summary>顶部工具栏的逻辑端，建造工具 / 撤销 / 网格开关都挂在它上面。</summary>
		public BuildToolController BuildTool => m_buildTool;

		/// <summary>Besiege 式的常驻抬头：播放键 + 镜头滑条，模拟中仍然显示。</summary>
		public BuildHud BuildHud => m_buildHud;

		/// <summary>左侧边栏那颗「放大镜」镜头按钮（概览缩放开关）。</summary>
		public BuildMagnifier Magnifier => m_magnifier;

		/// <summary>放大镜预览期间，场景指针归相机所有，不进入建造笔刷或工具。</summary>
		internal bool IsMagnifierPreviewing => m_magnifier != null && m_magnifier.IsPreviewing;

		public new static UserInterface Instance => UnitySingleton<UserInterface>.Instance;

		protected override void Awake()
		{
			base.Awake();
			m_actionBar.Initialize(this);
			UIExtensions.StyleScrollbars(gameObject);
			m_buildTool = gameObject.AddComponent<BuildToolController>();
			// HUD 必须比工具栏先建：它会把 ActionBar 上的「模拟」按钮搬到自己的画布上，
			// 工具栏随后排版时就只看到剩下的 4 个文件操作按钮。
			m_buildHud = gameObject.AddComponent<BuildHud>();
			m_buildHud.Initialize(this);
			// 模态面板（场景加载/保存、设置、截图）要压住 HUD 的播放键，否则选文件时还能误触模拟。
			m_subCanvas.sortingOrder = 10;
			m_actionBar.GameObject.AddComponent<BuildToolbar>().Initialize(this, m_buildTool);
			// 左侧边栏第 5 颗按钮：放大镜镜头按钮，排在「框选」右边。
			m_magnifier = gameObject.AddComponent<BuildMagnifier>();
			m_magnifier.Initialize(this);
		}

		private void Update()
		{
			m_canvas.gameObject.SetActive(!PartManager.Instance.IsSimulating);
			m_subCanvas.gameObject.SetActive(!PartManager.Instance.IsSimulating);
			m_mask.gameObject.SetActive(m_subCanvas.transform.childCount > 0);
			m_rightSidebar.GameObject.SetActive(m_rightSidebar.HasContent);
			m_actionBar.Update();
		}
	}
}
