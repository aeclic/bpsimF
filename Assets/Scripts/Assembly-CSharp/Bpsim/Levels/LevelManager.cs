using System;
using System.Collections;
using System.IO;
using System.Text;
using Bpsim.Parts;
using UnityEngine;

namespace Bpsim.Levels
{
	/// <summary>
	/// Owns the imported BPLE level: loads it, drops the level's own starter contraption
	/// into a fresh bpsim scene, frames the camera on it and tears everything down again.
	/// </summary>
	internal sealed class LevelManager : UnitySingleton<LevelManager>
	{
		private LevelImportResult m_current;

		private LevelEntryJson m_entry;

		private PartScene m_scene;

		private bool m_loading;

		public LevelImportResult Current => m_current;

		public LevelEntryJson Entry => m_entry;

		public bool HasLevel => m_current != null;

		public PartScene Scene => m_scene;

		public new static LevelManager Instance => UnitySingleton<LevelManager>.Instance;

		/// <summary>Raised after a level is imported or unloaded.</summary>
		public event Action Changed;

		private void Start()
		{
			if (PartManager.Instance != null)
			{
				PartManager.Instance.SceneUnloaded += OnSceneUnloaded;
			}
			if (!LevelSession.AutoRestore || (!LevelSession.HasLevel && !LevelSession.HasScene))
			{
				return;
			}
			StartCoroutine(RestoreSession());
		}

		/// <summary>
		/// Drops the recorded schematics file when its scene tab is closed, so quitting with
		/// the tab already gone does not bring the same file back on the next launch.
		/// </summary>
		private void OnSceneUnloaded(PartScene scene)
		{
			if (scene == null)
			{
				return;
			}
			string path = LevelSession.ScenePath;
			if (!string.IsNullOrEmpty(path) && Path.GetFileNameWithoutExtension(path) == scene.SceneName)
			{
				LevelSession.ClearScene();
			}
		}

		/// <summary>
		/// Replays the previous session once <see cref="PartManager"/> has built its ECS
		/// world. <c>Boot()</c> runs at AfterSceneLoad, i.e. before the scene's
		/// <c>Start()</c> methods, so the world is not there yet on the first frame.
		/// </summary>
		private IEnumerator RestoreSession()
		{
			int guard = 0;
			while ((PartManager.Instance == null || !PartManager.Instance.IsReady) && guard++ < 600)
			{
				yield return null;
			}
			if (PartManager.Instance == null || !PartManager.Instance.IsReady)
			{
				Debug.LogWarning("[BpsimLevels] PartManager never became ready; skipping the session restore.");
				yield break;
			}
			yield return null;
			LevelSession.Restore();
		}

		public bool Load(LevelEntryJson entry, out string error)
		{
			error = null;
			if (entry == null)
			{
				error = "没有选中关卡";
				return false;
			}
			if (m_loading)
			{
				error = "正在导入，请稍候";
				return false;
			}
			m_loading = true;
			try
			{
				Unload();
				LevelImportResult result = LevelImporter.Import(entry, base.transform);
				m_current = result;
				m_entry = entry;
				if (!LoadContraption(entry, out error))
				{
					Unload();
					return false;
				}
				FrameCamera(result);
				LevelSession.RecordLevel(entry.scene);
				Changed?.Invoke();
				Debug.Log($"[BpsimLevels] '{entry.title}' 已导入：地形 {result.TerrainCount} 块，道具 {result.PropCount} 个");
				return true;
			}
			catch (Exception exception)
			{
				error = exception.Message;
				Debug.LogException(exception);
				Unload();
				return false;
			}
			finally
			{
				m_loading = false;
			}
		}

		public void Unload()
		{
			if (m_scene != null)
			{
				PartManager.Instance.UnloadScene(m_scene.SceneID);
				m_scene = null;
			}
			LevelImporter.Destroy(m_current);
			m_current = null;
			m_entry = null;
			LevelSession.ClearLevel();
			Changed?.Invoke();
		}

		/// <summary>
		/// Loads the level's own <c>Level_XX_contraption.txt</c> (the starter vehicle the
		/// game ships with the level) as a new bpsim scene. The XML dialect is exactly what
		/// <c>SchematicsXmlLoader</c> already parses.
		/// </summary>
		private bool LoadContraption(LevelEntryJson entry, out string error)
		{
			error = null;
			PartManager partManager = PartManager.Instance;
			if (partManager == null)
			{
				error = "PartManager 尚未就绪";
				return false;
			}
			string xml = LevelCatalog.ReadContraption(entry);
			if (string.IsNullOrEmpty(xml))
			{
				if (!partManager.HasActiveScene())
				{
					partManager.LoadEmptyScene(entry.scene);
				}
				return true;
			}
			PartScene created = null;
			Action<PartScene> handler = delegate(PartScene scene)
			{
				created = scene;
			};
			partManager.SceneLoaded += handler;
			try
			{
				using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(xml), writable: false);
				partManager.LoadScene(stream, entry.scene, SchematicsFormat.Xml);
			}
			catch (Exception exception)
			{
				error = "载具加载失败：" + exception.Message;
				return false;
			}
			finally
			{
				partManager.SceneLoaded -= handler;
			}
			if (created == null)
			{
				error = "载具场景没有创建";
				return false;
			}
			m_scene = created;
			partManager.SelectScene(created.SceneID);
			return true;
		}

		private void FrameCamera(LevelImportResult result)
		{
			SceneCamera camera = SceneCamera.Instance;
			if (camera == null)
			{
				return;
			}
			Vector3 extents = result.HasTerrainBounds ? result.TerrainBounds.extents : new Vector3(100f, 50f, 0f);
			float aspect = camera.Camera != null && camera.Camera.aspect > 0.01f ? camera.Camera.aspect : 16f / 9f;
			// Fit the whole level: orthographicSize is the half height, so the width
			// constraint has to be divided by the aspect ratio. A BPLE level is roughly
			// 450 x 100 world units while the starter contraption is about 7 units wide,
			// so the fit is capped: framing the entire level would shrink the vehicle to
			// a few pixels. The player can still wheel out to m_maxSize (500).
			float size = Mathf.Max(extents.y * 1.1f, extents.x * 1.1f / aspect);
			size = Mathf.Clamp(size, 25f, 55f);
			Vector2 center = new Vector2(result.Start.x, result.Start.y);
			camera.MoveTo(center);
			camera.ScaleTo(size);
			if (m_scene != null)
			{
				m_scene.State.CameraPosition = center;
				m_scene.State.CameraSize = size;
			}
		}
	}
}
