using Bpsim.Parts;
using Bpsim.Parts.Simulation;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class SimulationInterface : UnitySingleton<SimulationInterface>
	{
		public readonly struct PointerData
		{
			public readonly bool HasValue;

			public readonly bool IsPressed;

			public readonly Vector2 Point;

			public readonly Vector2 Delta;

			public readonly float Scroll;

			public readonly float Time;

			public static PointerData Empty => default(PointerData);

			public PointerData(bool pressed, Vector2 point, Vector2 delta, float scroll, float time)
			{
				HasValue = true;
				IsPressed = pressed;
				Point = point;
				Delta = delta;
				Scroll = scroll;
				Time = time;
			}
		}

		[SerializeField]
		private GameObject m_panel;

		[SerializeField]
		private SceneScreen m_sceneScreen;

		[SerializeField]
		private GameObject m_expander;

		[SerializeField]
		private Text m_text;

		[SerializeField]
		private Button m_backButton;

		[SerializeField]
		private Button m_expandButton;

		[SerializeField]
		private PartButtonList m_partButtonListPrefab;

		private bool m_expanding;

		private UserInterface.FPSCounter m_fpsCounter;

		private PartButtonList m_partButtonList;

		/// <summary>
		/// 上一帧指针（鼠标左键 / 手指）是不是按着的，用来把触摸做成「按下边缘」触发。
		/// </summary>
		/// <remarks>
		/// 注意记录的是<b>原始</b>按压状态，而不是 <see cref="PointerData.IsPressed" />：
		/// 后者在指针压到 UI 上时会被 <see cref="GetPointer" /> 清成 <c>false</c>，
		/// 若用它做 latch，从 UI 上拖到画布上会被误判成一次新按下。
		/// </remarks>
		private bool m_pointerDown;

		public SceneScreen SceneScreen => m_sceneScreen;

		public new static SimulationInterface Instance => UnitySingleton<SimulationInterface>.Instance;

		protected override void Awake()
		{
			base.Awake();
			m_backButton.onClick.AddListener(EndSimulation);
			m_expandButton.onClick.AddListener(Expand);
			m_expanding = false;
			m_fpsCounter = new UserInterface.FPSCounter(0.5f);
			m_fpsCounter.Start();
			EnsureEnhancedTouch();
		}

		private void Update()
		{
			m_panel.gameObject.SetActive(PartManager.Instance.IsSimulating);
			m_fpsCounter.Tick();
			if (m_expanding && PartManager.Instance.IsSimulating)
			{
				Reference<PartSimulatorUnmanaged> unmanagedRef = PartManager.Instance.PartSimulator.UnmanagedRef;
				m_text.text = $"Fps: {m_fpsCounter.FPS}\nParts: {unmanagedRef.Value.Parts.Length}\nJoints: {unmanagedRef.Value.Joints.Length}\n";
			}
			if (PartManager.Instance.IsSimulating)
			{
				if (m_partButtonList == null)
				{
					m_partButtonList = Object.Instantiate(m_partButtonListPrefab);
					m_partButtonList.transform.SetParent(m_panel.transform, worldPositionStays: false);
					m_partButtonList.Initialize();
				}
				ProcessTouch();
			}
			else
			{
				// 模拟已经结束：把按压状态清掉，否则下一次模拟的第一帧会被当成「按住中」，
				// 把玩家的第一次点击吞掉。
				m_pointerDown = false;
			}
		}

		/// <summary>
		/// 结束模拟。HUD 的停止键也走这里（而不是直接 <c>PartManager.EndSimulation</c>），
		/// 否则模拟里那张部件按钮列表不会被销毁，下一次模拟会叠出第二份。
		/// </summary>
		internal void EndSimulation()
		{
			PartManager.Instance.EndSimulation();
			if (m_partButtonList != null)
			{
				Object.Destroy(m_partButtonList.gameObject);
				m_partButtonList = null;
			}
		}

		private void Expand()
		{
			m_expanding = !m_expanding;
			m_expander.SetActive(m_expanding);
		}

		/// <summary>
		/// 把当前指针状态交给模拟层（<see cref="PartSimulation.ProcessTouch(Reference{PartSimulatorUnmanaged}, Vector2)" />）。
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>只在按下的那一帧转发一次。</b> 原版 <c>GuiManager.PointerInput</c> 传的是
		/// <c>Input.GetMouseButtonDown(0)</c> / <c>TouchPhase.Began</c>（按下边缘），
		/// 部件拿到的是 Press 事件，按住不放不会重复触发。
		/// </para>
		/// <para>
		/// 以前这里是每帧转发 <c>pointer.IsPressed</c>：按住机炮
		/// （<see cref="Bpsim.Parts.Gameplay.AutoGunBehavior" />）时会每帧
		/// <c>Trigger()</c> 一次 —— 开关来回翻转、开火计时器每帧被清零，
		/// 结果<b>一发子弹都打不出来</b>；而且每次触发都写两条 <c>Debug.Log</c>，
		/// 按住期间持续掉帧。风扇等其它触摸部件同样会被按一次翻好几次。
		/// </para>
		/// </remarks>
		public void ProcessTouch()
		{
			ProcessTouch(GetPointer());
		}

		/// <summary>
		/// 用给定的指针状态驱动触摸分发（QA 用它复现「按住不放」的情形）。
		/// </summary>
		internal void ProcessTouch(PointerData pointer)
		{
			bool pressedThisFrame = pointer.IsPressed && !m_pointerDown;
			m_pointerDown = pointer.IsPressed || IsPointerPressed();
			if (!pressedThisFrame)
			{
				return;
			}
			PartSimulation.ProcessTouch(PartManager.Instance.PartSimulator.UnmanagedRef, pointer.Point);
		}

		/// <summary>
		/// <c>Touch.activeTouches</c> 只在 EnhancedTouch 打开时才有内容。这里用它判断
		/// 「这一帧手指是不是按着」，所以每次读之前补开一次（已开时只是一次 bool 检查）。
		/// </summary>
		private static void EnsureEnhancedTouch()
		{
			if (!EnhancedTouchSupport.enabled)
			{
				EnhancedTouchSupport.Enable();
			}
		}

		/// <summary>
		/// 不区分是否压在 UI 上：这一帧指针（鼠标左键 / 任一手指）是不是按着的。
		/// </summary>
		private static bool IsPointerPressed()
		{
			if (Touchscreen.current != null)
			{
				EnsureEnhancedTouch();
				return UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0;
			}
			if (Mouse.current != null)
			{
				return Mouse.current.leftButton.isPressed;
			}
			return false;
		}

		private PointerData GetPointer()
		{
			if (Touchscreen.current != null)
			{
				EnsureEnhancedTouch();
				ReadOnlyArray<UnityEngine.InputSystem.EnhancedTouch.Touch> activeTouches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
				if (activeTouches.Count == 0)
				{
					return PointerData.Empty;
				}
				UnityEngine.InputSystem.EnhancedTouch.Touch touch = activeTouches[0];
				if (EventSystem.current.IsPointerOverUIObject(touch.screenPosition))
				{
					return PointerData.Empty;
				}
				float scroll = 0f;
				if (activeTouches.Count >= 2)
				{
					UnityEngine.InputSystem.EnhancedTouch.Touch touch2 = activeTouches[1];
					Vector2 vector = touch.screenPosition - touch2.screenPosition;
					Vector2 vector2 = vector - (touch.delta - touch2.delta);
					scroll = vector.magnitude - vector2.magnitude;
				}
				return new PointerData(pressed: true, touch.screenPosition, touch.delta, scroll, (float)touch.time);
			}
			if (Mouse.current != null)
			{
				Mouse current = Mouse.current;
				Vector2 vector3 = current.position.ReadValue();
				if (EventSystem.current.IsPointerOverUIObject(vector3))
				{
					return PointerData.Empty;
				}
				Vector2 delta = current.delta.ReadValue();
				float scroll2 = current.scroll.ReadValue().y / 120f;
				return new PointerData(current.leftButton.isPressed, vector3, delta, scroll2, Time.unscaledTime);
			}
			return PointerData.Empty;
		}
	}
}
