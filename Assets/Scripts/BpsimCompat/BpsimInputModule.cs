using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.Utilities;

namespace Bpsim.Compat
{
	/// <summary>
	/// Mouse **and touch** pointer input module for uGUI, driven by the Input System package.
	///
	/// Why this exists: the release build shipped `InputSystemUIInputModule` (from
	/// com.unity.inputsystem) on the EventSystem.  That class is wrapped in
	/// `#if UNITY_INPUT_SYSTEM_ENABLE_UI`, which the package only defines when the
	/// **com.unity.ugui package** is installed.  This project instead carries the
	/// decompiled `Assets/Plugins/UnityEngine.UI.dll` (uGUI as a precompiled assembly),
	/// so the define is absent, the class does not exist, and every EventSystem in the
	/// project comes up with a *missing script* and no input module at all - the whole
	/// UI is dead.
	///
	/// Rather than migrate 100+ serialized component references onto the ugui package
	/// (which would also collide with the DLL's assembly name), this module re-implements
	/// the pointer half of that module on top of `PointerInputModule` from the DLL:
	/// raycast, enter/exit, press/release/click, drag and scroll.
	///
	/// <para><b>Android note (this used to be the bug).</b>  The first version of this
	/// module only read `Mouse.current`.  On a phone there is no mouse device, so
	/// `Process()` returned on the very first line and <i>every</i> button in the app was
	/// dead to touch - menu buttons, the top toolbar, the part sidebar, the HUD play key.
	/// Drag/长按提示 did work (they read `Touchscreen.current` directly), which is why it
	/// looked like "触控失效但长按有反应".  Everything now goes through
	/// <see cref="ProcessPointer"/>, which is fed by the mouse <i>and</i> by every
	/// pressed/released touch of `Touchscreen.current` (one uGUI pointer per finger).</para>
	///
	/// Keyboard/Submit/Cancel bindings of the original module are not reproduced; the
	/// game itself reads `Keyboard.current` directly where it needs keys.
	/// </summary>
	public sealed class BpsimInputModule : PointerInputModule
	{
		/// <summary>Per uGUI pointer (mouse or one finger) press/drag bookkeeping.</summary>
		private sealed class PointerState
		{
			public PointerEventData Data;
			public bool WasPressed;
			public Vector2 LastPosition;
			public bool HasPosition;
		}

		/// <summary>The mouse uses the same id uGUI reserves for the "mouse" pointer.</summary>
		private const int MousePointerId = -1;

		private readonly Dictionary<int, PointerState> m_states = new Dictionary<int, PointerState>(4);

		/// <summary>诊断：脚本/QA 用。打开后每次触摸状态变化都会打一条日志。</summary>
		public static bool DebugTouch;

		/// <summary>诊断：收到过多少次手指/指针的按下、抬起（QA 用来区分「没进来」和「进来但没命中」）。</summary>
		public static int DebugPressCount;

		public static int DebugReleaseCount;

		/// <summary>诊断：手指状态被 Process 处理的次数（含按住期间的每一帧）。</summary>
		public static int DebugTouchFrameCount;

		public override bool IsModuleSupported()
		{
			return true;
		}

		public override bool ShouldActivateModule()
		{
			return base.ShouldActivateModule();
		}

		public override void Process()
		{
			bool anyTouchPressed = false;

			Touchscreen touchscreen = Touchscreen.current;
			if (touchscreen == null)
			{
				// 触摸设备没了（热插拔 / 应用切后台）：把残留的手指状态清掉，
				// 否则下次回来会有一个「一直按着」的幽灵指针挡住所有点击。
				PruneTouchStates();
			}
			if (touchscreen != null)
			{
				// **primaryTouch 不在 touchscreen.touches 里**（Touchscreen.FinishSetup 明确把它
				// 从数组里剔掉），而单指点击 UI 时手指正好就是 primaryTouch —— 只遍历 touches
				// 会漏掉 Android 上唯一的那根手指，按钮照旧点不动。
				anyTouchPressed |= ProcessTouchControl(touchscreen.primaryTouch);
				ReadOnlyArray<TouchControl> touches = touchscreen.touches;
				for (int i = 0; i < touches.Count; i++)
				{
					anyTouchPressed |= ProcessTouchControl(touches[i]);
				}
			}

			Mouse mouse = Mouse.current;
			// 手指按着的时候不再喂鼠标：有些设备（DeX / 触屏笔驱动）会把触摸再合成一份鼠标事件，
			// 两边都处理会变成一次点击触发两次。
			if (mouse != null && !anyTouchPressed)
			{
				bool pressed = mouse.leftButton.isPressed;
				if (pressed || mouse.leftButton.wasReleasedThisFrame || m_states.ContainsKey(MousePointerId))
				{
					ProcessPointer(MousePointerId, mouse.position.ReadValue(), mouse.delta.ReadValue(),
						pressed, mouse.scroll.ReadValue() / 120f, isTouch: false);
				}
			}
		}

