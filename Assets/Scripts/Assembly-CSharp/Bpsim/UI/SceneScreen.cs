using System;
using Bpsim.Parts;
using Unity.Entities;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.Utilities;

namespace Bpsim.UI
{
	[DefaultExecutionOrder(-100)]
	internal class SceneScreen : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IEndDragHandler, IEventSystemHandler
	{
		private PartScene m_dragScene;
		private Entity m_dragPart;
		private bool m_sceneDrag;
		public readonly struct PointerData
		{
			public readonly bool HasValue;

			public readonly bool IsPressed;

			public readonly bool IsSecondaryPressed;

			public readonly bool IsMiddlePressed;

			public readonly Vector2 Point;

			public readonly Vector2 Delta;

			public readonly float Scroll;

			public readonly float Time;

			public static PointerData Empty => default(PointerData);

			public PointerData(bool pressed, Vector2 point, Vector2 delta, float scroll, float time)
				: this(pressed, false, false, point, delta, scroll, time)
			{
			}

			public PointerData(bool pressed, bool secondaryPressed, bool middlePressed, Vector2 point, Vector2 delta, float scroll, float time)
			{
				HasValue = true;
				IsPressed = pressed;
				IsSecondaryPressed = secondaryPressed;
				IsMiddlePressed = middlePressed;
				Point = point;
				Delta = delta;
				Scroll = scroll;
				Time = time;
			}
		}

		private PointerData m_previousPointer;

		private PointerData m_pointer;

		private ClickCounter m_counter;
		private Vector2 m_clickPoint;

		// Ownership is decided at the physical press, never when a held pointer enters the canvas.
		private bool m_leftHeld, m_rightHeld, m_middleHeld;
		private bool m_leftBlocked, m_rightBlocked, m_middleBlocked;
		private int m_touchId = -1;
		private bool m_touchBlocked;

		internal static bool CaptureBlocked(bool pressed, bool wasPressed, bool blocked, bool overScene)
		{
			return pressed && (wasPressed ? blocked : !overScene);
		}

		private bool IsOverScene(Vector2 point)
		{
			return EventSystem.current != null &&
				EventSystem.current.IsPointerOverUIObject(point, out var hit) && hit.gameObject == gameObject;
		}

		private void OnEnable()
		{
			// Reopening a canvas during an existing press must wait for its release.
			var mouse = Mouse.current;
			m_leftHeld = m_leftBlocked = mouse != null && mouse.leftButton.isPressed;
			m_rightHeld = m_rightBlocked = mouse != null && mouse.rightButton.isPressed;
			m_middleHeld = m_middleBlocked = mouse != null && mouse.middleButton.isPressed;
		}

		private void OnDisable()
		{
			m_pointer = m_previousPointer = PointerData.Empty;
			m_leftHeld = m_rightHeld = m_middleHeld = false;
			m_touchId = -1;
			m_sceneDrag = false;
			m_dragScene = null;
			m_dragPart = Entity.Null;
		}

		public PointerData PreviousPointer => m_previousPointer;

		public PointerData Pointer => m_pointer;

		public event Action<PointerEventData> DoubleClicked;

		private void Awake()
		{
			m_counter = new ClickCounter(0.3f);
			EnsureEnhancedTouch();
		}

		/// <summary>
		/// <c>Touch.activeTouches</c> 只有在 EnhancedTouch 打开时才有内容；Android 上的
		/// 「点/拖画布」「双指缩放」全靠它。以前只在 <c>Awake</c> 里、且要求
		/// <c>Touchscreen.current != null</c> 时才打开，设备注册稍晚（或 Awake 顺序变了）
		/// 就再也不会打开，触屏上看着像「画布拖不动」。每次取指针前补一次，
		/// 已打开时只是一次 bool 检查。
		/// </summary>
		private static void EnsureEnhancedTouch()
		{
			if (!EnhancedTouchSupport.enabled)
			{
				EnhancedTouchSupport.Enable();
			}
		}

		private void Update()
		{
			m_previousPointer = m_pointer;
			PointerData pointer = GetPointer();
			m_pointer = pointer;
		}

