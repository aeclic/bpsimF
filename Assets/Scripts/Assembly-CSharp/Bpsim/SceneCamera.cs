using System;
using Bpsim.Parts;
using Bpsim.UI;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim
{
	public class SceneCamera : UnitySingleton<SceneCamera>
	{
		[SerializeField]
		private float m_minSize;

		[SerializeField]
		private float m_maxSize;

		private Camera m_camera;

		private Vector2 m_targetPoint;

		private Vector2 m_movementVelocity;

		private float m_scalingSpeed;

		private float m_targetSize;

		private Vector2 m_originalCameraTargetPoint;

		public Camera Camera => m_camera;

		/// <summary>最小正交尺寸（最近）。镜头滑条的上端与滚轮缩放的夹取都用它。</summary>
		public float MinSize => m_minSize;

		/// <summary>最大正交尺寸（最远）。正交尺寸越大看到的范围越大。</summary>
		public float MaxSize => m_maxSize;

		public Vector2 TargetPoint => m_targetPoint;

		public float TargetSize => m_targetSize;

		public new static SceneCamera Instance => UnitySingleton<SceneCamera>.Instance;

		public void MoveTo(Vector2 position)
		{
			m_targetPoint = position;
			m_movementVelocity = Vector2.zero;
			base.transform.position = m_targetPoint.WithZ(base.transform.position.z);
		}

		public void ScaleTo(float size)
		{
			m_targetSize = size;
			m_scalingSpeed = 0f;
			m_camera.orthographicSize = size;
		}

		/// <summary>
		/// Smooth zoom to a target orthographic size: only writes the target of the
		/// LateUpdate zoom spring, so the camera eases over instead of snapping.
		/// Unlike <see cref="ScaleTo"/> it never touches orthographicSize -- this is what
		/// the build-screen magnifier button uses to mimic the BPSS overview zoom-out.
		/// </summary>
		public void ZoomTo(float size)
		{
			m_targetSize = Math.Clamp(size, m_minSize, m_maxSize);
			m_scalingSpeed = 0f;
		}

		protected override void Awake()
		{
			base.Awake();
			m_camera = GetComponent<Camera>();
			m_targetPoint = base.transform.position;
			m_targetSize = m_camera.orthographicSize;
		}

		private void Start()
		{
			PartManager.Instance.SceneSelected += OnSceneSelected;
		}

		private bool CanMove()
		{
			if (UserInterface.Instance.PartOperator.Mode != PartOperationMode.View)
			{
				return PartManager.Instance.IsSimulating;
			}
			return true;
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
		/// The magnifier is the build-screen equivalent of BPLE's preview camera.
		/// While it is open, the primary pointer must pan the canvas even when
		/// QuickBuild is enabled or a build operation mode is selected.
		/// </summary>
		private static bool IsMagnifierPreviewing
		{
			get
			{
				UserInterface userInterface = UserInterface.Instance;
				return userInterface != null && userInterface.IsMagnifierPreviewing;
			}
		}

		private static bool IsMiddleButtonPanEnabled
		{
			get
			{
				InteractionSettings currentInteractionSettings = CurrentInteractionSettings;
				if (currentInteractionSettings != null)
				{
					return currentInteractionSettings.MiddleButtonPan;
				}
				return true;
			}
		}

		private void OnSceneSelected(PartScene partScene)
		{
			MoveTo(partScene.State.CameraPosition);
			ScaleTo(partScene.State.CameraSize);
		}

		private void Update()
		{
			// 用不受时间倍速影响的时间：模拟里的倍速滑条会把 Time.timeScale 压到 0（暂停），
			// 那时 Time.deltaTime 是 0，下面 0.5f / deltaTime 会算出 Infinity。
			float deltaTime = Time.unscaledDeltaTime;
			bool isSimulating = PartManager.Instance.IsSimulating;
			SceneScreen obj = (isSimulating ? SimulationInterface.Instance.SceneScreen : UserInterface.Instance.SceneScreen);
			SceneScreen.PointerData previousPointer = obj.PreviousPointer;
			SceneScreen.PointerData pointer = obj.Pointer;
			bool magnifierPreviewing = !isSimulating && IsMagnifierPreviewing;
			bool flag = !isSimulating && IsQuickBuildEnabled && !magnifierPreviewing;
			bool flag2 = !isSimulating && IsMiddleButtonPanEnabled && pointer.IsMiddlePressed && previousPointer.IsMiddlePressed;
			bool flag3 = pointer.IsPressed && previousPointer.IsPressed && (magnifierPreviewing || CanMove()) && !flag;
			if (flag2 || flag3)
			{
				Vector2 vector2 = m_camera.ScreenToWorldPoint(pointer.Point) - m_camera.ScreenToWorldPoint(previousPointer.Point);
				m_targetPoint -= vector2;
				m_movementVelocity = 0.5f / deltaTime * vector2 + 0.5f * m_movementVelocity;
			}
			else
			{
				if (m_movementVelocity.magnitude > 0.1f)
				{
					m_movementVelocity *= MathF.Exp(-5f * deltaTime);
				}
				else
				{
					m_movementVelocity = Vector2.zero;
				}
				m_targetPoint -= m_movementVelocity * deltaTime;
			}
			float num = 0.1f * m_targetSize + 1f;
			m_targetSize = Math.Clamp(m_targetSize - num * pointer.Scroll, m_minSize, m_maxSize);
		}

		private void LateUpdate()
		{
			if (PartManager.Instance.IsSimulating)
			{
				ComponentLookup<PartTypeValue> componentLookup = PartManager.Instance.System.GetComponentLookup<PartTypeValue>(isReadOnly: true);
				Entity entity = PartManager.Instance.PartSimulator.Unmanaged.FindLastPart(componentLookup, PartType.Pig);
				EntityManager entityManager = PartManager.Instance.System.EntityManager;
				if (entity != Entity.Null && entityManager.HasComponent<LocalToWorld>(entity))
				{
					// 这里必须读物理插值后的 LocalToWorld：SceneRenderSystem.CopyPropertiesJob 上传给 GPU 的就是
					// 这个矩阵（SmoothRigidBodiesGraphicalMotion 每帧把它平滑到两次定步长之间），而 LocalTransform
					// 只在定步长里跳变。相机读 LocalTransform 会让整个世界相对镜头一顿一顿地抽搐
					// （bpss / 旧 bpsim 没有图形插值，所以那时看不出来）。
					LocalToWorld localToWorld = entityManager.GetComponentData<LocalToWorld>(entity);
					Vector2 vector2 = new Vector2(localToWorld.Value.c3.x, localToWorld.Value.c3.y);
					if (float.IsNaN(m_originalCameraTargetPoint.x))
					{
						m_originalCameraTargetPoint = vector2;
					}
					base.transform.position = (m_targetPoint + vector2 - m_originalCameraTargetPoint).WithZ(base.transform.position.z);
				}
				else
				{
					m_originalCameraTargetPoint.x = float.NaN;
					base.transform.position = m_targetPoint.WithZ(base.transform.position.z);
				}
			}
			else
			{
				m_originalCameraTargetPoint.x = float.NaN;
				base.transform.position = m_targetPoint.WithZ(base.transform.position.z);
			}
			// 同上：镜头缩放的弹簧也不吃时间倍速，否则 0.1x 时镜头几乎不动。
			float deltaTime = Time.unscaledDeltaTime;
			for (float num = 0f; num < deltaTime; num += 0.02f)
			{
				float num2 = Math.Min(0.02f, deltaTime - num);
				float orthographicSize = m_camera.orthographicSize;
				float num3 = 144f * (m_targetSize - orthographicSize) - 24f * m_scalingSpeed;
				m_scalingSpeed += num3 * num2;
				m_camera.orthographicSize = orthographicSize + m_scalingSpeed * num2;
			}
		}
	}
}
