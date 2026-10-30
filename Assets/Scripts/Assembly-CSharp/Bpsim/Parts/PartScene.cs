using System;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Bpsim.Parts
{
	public class PartScene : MonoBehaviour
	{
		[SerializeField]
		private GameObject m_grid;

		[SerializeField]
		private GameObject m_largeGrid;

		private bool m_gridEnabled;

		/// <summary>
		/// 无网格建造开关（与 BPRC 的 <c>NoGridMode</c> 对应）：开启后单击放置不再吸附格心，
		/// 而是把光标相对格心的偏移记进部件的 <see cref="PartFreeTransform" />。
		/// 只影响建造期表现，不改变网格占用表。
		/// </summary>
		private bool m_gridlessEnabled;

		private int m_id;

		private string m_name;

		private World m_world;

		private PartSceneState m_state;

		private Reference<PartSceneUnmanaged> m_unmanaged;

		public int SceneID => m_id;

		public PartSceneState State => m_state;

		public ref PartSceneUnmanaged Unmanaged => ref m_unmanaged.Value;

		public Reference<PartSceneUnmanaged> UnmanagedRef => m_unmanaged;

		internal PartSceneSystem System => m_world.GetOrCreateSystemManaged<PartSceneSystem>();

		public string SceneName
		{
			get
			{
				return m_name;
			}
			set
			{
				m_name = value;
			}
		}

		public bool GridEnabled
		{
			get
			{
				return m_gridEnabled;
			}
			set
			{
				m_gridEnabled = value;
				SetGridEnabled();
			}
		}

		/// <summary>无网格建造：放置时按光标位置记录自由偏移（见 <see cref="PartFreeTransform" />）。</summary>
		public bool GridlessEnabled
		{
			get
			{
				return m_gridlessEnabled;
			}
			set
			{
				m_gridlessEnabled = value;
				SetGridEnabled();
			}
		}

		/// <summary>
		/// 网格当前是否真的画在屏幕上。工具栏那颗按钮是三态的
		/// （显示+吸附 / 隐藏 / 自由放置），三态都由
		/// <see cref="GridEnabled" /> 与 <see cref="GridlessEnabled" /> 两个开关合成，
		/// 这里就是唯一的那条合成规则 —— UI 显示的状态必须从这里读，不要各自按单个开关猜。
		/// </summary>
		public bool GridVisible => m_gridEnabled && !m_gridlessEnabled;

		/// <summary>
		/// 网格平面中心的世界 X/Y。网格按 50 单位吸附跟随相机，背景锚在这里就会和网格一起走，
		/// 而不是跟着相机连续平移。
		/// </summary>
		internal Vector2 GridAnchor
		{
			get
			{
				GameObject grid = (m_grid != null) ? m_grid : m_largeGrid;
				if (grid != null)
				{
					Vector3 position = grid.transform.position;
					return new Vector2(position.x, position.y);
				}
				return Vector2.zero;
			}
		}

		/// <summary>
		/// 网格 SpriteRenderer 所在的世界 z。自定义背景要「贴在网格平面后面」，
		/// 必须比这个值更远（相机在 -Z 方向看向 +Z，越大的 z 越远），
		/// 否则会画在部件和网格上面，看起来像浮在半空的贴纸。
		/// </summary>
		internal float GridPlaneZ
		{
			get
			{
				if (m_grid != null)
				{
					return m_grid.transform.position.z;
				}
				if (m_largeGrid != null)
				{
					return m_largeGrid.transform.position.z;
				}
				return 5f;
			}
		}

		public void Initialize(int id, World world)
		{
			m_id = id;
			m_world = world;
			m_state = new PartSceneState();
			m_unmanaged = Reference<PartSceneUnmanaged>.Allocate(Allocator.Persistent);
			m_unmanaged.Value.Initialize(id, world.Unmanaged);
		}

		private void Awake()
		{
			m_gridEnabled = true;
		}

		private void Update()
		{
		}

		private void LateUpdate()
		{
			Vector3 position = SceneCamera.Instance.transform.position;
			m_grid.transform.position = new Vector3(Round(position.x, 24.5f, 50f), Round(position.y, 24.5f, 50f), m_grid.transform.position.z);
			m_largeGrid.transform.position = new Vector3(Round(position.x, -0.5f, 50f), Round(position.y, -0.5f, 50f), m_largeGrid.transform.position.z);
			SetGridEnabled();
			Transform transform = base.transform.Find("Selection");
			transform.gameObject.SetActive(m_state.HasSelection);
			if (m_state.HasSelection)
			{
				RectInt selection = m_state.Selection;
				transform.position = new Vector3((float)selection.xMin + 0.5f * ((float)selection.width - 1f), (float)selection.yMin + 0.5f * ((float)selection.height - 1f), transform.position.z);
				transform.localScale = new Vector3(selection.width, selection.height, 1f);
			}
		}

		private void OnDestroy()
		{
			if (m_world.IsCreated)
			{
				m_unmanaged.Value.OnDestroy();
			}
			m_unmanaged.Value.Dispose();
			Reference<PartSceneUnmanaged>.Free(m_unmanaged, Allocator.Persistent);
		}

		private void SetGridEnabled()
		{
			bool gridVisible = GridVisible;
			float orthographicSize = SceneCamera.Instance.Camera.orthographicSize;
			m_grid.SetActive(gridVisible && orthographicSize < 50f);
			m_largeGrid.SetActive(gridVisible);
		}

		private static float Round(float value, float offset, float scale)
		{
			return MathF.Round((value - offset) / scale) * scale + offset;
		}
	}
}
