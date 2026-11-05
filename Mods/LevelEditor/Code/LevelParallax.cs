using System.Collections.Generic;
using Bpsim;
using UnityEngine;

namespace Bpsim.LevelEditor
{
	/// <summary>Original layer follow ratios applied to imported background groups, not gameplay geometry.</summary>
	public sealed class LevelParallax : MonoBehaviour
	{
		private sealed class Layer
		{
			public Transform Node;
			public Vector3 Position, Scale;
			public float Follow, Width;
			public bool Foreground, Sky;
			public readonly List<Transform> Tiles = new List<Transform>();
		}
		private readonly List<Layer> m_layers = new List<Layer>();
		private Vector3 m_anchor;
		public int LayerCount { get { return m_layers.Count; } }

		public void Initialize(Transform levelRoot, Vector3 anchor)
		{
			m_anchor = anchor;
			HashSet<Transform> selected = new HashSet<Transform>();
			foreach (Transform node in levelRoot.GetComponentsInChildren<Transform>(true))
			{
				string name = node.name.ToLowerInvariant();
				float follow;
				bool sky = name == "background_sky";
				bool foreground = name == "fglayer" || name == "bglayerforeground";
				if (sky) follow = .8f;
				else if (name.Contains("fixedfollowcamera")) follow = 1f;
				else if (name == "bglayerfurther" || name == "bglayerclouds" || name.StartsWith("background_cloud")) follow = .7f;
				else if (name == "bglayerfar" || name.StartsWith("background_moon") || name == "moon") follow = .6f;
				else if (name == "bglayernear") follow = .4f;
				else if (foreground) follow = -.4f;
				else if (name == "beachlayer" || name == "grasslayer" || name == "groundlayer" || name == "ocean") follow = 0;
				else continue;
				bool nested = false;
				for (Transform parent = node.parent; parent != null && parent != levelRoot; parent = parent.parent)
					if (selected.Contains(parent)) { nested = true; break; }
				if (nested) continue;
				Renderer[] renderers = node.GetComponentsInChildren<Renderer>(true);
				if (renderers.Length == 0) continue;
				Bounds bounds = renderers[0].bounds;
				foreach (Renderer renderer in renderers) bounds.Encapsulate(renderer.bounds);
				Layer layer = new Layer
				{
					Node = node, Position = node.position, Scale = node.localScale,
					Follow = follow, Foreground = foreground, Sky = sky, Width = bounds.size.x
				};
				selected.Add(node);
				// Existing strips repeat as a whole; no third-party textures or new materials are generated.
				if (!sky && (name.StartsWith("bglayer") || foreground || follow == 0) && layer.Width > 8f)
				{
					for (int tile = -1; tile <= 1; tile += 2)
					{
						GameObject copy = Instantiate(node.gameObject, node.parent);
						copy.name = "LevelEditorParallaxTile_" + node.name;
						copy.transform.position = node.position + new Vector3(tile * layer.Width, 0, 0);
						layer.Tiles.Add(copy.transform);
					}
				}
				m_layers.Add(layer);
			}
		}

		private void LateUpdate()
		{
			Camera camera = SceneCamera.Instance == null ? null : SceneCamera.Instance.Camera;
			if (camera == null) return;
			float absoluteX = camera.transform.position.x + m_anchor.x;
			float absoluteY = camera.transform.position.y + m_anchor.y;
			foreach (Layer layer in m_layers)
			{
				if (layer.Node == null) continue;
				Vector3 position = layer.Position + new Vector3(absoluteX * layer.Follow,
					layer.Foreground ? Mathf.Min(absoluteY * .2f, 0) : 0, 0);
				if (layer.Tiles.Count != 0)
				{
					float tile = Mathf.Round((camera.transform.position.x - position.x) / layer.Width);
					position.x += tile * layer.Width;
					for (int i = 0; i < layer.Tiles.Count; i++)
						if (layer.Tiles[i] != null) layer.Tiles[i].position = position + new Vector3((i == 0 ? -1 : 1) * layer.Width, 0, 0);
				}
				layer.Node.position = position;
				if (layer.Sky && layer.Width > .01f && camera.orthographic)
				{
					float need = camera.orthographicSize * camera.aspect * 2f + Mathf.Abs(position.x - camera.transform.position.x) * 2f + 8f;
					Vector3 scale = layer.Scale; scale.x *= Mathf.Max(1f, need / layer.Width);
					layer.Node.localScale = scale;
				}
			}
		}

		private void OnDisable()
		{
			foreach (Layer layer in m_layers)
			{
				if (layer.Node != null) { layer.Node.position = layer.Position; layer.Node.localScale = layer.Scale; }
				foreach (Transform tile in layer.Tiles)
					if (tile != null) { tile.gameObject.SetActive(false); Destroy(tile.gameObject); }
			}
			m_layers.Clear();
		}
	}
}
