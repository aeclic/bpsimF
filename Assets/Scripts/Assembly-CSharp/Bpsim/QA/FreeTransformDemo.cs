#if UNITY_EDITOR
using System.Collections;
using Bpsim.Parts;
using Bpsim.UI;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.QA
{
	/// <summary>
	/// Play-mode visual harness for the free-transform gizmo. It is not a test: it seeds the
	/// active scene with two frames, turns gridless placement on, selects the first part and
	/// switches to the part-transform tool so a screenshot shows the toolbar buttons and the
	/// gizmo exactly as the player sees them.
	///
	///   invoke_static type=Bpsim.QA.FreeTransformDemo method=Setup args=[]
	///   invoke_static type=Bpsim.QA.FreeTransformDemo method=Status args=[]
	/// </summary>
	public sealed class FreeTransformDemo : MonoBehaviour
	{
		private static FreeTransformDemo s_instance;

		private static string s_status = "idle";

		public static string Setup()
		{
			FreeTransformDemo runner = Ensure();
			s_status = "running";
			runner.StartCoroutine(runner.RunSetup());
			return "started";
		}

		public static string Status()
		{
			return s_status;
		}

		/// <summary>Switches the build tool live (0=None … 8=TransformMachine) for visual capture.</summary>
		public static string Tool(int tool)
		{
			BuildToolController controller = Object.FindObjectOfType<BuildToolController>();
			if (controller == null)
			{
				return "no controller";
			}
			controller.SetTool((BuildTool)tool);
			return "tool=" + controller.CurrentTool + " selected=" + (controller.SelectedPart != Entity.Null);
		}

		/// <summary>Selects the first part in the active scene (used after switching back to a part tool).</summary>
		public static string SelectFirst()
		{
			BuildToolController controller = Object.FindObjectOfType<BuildToolController>();
			PartManager manager = PartManager.Instance;
			if (controller == null || manager == null || !manager.HasActiveScene())
			{
				return "not ready";
			}
			PartScene scene = manager.ActiveScene;
			if (scene.Unmanaged.Parts.Length == 0)
			{
				return "no parts";
			}
			Entity part = scene.Unmanaged.Parts[0];
			bool selected = controller.SelectPart(part);
			return "selected=" + selected + " part=" + part.Index;
		}

		/// <summary>Applies a free transform to the selected part and returns its new state.</summary>
		public static string Transform(float offsetX, float offsetY, float degrees, float scaleX, float scaleY)
		{
			BuildToolController controller = Object.FindObjectOfType<BuildToolController>();
			PartManager manager = PartManager.Instance;
			if (controller == null || manager == null || !manager.HasActiveScene())
			{
				return "not ready";
			}
			PartScene scene = manager.ActiveScene;
			Entity part = controller.SelectedPart;
			if (part == Entity.Null)
			{
				return "no selection";
			}
			scene.Unmanaged.TryMovePartFree(part, new float2(offsetX, offsetY));
			scene.Unmanaged.TryRotatePartFree(part, degrees);
			scene.Unmanaged.TryScalePartFree(part, new float2(scaleX, scaleY));
			PartFreeTransform value = scene.Unmanaged.GetPartFreeTransform(part);
			return "free=" + value;
		}

		private static FreeTransformDemo Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[FreeTransformDemo]");
			gameObject.hideFlags = HideFlags.DontSave;
			Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<FreeTransformDemo>();
			return s_instance;
		}

		private IEnumerator RunSetup()
		{
			for (int i = 0; i < 600 && PartManager.Instance == null; i++)
			{
				yield return null;
			}
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				s_status = "no PartManager";
				yield break;
			}
			manager.LoadEmptyScene("FreeTransformDemo");
			yield return null;
			yield return null;
			PartScene scene = manager.HasActiveScene() ? manager.ActiveScene : null;
			if (scene == null)
			{
				s_status = "no scene";
				yield break;
			}
			scene.Unmanaged.ClearParts();
			yield return null;
			Entity first = PlaceFrame(scene, manager, 0, 0);
			Entity second = PlaceFrame(scene, manager, 4, 0);
			scene.GridlessEnabled = true;
			yield return null;
			BuildToolController controller = Object.FindObjectOfType<BuildToolController>();
			if (controller == null)
			{
				s_status = "no controller";
				yield break;
			}
			controller.GridlessEnabled = true;
			controller.SetTool(BuildTool.TransformPart);
			bool selected = controller.SelectPart(first);
			s_status = "ready parts=" + scene.Unmanaged.Parts.Length + " selected=" + selected + " second=" + (second != Entity.Null) + " tool=" + controller.CurrentTool + " gridless=" + controller.GridlessEnabled;
		}

		private static Entity PlaceFrame(PartScene scene, PartManager manager, int x, int y)
		{
			PartTypeInfo info = new PartTypeInfo(PartType.MetalFrame, 0);
			Entity prefab = manager.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null)
			{
				return Entity.Null;
			}
			PartCreationRequest request = new PartCreationRequest(prefab, scene.SceneID, info, x, y, 0, false);
			if (!scene.Unmanaged.PlacePartAt(in request, false))
			{
				return Entity.Null;
			}
			return scene.Unmanaged.FindPartAt(x, y, out var part) ? part : Entity.Null;
		}
	}
}
#endif