		/// <summary>
		/// 把一个手指控件的当前状态送进 <see cref="ProcessPointer"/>；返回它这一帧是不是按着。
		/// </summary>
		private bool ProcessTouchControl(TouchControl touch)
		{
			bool pressed = touch.press.isPressed;
			int touchId = touch.touchId.ReadValue();
			// 抬起那一帧 press 已经是 false：除了 wasReleasedThisFrame，还要认「这一帧之前我们跟踪过
			// 这个 touchId」——脚本手动 InputSystem.Update() 时，wasReleasedThisFrame 有可能
			// 在 EventSystem.Update 跑到之前就被下一次输入更新清掉，那样 click 会丢。
			if (!pressed && !touch.press.wasReleasedThisFrame && !m_states.ContainsKey(touchId))
			{
				return false;
			}
			DebugTouchFrameCount++;
			// 系统可能把一根手指「取消」掉（下划呼出通知栏、边缘返回手势…）：
			// phase=Canceled 时 press 也是 false，但这次抬起不算点击，否则会误触按钮。
			bool canceled = touch.phase.ReadValue() == UnityEngine.InputSystem.TouchPhase.Canceled;
			ProcessPointer(touchId, touch.position.ReadValue(), touch.delta.ReadValue(),
				pressed, Vector2.zero, isTouch: true, allowClick: !canceled);
			return pressed;
		}

		private readonly List<int> m_pruneScratch = new List<int>(4);

		/// <summary>丢掉所有手指（非鼠标）的残留状态。</summary>
		private void PruneTouchStates()
		{
			if (m_states.Count == 0)
			{
				return;
			}
			m_pruneScratch.Clear();
			foreach (KeyValuePair<int, PointerState> pair in m_states)
			{
				if (pair.Key != MousePointerId)
				{
					m_pruneScratch.Add(pair.Key);
				}
			}
			for (int i = 0; i < m_pruneScratch.Count; i++)
			{
				m_states.Remove(m_pruneScratch[i]);
			}
			m_pruneScratch.Clear();
		}

		/// <summary>Feeds one frame of one pointer (mouse or a single finger) through uGUI.</summary>
		private void ProcessPointer(int pointerId, Vector2 position, Vector2 delta, bool pressed, Vector2 scrollDelta, bool isTouch, bool allowClick = true)
		{
			PointerState state;
			if (!m_states.TryGetValue(pointerId, out state))
			{
				state = new PointerState
				{
					Data = new PointerEventData(eventSystem)
				};
				m_states.Add(pointerId, state);
			}

			PointerEventData data = state.Data;
			// 刻意不调用 PointerEventData.Reset()：它会把 pointerPress / pointerEnter 也清掉，
			// 那样 release 那一帧就找不到按下的对象（click 永远发不出去），
			// 悬停的 PointerExit 也不会补发。这里只逐帧刷新「本帧状态」字段，
			// 按下/抬起相关的字段完全由 Press()/Release() 管理。
			data.pointerId = pointerId;
			data.delta = state.HasPosition ? position - state.LastPosition : delta;
			data.position = position;
			data.scrollDelta = scrollDelta;
			data.button = PointerEventData.InputButton.Left;

			eventSystem.RaycastAll(data, m_RaycastResultCache);
			RaycastResult current = FindFirstRaycast(m_RaycastResultCache);
			m_RaycastResultCache.Clear();
			data.pointerCurrentRaycast = current;
			HandlePointerExitAndEnter(data, current.gameObject);

			if (pressed && !state.WasPressed)
			{
				Press(data, current);
				DebugPressCount++;
				if (DebugTouch)
				{
					Debug.Log("[BpsimCompat] press id=" + pointerId + " at " + position + " hit=" + (current.gameObject != null ? current.gameObject.name : "<null>") + " press=" + (data.pointerPress != null ? data.pointerPress.name : "<null>"));
				}
			}
			else if (!pressed && state.WasPressed)
			{
				Release(data, current, allowClick);
				DebugReleaseCount++;
				if (DebugTouch)
				{
					Debug.Log("[BpsimCompat] release id=" + pointerId + " at " + position + " hit=" + (current.gameObject != null ? current.gameObject.name : "<null>"));
				}
			}

			ProcessDrag(data);

			if (!Mathf.Approximately(data.scrollDelta.sqrMagnitude, 0f))
			{
				GameObject scrollTarget = ExecuteEvents.GetEventHandler<IScrollHandler>(current.gameObject);
				if (scrollTarget != null)
				{
					ExecuteEvents.Execute(scrollTarget, data, ExecuteEvents.scrollHandler);
				}
			}

			state.WasPressed = pressed;
			state.LastPosition = position;
			state.HasPosition = true;

			// 手指抬起后这个 pointer 就结束了，下次触摸是新的 pointer。
			if (!pressed && isTouch)
			{
				m_states.Remove(pointerId);
			}
		}

