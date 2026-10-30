using System;
using Bpsim.ComponentModel;
using Bpsim.UI;
using Unity.Burst;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.Parts
{
	public class PartDrawer
	{
		public enum Shape
		{
			None = 0,
			Circle = 1,
			Square = 2
		}

		[BurstCompile]
		private struct PlacePartsJob : IJob
		{
			public bool Overlay;

			public Segment Segment;

			public PartCreationRequest Request;

			public Reference<PartSceneUnmanaged> PartScene;

			public void Execute()
			{
				int4 bounds = Segment.GetBounds();
				int2 @int = new int2(bounds.x, bounds.y);
				int2 int2 = new int2(bounds.z, bounds.w);
				for (int i = @int.x; i <= int2.x; i++)
				{
					for (int j = @int.y; j <= int2.y; j++)
					{
						if (Segment.IsInside(new float2(i, j)))
						{
							PartScene.Value.PlacePartAt(Request.WithPosition(i, j), Overlay);
						}
					}
				}
				PartScene.Value.Update();
			}
		}

		[BurstCompile]
		private struct ErasePartsJob : IJob
		{
			public PartType PartType;

			public int PartIndex;

			public Segment Segment;

			public Reference<PartSceneUnmanaged> PartScene;

			public void Execute()
			{
				int4 bounds = Segment.GetBounds();
				int2 @int = new int2(bounds.x, bounds.y);
				int2 int2 = new int2(bounds.z, bounds.w);
				for (int i = @int.x; i <= int2.x; i++)
				{
					for (int j = @int.y; j <= int2.y; j++)
					{
						if (Segment.IsInside(new float2(i, j)))
						{
							PartScene.Value.RemovePartAt(i, j, PartType, PartIndex);
						}
					}
				}
				PartScene.Value.Update();
			}
		}

		public readonly struct Segment
		{
			public readonly float2 Point0;

			public readonly float2 Point1;

			public readonly Shape Shape;

			public readonly float2 Size;

			public Segment(float2 point0, float2 point1, Shape shape, float2 size)
			{
				Point0 = point0;
				Point1 = point1;
				Shape = shape;
				Size = size;
			}

			public int4 GetBounds()
			{
				int2 xy = (int2)math.ceil(math.min(Point0, Point1) - 0.5f * math.cmax(Size));
				int2 zw = (int2)math.floor(math.max(Point0, Point1) + 0.5f * math.cmax(Size));
				return new int4(xy, zw);
			}

			public bool IsInside(float2 value)
			{
				float2 y = value - Point0;
				float2 halfSize = 0.5f * Size;
				float num = math.distance(Point0, Point1);
				if (num < 1E-05f)
				{
					return Shape switch
					{
						Shape.None => false, 
						Shape.Circle => math.distancesq(value, Point0) < halfSize.x * halfSize.x, 
						Shape.Square => math.abs(y.x) < halfSize.x && math.abs(y.y) < halfSize.y, 
						_ => false, 
					};
				}
				var (num4, x) = vector.invrotate((Point1 - Point0) / num, y);
				switch (Shape)
				{
				case Shape.None:
				{
					float num5 = num4 / num;
					if (num5 > -0.1f && num5 < 1.1f)
					{
						return math.abs(x) < 0.5f * Size.x;
					}
					return false;
				}
				case Shape.Circle:
				{
					float num5 = num4 / num;
					if (num5 >= 0f)
					{
						if (num5 <= 1f)
						{
							return math.abs(x) < halfSize.x;
						}
						return math.distancesq(value, Point1) < halfSize.x * halfSize.x;
					}
					if (num5 < 0f)
					{
						return math.distancesq(value, Point0) < halfSize.x * halfSize.x;
					}
					return false;
				}
				case Shape.Square:
					return IntersectRect(value - Point0, value - Point1, halfSize);
				default:
					return false;
				}
			}

			private static bool IntersectRect(float2 point0, float2 point1, float2 halfSize)
			{
				float num = point1.x - point0.x;
				float num2 = point1.y - point0.y;
				if (math.abs(num) < 1E-05f)
				{
					if (math.abs(point0.x) < halfSize.x && math.min(point0.y, point1.y) < halfSize.y)
					{
						return math.max(point0.y, point1.y) > 0f - halfSize.y;
					}
					return false;
				}
				if (math.abs(num2) < 1E-05f)
				{
					if (math.abs(point0.y) < halfSize.y && math.min(point0.x, point1.x) < halfSize.x)
					{
						return math.max(point0.x, point1.x) > 0f - halfSize.x;
					}
					return false;
				}
				float num3 = math.sign(num) * halfSize.x;
				float num4 = math.sign(num2) * halfSize.y;
				float num5 = math.max(0f, math.max((0f - num3 - point0.x) / num, (0f - num4 - point0.y) / num2));
				float num6 = math.min(1f, math.min((num3 - point0.x) / num, (num4 - point0.y) / num2));
				return num5 < num6;
			}
		}

		private PartDrawingSettings m_settings;

		private bool m_selecting;

		private Vector2Int m_selectionStart;

		private DependencyProperty<RectInt> m_selection;

		/// <summary>长按判定阈值（秒），与 BPSS <c>ConstructionUI.PartLongPressDuration</c> 一致。</summary>
		private const float PartLongPressDuration = 0.25f;

		/// <summary>拖动判定阈值（1080p 下约 8 像素），随屏幕高度缩放，与 BPSS 的触屏/鼠标双阈值同义。</summary>
		private const float PartDragThresholdPixels = 8f;

		private bool m_partPressed;

		private bool m_partDragging;

		private Entity m_partPressEntity;

		private int m_partPressX;

		private int m_partPressY;

		private float m_partPressTime;

		private Vector2 m_partPressScreenPosition;

		private Vector2 m_partPressWorldPosition;

		private float2 m_partBaseOffset;

		private float m_partBaseRotationDegrees;

		private float2 m_partBaseScale;
		/// <summary>按下那一刻，手里这个件是否允许叠放进该格（框/绳/弹簧的 structural 分支）。</summary>
		private bool m_partPressPaintable;

		public PartDrawingSettings Settings => m_settings;

		public DependencyProperty<RectInt> Selection => m_selection;

		public PartDrawer()
		{
			m_settings = new PartDrawingSettings();
			m_selection = new DependencyProperty<RectInt>();
			Bind(PartManager.Instance);
		}

		private void Bind(PartManager partManager)
		{
			m_selection.Bind(delegate
			{
				if (partManager.HasActiveScene())
				{
					partManager.ActiveScene.State.Selection = m_selection.Value;
				}
			});
			PartManager.Instance.SceneSelected += OnSceneSelected;
		}

		private void OnSceneSelected(PartScene partScene)
		{
			m_selection.Value = partScene.State.Selection;
		}

		public void Update(PartManager partManager)
		{
			if (!partManager.HasActiveScene())
			{
				return;
			}
			UserInterface instance = UserInterface.Instance;
			PartOperationMode mode = instance.PartOperator.Mode;
			if (!partManager.IsSimulating && instance.IsMagnifierPreviewing)
			{
				// BPLE's preview state owns the canvas gesture. Cancel a pending
				// quickbuild press so a tap/drag cannot place a part underneath it.
				CancelPartInteraction(partManager.ActiveScene);
				m_selecting = false;
				return;
			}
			if (!partManager.IsSimulating && IsQuickBuildEnabled && !HasActiveBuildTool(instance))
			{
				if (mode == PartOperationMode.Select)
				{
					UpdateSelectionState(instance);
				}
				else
				{
					UpdateQuickBuild(partManager, instance, mode);
				}
				return;
			}
			switch (mode)
			{
			case PartOperationMode.Place:
			case PartOperationMode.Erase:
				PlaceOrEraseParts(partManager, instance);
				break;
			case PartOperationMode.Select:
				UpdateSelectionState(instance);
				break;
			}
		}

		private static InteractionSettings CurrentInteractionSettings
		{
			get
			{
				UserSettings instance = UserSettings.Instance;
				if (instance == null)
				{
					return null;
				}
				return instance.InteractionSettings;
			}
		}

		private static bool IsQuickBuildEnabled
		{
			get
			{
				InteractionSettings currentInteractionSettings = CurrentInteractionSettings;
				if (currentInteractionSettings != null)
				{
					return currentInteractionSettings.QuickBuild;
				}
				return true;
			}
		}

		/// <summary>
		/// 顶部工具栏激活了工具时，指针属于那个工具（平移/旋转/整车移动），
		/// 快速建造要让路，否则左键会一边移动部件一边往场上贴新件。
		/// </summary>
		private static bool HasActiveBuildTool(UserInterface userInterface)
		{
			BuildToolController buildTool = userInterface.BuildTool;
			return buildTool != null && buildTool.CurrentTool != BuildTool.None;
		}

		[RequiresSyncPoint]
		private void UpdateQuickBuild(PartManager partManager, UserInterface userInterface, PartOperationMode mode)
		{
			SceneScreen sceneScreen = userInterface.SceneScreen;
			SceneScreen.PointerData previousPointer = sceneScreen.PreviousPointer;
			SceneScreen.PointerData pointer = sceneScreen.Pointer;
			if (pointer.IsMiddlePressed || previousPointer.IsMiddlePressed)
			{
				return;
			}
			// 右键按住沿拖动连续删除（BPSS 的 secondaryDragging 语义），按下的那一帧也删一格。
			bool eraseHeld = pointer.IsSecondaryPressed && previousPointer.IsSecondaryPressed;
			bool erasePressed = pointer.IsSecondaryPressed && !previousPointer.IsSecondaryPressed;
			// 左键在擦除工具下继续擦除，其余工具（含默认的查看模式）下改为放置。
			bool toolEraseHeld = false;
			bool toolErasePressed = false;
			bool placeHeld = false;
			bool placePressed = false;
			if (pointer.IsPressed)
			{
				if (mode == PartOperationMode.Erase)
				{
					toolEraseHeld = previousPointer.IsPressed;
					toolErasePressed = !previousPointer.IsPressed;
				}
				else
				{
					placeHeld = previousPointer.IsPressed;
					placePressed = !previousPointer.IsPressed;
				}
			}
			bool erasing = eraseHeld || erasePressed || toolEraseHeld || toolErasePressed;
			bool placing = placeHeld || placePressed;
			// 指针落在已有部件上时，左键先归「部件交互」（短按旋转 / 长按拖动），
			// 否则会一边把光标下的部件拎起来、一边又往同一格刷一个新件。
			if (UpdatePartInteraction(partManager, userInterface, mode))
			{
				return;
			}
			if (!erasing && !placing)
			{
				return;
			}
			PartScene activeScene = partManager.ActiveScene;
			Vector2 vector = SceneCamera.Instance.Camera.ScreenToWorldPoint(previousPointer.Point);
			Vector2 vector2 = SceneCamera.Instance.Camera.ScreenToWorldPoint(pointer.Point);
			float2 gridlessOffset = float2.zero;
			bool singleCell = erasePressed || toolErasePressed || placePressed;
			if (singleCell)
			{
				// 单击那一帧没有拖动线段，取光标所在格作为落点；零长度线段若正好压在格线上，
				// IsInside 的严格小于判断会一格都不命中，所以这里先吸附到格心。
				Vector2 raw = vector2;
				vector2 = new Vector2(Mathf.Round(vector2.x), Mathf.Round(vector2.y));
				vector = vector2;
				if (placing && activeScene != null && activeScene.GridlessEnabled)
				{
					// 无网格放置：把「光标相对格心」的残余偏移记进部件，限制在 ±0.49 格内，
					// 避免偏移过大时视觉落点跨到相邻格却仍占着原格。
					gridlessOffset = new float2(Mathf.Clamp(raw.x - vector2.x, -0.49f, 0.49f), Mathf.Clamp(raw.y - vector2.y, -0.49f, 0.49f));
				}
			}
			Shape shape = m_settings.BrushShape;
			if (singleCell && shape == Shape.None)
			{
				// Shape.None 对零长度线段恒返回 false（线段带状判定），单击时退化成圆形笔刷。
				shape = Shape.Circle;
			}
			Segment segment = new Segment(vector, vector2, shape, m_settings.BrushSize);
			activeScene.Unmanaged.EntityManager.CompleteAllTrackedJobs();
			if (erasing)
			{
				IJobExtensions.Run(new ErasePartsJob
				{
					PartType = Settings.PartType,
					PartIndex = Settings.PartIndex,
					Segment = segment,
					PartScene = activeScene.UnmanagedRef
				});
			}
			if (placing)
			{
				PartSelector partSelector = userInterface.PartSelector;
				PartTypeInfo partTypeInfo = new PartTypeInfo(partSelector.PartType, partSelector.PartIndex);
				Entity entity = partManager.EntitySpawner.FindPrefab(partTypeInfo);
				if (entity != Entity.Null)
				{
					// quickbuild 放置的部件在落位那一刻按邻居自动对齐朝向（BPSS SetPartAt 的 autoalign）。
					PartCreationRequest request = new PartCreationRequest(entity, activeScene.SceneID, partTypeInfo, 0, 0, partSelector.Rotation, partSelector.Flipped).WithFreeTransform(new PartFreeTransform
					{
						Offset = gridlessOffset,
						RotationDegrees = 0f,
						Scale = new float2(1f, 1f)
					}).WithLayer(activeScene.Unmanaged.ActiveLayer).WithAutoAlign(true);
					IJobExtensions.Run(new PlacePartsJob
					{
						Overlay = Settings.Overlay,
						Segment = segment,
						Request = request,
						PartScene = activeScene.UnmanagedRef
					});
				}
			}
		}

		/// <summary>
		/// quickbuild 下的「点按旋转 / 长按拖动」部件交互（BPSS <c>ConstructionUI</c> 的 press 跟踪逻辑）。
		/// </summary>
		/// <remarks>
		/// 返回 true 表示这一帧的指针已被部件交互消费，调用方不能再往场上刷笔刷。
		/// 拖动期间只改 <see cref="PartFreeTransform.Offset" />（由 <see cref="PartFreeTransformSystem" />
		/// 的 Burst 作业每帧合成进 LocalTransform），完全不碰网格表；松手时才落一次
		/// <see cref="PartSceneUnmanaged.TryMovePartContent" />，所以拖动过程没有重建网格或邻域查询的开销。
		/// 笔刷仍然优先：开启覆盖（Overlay）或选中的是结构件（绳/弹簧，本来就允许压在已有件上）时，
		/// 以及光标停在「空框」上且选中的是可被包容件时，都把点击让给放置逻辑，避免挡住「往框里塞箱子」这类常规操作。
		/// </remarks>
		/// <summary>按下在已有部件上之后，这一下归谁。</summary>
		public enum PartPressAction
		{
			/// <summary>还看不出来：保持按下状态，继续等。</summary>
			None = 0,
			/// <summary>搬运这个部件（长按拖动）。</summary>
			Drag = 1,
			/// <summary>交还给笔刷，按原来的方式接着画。</summary>
			Paint = 2
		}

		/// <summary>
		/// 判定 quickbuild 里按下已有部件之后的归属：是把这个部件拎起来搬走，还是放行给笔刷继续画。
		///
		/// 这条分界就是「从已有部件上接着画墙」与「把墙拖走」的区别，改错了不会抛异常、只表现为手感
		/// 变差，所以抽成纯函数交给 QA 直接断言。
		/// </summary>
		/// <param name="moved">指针位移是否已越过拖动阈值。</param>
		/// <param name="held">是否已停住按住超过 <see cref="PartLongPressDuration" />。</param>
		/// <param name="paintable">手里这个件是否允许叠放进该格（框/绳/弹簧的 structural 分支）。</param>
		public static PartPressAction DecidePartPress(bool moved, bool held, bool paintable)
		{
			if (held)
			{
				// 停住满长按阈值：BPSS ConstructionUI 里这一下是打开参数面板，这里改成进入搬运。
				return PartPressAction.Drag;
			}
			if (moved)
			{
				// 能叠着放说明这一格本来就是笔刷的合法落点，指针一走就说明玩家在接着画；
				// 放不下（比如手里是轮子，这一格有铁框）则移动只能是搬运。
				return paintable ? PartPressAction.Paint : PartPressAction.Drag;
			}
			return PartPressAction.None;
		}

		private bool UpdatePartInteraction(PartManager partManager, UserInterface userInterface, PartOperationMode mode)
		{
			SceneScreen sceneScreen = userInterface.SceneScreen;
			SceneScreen.PointerData pointer = sceneScreen.Pointer;
			SceneScreen.PointerData previousPointer = sceneScreen.PreviousPointer;
			PartScene activeScene = (partManager.HasActiveScene() ? partManager.ActiveScene : null);
			if (m_partPressed && (activeScene == null || mode == PartOperationMode.Erase))
			{
				CancelPartInteraction(activeScene);
				return false;
			}
			if (activeScene == null || mode == PartOperationMode.Erase || !activeScene.Unmanaged.CanEdit)
			{
				return false;
			}
			if (!pointer.HasValue)
			{
				// 指针跑到 UI 上（PointerData.Empty）时按取消处理，避免部件卡在半拎起的状态。
				CancelPartInteraction(activeScene);
				return false;
			}
			if (pointer.IsMiddlePressed || pointer.IsSecondaryPressed || previousPointer.IsMiddlePressed || previousPointer.IsSecondaryPressed)
			{
				CancelPartInteraction(activeScene);
				return false;
			}
			bool pressed = pointer.IsPressed && !previousPointer.IsPressed;
			bool released = !pointer.IsPressed && previousPointer.IsPressed;
			if (!m_partPressed)
			{
				if (!pressed)
				{
					return false;
				}
				if (m_settings.Overlay)
				{
					// 开着覆盖时笔刷可以压掉已有部件，这时点击一律归放置。
					return false;
				}
				PartSelector partSelector = userInterface.PartSelector;
				PartTypeInfo selectedTypeInfo = new PartTypeInfo(partSelector.PartType, partSelector.PartIndex);
				// 手里是框/绳/弹簧时，笔刷允许把件叠在已有部件上（CanPlacePartAt 的 structural 分支）。
				// 这种情况不能一按下就归搬运，否则「从已有部件上接着画墙」会变成把它拖走；
				// 先记下这个状态，等指针真的移动了再决定这一下是接着画还是搬运（见下面的阈值分支）。
				m_partPressPaintable = BasePart.IsStructural(selectedTypeInfo);
				Vector2 world = SceneCamera.Instance.Camera.ScreenToWorldPoint(pointer.Point);
				if (!activeScene.Unmanaged.TryFindPartAtWorld(new float2(world.x, world.y), out Entity entity))
				{
					return false;
				}
				PartTypeInfo pickedTypeInfo = activeScene.Unmanaged.EntityManager.GetComponentData<PartTypeValue>(entity).Value;
				PartTransform pickedTransform = activeScene.Unmanaged.EntityManager.GetComponentData<PartTransform>(entity);
				if (BasePart.IsContainer(pickedTypeInfo) && BasePart.CanBeContained(selectedTypeInfo))
				{
					// 空框 + 手里是箱子：这一下是「把箱子塞进框里」，让笔刷去放。
					int pickedLayer = activeScene.Unmanaged.GetPartLayer(entity);
					if (activeScene.Unmanaged.FindPartGridAt(pickedTransform.X, pickedTransform.Y, pickedLayer, out PartGridInfo pickedGrid) && pickedGrid.HasPartContainer && !pickedGrid.HasPart)
					{
						return false;
					}
				}
				m_partPressed = true;
				m_partDragging = false;
				m_partPressEntity = entity;
				// 按下格取部件自己的格坐标，而不是指针下方的格：无网格平移后指针可能落在相邻格上。
				m_partPressX = pickedTransform.X;
				m_partPressY = pickedTransform.Y;
				m_partPressTime = 0f;
				m_partPressScreenPosition = pointer.Point;
				m_partPressWorldPosition = world;
				PartFreeTransform freeTransform = activeScene.Unmanaged.GetPartFreeTransform(entity);
				m_partBaseOffset = freeTransform.Offset;
				m_partBaseRotationDegrees = freeTransform.RotationDegrees;
				m_partBaseScale = freeTransform.Scale;
				return true;
			}
			if (!activeScene.Unmanaged.EntityManager.Exists(m_partPressEntity))
			{
				ResetPartInteraction();
				return false;
			}
			Vector2 currentWorld = SceneCamera.Instance.Camera.ScreenToWorldPoint(pointer.Point);
			if (!m_partDragging)
			{
				m_partPressTime += Time.unscaledDeltaTime;
				float threshold = PartDragThresholdPixels * Mathf.Max(0.5f, (float)Screen.height / 1080f);
				float dragDistance = (pointer.Point - m_partPressScreenPosition).magnitude;
				bool moved = dragDistance >= threshold;
				// 只有「没动」的按住才算长按：慢慢拉一条墙不该在满 0.25 秒时突然变成搬箱子。
				bool held = !moved && m_partPressTime >= PartLongPressDuration;
				switch (DecidePartPress(moved, held, m_partPressPaintable))
				{
				case PartPressAction.Drag:
					m_partDragging = true;
					BuildToolController buildTool = userInterface.BuildTool;
					if (buildTool != null)
					{
						buildTool.Record("拖动部件");
					}
					break;
				case PartPressAction.Paint:
					// 手里是框/绳/弹簧，这一格本来就能叠着放：把点击还给笔刷，按原来的方式接着画。
					CancelPartInteraction(activeScene);
					return false;
				}
			}
			if (m_partDragging)
			{
				if (released)
				{
					// 落格按「按下格 + 指针相对按下点的位移」算：拖动中部件一直带着自由偏移跟手，
					// 松手时把这段位移折算回格数，才不会因为偏移而跳一下。
					int dropX = m_partPressX + (int)Math.Round(currentWorld.x - m_partPressWorldPosition.x);
					int dropY = m_partPressY + (int)Math.Round(currentWorld.y - m_partPressWorldPosition.y);
					bool moved = activeScene.Unmanaged.TryMovePartContent(m_partPressX, m_partPressY, dropX, dropY);
					RestorePartFreeTransform(activeScene);
					Entity movedPart = m_partPressEntity;
					ResetPartInteraction();
					if (moved)
					{
						// 落位后按新邻居重算朝向（BPSS SetPartAt 的 autoalign 参数）。
						activeScene.Unmanaged.TryAutoAlignPart(movedPart);
					}
					return true;
				}
				// 拖动中只写自由偏移，让 PartFreeTransformSystem 合成到渲染变换上；网格表保持原样。
				float2 delta = new float2(currentWorld.x - m_partPressWorldPosition.x, currentWorld.y - m_partPressWorldPosition.y);
				activeScene.Unmanaged.TrySetPartFree(m_partPressEntity, new PartFreeTransform
				{
					Offset = m_partBaseOffset + delta,
					RotationDegrees = m_partBaseRotationDegrees,
					Scale = m_partBaseScale
				});
				return true;
			}
			if (released)
			{
				// 短按：步进一个朝向单位（BPSS ConstructionUI 的 pointer.up 分支）。
				BuildToolController buildTool2 = userInterface.BuildTool;
				if (buildTool2 != null)
				{
					buildTool2.Record("旋转部件");
				}
				activeScene.Unmanaged.TryStepPartRotation(m_partPressEntity);
				ResetPartInteraction();
				return true;
			}
			return true;
		}

		/// <summary>取消正在进行的部件交互，拖动中则把自由偏移还原成按下前的值。</summary>
		private void CancelPartInteraction(PartScene partScene)
		{
			if (!m_partPressed)
			{
				return;
			}
			if (m_partDragging)
			{
				RestorePartFreeTransform(partScene);
			}
			ResetPartInteraction();
		}

		private void RestorePartFreeTransform(PartScene partScene)
		{
			if (partScene == null || m_partPressEntity == Entity.Null)
			{
				return;
			}
			if (!partScene.Unmanaged.EntityManager.Exists(m_partPressEntity))
			{
				return;
			}
			partScene.Unmanaged.TrySetPartFree(m_partPressEntity, new PartFreeTransform
			{
				Offset = m_partBaseOffset,
				RotationDegrees = m_partBaseRotationDegrees,
				Scale = m_partBaseScale
			});
		}

		private void ResetPartInteraction()
		{
			m_partPressed = false;
			m_partDragging = false;
			m_partPressEntity = Entity.Null;
			m_partPressX = 0;
			m_partPressY = 0;
			m_partPressTime = 0f;
			m_partPressPaintable = false;
		}

		private void UpdateSelectionState(UserInterface userInterface)
		{
			SceneScreen sceneScreen = userInterface.SceneScreen;
			if (sceneScreen.Pointer.IsPressed)
			{
				Vector2 vector = SceneCamera.Instance.Camera.ScreenToWorldPoint(sceneScreen.Pointer.Point);
				Vector2Int vector2Int = new Vector2Int((int)Math.Round(vector.x), (int)Math.Round(vector.y));
				if (!m_selecting)
				{
					m_selecting = true;
					m_selectionStart = vector2Int;
					m_selection.Value = default(RectInt);
				}
				else if ((float)(m_selectionStart - vector2Int).sqrMagnitude >= 1f)
				{
					int num = Math.Min(m_selectionStart.x, vector2Int.x);
					int num2 = Math.Min(m_selectionStart.y, vector2Int.y);
					int num3 = Math.Max(m_selectionStart.x, vector2Int.x);
					int num4 = Math.Max(m_selectionStart.y, vector2Int.y);
					m_selection.Value = new RectInt(num, num2, num3 - num + 1, num4 - num2 + 1);
				}
			}
			else
			{
				m_selecting = false;
			}
		}

		[RequiresSyncPoint]
		private void PlaceOrEraseParts(PartManager partManager, UserInterface userInterface)
		{
			PartOperationMode mode = userInterface.PartOperator.Mode;
			SceneScreen sceneScreen = userInterface.SceneScreen;
			if (sceneScreen.PreviousPointer.IsPressed && sceneScreen.Pointer.IsPressed)
			{
				PartScene activeScene = partManager.ActiveScene;
				PartSelector partSelector = userInterface.PartSelector;
				PartTypeInfo partTypeInfo = new PartTypeInfo(partSelector.PartType, partSelector.PartIndex);
				Vector2 vector = SceneCamera.Instance.Camera.ScreenToWorldPoint(sceneScreen.PreviousPointer.Point);
				Vector2 vector2 = SceneCamera.Instance.Camera.ScreenToWorldPoint(sceneScreen.Pointer.Point);
				Segment segment = new Segment(vector, vector2, m_settings.BrushShape, m_settings.BrushSize);
				activeScene.Unmanaged.EntityManager.CompleteAllTrackedJobs();
				if (mode == PartOperationMode.Place)
				{
					Entity entity = partManager.EntitySpawner.FindPrefab(partTypeInfo);
					PartCreationRequest request = new PartCreationRequest(entity, activeScene.SceneID, partTypeInfo, 0, 0, partSelector.Rotation, partSelector.Flipped);
					IJobExtensions.Run(new PlacePartsJob
					{
						Overlay = Settings.Overlay,
						Segment = segment,
						Request = request,
						PartScene = activeScene.UnmanagedRef
					});
				}
				else
				{
					IJobExtensions.Run(new ErasePartsJob
					{
						PartType = Settings.PartType,
						PartIndex = Settings.PartIndex,
						Segment = segment,
						PartScene = activeScene.UnmanagedRef
					});
				}
			}
		}
	}
}
