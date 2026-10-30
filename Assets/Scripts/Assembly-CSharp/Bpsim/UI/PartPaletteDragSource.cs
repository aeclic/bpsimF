using Bpsim.Parts;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Bpsim.UI
{
	/// <summary>
	/// Drag source for a row in the build part palette.  The normal click still
	/// selects the row; only a completed drag performs a placement.
	/// </summary>
	internal sealed class PartPaletteDragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
	{
		private PartSelector m_selector;
		private PartType m_partType;
		private bool m_dragging;

		internal void Initialize(PartSelector selector, PartType partType)
		{
			m_selector = selector;
			m_partType = partType;
		}

		public void OnBeginDrag(PointerEventData eventData)
		{
			m_dragging = eventData.button == PointerEventData.InputButton.Left && QuickBuildDrag.CanStart(m_selector);
			if (m_dragging)
			{
				eventData.useDragThreshold = false;
			}
		}

		public void OnDrag(PointerEventData eventData)
		{
			// The scene is the drop target; no visual ghost is needed because the
			// existing quickbuild preview follows the pointer once the part is placed.
		}

		public void OnEndDrag(PointerEventData eventData)
		{
			if (m_dragging && QuickBuildDrag.IsScenePoint(eventData.position))
			{
				PartTypeInfo typeInfo = m_selector.GetDragPart(m_partType, out int rotation, out bool flipped);
				QuickBuildDrag.PlaceFromPalette(typeInfo, rotation, flipped, eventData.position);
			}
			m_dragging = false;
		}
	}

	/// <summary>Shared implementation for palette-to-scene and scene-to-palette drags.</summary>
	internal static class QuickBuildDrag
	{
		internal static bool IsScenePoint(Vector2 screenPoint)
		{
			if (IsMagnifierPreviewing())
			{
				return false;
			}
			SceneScreen screen = UserInterface.Instance != null ? UserInterface.Instance.SceneScreen : null;
			if (screen == null || !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)screen.transform, screenPoint, null))
			{
				return false;
			}
			if (EventSystem.current == null)
			{
				return true;
			}
			return EventSystem.current.IsPointerOverUIObject(screenPoint, out RaycastResult hit)
				&& hit.gameObject == screen.gameObject;
		}

		internal static bool IsPalettePoint(Vector2 screenPoint)
		{
			if (IsMagnifierPreviewing())
			{
				return false;
			}
			PartSelector selector = UserInterface.Instance != null ? UserInterface.Instance.PartSelector : null;
			if (selector == null || !RectTransformUtility.RectangleContainsScreenPoint((RectTransform)selector.transform, screenPoint, null))
			{
				return false;
			}
			if (EventSystem.current == null)
			{
				return true;
			}
			return EventSystem.current.IsPointerOverUIObject(screenPoint, out RaycastResult hit)
				&& (hit.gameObject == selector.gameObject || hit.gameObject.transform.IsChildOf(selector.transform));
		}

		internal static bool CanStart(PartSelector selector)
		{
			PartManager manager = PartManager.Instance;
			return !IsMagnifierPreviewing() && selector != null && manager != null && manager.HasActiveScene() && !manager.IsSimulating
				&& manager.ActiveScene.Unmanaged.CanEdit;
		}

		internal static bool CanStartScene()
		{
			PartManager manager = PartManager.Instance;
			return !IsMagnifierPreviewing() && manager != null && manager.HasActiveScene() && !manager.IsSimulating
				&& manager.ActiveScene.Unmanaged.CanEdit;
		}

		internal static bool IsMagnifierPreviewing()
		{
			UserInterface userInterface = UserInterface.Instance;
			return userInterface != null && userInterface.IsMagnifierPreviewing;
		}

		internal static bool PlaceFromPalette(PartTypeInfo typeInfo, int rotation, bool flipped, Vector2 screenPoint)
		{
			if (IsMagnifierPreviewing())
			{
				return false;
			}
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.IsSimulating || !manager.HasActiveScene())
			{
				return false;
			}
			PartScene scene = manager.ActiveScene;
			if (!scene.Unmanaged.CanEdit)
			{
				return false;
			}
			Entity prefab = manager.EntitySpawner.FindPrefab(typeInfo);
			if (prefab == Entity.Null)
			{
				return false;
			}
			Vector3 world = SceneCamera.Instance.Camera.ScreenToWorldPoint(screenPoint);
			int x = Mathf.RoundToInt(world.x);
			int y = Mathf.RoundToInt(world.y);
			Vector2 raw = new Vector2(world.x, world.y);
			Vector2Int cell = new Vector2Int(x, y);
			Vector2 offset = scene.GridlessEnabled
				? new Vector2(Mathf.Clamp(raw.x - cell.x, -0.49f, 0.49f), Mathf.Clamp(raw.y - cell.y, -0.49f, 0.49f))
				: Vector2.zero;
			PartCreationRequest request = new PartCreationRequest(prefab, scene.SceneID, typeInfo, x, y, rotation, flipped)
				.WithFreeTransform(new PartFreeTransform
				{
					Offset = new float2(offset.x, offset.y),
					RotationDegrees = 0f,
					Scale = new float2(1f, 1f)
				})
				.WithLayer(scene.Unmanaged.ActiveLayer)
				.WithAutoAlign(true);
			scene.Unmanaged.EntityManager.CompleteAllTrackedJobs();
			// Overlay is intentional here: quickbuild palette drops replace an
			// existing part inside a frame, matching BPLE's construction UI.
			UserInterface.Instance?.BuildTool?.Record("拖放部件");
			bool placed = scene.Unmanaged.PlacePartAt(request, overlay: true);
			scene.Unmanaged.Update();
			return placed;
		}

		internal static bool RemoveScenePart(PartScene scene, Entity part)
		{
			if (IsMagnifierPreviewing())
			{
				return false;
			}
			if (scene == null || part == Entity.Null || !scene.Unmanaged.EntityManager.Exists(part))
			{
				return false;
			}
			EntityManager entityManager = scene.Unmanaged.EntityManager;
			PartTransform transform = entityManager.GetComponentData<PartTransform>(part);
			PartTypeInfo typeInfo = entityManager.GetComponentData<PartTypeValue>(part).Value;
			UserInterface.Instance?.BuildTool?.Record("删除部件");
			bool removed = scene.Unmanaged.RemovePartAt(transform.X, transform.Y, typeInfo.PartType, typeInfo.PartIndex);
			if (removed)
			{
				scene.Unmanaged.Update();
			}
			return removed;
		}
	}
}
