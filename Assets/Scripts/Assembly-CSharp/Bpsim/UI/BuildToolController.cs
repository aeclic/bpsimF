using System;
using Bpsim.Parts;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Bpsim.UI
{
	/// <summary>
	/// 顶部工具栏的逻辑端：持有当前工具、撤销历史与网格开关，并把场景指针事件翻译成部件级操作。
	/// 视图端是 <see cref="T:Bpsim.UI.BuildToolbar" />，两者通过 <see cref="E:Bpsim.UI.BuildToolController.StateChanged" />
	/// 解耦，工具状态因此可以在没有工具栏 UI 的情况下单独测试。
	/// 执行顺序设为 -50：绘制/擦除的撤销快照必须比 <see cref="T:Bpsim.Parts.PartManager" /> 的
	/// <c>Update()</c>（它才真正跑放置 job）更早抓，否则记下的是「已经改完」的状态。
	/// </summary>
	[DefaultExecutionOrder(-50)]
	internal class BuildToolController : MonoBehaviour
	{
		private BuildTool m_tool;

		private readonly BuildUndoHistory m_history = new BuildUndoHistory();

		private bool m_dragging;

		private bool m_subscribed;

		private bool m_drawingRecorded;

		private bool m_recorded;

		/// <summary>本次按格手势里真的改动过场景；为假说明是「原地单击」，松手时按旋转处理。</summary>
		private bool m_dragMoved;

		private bool m_wholeScene;

		private Entity m_dragEntity;

		private int m_startCellX;

		private int m_startCellY;

		private int m_lastCellX;

		private int m_lastCellY;

		/// <summary>按下瞬间的世界坐标：Move 工具按「指针位移」换算目标格，无网格平移后仍能跟手。</summary>
		private Vector2 m_dragHoldWorld;

		private int m_appliedDeltaX;

		private int m_appliedDeltaY;

		private bool m_lastGridEnabled;

		/// <summary>网格按钮是三态的，只看 <see cref="GridEnabled" /> 会漏掉「无网格」那一档。</summary>
		private bool m_lastGridlessEnabled;

		private bool m_lastCanUndo;

		private bool m_lastStateValid;

		// ------------------------------------------------------------- 无网格 gizmo 状态
		/// <summary>当前选中的部件（gizmo 枢轴）；<see cref="Entity.Null" /> 表示未选中。</summary>
		private Entity m_selectedPart;

		private bool m_gizmoDragging;

		private bool m_lastPointerHeld;

		private GizmoHandle m_gizmoHandle;

		private int m_gizmoHandleIndex;

		private GizmoFrame m_startFrame;

		private PartTransform m_startPartTransform;

		private PartFreeTransform m_startFree;

		private Vector2 m_dragStartWorld;

		private float m_lastDragAngle;

		private float m_appliedRotation;

		private float m_appliedRotationSnapped;

		private float m_appliedScale = 1f;

		private Vector2 m_appliedWorldDelta;

		public BuildTool CurrentTool => m_tool;

		public BuildUndoHistory History => m_history;

		public bool CanUndo => m_history.CanUndo;

		public bool IsDragging => m_dragging || m_gizmoDragging;

		/// <summary>供 gizmo 视图读取的选中部件；无效时返回 <see cref="Entity.Null" />。</summary>
		internal Entity SelectedPart => m_selectedPart;

		internal bool IsGizmoDragging => m_gizmoDragging;

		public event Action StateChanged;

		public bool GridEnabled
		{
			get
			{
				PartScene partScene = ActiveScene();
				return partScene != null && partScene.GridEnabled;
			}
			set
			{
				PartScene partScene = ActiveScene();
				if (partScene != null && partScene.GridEnabled != value)
				{
					partScene.GridEnabled = value;
					NotifyStateChanged();
				}
			}
		}

		/// <summary>无网格建造开关：开启后单击放置记录光标相对格心的偏移。</summary>
		public bool GridlessEnabled
		{
			get
			{
				PartScene partScene = ActiveScene();
				return partScene != null && partScene.GridlessEnabled;
			}
			set
			{
				PartScene partScene = ActiveScene();
				if (partScene != null && partScene.GridlessEnabled != value)
				{
					partScene.GridlessEnabled = value;
					NotifyStateChanged();
				}
			}
		}

		public void ToggleGridless()
		{
			GridlessEnabled = !GridlessEnabled;
		}

		private void Start()
		{
			SubscribePartManager();
			if (GetComponent<PartTransformGizmo>() == null)
			{
				base.gameObject.AddComponent<PartTransformGizmo>().Initialize(this);
			}
			if (GetComponent<GridlessOverlay>() == null)
			{
				base.gameObject.AddComponent<GridlessOverlay>();
			}
			if (GetComponent<PartConnectionPreview>() == null)
			{
				base.gameObject.AddComponent<PartConnectionPreview>();
			}
			if (GetComponent<LayerPanelUI>() == null)
			{
				base.gameObject.AddComponent<LayerPanelUI>().Initialize(this);
			}
			NotifyStateChanged();
		}

		/// <summary>当前活动建造场景（图层面板/预览层从这里取场景）。</summary>
		public PartScene CurrentScene => ActiveScene();

		/// <summary>切换活动建造图层；0 = 全部（只读查看）。</summary>
		public void SetActiveLayer(int layer)
		{
			PartScene partScene = ActiveScene();
			if (partScene != null)
			{
				ref PartSceneUnmanaged unmanaged = ref partScene.Unmanaged;
				unmanaged.SetActiveLayer(layer);
				NotifyStateChanged();
			}
		}

		private void SubscribePartManager()
		{
			if (m_subscribed)
			{
				return;
			}
			PartManager instance = PartManager.Instance;
			if (!(instance != null))
			{
				return;
			}
			instance.SceneLoaded += OnSceneChanged;
			instance.SceneSelected += OnSceneChanged;
			m_subscribed = true;
		}

		private void OnDestroy()
		{
			PartManager instance = PartManager.Instance;
			if (m_subscribed && instance != null)
			{
				instance.SceneLoaded -= OnSceneChanged;
				instance.SceneSelected -= OnSceneChanged;
			}
			m_history.Dispose();
		}

		private void OnSceneChanged(PartScene scene)
		{
			m_history.Clear();
			m_tool = BuildTool.None;
			m_selectedPart = Entity.Null;
			CancelGizmo(false);
			CancelDrag(false);
			NotifyStateChanged();
		}

		/// <summary>设置当前工具；传入当前工具表示取消。</summary>
		public void SetTool(BuildTool tool)
		{
			// 先归一化再比对：旧值（Rotate / FreeTransformPart …）要按合并后的工具判断「再点一次取消」，
			// 否则「当前是 Move，传入旧值 Rotate」会被当成同一个工具而静默忽略。
			BuildTool requested = Normalize(tool);
			BuildTool buildTool = (m_tool == requested) ? BuildTool.None : requested;
			if (buildTool == m_tool)
			{
				return;
			}
			CancelDrag(true);
			CancelGizmo(true);
			m_tool = buildTool;
			if (!IsPartTransformTool(m_tool))
			{
				m_selectedPart = Entity.Null;
			}
			PartOperator partOperator = UserInterface.Instance ? UserInterface.Instance.PartOperator : null;
			if (partOperator != null)
			{
				if (m_tool != BuildTool.None)
				{
					partOperator.SetNeutral();
				}
				else
				{
					partOperator.SetMode(PartOperationMode.View);
				}
			}
			NotifyStateChanged();
		}

		public void Undo()
		{
			PartScene partScene = ActiveScene();
			if (partScene != null && m_history.Count != 0)
			{
				CancelDrag(false);
				CancelGizmo(false);
				m_selectedPart = Entity.Null;
				if (m_history.Undo(partScene))
				{
					NotifyStateChanged();
				}
			}
		}

		public void ToggleGrid()
		{
			GridEnabled = !GridEnabled;
		}

		/// <summary>在会改动场景的操作开始前抓快照，供 <see cref="M:Bpsim.UI.BuildUndoHistory.Undo(Bpsim.Parts.PartScene)" /> 回退。</summary>
		public void Record(string label)
		{
			m_history.Record(ActiveScene(), label);
			NotifyStateChanged();
		}

		public void NotifyStateChanged()
		{
			this.StateChanged?.Invoke();
		}

		private void Update()
		{
			SubscribePartManager();
			HandleShortcuts();
			SyncVisibleState();
			PartScene scene = ActiveScene();
			SceneScreen sceneScreen = UserInterface.Instance ? UserInterface.Instance.SceneScreen : null;
			if (UserInterface.Instance != null && UserInterface.Instance.IsMagnifierPreviewing)
			{
				m_drawingRecorded = false;
				CancelDrag(false);
				CancelGizmo(false);
				return;
			}
			if (sceneScreen != null && scene != null)
			{
				TrackDrawingUndo(scene, sceneScreen);
			}
			if (m_tool == BuildTool.None)
			{
				CancelDrag(false);
				CancelGizmo(false);
				return;
			}
			PartManager instance = PartManager.Instance;
			if (scene == null || instance.IsSimulating)
			{
				CancelDrag(false);
				CancelGizmo(false);
				return;
			}
			PartOperator partOperator = UserInterface.Instance ? UserInterface.Instance.PartOperator : null;
			if (partOperator != null && partOperator.Mode != PartOperationMode.None)
			{
				partOperator.SetNeutral();
			}
			if (IsGizmoTool(m_tool))
			{
				// 无网格工具走自己的 gizmo 手势，不再落到按格拖动逻辑。
				CancelDrag(false);
				HandleGizmo(scene, sceneScreen);
				return;
			}
			CancelGizmo(false);
			if (sceneScreen == null)
			{
				return;
			}
			SceneScreen.PointerData pointer = sceneScreen.Pointer;
			if (!pointer.HasValue || !pointer.IsPressed)
			{
				// 松手这一帧才结算：整段手势没移动过 = 原地单击 = 旋转（见 EndDrag）。
				if (m_dragging)
				{
					EndDrag(scene);
				}
				else
				{
					CancelDrag(false);
				}
				return;
			}
			if (!TryGetCell(in pointer, out var x, out var y) || !TryGetWorld(pointer.Point, out Vector2 dragWorld))
			{
				return;
			}
			if (!m_dragging)
			{
				BeginDrag(scene, x, y, dragWorld);
			}
			else
			{
				ContinueDrag(scene, x, y, dragWorld);
			}
		}

		/// <summary>
		/// 放置/擦除笔刷由 <see cref="T:Bpsim.Parts.PartDrawer" /> 驱动，它在「上一帧也按住」的那一帧才动手，
		/// 所以这里在同条件的第一帧抓快照，正好落在改动之前；一次拖动只记一步。
		/// </summary>
		private void TrackDrawingUndo(PartScene scene, SceneScreen sceneScreen)
		{
			PartOperator partOperator = UserInterface.Instance ? UserInterface.Instance.PartOperator : null;
			bool flag = partOperator != null && !PartManager.Instance.IsSimulating && (partOperator.Mode == PartOperationMode.Place || partOperator.Mode == PartOperationMode.Erase);
			SceneScreen.PointerData pointer = sceneScreen.Pointer;
			bool flag2 = pointer.HasValue && pointer.IsPressed && sceneScreen.PreviousPointer.HasValue && sceneScreen.PreviousPointer.IsPressed;
			if (!flag || !flag2)
			{
				m_drawingRecorded = false;
				return;
			}
			if (!m_drawingRecorded)
			{
				m_drawingRecorded = true;
				m_history.Record(scene, (partOperator.Mode == PartOperationMode.Place) ? "放置部件" : "擦除部件");
				NotifyStateChanged();
			}
		}

		/// <summary>
		/// 场景在工具栏挂载之后才加载时（「网格 开/关」「撤销」两枚按钮的可读状态依赖场景），
		/// <see cref="E:Bpsim.UI.BuildToolController.StateChanged" /> 没有事件可等，按钮就会停在初始化时的旧文案。
		/// 这里每帧比对三个可见量，只在真的变化时广播一次，避免每帧重建 UI。
		/// </summary>
		private void SyncVisibleState()
		{
			bool gridEnabled = GridEnabled;
			bool gridlessEnabled = GridlessEnabled;
			bool canUndo = CanUndo;
			if (m_lastStateValid && gridEnabled == m_lastGridEnabled && gridlessEnabled == m_lastGridlessEnabled && canUndo == m_lastCanUndo)
			{
				return;
			}
			m_lastStateValid = true;
			m_lastGridEnabled = gridEnabled;
			m_lastGridlessEnabled = gridlessEnabled;
			m_lastCanUndo = canUndo;
			NotifyStateChanged();
		}

		private void HandleShortcuts()
		{
			Keyboard current = Keyboard.current;
			if (!(current != null))
			{
				return;
			}
			if ((current.leftCtrlKey.isPressed || current.rightCtrlKey.isPressed) && current.zKey.wasPressedThisFrame)
			{
				Undo();
			}
		}

		private void BeginDrag(PartScene partScene, int cellX, int cellY, Vector2 world)
		{
			m_dragging = true;
			m_recorded = false;
			m_dragMoved = false;
			m_wholeScene = (m_tool == BuildTool.MoveAll);
			m_dragEntity = Entity.Null;
			m_startCellX = cellX;
			m_startCellY = cellY;
			m_lastCellX = cellX;
			m_lastCellY = cellY;
			m_dragHoldWorld = world;
			m_appliedDeltaX = 0;
			m_appliedDeltaY = 0;
			if (m_wholeScene)
			{
				return;
			}
			// 按视觉位置命中：无网格平移过的部件要点它看起来所在的位置，而不是原来的整数格。
			if (!partScene.Unmanaged.TryFindPartAtWorld(new float2(world.x, world.y), out Entity pickedPart))
			{
				m_dragging = false;
				return;
			}
			m_dragEntity = pickedPart;
			// 起点格取部件自身坐标：指针可能落在它的自由偏移所覆盖的相邻格上。
			PartTransform transform = partScene.Unmanaged.EntityManager.GetComponentData<PartTransform>(m_dragEntity);
			m_startCellX = transform.X;
			m_startCellY = transform.Y;
			m_lastCellX = transform.X;
			m_lastCellY = transform.Y;
		}

		/// <summary>
		/// 松手时结算本次按格手势。平移/旋转以前是两枚独立按钮（平移部件 / 旋转部件、
		/// 整车平移 / 整车旋转），但「拖动 = 平移、原地单击 = 旋转」本来就是快速建造里
		/// 已有的手感（<see cref="T:Bpsim.Parts.PartDrawer" /> 的短按旋转 / 长按拖动），
		/// 所以合并成一枚按钮：整段手势没移动过就按旋转处理，否则什么都不做（拖动已经生效）。
		/// </summary>
		private void EndDrag(PartScene partScene)
		{
			bool clicked = m_dragging && !m_dragMoved;
			bool wholeScene = m_wholeScene;
			Entity entity = m_dragEntity;
			CancelDrag(false);
			if (!clicked)
			{
				return;
			}
			if (wholeScene)
			{
				Record("整车旋转");
				partScene.Unmanaged.TryRotateAllParts(1);
			}
			else if (entity != Entity.Null)
			{
				Record("旋转部件");
				partScene.Unmanaged.TryRotatePart(entity, 1);
			}
		}

		private void ContinueDrag(PartScene partScene, int cellX, int cellY, Vector2 world)
		{
			if (m_tool == BuildTool.MoveAll)
			{
				int num = cellX - m_startCellX;
				int num2 = cellY - m_startCellY;
				if (num == m_appliedDeltaX && num2 == m_appliedDeltaY)
				{
					return;
				}
				// 只要指针真的离开过按下格就算「拖动」，松手时不再当单击旋转。
				m_dragMoved = true;
				if (!m_recorded)
				{
					Record("整车平移");
					m_recorded = true;
				}
				if (partScene.Unmanaged.TryMoveAllParts(num - m_appliedDeltaX, num2 - m_appliedDeltaY))
				{
					m_appliedDeltaX = num;
					m_appliedDeltaY = num2;
				}
				return;
			}
			if (m_tool != BuildTool.Move || m_dragEntity == Entity.Null)
			{
				return;
			}
			// 目标格由「从按下点开始的指针位移」决定，而不是指针当前所在格：
			// 无网格平移后的部件可能被按在相邻格上，用指针格会把它的自由偏移吃掉、拖动时跳一下。
			int targetX = m_startCellX + (int)Math.Round(world.x - m_dragHoldWorld.x);
			int targetY = m_startCellY + (int)Math.Round(world.y - m_dragHoldWorld.y);
			if (targetX == m_lastCellX && targetY == m_lastCellY)
			{
				return;
			}
			m_dragMoved = true;
			if (!m_recorded)
			{
				Record("平移部件");
				m_recorded = true;
			}
			if (partScene.Unmanaged.TryMovePartContent(m_lastCellX, m_lastCellY, targetX, targetY))
			{
				m_lastCellX = targetX;
				m_lastCellY = targetY;
			}
		}

		/// <summary>结束当前手势；<paramref name="revert" /> 为真时把本次手势已经做的改动回退（取消工具时用）。</summary>
		private void CancelDrag(bool revert)
		{
			if (m_dragging && revert && m_recorded)
			{
				PartScene partScene = ActiveScene();
				if (partScene != null)
				{
					m_history.Undo(partScene);
				}
			}
			m_dragging = false;
			m_recorded = false;
			m_dragMoved = false;
			m_wholeScene = false;
			m_dragEntity = Entity.Null;
			m_appliedDeltaX = 0;
			m_appliedDeltaY = 0;
		}

		// ------------------------------------------------------------- 无网格 gizmo 手势

		/// <summary>
		/// 把工具栏已经不再提供的旧工具值翻译成合并后的工具。旧值仍然留在
		/// <see cref="T:Bpsim.UI.BuildTool" /> 里（外部脚本、旧探针还会引用），
		/// 但行为归到留下的按钮上：旋转并入平移、自由变换并入部件变换、部件调整并入双击。
		/// </summary>
		internal static BuildTool Normalize(BuildTool tool)
		{
			switch (tool)
			{
			case BuildTool.Rotate:
				return BuildTool.Move;
			case BuildTool.RotateAll:
				return BuildTool.MoveAll;
			case BuildTool.FreeTransformPart:
				return BuildTool.TransformPart;
			case BuildTool.Adjust:
				return BuildTool.None;
			default:
				return tool;
			}
		}

		internal static bool IsPartTransformTool(BuildTool tool)
		{
			return tool == BuildTool.TransformPart;
		}

		internal static bool IsMachineTransformTool(BuildTool tool)
		{
			return tool == BuildTool.TransformMachine;
		}

		internal static bool IsGizmoTool(BuildTool tool)
		{
			return IsPartTransformTool(tool) || IsMachineTransformTool(tool);
		}

		/// <summary>
		/// gizmo 是否画四角/四边缩放手柄。三个 gizmo 工具都画：部件变换与「自由变换」本来
		/// 共用同一套 <see cref="ContinueScale" /> 代码，唯一差别就是手柄画不画，
		/// 那个差别让工具栏多出一枚语义重复的按钮，所以手柄改成常显。
		/// </summary>
		internal static bool GizmoIncludesScale(BuildTool tool)
		{
			return IsGizmoTool(tool);
		}

		private static float UiScale => Mathf.Max(0.5f, (float)Screen.height / 1080f);

		private static bool IsShiftHeld => Keyboard.current != null && Keyboard.current.shiftKey.isPressed;

		/// <summary>当前工具应显示的 gizmo 框；部件工具要求有选中部件，整车工具要求场景里有部件。</summary>
		internal bool TryGetGizmoFrame(PartScene scene, out GizmoFrame frame)
		{
			frame = default;
			if (scene == null)
			{
				return false;
			}
			if (IsMachineTransformTool(m_tool))
			{
				if (!scene.Unmanaged.TryGetPartsBounds(out var min, out var max))
				{
					return false;
				}
				frame = GizmoFrame.ForMachine(min, max);
				return true;
			}
			if (!IsPartTransformTool(m_tool))
			{
				return false;
			}
			EntityManager entityManager = scene.Unmanaged.EntityManager;
			Entity entity = m_selectedPart;
			if (entity == Entity.Null || !entityManager.Exists(entity) || !entityManager.HasComponent<PartTransform>(entity))
			{
				return false;
			}
			frame = GizmoFrame.ForPart(entityManager.GetComponentData<PartTransform>(entity), scene.Unmanaged.GetPartFreeTransform(entity));
			return true;
		}

		private void HandleGizmo(PartScene scene, SceneScreen sceneScreen)
		{
			if (sceneScreen == null)
			{
				CancelGizmo(false);
				return;
			}
			SceneScreen.PointerData pointer = sceneScreen.Pointer;
			bool pressed = pointer.HasValue && pointer.IsPressed;
			if (m_gizmoDragging)
			{
				if (!pressed)
				{
					EndGizmo();
					return;
				}
				if (TryGetWorld(pointer.Point, out var world))
				{
					ContinueGizmo(scene, world, IsShiftHeld);
				}
				m_lastPointerHeld = true;
				return;
			}
			if (pressed && !m_lastPointerHeld && TryGetWorld(pointer.Point, out var pressWorld))
			{
				SceneCamera sceneCamera = SceneCamera.Instance;
				bool includeScale = GizmoIncludesScale(m_tool);
				if (sceneCamera != null && sceneCamera.Camera != null && TryGetGizmoFrame(scene, out var frame) && PartGizmoMath.HitTest(pointer.Point, sceneCamera.Camera, in frame, includeScale, UiScale, out var hit))
				{
					BeginGizmo(scene, frame, hit, pressWorld);
				}
				else if (IsPartTransformTool(m_tool))
				{
					SelectPartAt(scene, pressWorld);
				}
			}
			m_lastPointerHeld = pressed;
		}

		private void BeginGizmo(PartScene scene, in GizmoFrame frame, in GizmoHit hit, Vector2 world)
		{
			m_gizmoDragging = true;
			m_recorded = false;
			m_gizmoHandle = hit.Handle;
			m_gizmoHandleIndex = hit.Index;
			m_startFrame = frame;
			m_startFree = PartFreeTransform.Identity;
			m_startPartTransform = default(PartTransform);
			if (IsPartTransformTool(m_tool) && m_selectedPart != Entity.Null)
			{
				EntityManager entityManager = scene.Unmanaged.EntityManager;
				if (entityManager.Exists(m_selectedPart))
				{
					if (entityManager.HasComponent<PartTransform>(m_selectedPart))
					{
						m_startPartTransform = entityManager.GetComponentData<PartTransform>(m_selectedPart);
					}
					m_startFree = scene.Unmanaged.GetPartFreeTransform(m_selectedPart);
				}
			}
			m_dragStartWorld = world;
			m_lastDragAngle = math.atan2(world.y - frame.Pivot.y, world.x - frame.Pivot.x);
			m_appliedRotation = 0f;
			m_appliedRotationSnapped = 0f;
			m_appliedScale = 1f;
			m_appliedWorldDelta = Vector2.zero;
			NotifyStateChanged();
		}

		private void EndGizmo()
		{
			m_gizmoDragging = false;
			m_recorded = false;
			m_lastPointerHeld = false;
			m_gizmoHandle = GizmoHandle.None;
			m_gizmoHandleIndex = 0;
		}

		private void CancelGizmo(bool revert)
		{
			if (m_gizmoDragging && revert && m_recorded)
			{
				PartScene partScene = ActiveScene();
				if (partScene != null)
				{
					m_history.Undo(partScene);
				}
			}
			EndGizmo();
		}

		/// <summary>
		/// Selects a part entity for the part-transform gizmo. Used by the QA/demo harness and by
		/// any future outliner-style selection; returns false when the entity does not belong to
		/// the active scene.
		/// </summary>
		internal bool SelectPart(Entity entity)
		{
			PartScene partScene = ActiveScene();
			if (partScene == null)
			{
				return false;
			}
			if (entity != Entity.Null && partScene.Unmanaged.EntityManager.Exists(entity) && partScene.Unmanaged.EntityManager.HasComponent<PartTransform>(entity))
			{
				if (entity != m_selectedPart)
				{
					m_selectedPart = entity;
					NotifyStateChanged();
				}
				return true;
			}
			if (m_selectedPart != Entity.Null)
			{
				m_selectedPart = Entity.Null;
				NotifyStateChanged();
			}
			return false;
		}

		private void SelectPartAt(PartScene scene, Vector2 world)
		{
			Entity entity = Entity.Null;
			if (scene.Unmanaged.TryFindPartAtWorld(new float2(world.x, world.y), out Entity pickedPart))
			{
				entity = pickedPart;
			}
			if (entity != m_selectedPart)
			{
				m_selectedPart = entity;
				NotifyStateChanged();
			}
		}

		private void ContinueGizmo(PartScene scene, Vector2 world, bool shift)
		{
			switch (m_gizmoHandle)
			{
			case GizmoHandle.FreeMove:
			case GizmoHandle.MoveX:
			case GizmoHandle.MoveY:
				ContinueTranslate(scene, world, shift);
				break;
			case GizmoHandle.Rotate:
				ContinueRotate(scene, world, shift);
				break;
			case GizmoHandle.ScaleCorner:
			case GizmoHandle.ScaleEdgeX:
			case GizmoHandle.ScaleEdgeY:
				ContinueScale(scene, world, shift);
				break;
			}
		}

		private void EnsureGizmoRecord(string label)
		{
			if (!m_recorded)
			{
				Record(label);
				m_recorded = true;
			}
		}

		private void ContinueTranslate(PartScene scene, Vector2 world, bool shift)
		{
			m_startFrame.Axes(out var axisX, out var axisY);
			Vector2 delta = world - m_dragStartWorld;
			if (shift)
			{
				delta = new Vector2(Mathf.Round(delta.x * 2f) * 0.5f, Mathf.Round(delta.y * 2f) * 0.5f);
			}
			if (m_gizmoHandle == GizmoHandle.MoveX)
			{
				delta = axisX * Vector2.Dot(delta, axisX);
			}
			else if (m_gizmoHandle == GizmoHandle.MoveY)
			{
				delta = axisY * Vector2.Dot(delta, axisY);
			}
			if (IsPartTransformTool(m_tool))
			{
				Entity entity = m_selectedPart;
				if (entity == Entity.Null)
				{
					return;
				}
				Vector2 offset = (Vector2)m_startFree.Offset + delta;
				if ((offset - (Vector2)m_startFree.Offset).sqrMagnitude < 1E-08f)
				{
					return;
				}
				EnsureGizmoRecord("平移部件");
				scene.Unmanaged.TryMovePartFree(entity, new float2(offset.x, offset.y));
			}
			else
			{
				if ((delta - m_appliedWorldDelta).sqrMagnitude < 1E-08f)
				{
					return;
				}
				EnsureGizmoRecord("整车自由平移");
				Vector2 diff = delta - m_appliedWorldDelta;
				scene.Unmanaged.TryTranslateAllPartsFree(new float2(diff.x, diff.y));
				m_appliedWorldDelta = delta;
			}
		}

		private void ContinueRotate(PartScene scene, Vector2 world, bool shift)
		{
			float angle = math.atan2(world.y - m_startFrame.Pivot.y, world.x - m_startFrame.Pivot.x);
			float delta = PartGizmoMath.DeltaAngle(m_lastDragAngle, angle);
			m_lastDragAngle = angle;
			m_appliedRotation += Mathf.Rad2Deg * delta;
			float target = m_appliedRotation;
			if (shift)
			{
				target = Mathf.Round(target / 15f) * 15f;
			}
			if (Mathf.Abs(target - m_appliedRotationSnapped) < 0.01f)
			{
				return;
			}
			if (IsPartTransformTool(m_tool))
			{
				Entity entity = m_selectedPart;
				if (entity == Entity.Null)
				{
					return;
				}
				EnsureGizmoRecord("旋转部件");
				scene.Unmanaged.TryRotatePartFree(entity, m_startFree.RotationDegrees + target);
			}
			else
			{
				EnsureGizmoRecord("整车自由旋转");
				scene.Unmanaged.TryRotateAllPartsFree(target - m_appliedRotationSnapped, m_startFrame.Pivot);
			}
			m_appliedRotationSnapped = target;
		}

		private void ContinueScale(PartScene scene, Vector2 world, bool shift)
		{
			if (IsMachineTransformTool(m_tool))
			{
				ContinueMachineScale(scene, world, shift);
				return;
			}
			Entity entity = m_selectedPart;
			if (entity == Entity.Null)
			{
				return;
			}
			m_startFrame.Axes(out var axisX, out var axisY);
			float2 worldPoint = new float2(world.x, world.y);
			float2 startScale = m_startFree.Scale;
			float2 startHalf = m_startFrame.HalfExtents;
			float2 newScale = startScale;
			float2 anchor;
			if (m_gizmoHandle == GizmoHandle.ScaleCorner)
			{
				float2 sign = PartGizmoMath.AxisSign(m_gizmoHandleIndex);
				anchor = m_startFrame.Pivot - axisX * (startHalf.x * sign.x) - axisY * (startHalf.y * sign.y);
				float2 offset = worldPoint - anchor;
				float ratioX = sign.x * math.dot(offset, axisX) / math.max(2f * startHalf.x, 0.001f);
				float ratioY = sign.y * math.dot(offset, axisY) / math.max(2f * startHalf.y, 0.001f);
				ratioX = math.max(ratioX, 0.01f);
				ratioY = math.max(ratioY, 0.01f);
				if (shift)
				{
					float uniform = math.max(ratioX, ratioY);
					ratioX = uniform;
					ratioY = uniform;
				}
				newScale = new float2(startScale.x * ratioX, startScale.y * ratioY);
			}
			else if (m_gizmoHandle == GizmoHandle.ScaleEdgeX)
			{
				float signX = ((m_gizmoHandleIndex == 0) ? 1f : (-1f));
				anchor = m_startFrame.Pivot - axisX * (startHalf.x * signX);
				float ratio = signX * math.dot(worldPoint - anchor, axisX) / math.max(2f * startHalf.x, 0.001f);
				newScale = new float2(startScale.x * math.max(ratio, 0.01f), startScale.y);
			}
			else
			{
				float signY = ((m_gizmoHandleIndex == 2) ? 1f : (-1f));
				anchor = m_startFrame.Pivot - axisY * (startHalf.y * signY);
				float ratio2 = signY * math.dot(worldPoint - anchor, axisY) / math.max(2f * startHalf.y, 0.001f);
				newScale = new float2(startScale.x, startScale.y * math.max(ratio2, 0.01f));
			}
			newScale = math.clamp(newScale, PartFreeTransform.MinScale, PartFreeTransform.MaxScale);
			float2 ratioScale = newScale / startScale;
			float2 center;
			if (m_gizmoHandle == GizmoHandle.ScaleCorner)
			{
				float2 sign2 = PartGizmoMath.AxisSign(m_gizmoHandleIndex);
				center = anchor + axisX * (startHalf.x * ratioScale.x * sign2.x) + axisY * (startHalf.y * ratioScale.y * sign2.y);
			}
			else if (m_gizmoHandle == GizmoHandle.ScaleEdgeX)
			{
				float signX2 = ((m_gizmoHandleIndex == 0) ? 1f : (-1f));
				center = anchor + axisX * (startHalf.x * ratioScale.x * signX2);
			}
			else
			{
				float signY2 = ((m_gizmoHandleIndex == 2) ? 1f : (-1f));
				center = anchor + axisY * (startHalf.y * ratioScale.y * signY2);
			}
			float2 cell = new float2(m_startPartTransform.X, m_startPartTransform.Y);
			float2 offset2 = center - cell;
			EnsureGizmoRecord("缩放部件");
			scene.Unmanaged.TrySetPartFree(entity, new PartFreeTransform
			{
				Offset = offset2,
				RotationDegrees = m_startFree.RotationDegrees,
				Scale = newScale
			});
		}

		private void ContinueMachineScale(PartScene scene, Vector2 world, bool shift)
		{
			float startDistance = Vector2.Distance(m_dragStartWorld, (Vector2)m_startFrame.Pivot);
			if (startDistance < 0.25f)
			{
				return;
			}
			float distance = Vector2.Distance(world, (Vector2)m_startFrame.Pivot);
			if (shift)
			{
				distance = Mathf.Round(distance / 0.25f) * 0.25f;
			}
			float target = math.clamp(distance / startDistance, PartFreeTransform.MinScale, PartFreeTransform.MaxScale);
			if (Mathf.Abs(target - m_appliedScale) < 0.0005f)
			{
				return;
			}
			EnsureGizmoRecord("整车自由缩放");
			scene.Unmanaged.TryScaleAllPartsFree(target / m_appliedScale, m_startFrame.Pivot);
			m_appliedScale = target;
		}

		private static bool TryGetWorld(Vector2 screenPoint, out Vector2 world)
		{
			world = default(Vector2);
			SceneCamera sceneCamera = SceneCamera.Instance;
			if (!(sceneCamera != null) || !(sceneCamera.Camera != null))
			{
				return false;
			}
			Vector3 vector = sceneCamera.Camera.ScreenToWorldPoint(new Vector3(screenPoint.x, screenPoint.y, 0f));
			world = new Vector2(vector.x, vector.y);
			return true;
		}

		private static bool TryGetCell(in SceneScreen.PointerData pointer, out int x, out int y)
		{
			x = 0;
			y = 0;
			SceneCamera sceneCamera = SceneCamera.Instance;
			if (!(sceneCamera != null) || !(sceneCamera.Camera != null))
			{
				return false;
			}
			Vector3 vector = sceneCamera.Camera.ScreenToWorldPoint(new Vector3(pointer.Point.x, pointer.Point.y, 0f));
			x = (int)MathF.Round(vector.x);
			y = (int)MathF.Round(vector.y);
			return true;
		}

		private static PartScene ActiveScene()
		{
			PartManager instance = PartManager.Instance;
			if (!(instance != null) || !instance.HasActiveScene())
			{
				return null;
			}
			return instance.ActiveScene;
		}
	}
}