		private PointerData GetPointer()
		{
			if (Touchscreen.current != null)
			{
				EnsureEnhancedTouch();
				ReadOnlyArray<UnityEngine.InputSystem.EnhancedTouch.Touch> activeTouches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
				if (activeTouches.Count == 0)
				{
					m_touchId = -1;
				}
				else
				{
					UnityEngine.InputSystem.EnhancedTouch.Touch touch = activeTouches[0];
					if (m_touchId != touch.touchId)
					{
						m_touchId = touch.touchId;
						m_touchBlocked = touch.phase != UnityEngine.InputSystem.TouchPhase.Began || !IsOverScene(touch.startScreenPosition);
					}
					if (m_touchBlocked || !IsOverScene(touch.screenPosition)) return PointerData.Empty;
					float scroll = 0f;
					if (activeTouches.Count >= 2)
					{
						UnityEngine.InputSystem.EnhancedTouch.Touch touch2 = activeTouches[1];
						if (!IsOverScene(touch2.startScreenPosition)) { m_touchBlocked = true; return PointerData.Empty; }
						Vector2 vector = touch.screenPosition - touch2.screenPosition;
						Vector2 vector2 = vector - (touch.delta - touch2.delta);
						scroll = Math.Clamp(10f * (vector.magnitude / (vector2.magnitude + 1f) - 1f), -1f, 1f);
					}
					bool pressed = activeTouches.Count == 1 && touch.phase != UnityEngine.InputSystem.TouchPhase.Ended && touch.phase != UnityEngine.InputSystem.TouchPhase.Canceled;
					return new PointerData(pressed, touch.screenPosition, touch.delta, scroll, (float)touch.time);
				}
			}
			if (Mouse.current != null)
			{
				Mouse current = Mouse.current;
				Vector2 vector3 = current.position.ReadValue();
				bool overScene = IsOverScene(vector3);
				m_leftBlocked = CaptureBlocked(current.leftButton.isPressed, m_leftHeld, m_leftBlocked, overScene);
				m_rightBlocked = CaptureBlocked(current.rightButton.isPressed, m_rightHeld, m_rightBlocked, overScene);
				m_middleBlocked = CaptureBlocked(current.middleButton.isPressed, m_middleHeld, m_middleBlocked, overScene);
				m_leftHeld = current.leftButton.isPressed;
				m_rightHeld = current.rightButton.isPressed;
				m_middleHeld = current.middleButton.isPressed;
				if (!overScene)
				{
					return PointerData.Empty;
				}
				Vector2 delta = current.delta.ReadValue();
				float scroll2 = current.scroll.ReadValue().y / 120f;
				return new PointerData(m_leftHeld && !m_leftBlocked, m_rightHeld && !m_rightBlocked, m_middleHeld && !m_middleBlocked, vector3, delta, scroll2, Time.unscaledTime);
			}
			return PointerData.Empty;
		}

		public void OnPointerClick(PointerEventData eventData)
		{
			if (eventData.button != PointerEventData.InputButton.Left || eventData.dragging || QuickBuildDrag.IsMagnifierPreviewing()) return;
			float radius = 12f * Mathf.Max(.5f, Screen.height / 1080f);
			if ((eventData.position - m_clickPoint).sqrMagnitude > radius * radius) m_counter = new ClickCounter(.3f);
			m_clickPoint = eventData.position;
			m_counter.Click();
			if (m_counter.ClickCount == 2)
			{
				m_counter = new ClickCounter(.3f);
				this.DoubleClicked?.Invoke(eventData);
			}
		}

		public void OnBeginDrag(PointerEventData eventData)
		{
			m_sceneDrag = false;
			m_dragScene = null;
			m_dragPart = Entity.Null;
			if (eventData.button != PointerEventData.InputButton.Left || !QuickBuildDrag.CanStartScene() || !QuickBuildDrag.IsScenePoint(eventData.position))
			{
				return;
			}
			PartManager manager = PartManager.Instance;
			PartScene scene = manager.ActiveScene;
			Vector3 world = SceneCamera.Instance.Camera.ScreenToWorldPoint(eventData.position);
			if (scene.Unmanaged.TryFindPartAtWorld(new Unity.Mathematics.float2(world.x, world.y), out Entity part))
			{
				m_dragScene = scene;
				m_dragPart = part;
				m_sceneDrag = true;
			}
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			if (m_sceneDrag && QuickBuildDrag.IsPalettePoint(eventData.position))
			{
				QuickBuildDrag.RemoveScenePart(m_dragScene, m_dragPart);
			}
			m_sceneDrag = false;
			m_dragScene = null;
			m_dragPart = Entity.Null;
		}
	}
}
