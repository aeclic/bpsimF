using System.Collections.Generic;
using Bpsim.Parts;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.UI
{
	/// <summary>
	/// 无网格建造的虚线提示层（对应 BPRC 的 <c>INMachineBuildEditor</c> 在 NoGridMode 下画的
	/// 建造区虚线框 + 每个连通组包围盒）。普通网格在无网格模式下由 <see cref="PartScene" />
	/// 隐藏，这里用一圈黄色虚线框住整个装配，并用象牙色细虚线标出互相连在一起的部件组。
	///
	/// 全部线段用池化的 <see cref="LineRenderer" /> 画在 z = -0.6（部件在 -0.1 附近），
	/// 所以不需要改任何 URP 材质，也不会被部件遮挡。
	/// </summary>
	[DefaultExecutionOrder(-30)]
	internal sealed class GridlessOverlay : MonoBehaviour
	{
		private const int PoolSize = 768;

		private const float DashLength = 0.34f;

		private const float DashGap = 0.22f;

		private const float LineZ = -0.6f;

		private static readonly Color s_buildColor = new Color(0.71f, 0.59f, 0.37f, 1f);

		private static readonly Color s_groupColor = new Color(0.93f, 0.89f, 0.79f, 0.92f);

		private static readonly Color s_originColor = new Color(0.93f, 0.89f, 0.79f, 0.45f);

		private readonly List<LineRenderer> m_pool = new List<LineRenderer>(PoolSize);

		private readonly HashSet<Entity> m_visited = new HashSet<Entity>();

		private readonly Queue<Entity> m_queue = new Queue<Entity>();

		private int m_used;

		private Material m_material;

		private Camera m_camera;

		private void Awake()
		{
			EnsureMaterial();
		}

		private void OnDestroy()
		{
			if (m_material != null)
			{
				Object.Destroy(m_material);
				m_material = null;
			}
		}

		private void LateUpdate()
		{
			PartManager manager = PartManager.Instance;
			PartScene scene = manager?.ActiveScene;
			bool visible = manager != null && !manager.IsSimulating && scene != null && scene.GridlessEnabled;
			m_used = 0;
			if (!visible)
			{
				HideFrom(0);
				return;
			}
			EnsureMaterial();
			m_camera = SceneCamera.Instance != null ? SceneCamera.Instance.Camera : null;
			float width = ((m_camera != null && m_camera.orthographic) ? (m_camera.orthographicSize * 0.008f) : 0.055f);
			width = math.clamp(width, 0.022f, 0.1f);
			DrawAssembly(scene, width);
			DrawOriginCross(width);
			HideFrom(m_used);
		}

		private void DrawAssembly(PartScene scene, float width)
		{
			ref PartSceneUnmanaged unmanaged = ref scene.Unmanaged;
			EntityManager entityManager = unmanaged.EntityManager;
			NativeList<Entity> parts = unmanaged.Parts;
			if (parts.Length == 0)
			{
				DrawRect(new Rect(-4f, -2.5f, 8f, 5f), s_buildColor, width);
				return;
			}
			m_visited.Clear();
			bool hasBounds = false;
			Rect union = default(Rect);
			int groupCount = 0;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity root = parts[i];
				if (m_visited.Contains(root) || !entityManager.Exists(root) || !entityManager.HasComponent<PartTransform>(root))
				{
					continue;
				}
				if (!CollectGroup(entityManager, ref unmanaged, root, out Rect groupBounds))
				{
					continue;
				}
				groupCount++;
				hasBounds = true;
				union = (groupCount == 1) ? groupBounds : Encapsulate(union, groupBounds);
				DrawGroupOutline(entityManager, ref unmanaged, groupBounds, width);
			}
			if (hasBounds)
			{
				// 整个装配的外框用 Brass（与 BPRC 的建造区同色）；只有一组时组框与外框合一。
				Rect expanded = Expand(union, 0.35f);
				DrawRect(expanded, s_buildColor, width);
			}
			if (groupCount == 1)
			{
				// 单组：外框已经画出，避免两条虚线重叠。
			}
		}

		private void DrawGroupOutline(EntityManager entityManager, ref PartSceneUnmanaged unmanaged, Rect bounds, float width)
		{
			DrawRect(Expand(bounds, 0.12f), s_groupColor, width);
		}

		/// <summary>按同层四邻域把相连的部件聚成一组，并计算组的世界包围盒。</summary>
		private bool CollectGroup(EntityManager entityManager, ref PartSceneUnmanaged unmanaged, Entity root, out Rect bounds)
		{
			m_queue.Clear();
			m_queue.Enqueue(root);
			m_visited.Add(root);
			bool any = false;
			bounds = default(Rect);
			while (m_queue.Count > 0)
			{
				Entity entity = m_queue.Dequeue();
				if (!entityManager.Exists(entity) || !entityManager.HasComponent<PartTransform>(entity))
				{
					continue;
				}
				PartTransform transform = entityManager.GetComponentData<PartTransform>(entity);
				PartFreeTransform free = unmanaged.GetPartFreeTransform(entity);
				PartTypeValue part = entityManager.GetComponentData<PartTypeValue>(entity);
				Rect local = PartBounds(transform, free, new PartTypeInfo(part.Type, part.Index));
				bounds = any ? Encapsulate(bounds, local) : local;
				any = true;
				byte layer = PartBuildLayerUtility.GetLayer(entityManager, entity);
				for (int d = 0; d < 4; d++)
				{
					int nx = transform.X + (d == 0 ? 1 : (d == 1 ? -1 : 0));
					int ny = transform.Y + (d == 2 ? 1 : (d == 3 ? -1 : 0));
					if (!unmanaged.FindPartGridAt(nx, ny, layer, out PartGridInfo grid))
					{
						continue;
					}
					Enqueue(entityManager, grid.PartContainer);
					Enqueue(entityManager, grid.Part);
				}
				// BPIR 规则：层 2..N 的零件还连接「正下方」层 1 的同格零件，组包围盒把它们算作同一组。
				if (layer > 1 && unmanaged.FindPartGridAt(transform.X, transform.Y, 1, out PartGridInfo below))
				{
					Enqueue(entityManager, below.PartContainer);
					Enqueue(entityManager, below.Part);
				}
			}
			return any;
		}

		private void Enqueue(EntityManager entityManager, Entity entity)
		{
			if (entity == Entity.Null || m_visited.Contains(entity) || !entityManager.Exists(entity) || !entityManager.HasComponent<PartTransform>(entity))
			{
				return;
			}
			m_visited.Add(entity);
			m_queue.Enqueue(entity);
		}

		private static Rect PartBounds(in PartTransform transform, in PartFreeTransform free, in PartTypeInfo info)
		{
			float2 scale = math.max(free.Scale, new float2(0.05f));
			RectInt cells = BasePart.GetGridRect(info);
			int width = cells.width;
			int height = cells.height;
			if ((transform.Rotation & 1) != 0)
			{
				int swap = width;
				width = height;
				height = swap;
			}
			float2 center = new float2(transform.X + free.Offset.x, transform.Y + free.Offset.y);
			float2 min = center + new float2((float)cells.xMin, (float)cells.yMin) * scale - 0.5f * scale;
			float2 max = center + new float2((float)(cells.xMax - 1), (float)(cells.yMax - 1)) * scale + 0.5f * scale;
			// 旋转交换后，宽度/高度以 leader 格为锚重新展开。
			if ((transform.Rotation & 1) != 0)
			{
				float halfWidth = 0.5f * width * scale.x;
				float halfHeight = 0.5f * height * scale.y;
				min = center - new float2(halfWidth, halfHeight);
				max = center + new float2(halfWidth, halfHeight);
			}
			return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
		}

		private void DrawOriginCross(float width)
		{
			// 原点小十字，方便在无边界的无网格画布里定位。
			float arm = 0.45f;
			DrawSegment(new Vector3(-arm, 0f, LineZ), new Vector3(-0.12f, 0f, LineZ), s_originColor, width);
			DrawSegment(new Vector3(0.12f, 0f, LineZ), new Vector3(arm, 0f, LineZ), s_originColor, width);
			DrawSegment(new Vector3(0f, -arm, LineZ), new Vector3(0f, -0.12f, LineZ), s_originColor, width);
			DrawSegment(new Vector3(0f, 0.12f, LineZ), new Vector3(0f, arm, LineZ), s_originColor, width);
		}

		private void DrawRect(Rect rect, Color color, float width)
		{
			Vector3 bl = new Vector3(rect.xMin, rect.yMin, LineZ);
			Vector3 br = new Vector3(rect.xMax, rect.yMin, LineZ);
			Vector3 tr = new Vector3(rect.xMax, rect.yMax, LineZ);
			Vector3 tl = new Vector3(rect.xMin, rect.yMax, LineZ);
			DrawDashedSide(bl, br, color, width);
			DrawDashedSide(br, tr, color, width);
			DrawDashedSide(tr, tl, color, width);
			DrawDashedSide(tl, bl, color, width);
		}

		private void DrawDashedSide(Vector3 from, Vector3 to, Color color, float width)
		{
			float length = Vector3.Distance(from, to);
			if (length < 0.02f)
			{
				return;
			}
			Vector3 direction = (to - from) / length;
			float cursor = 0f;
			while (cursor < length)
			{
				float dash = math.min(DashLength, length - cursor);
				if (dash < 0.05f)
				{
					break;
				}
				DrawSegment(from + direction * cursor, from + direction * (cursor + dash), color, width);
				cursor += DashLength + DashGap;
			}
		}

		private void DrawSegment(Vector3 from, Vector3 to, Color color, float width)
		{
			LineRenderer line = TakeLine();
			line.startWidth = width;
			line.endWidth = width;
			line.startColor = color;
			line.endColor = color;
			line.SetPosition(0, from);
			line.SetPosition(1, to);
			m_used++;
		}

		private LineRenderer TakeLine()
		{
			int index = m_used;
			while (m_pool.Count <= index)
			{
				m_pool.Add(CreateLine());
			}
			LineRenderer line = m_pool[index];
			if (!line.gameObject.activeSelf)
			{
				line.gameObject.SetActive(true);
			}
			return line;
		}

		private LineRenderer CreateLine()
		{
			GameObject go = new GameObject("GridlessDash");
			go.transform.SetParent(base.transform, worldPositionStays: false);
			LineRenderer line = go.AddComponent<LineRenderer>();
			line.useWorldSpace = true;
			line.positionCount = 2;
			line.numCapVertices = 0;
			line.numCornerVertices = 0;
			line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			line.receiveShadows = false;
			line.sharedMaterial = m_material;
			line.sortingOrder = 50;
			go.SetActive(false);
			return line;
		}

		private void HideFrom(int index)
		{
			for (int i = index; i < m_pool.Count; i++)
			{
				GameObject go = m_pool[i].gameObject;
				if (go.activeSelf)
				{
					go.SetActive(false);
				}
			}
		}

		private void EnsureMaterial()
		{
			if (m_material != null)
			{
				return;
			}
			Shader shader = Shader.Find("Sprites/Default");
			if (shader == null)
			{
				shader = Shader.Find("Universal Render Pipeline/Unlit");
			}
			m_material = new Material(shader)
			{
				name = "GridlessOverlayLine",
				hideFlags = HideFlags.HideAndDontSave
			};
			m_material.color = Color.white;
		}

		private static Rect Expand(Rect rect, float amount)
		{
			return Rect.MinMaxRect(rect.xMin - amount, rect.yMin - amount, rect.xMax + amount, rect.yMax + amount);
		}

		private static Rect Encapsulate(Rect a, Rect b)
		{
			return Rect.MinMaxRect(math.min(a.xMin, b.xMin), math.min(a.yMin, b.yMin), math.max(a.xMax, b.xMax), math.max(a.yMax, b.yMax));
		}
	}
}
