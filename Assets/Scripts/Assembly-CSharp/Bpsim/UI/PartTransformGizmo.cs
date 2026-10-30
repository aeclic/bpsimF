using System.Collections.Generic;
using Bpsim.Parts;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Bpsim.UI
{
	/// <summary>
	/// 无网格工具的 gizmo 视图：用世界空间 LineRenderer 画平移箭头、旋转圆环、
	/// 部件包围盒与四角/四边缩放手柄。所有交互数学都在 <see cref="BuildToolController" />，
	/// 这里只读 <see cref="BuildToolController.TryGetGizmoFrame" /> 给出的框。
	/// </summary>
	internal class PartTransformGizmo : MonoBehaviour
	{
		private const int RingSegments = 64;

		private BuildToolController m_controller;

		private readonly List<LineRenderer> m_lines = new List<LineRenderer>(48);

		private int m_used;

		private Material m_materialX;

		private Material m_materialY;

		private Material m_materialRing;

		private Material m_materialBounds;

		private Material m_materialHandle;

		public void Initialize(BuildToolController controller)
		{
			m_controller = controller;
			Transform parent = base.transform;
			m_materialX = CreateMaterial(new Color(0.95f, 0.28f, 0.25f, 1f));
			m_materialY = CreateMaterial(new Color(0.32f, 0.9f, 0.36f, 1f));
			m_materialRing = CreateMaterial(new Color(0.25f, 0.65f, 1f, 1f));
			m_materialBounds = CreateMaterial(new Color(1f, 1f, 1f, 0.55f));
			m_materialHandle = CreateMaterial(new Color(1f, 0.82f, 0.2f, 1f));
			for (int i = 0; i < 48; i++)
			{
				GameObject lineObject = new GameObject("GizmoLine", typeof(LineRenderer));
				lineObject.transform.SetParent(parent, false);
				LineRenderer renderer = lineObject.GetComponent<LineRenderer>();
				renderer.useWorldSpace = true;
				renderer.positionCount = 2;
				renderer.alignment = LineAlignment.View;
				renderer.textureMode = LineTextureMode.Stretch;
				renderer.numCapVertices = 2;
				renderer.shadowCastingMode = ShadowCastingMode.Off;
				renderer.receiveShadows = false;
				renderer.generateLightingData = false;
				renderer.enabled = false;
				m_lines.Add(renderer);
			}
		}

		private void OnDestroy()
		{
			DestroyMaterial(m_materialX);
			DestroyMaterial(m_materialY);
			DestroyMaterial(m_materialRing);
			DestroyMaterial(m_materialBounds);
			DestroyMaterial(m_materialHandle);
		}

		private void LateUpdate()
		{
			m_used = 0;
			PartManager manager = PartManager.Instance;
			if (m_controller == null || manager == null || manager.IsSimulating)
			{
				HideUnused();
				return;
			}
			PartScene scene = manager.ActiveScene;
			if (scene == null || !BuildToolController.IsGizmoTool(m_controller.CurrentTool) || !m_controller.TryGetGizmoFrame(scene, out var frame))
			{
				HideUnused();
				return;
			}
			SceneCamera sceneCamera = SceneCamera.Instance;
			if (sceneCamera == null || sceneCamera.Camera == null)
			{
				HideUnused();
				return;
			}
			float width = sceneCamera.Camera.orthographicSize * 2f / Mathf.Max(1, Screen.height) * 3f;
			// 缩放手柄以前只在「自由变换 / 载具变换」下画，现在部件变换也常显：
			// 那两个按钮本来共用同一套手势，唯一的差别就是这一行。
			bool includeScale = BuildToolController.GizmoIncludesScale(m_controller.CurrentTool);
			DrawFrame(frame, includeScale, width);
			HideUnused();
		}

		private void DrawFrame(in GizmoFrame frame, bool includeScale, float width)
		{
			frame.Axes(out var axisX, out var axisY);
			float2 pivot = frame.Pivot;
			DrawBox(frame, width);
			DrawLine(pivot + new float2(-0.13f, 0f), pivot + new float2(0.13f, 0f), m_materialHandle, width);
			DrawLine(pivot + new float2(0f, -0.13f), pivot + new float2(0f, 0.13f), m_materialHandle, width);
			float arrowLength = frame.ArrowLength;
			DrawArrow(pivot, axisX, arrowLength, m_materialX, width);
			DrawArrow(pivot, -axisX, arrowLength, m_materialX, width);
			DrawArrow(pivot, axisY, arrowLength, m_materialY, width);
			DrawArrow(pivot, -axisY, arrowLength, m_materialY, width);
			float ringRadius = frame.RingRadius;
			Vector3 previous = ToWorld(pivot + new float2(ringRadius, 0f));
			for (int i = 1; i <= RingSegments; i++)
			{
				float radians = math.radians(360f * (float)i / RingSegments);
				Vector3 current = ToWorld(pivot + new float2(math.cos(radians), math.sin(radians)) * ringRadius);
				DrawSegment(previous, current, m_materialRing, width);
				previous = current;
			}
			if (!includeScale)
			{
				return;
			}
			float handleSize = math.clamp(math.min(frame.HalfExtents.x, frame.HalfExtents.y) * 0.28f, 0.06f, 0.18f);
			for (int j = 0; j < 4; j++)
			{
				float2 corner = frame.Corner(j);
				DrawLine(corner + new float2(-handleSize, -handleSize), corner + new float2(handleSize, handleSize), m_materialHandle, width);
				DrawLine(corner + new float2(-handleSize, handleSize), corner + new float2(handleSize, -handleSize), m_materialHandle, width);
			}
			if (frame.Machine)
			{
				return;
			}
			for (int k = 0; k < 4; k++)
			{
				float2 edge = frame.EdgePoint(k);
				float2 tangent = ((k == 0 || k == 1) ? axisY : axisX) * handleSize;
				DrawLine(edge - tangent, edge + tangent, m_materialHandle, width);
			}
		}

		private void DrawBox(in GizmoFrame frame, float width)
		{
			for (int i = 0; i < 4; i++)
			{
				float2 start = frame.Corner(i);
				float2 end = frame.Corner((i + 1) % 4);
				DrawLine(start, end, m_materialBounds, width);
			}
		}

		private void DrawArrow(float2 origin, float2 direction, float length, Material material, float width)
		{
			float2 tip = origin + direction * length;
			DrawLine(origin, tip, material, width);
			float2 side = new float2(-direction.y, direction.x);
			float head = math.max(0.12f, length * 0.08f);
			DrawLine(tip, tip - direction * head + side * (head * 0.55f), material, width);
			DrawLine(tip, tip - direction * head - side * (head * 0.55f), material, width);
		}

		private void DrawLine(float2 start, float2 end, Material material, float width)
		{
			DrawSegment(ToWorld(start), ToWorld(end), material, width);
		}

		private void DrawSegment(Vector3 start, Vector3 end, Material material, float width)
		{
			LineRenderer line = NextLine();
			line.sharedMaterial = material;
			line.startWidth = width;
			line.endWidth = width;
			line.SetPosition(0, start);
			line.SetPosition(1, end);
			line.enabled = true;
		}

		private LineRenderer NextLine()
		{
			if (m_used >= m_lines.Count)
			{
				GameObject lineObject = new GameObject("GizmoLine", typeof(LineRenderer));
				lineObject.transform.SetParent(base.transform, false);
				LineRenderer renderer = lineObject.GetComponent<LineRenderer>();
				renderer.useWorldSpace = true;
				renderer.positionCount = 2;
				renderer.alignment = LineAlignment.View;
				renderer.numCapVertices = 2;
				renderer.shadowCastingMode = ShadowCastingMode.Off;
				renderer.receiveShadows = false;
				m_lines.Add(renderer);
			}
			return m_lines[m_used++];
		}

		private void HideUnused()
		{
			for (int i = m_used; i < m_lines.Count; i++)
			{
				if (m_lines[i].enabled)
				{
					m_lines[i].enabled = false;
				}
			}
		}

		private static Vector3 ToWorld(float2 point)
		{
			return new Vector3(point.x, point.y, -1f);
		}

		private static Material CreateMaterial(Color color)
		{
			Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
			if (shader == null)
			{
				shader = Shader.Find("Sprites/Default");
			}
			Material material = new Material(shader)
			{
				hideFlags = HideFlags.HideAndDontSave
			};
			if (material.HasProperty("_BaseColor"))
			{
				material.SetColor("_BaseColor", color);
			}
			if (material.HasProperty("_Color"))
			{
				material.SetColor("_Color", color);
			}
			material.renderQueue = 3000;
			return material;
		}

		private static void DestroyMaterial(Material material)
		{
			if (material != null)
			{
				Object.Destroy(material);
			}
		}
	}
}