		private void Press(PointerEventData data, RaycastResult current)
		{
			GameObject target = current.gameObject;
			data.eligibleForClick = true;
			data.dragging = false;
			data.useDragThreshold = true;
			data.pressPosition = data.position;
			data.pointerPressRaycast = current;

			// 先走 pointerDown 链（Slider / Toggle / InputField 要它），没有的话退回到 click 处理器。
			GameObject pressed = ExecuteEvents.ExecuteHierarchy(target, data, ExecuteEvents.pointerDownHandler);
			if (pressed == null)
			{
				pressed = ExecuteEvents.GetEventHandler<IPointerClickHandler>(target);
			}
			data.pointerPress = pressed;
			data.rawPointerPress = target;
			data.clickCount = 1;
			data.clickTime = Time.unscaledTime;

			data.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(target);
			if (data.pointerDrag != null)
			{
				ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.initializePotentialDrag);
			}
		}

		private void Release(PointerEventData data, RaycastResult current, bool allowClick)
		{
			if (data.pointerPress != null)
			{
				ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerUpHandler);

				GameObject clickHandler = ExecuteEvents.GetEventHandler<IPointerClickHandler>(current.gameObject);
				if (allowClick && data.eligibleForClick && clickHandler == data.pointerPress)
				{
					ExecuteEvents.Execute(data.pointerPress, data, ExecuteEvents.pointerClickHandler);
				}
			}

			if (data.pointerDrag != null && data.dragging)
			{
				ExecuteEvents.Execute(data.pointerDrag, data, ExecuteEvents.endDragHandler);
			}

			data.eligibleForClick = false;
			data.dragging = false;
			data.pointerPress = null;
			data.rawPointerPress = null;
			data.pointerDrag = null;
		}
	}

	/// <summary>
	/// Installs <see cref="BpsimInputModule"/> on whichever EventSystem becomes current,
	/// but only when that EventSystem has no input module at all - so a project that
	/// later restores the real InputSystemUIInputModule keeps using it.
	/// </summary>
	public static class BpsimInputInstaller
	{
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Install()
		{
			if (Object.FindObjectOfType<BpsimInputGuard>() != null)
			{
				return;
			}
			GameObject guard = new GameObject("[BpsimInputGuard]");
			guard.hideFlags = HideFlags.HideAndDontSave;
			Object.DontDestroyOnLoad(guard);
			guard.AddComponent<BpsimInputGuard>();
		}
	}

	/// <summary>Per-frame watch dog: EventSystems are created together with the UI prefab.</summary>
	public sealed class BpsimInputGuard : MonoBehaviour
	{
		private void Update()
		{
			EventSystem eventSystem = EventSystem.current;
			if (eventSystem == null)
			{
				return;
			}
			if (eventSystem.GetComponent<BaseInputModule>() != null)
			{
				return;
			}
			eventSystem.gameObject.AddComponent<BpsimInputModule>();
			Debug.Log("[BpsimCompat] added BpsimInputModule to EventSystem '" + eventSystem.name + "' (no input module was present)");
		}
	}
}
