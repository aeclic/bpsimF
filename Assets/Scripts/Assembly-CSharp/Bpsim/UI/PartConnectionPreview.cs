using System.Collections.Generic;
using Bpsim.Parts;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.UI
{
	/// <summary>
	/// 建造期连接预览（对应 BPRC 的 <c>INConnectionPreview</c>）：把「会焊在一起」的邻接部件
	/// 用线连起来。青色 = 同层四邻域连接；橙色 = 层 2..N 焊到下方公共层 1 的桥接。
	/// 只在无网格模式或活动层 &gt; 1 时显示，避免网格模式下的视觉噪声。
	/// 连接对的计算直接查各层独立的 <see cref="PartSceneUnmanaged"/> 占位表，
	/// 所以和 <see cref="Simulation.PartSimulator" /> 的建关节规则共享同一份图层拓扑。
	/// </summary>
	[DefaultExecutionOrder(-25)]
	internal sealed class PartConnectionPreview : MonoBehaviour
	{
		private const int PoolSize = 512;

		private const float LineZ = -0.55f;

		private static readonly Color s_sameLayerColor = new Color(0.25f, 0.9f, 1f, 0.9f);

		private static readonly Color s_bridgeColor = new Color(1f, 0.55f, 0.15f, 0.95f);

		private readonly List<LineRenderer> m_pool = new List<LineRenderer>(PoolSize);

		private Material m_material;

		private int m_used;

		private void OnDestroy()
		{
			if (m_material != null)
			{
				Object.Destroy(m_material);
			}
		}

		private void LateUpdate()
		{
			PartManager manager = PartManager.Instance;
			PartScene scene = manager?.ActiveScene;
			bool visible = manager != null && !manager.IsSimulating && scene != null && (scene.GridlessEnabled || scene.Unmanaged.ActiveLayer > PartBuildLayer.MinLayer);
			m_used = 0;
			if (!visible)
			{
				HideFrom(0);
				return;
			}
			EnsureMaterial();
			ref PartSceneUnmanaged unmanaged = ref scene.Unmanaged;
			EntityManager entityManager = unmanaged.EntityManager;
			NativeList<Entity> parts = unmanaged.Parts;
			float width = ((SceneCamera.Instance != null && SceneCamera.Instance.Camera.orthographic) ? (SceneCamera.Instance.Camera.orthographicSize * 0.008f) : 0.055f);
			width = math.clamp(width, 0.022f, 0.1f);
			for (int i = 0; i < parts.Length; i++)
			{
				Entity entity = parts[i];
				if (!entityManager.Exists(entity) || !entityManager.HasComponent<PartTransform>(entity))
				{
					continue;
				}
				PartTransform transform = entityManager.GetComponentData<PartTransform>(entity);
				byte layer = PartBuildLayerUtility.GetLayer(entityManager, entity);
				float2 center = Center(entityManager, unmanaged, entity, in transform);
				for (int d = 0; d < 4; d++)
				{
					int nx = transform.X + (d == 0 ? 1 : (d == 1 ? -1 : 0));
					int ny = transform.Y + (d == 2 ? 1 : (d == 3 ? -1 : 0));
					if (!unmanaged.FindPartGridAt(nx, ny, layer, out PartGridInfo grid))
					{
						continue;
					}
					DrawPair(entityManager, unmanaged, entity, center, grid, s_sameLayerColor, width, dedupe: true, bridge: false);
				}
				if (layer > PartBuildLayer.MinLayer)
				{
					for (int d = 0; d < 5; d++)
					{
						int bx = transform.X + (d == 1 ? 1 : (d == 2 ? -1 : 0));
						int by = transform.Y + (d == 3 ? 1 : (d == 4 ? -1 : 0));
						if (unmanaged.FindPartGridAt(bx, by, PartBuildLayer.MinLayer, out PartGridInfo below))
						{
							DrawPair(entityManager, unmanaged, entity, center, below, s_bridgeColor, width, dedupe: false, bridge: true);
						}
					}
				}
			}
			HideFrom(m_used);
		}

		private void DrawPair(EntityManager entityManager, PartSceneUnmanaged unmanaged, Entity entity, float2 center, PartGridInfo grid, Color color, float width, bool dedupe, bool bridge)
		{
			Entity other = ((grid.Part != Entity.Null) ? grid.Part : grid.PartContainer);
			if (other == Entity.Null || other == entity || !entityManager.Exists(other) || !entityManager.HasComponent<PartTransform>(other))
			{
				return;
			}
			// 同层四邻域每对只画一次；跨层桥接只有上层零件会走到这里，不需要去重。
			if (dedupe && other.Index < entity.Index)
			{
				return;
			}
			PartTransform otherTransform = entityManager.GetComponentData<PartTransform>(other);
			float2 otherCenter = Center(entityManager, unmanaged, other, in otherTransform);
			float2 delta = center - otherCenter;
			bool sameCell = math.lengthsq(delta) < 0.0001f;
			if (!bridge && sameCell)
			{
				return;
			}
			float3 from;
			float3 to;
			if (bridge)
			{
				// 跨层桥接与同层青色线在同一对格心之间会完全重合，向侧面偏一段；
				// 同格叠放没有方向可借，画成一根竖直短线表示上下堆叠。
				if (sameCell)
				{
					from = new float3(center.x, center.y + 0.16f, LineZ);
					to = new float3(center.x, center.y - 0.16f, LineZ);
				}
				else
				{
					float2 side = math.normalize(new float2(0f - delta.y, delta.x)) * 0.14f;
					from = new float3(center.x + side.x, center.y + side.y, LineZ);
					to = new float3(otherCenter.x + side.x, otherCenter.y + side.y, LineZ);
				}
			}
			else
			{
				from = new float3(center.x, center.y, LineZ);
				to = new float3(otherCenter.x, otherCenter.y, LineZ);
			}
			DrawSegment(from, to, color, width);
		}

		private static float2 Center(EntityManager entityManager, PartSceneUnmanaged unmanaged, Entity entity, in PartTransform transform)
		{
			PartFreeTransform free = entityManager.HasComponent<PartFreeTransform>(entity) ? PartFreeTransform.Sanitize(entityManager.GetComponentData<PartFreeTransform>(entity)) : PartFreeTransform.Identity;
			return new float2(transform.X + free.Offset.x, transform.Y + free.Offset.y);
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
			while (m_pool.Count <= m_used)
			{
				m_pool.Add(CreateLine());
			}
			LineRenderer line = m_pool[m_used];
			if (!line.gameObject.activeSelf)
			{
				line.gameObject.SetActive(value: true);
			}
			return line;
		}

		private LineRenderer CreateLine()
		{
			GameObject go = new GameObject("ConnectionPreview");
			go.transform.SetParent(base.transform, worldPositionStays: false);
			LineRenderer line = go.AddComponent<LineRenderer>();
			line.useWorldSpace = true;
			line.positionCount = 2;
			line.numCapVertices = 2;
			line.numCornerVertices = 0;
			line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			line.receiveShadows = false;
			line.sharedMaterial = m_material;
			line.sortingOrder = 45;
			go.SetActive(value: false);
			return line;
		}

		private void HideFrom(int index)
		{
			for (int i = index; i < m_pool.Count; i++)
			{
				GameObject go = m_pool[i].gameObject;
				if (go.activeSelf)
				{
					go.SetActive(value: false);
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
				name = "ConnectionPreviewLine",
				hideFlags = HideFlags.HideAndDontSave
			};
			m_material.color = Color.white;
		}
	}
}
