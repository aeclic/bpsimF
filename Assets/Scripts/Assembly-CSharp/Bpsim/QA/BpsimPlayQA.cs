#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Bpsim.Compat;
using Bpsim.Parts;
using Bpsim.Parts.Simulation;
using Bpsim.Physics;
using Bpsim.Rendering;
using Bpsim.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.Controls;
using UnityEngine.UI;

namespace Bpsim.QA
{
	/// <summary>
	/// Play-mode acceptance harness for the bpsim reconstruction.
	/// Drive it from the DshBridge:
	///   invoke_static  type=Bpsim.QA.BpsimPlayQA  method=Run  args=["all"]
	///   invoke_static  type=Bpsim.QA.BpsimPlayQA  method=Status  args=[]
	/// The scenario runs on a coroutine (so it spans frames); Status() returns the
	/// report that has been produced so far.  Everything is also appended to
	/// &lt;project&gt;/Logs/bpsim-playqa.log.
	///
	/// This file is compiled only inside the editor (UNITY_EDITOR) because it needs
	/// internal game types and is meant as an acceptance tool, not shipping code.
	/// </summary>
	public sealed class BpsimPlayQA : MonoBehaviour
	{
		private static BpsimPlayQA s_instance;

		private static readonly StringBuilder s_report = new StringBuilder();

		private static string s_status = "idle";

		private static bool s_running;

		private sealed class ScenarioSpec
		{
			public string Name = "";
			public PartType Type;
			public int Index;
			public int X;
			public int Y;
			public int Rotation;
		}

		public static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-playqa.log"));

		private static BpsimPlayQA Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[BpsimPlayQA]");
			gameObject.hideFlags = HideFlags.DontSave;
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<BpsimPlayQA>();
			return s_instance;
		}

		/// <summary>Starts a scenario.  Returns immediately; poll Status().</summary>
		public static string Run(string scenario)
		{
			if (s_running)
			{
				return "busy: " + s_status;
			}
			s_running = true;
			s_status = "running:" + scenario;
			s_report.Length = 0;
			BpsimPlayQA runner = Ensure();
			Application.logMessageReceived += runner.OnLog;
			runner.StartCoroutine(runner.RunScenario(string.IsNullOrEmpty(scenario) ? "all" : scenario));
			return "started: " + scenario;
		}

		public static string Status()
		{
			return s_status + "\n" + s_report;
		}

		public static string Reset()
		{
			s_running = false;
			s_status = "idle";
			s_report.Length = 0;
			return "reset";
		}

		/// <summary>
		/// Editor-only convenience: switch the physics backend without going through the
		/// settings panel.  "UnityPhysics" / "HavokPhysics" (or 0 / 1) are both accepted.
		/// </summary>
		public static string SetSimulationType(string value)
		{
			if (UserSettings.Instance == null)
			{
				return "no UserSettings (CoreManager has not booted)";
			}
			SimulationType parsed;
			if (!Enum.TryParse<SimulationType>(value, ignoreCase: true, out parsed))
			{
				// The editor port ships without the Havok backend (licence gated), so anything
				// that is not an explicit Unity.Physics request falls back to Unity.Physics.
				parsed = SimulationType.UnityPhysics;
			}
			UserSettings.Instance.SimulationSettings.SimulationType = parsed;
			UserSettings.Save();
			return GetSimulationType();
		}

		public static string GetSimulationType()
		{
			if (UserSettings.Instance == null)
			{
				return "no UserSettings (CoreManager has not booted)";
			}
			return "SimulationType=" + UserSettings.Instance.SimulationSettings.SimulationType;
		}

		private void OnLog(string message, string stackTrace, LogType type)
		{
			if (type == LogType.Exception || type == LogType.Error)
			{
				s_report.AppendLine("[log:" + type + "] " + message);
				if (!string.IsNullOrEmpty(stackTrace))
				{
					s_report.AppendLine("    " + stackTrace.Replace("\n", "\n    "));
				}
			}
		}

		#region scenarios

		private IEnumerator RunScenario(string scenario)
		{
			Line("=== bpsim play QA: " + scenario + " @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
			yield return null;
			Line(ProbeEnvironment());
			Line(MissingScriptScan());

			if (scenario == "scan")
			{
				Finish();
				yield break;
			}

			if (scenario == "ui" || scenario == "all")
			{
				yield return UiButtons();
			}

			if (scenario == "place" || scenario == "all" || scenario == "sim")
			{
				yield return PlaceParts(scenario);
			}

			if (scenario == "sim" || scenario == "all")
			{
				yield return Simulate();
			}

			if (scenario == "phys")
			{
				yield return PhysicsProbe();
			}

			if (scenario == "fall")
			{
				yield return SimFall();
			}

			if (scenario == "switch")
			{
				yield return SimSwitch();
			}

			if (scenario == "sprite")
			{
				yield return SpriteProbe();
			}

			if (scenario == "buffer")
			{
				BufferProbe();
				Finish();
				yield break;
			}

			if (scenario == "save" || scenario == "all")
			{
				yield return SaveLoad();
			}

			if (scenario == "input")
			{
				yield return SyntheticInput();
			}

			if (scenario == "touch")
			{
				yield return TouchInput();
			}

			Finish();
		}

		private void Finish()
		{
			s_status = "done";
			WriteFile();
			Application.logMessageReceived -= OnLog;
			s_running = false;
		}

		private IEnumerator UiButtons()
		{
			UserInterface ui = UserInterface.Instance;
			if (ui == null)
			{
				Line("UI: MISSING (UserInterface.Instance == null)");
				yield break;
			}
			Line("UI: buttons under '" + ui.name + "': " + string.Join(", ", DescribeButtons(ui.transform)));
			Line("UI: mode before = " + ui.PartOperator.Mode);

			Button settings = FindButton(ui.transform, "设置");
			if (settings != null)
			{
				settings.onClick.Invoke();
				yield return null;
				yield return null;
				Line("UI: clicked 设置 -> UserSettingsPanel present = " + (FindObjectOfType(typeof(UserSettingsPanel)) != null));
			}
			else
			{
				Line("UI: 设置 button NOT FOUND");
			}

			yield return SyntheticClickByName(ui.transform, "PlaceButton");
			Line("UI: mode after clicking PlaceButton = " + ui.PartOperator.Mode);
			yield return null;
		}

		private static IEnumerable<string> DescribeButtons(Transform root)
		{
			Button[] buttons = root.GetComponentsInChildren<Button>(true);
			List<string> names = new List<string>(buttons.Length);
			for (int i = 0; i < buttons.Length && i < 40; i++)
			{
				names.Add(buttons[i].name);
			}
			return names;
		}

		private static Button FindButton(Transform root, string name)
		{
			Button[] all = root.GetComponentsInChildren<Button>(true);
			foreach (Button button in all)
			{
				if (button.name == name)
				{
					return button;
				}
			}
			return null;
		}

		private IEnumerator SyntheticClickByName(Transform root, string name)
		{
			Transform target = null;
			foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
			{
				if (t.name == name)
				{
					target = t;
					break;
				}
			}
			if (target == null)
			{
				Line("input: '" + name + "' not found");
				yield break;
			}
			RectTransform rt = target as RectTransform;
			Vector3 world = (rt != null) ? rt.TransformPoint(rt.rect.center) : target.position;
			Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, world);
			yield return SyntheticClickAt(screen, name);
		}

		private IEnumerator SyntheticClickAt(Vector2 screen, string label)
		{
			Mouse mouse = Mouse.current;
			if (mouse == null)
			{
				Line("input: no Mouse.current device (label=" + label + ")");
				yield break;
			}
			Line("input: clicking " + label + " at screen " + screen);
			InputSystem.QueueStateEvent(mouse, new MouseState
			{
				position = screen,
				delta = Vector2.zero
			});
			InputSystem.Update();
			yield return null;
			InputSystem.QueueStateEvent(mouse, new MouseState
			{
				position = screen,
				delta = Vector2.zero,
				buttons = 1
			});
			InputSystem.Update();
			yield return null;
			yield return null;
			InputSystem.QueueStateEvent(mouse, new MouseState
			{
				position = screen,
				delta = Vector2.zero,
				buttons = 0
			});
			InputSystem.Update();
			yield return null;
			yield return null;
		}

		private IEnumerator SyntheticInput()
		{
			UserInterface ui = UserInterface.Instance;
			if (ui == null)
			{
				Line("input: no UserInterface");
				yield break;
			}
			Line("input: devices = " + string.Join(", ", DescribeDevices()));
			Line("input: EventSystem module = " + DescribeEventSystem());
			yield return SyntheticClickByName(ui.transform, "PlaceButton");
			Line("input: mode after synthetic PlaceButton click = " + ui.PartOperator.Mode);

			PartManager pm = PartManager.Instance;
			if (!pm.HasActiveScene())
			{
				pm.LoadEmptyScene("InputQA");
				yield return null;
			}
			PartTypeInfo info = new PartTypeInfo(PartType.WoodenFrame, 0);
			yield return null;
			int before = pm.ActiveScene.Unmanaged.Parts.Length;
			Vector2 center = new Vector2(Screen.width * 0.62f, Screen.height * 0.5f);
			yield return SyntheticClickAt(center, "canvas");
			yield return null;
			yield return null;
			int after = pm.ActiveScene.Unmanaged.Parts.Length;
			Line("input: selector=" + ui.PartSelector.PartType + "/" + ui.PartSelector.PartIndex
				+ " mode=" + ui.PartOperator.Mode
				+ " parts before=" + before + " after canvas click=" + after + " (wanted " + info.PartType + ")");
		}

		/// <summary>
		/// 触屏路径验收：没有物理触摸屏的桌面上临时挂一个 <see cref="Touchscreen"/>，
		/// 用 TouchState 事件走一遍「手指按下 → 抬起」，看 uGUI 到底有没有收到这次点击。
		/// 这段代码验的正是 Android 上「按钮全部点不动」的那个洞：
		/// <see cref="Bpsim.Compat.BpsimInputModule" /> 以前只读 Mouse.current，
		/// 手机上根本没有鼠标设备，Process() 第一句就返回，整套 UI 对触摸全哑。
		/// </summary>
		private IEnumerator TouchInput()
		{
			UserInterface ui = UserInterface.Instance;
			if (ui == null)
			{
				Line("touch: no UserInterface");
				yield break;
			}
			Line("touch: devices before = " + DescribeDevices());
			Line("touch: EventSystem module = " + DescribeEventSystem());

			Touchscreen touchscreen = Touchscreen.current;
			bool added = false;
			if (touchscreen == null)
			{
				touchscreen = InputSystem.AddDevice<Touchscreen>();
				added = true;
				yield return null;
				yield return null;
			}
			Line("touch: touchscreen = " + touchscreen.displayName + "/" + touchscreen.layout + " (synthetic=" + added + ")");
			Line("touch: primaryTouch in touches = " + ContainsTouchControl(touchscreen, touchscreen.primaryTouch));

			Transform target = null;
			foreach (Transform child in ui.transform.GetComponentsInChildren<Transform>(true))
			{
				if (child.name == "PlaceButton")
				{
					target = child;
					break;
				}
			}
			if (target == null)
			{
				Line("touch: PlaceButton not found");
				if (added)
				{
					InputSystem.RemoveDevice(touchscreen);
				}
				yield break;
			}

			RectTransform rect = target as RectTransform;
			Vector3 world = (rect != null) ? rect.TransformPoint(rect.rect.center) : target.position;
			Vector2 screen = RectTransformUtility.WorldToScreenPoint(null, world);
			string modeBefore = ui.PartOperator.Mode.ToString();
			BpsimInputModule.DebugTouch = true;
			BpsimInputModule.DebugPressCount = 0;
			BpsimInputModule.DebugReleaseCount = 0;
			BpsimInputModule.DebugTouchFrameCount = 0;
			yield return TouchTap(touchscreen, screen, "PlaceButton");
			yield return null;
			yield return null;
			string modeAfter = ui.PartOperator.Mode.ToString();
			BpsimInputModule.DebugTouch = false;
			Line("touch: module frames=" + BpsimInputModule.DebugTouchFrameCount
				+ " presses=" + BpsimInputModule.DebugPressCount
				+ " releases=" + BpsimInputModule.DebugReleaseCount
				+ " module=" + DescribeEventSystem());
			Line("touch: PlaceButton at " + screen + " mode " + modeBefore + " -> " + modeAfter
				+ (modeBefore != modeAfter ? "  [TOUCH CLICK OK]" : "  [TOUCH CLICK LOST]"));

			if (added)
			{
				InputSystem.RemoveDevice(touchscreen);
				Line("touch: removed synthetic touchscreen");
			}
		}

		private static bool ContainsTouchControl(Touchscreen touchscreen, TouchControl control)
		{
			for (int i = 0; i < touchscreen.touches.Count; i++)
			{
				if (touchscreen.touches[i] == control)
				{
					return true;
				}
			}
			return false;
		}

		private IEnumerator TouchTap(Touchscreen touchscreen, Vector2 screen, string label)
		{
			Line("touch: tap " + label + " at " + screen);
			InputSystem.QueueStateEvent(touchscreen, new TouchState
			{
				touchId = 7,
				phase = UnityEngine.InputSystem.TouchPhase.Began,
				position = screen,
				delta = Vector2.zero,
				isPrimaryTouch = true,
				pressure = 1f
			});
			InputSystem.Update();
			Line("touch: after Began -> press=" + touchscreen.primaryTouch.press.isPressed
				+ " phase=" + touchscreen.primaryTouch.phase.ReadValue()
				+ " pos=" + touchscreen.primaryTouch.position.ReadValue()
				+ " current=" + (Touchscreen.current != null));
			yield return null;
			yield return null;
			InputSystem.QueueStateEvent(touchscreen, new TouchState
			{
				touchId = 7,
				phase = UnityEngine.InputSystem.TouchPhase.Ended,
				position = screen,
				delta = Vector2.zero,
				isPrimaryTouch = true,
				pressure = 0f
			});
			InputSystem.Update();
			yield return null;
			yield return null;
		}

		private static string DescribeDevices()
		{
			List<string> names = new List<string>();
			foreach (InputDevice device in InputSystem.devices)
			{
				names.Add(device.displayName + "/" + device.layout);
			}
			return string.Join("; ", names);
		}

		private static string DescribeEventSystem()
		{
			EventSystem es = EventSystem.current;
			if (es == null)
			{
				return "none";
			}
			List<string> modules = new List<string>();
			BaseInputModule[] mods = es.GetComponents<BaseInputModule>();
			foreach (BaseInputModule module in mods)
			{
				modules.Add(module == null ? "<missing>" : module.GetType().Name);
			}
			return es.name + " -> " + string.Join(", ", modules);
		}

		private IEnumerator PlaceParts(string scenario)
		{
			PartManager pm = PartManager.Instance;
			if (pm == null)
			{
				Line("place: PartManager missing");
				yield break;
			}
			if (!pm.HasActiveScene())
			{
				pm.LoadEmptyScene("PlayQA");
				Line("place: created empty scene 'PlayQA'");
				yield return null;
			}
			PartScene scene = pm.ActiveScene;
			if (scene == null)
			{
				Line("place: no active scene after LoadEmptyScene");
				yield break;
			}
			Line("place: scene id=" + scene.SceneID + " name='" + scene.SceneName + "' parts(before)=" + scene.Unmanaged.Parts.Length);

			List<ScenarioSpec> specs = new List<ScenarioSpec>
			{
				new ScenarioSpec { Type = PartType.WoodenFrame, X = 0, Y = 0 },
				new ScenarioSpec { Type = PartType.WoodenFrame, X = 1, Y = 0 },
				new ScenarioSpec { Type = PartType.WoodenFrame, X = 2, Y = 0 },
				new ScenarioSpec { Type = PartType.MetalFrame, X = 3, Y = 0 },
				new ScenarioSpec { Type = PartType.Pig, X = 1, Y = 1 },
				new ScenarioSpec { Type = PartType.NormalWheel, X = 0, Y = -1 },
				new ScenarioSpec { Type = PartType.NormalWheel, X = 3, Y = -1 }
			};

			int placed = 0;
			foreach (ScenarioSpec spec in specs)
			{
				PartTypeInfo info = new PartTypeInfo(spec.Type, spec.Index);
				Entity prefab = pm.EntitySpawner.FindPrefab(info);
				if (prefab == Entity.Null)
				{
					Line("place: no prefab entity for " + info.PartType + "#" + info.PartIndex);
					continue;
				}
				PartCreationRequest request = new PartCreationRequest(prefab, scene.SceneID, info, spec.X, spec.Y, spec.Rotation, false);
				bool ok;
				try
				{
					ok = scene.Unmanaged.PlacePartAt(request, overlay: false);
				}
				catch (Exception ex)
				{
					Line("place: EXCEPTION placing " + info.PartType + " at (" + spec.X + "," + spec.Y + "): " + ex);
					continue;
				}
				if (ok)
				{
					placed++;
				}
				else
				{
					Line("place: rejected " + info.PartType + " at (" + spec.X + "," + spec.Y + ")");
				}
			}
			scene.Unmanaged.Update();
			yield return null;
			Line("place: placed=" + placed + "/" + specs.Count + " parts(after)=" + scene.Unmanaged.Parts.Length);
			Line("place: types = " + DescribeParts(scene));
			Line("place: managed clones under PartFactory = " + CountManagedClones(pm));
			Line("place: transforms immediately = " + DescribeTransforms(scene));
			for (int i = 0; i < 6; i++)
			{
				yield return null;
			}
			Line("place: transforms after 6 frames = " + DescribeTransforms(scene));
		}

		private IEnumerator SpriteProbe()
		{
			SpriteManager sm = SpriteManager.Instance;
			if (sm == null)
			{
				Line("sprite: no SpriteManager");
				yield break;
			}
			Line("sprite: SpriteManager sprites=" + sm.SpriteCount);

			UnityEngine.Material material = CoreManager.Instance.Resources.LoadAsset<UnityEngine.Material>("Part_Texture_0");
			if (material == null)
			{
				Line("sprite: material Part_Texture_0 NOT FOUND");
			}
			else
			{
				Texture tex = material.mainTexture;
				Line("sprite: material shader='" + material.shader.name + "' instancing=" + material.enableInstancing
					+ " tex=" + ((tex != null) ? (tex.width + "x" + tex.height) : "null")
					+ " hasUVRect=" + material.HasProperty("_UVRect")
					+ " matUVRect=" + material.GetVector("_UVRect")
					+ " matRect=" + material.GetVector("_Rect"));
			}

			PartManager pm = PartManager.Instance;
			PartScene scene = pm.ActiveScene;
			if (scene == null || scene.Unmanaged.Parts.Length == 0)
			{
				Line("sprite: no parts");
				yield break;
			}
			EntityManager em = scene.Unmanaged.EntityManager;
			NativeList<Entity> parts = scene.Unmanaged.Parts;
			for (int i = 0; i < parts.Length && i < 6; i++)
			{
				Entity e = parts[i];
				if (!em.HasComponent<SpriteID>(e) || !em.HasComponent<MeshRect>(e) || !em.HasComponent<MeshUVRect>(e))
				{
					Line("sprite[" + i + "]: no SpriteID/MeshRect/MeshUVRect (part type without sprite)");
					continue;
				}
				int id = em.GetComponentData<SpriteID>(e).Value;
				float4 rect = em.GetComponentData<MeshRect>(e).Value;
				float4 uv = em.GetComponentData<MeshUVRect>(e).Value;
				string entry = "?";
				try
				{
					SpriteRect sprite = sm.GetSprite(id);
					entry = "V(" + sprite.VertexX.ToString("0.00") + "," + sprite.VertexY.ToString("0.00") + ","
						+ sprite.VertexW.ToString("0.00") + "," + sprite.VertexH.ToString("0.00") + ") UV("
						+ sprite.U.ToString("0.000") + "," + sprite.V.ToString("0.000") + ","
						+ sprite.W.ToString("0.000") + "," + sprite.H.ToString("0.000") + ")";
				}
				catch (Exception ex)
				{
					entry = ex.GetType().Name;
				}
				Line("sprite[" + i + "]: id=" + id + " rect=" + rect + " uv=" + uv + " -> " + entry);
			}
		}

		// Behaviour-based backend check: read the *simulator* entities (PartSimulatorUnmanaged.Parts),
		// not the build-time scene parts.  A working backend lets a free part fall ~ -4.9 m in 1 s;
		// a dead/absent solver leaves it exactly where it was.
		private IEnumerator SimFall()
		{
			PartManager pm = PartManager.Instance;
			if (pm == null)
			{
				Line("fall: PartManager missing");
				yield break;
			}
			if (pm.IsSimulating)
			{
				pm.EndSimulation();
				yield return null;
			}
			if (!pm.HasActiveScene())
			{
				pm.LoadEmptyScene("HavokQA");
				yield return null;
			}
			PartScene scene = pm.ActiveScene;
			if (scene == null || scene.Unmanaged.Parts.Length == 0)
			{
				Line("fall: no parts (run the 'place' scenario first)");
				yield break;
			}
			Line("fall: backend=" + UserSettings.Instance.SimulationSettings.SimulationType
				+ " scene parts=" + scene.Unmanaged.Parts.Length);
			try
			{
				pm.StartSimulation();
			}
			catch (Exception ex)
			{
				Line("fall: StartSimulation EXCEPTION " + ex);
				yield break;
			}
			yield return null;
			Reference<PartSimulatorUnmanaged> simRef = pm.PartSimulator.UnmanagedRef;
			NativeList<Entity> simParts = simRef.Value.Parts;
			EntityManager em = scene.Unmanaged.EntityManager;
			Line("fall: simulator parts=" + simParts.Length + " joints=" + simRef.Value.Joints.Length
				+ " components=" + simRef.Value.ConnectedComponents.Length);
			Line("fall: world " + DescribeSimulationWorld());
			Line("fall: t=0.0 " + DescribeSimParts(em, simParts, 6));
			yield return new WaitForSeconds(1.0f);
			Line("fall: t=1.0 " + DescribeSimParts(em, simParts, 6));
			// Direct velocity injection: asks the solver only "does an imposed velocity move the
			// body?".  Velocity keeping its value while the position never changes means the
			// integrator is the missing piece, not the force pipeline.
			if (simParts.Length > 0 && em.HasComponent<PhysicsVelocity>(simParts[0]))
			{
				em.SetComponentData(simParts[0], new PhysicsVelocity { Linear = new float3(0f, 5f, 0f) });
				Line("fall: injected v=(0,5) on part[0]");
			}
			yield return new WaitForSeconds(1.0f);
			Line("fall: t=2.0 " + DescribeSimParts(em, simParts, 6));
			pm.EndSimulation();
			Line("fall: done IsSimulating=" + pm.IsSimulating);
		}

		// Ordering experiment: start on UnityPhysics so Unity.Physics builds a fully populated
		// PhysicsWorld and ticks it, then flip PhysicsStep.SimulationType to HavokPhysics.
		// Falling after the flip means the Havok world is created against an empty/stale world.
		private IEnumerator SimSwitch()
		{
			PartManager pm = PartManager.Instance;
			if (pm == null)
			{
				Line("switch: PartManager missing");
				yield break;
			}
			if (pm.IsSimulating)
			{
				pm.EndSimulation();
				yield return null;
			}
			if (!pm.HasActiveScene())
			{
				pm.LoadEmptyScene("HavokQA");
				yield return null;
			}
			PartScene scene = pm.ActiveScene;
			if (scene == null || scene.Unmanaged.Parts.Length == 0)
			{
				Line("switch: no parts (run the 'place' scenario first)");
				yield break;
			}
			SimulationType original = UserSettings.Instance.SimulationSettings.SimulationType;
			UserSettings.Instance.SimulationSettings.SimulationType = SimulationType.UnityPhysics;
			Line("switch: start with UnityPhysics");
			try
			{
				pm.StartSimulation();
			}
			catch (Exception ex)
			{
				Line("switch: StartSimulation EXCEPTION " + ex);
				UserSettings.Instance.SimulationSettings.SimulationType = original;
				yield break;
			}
			Reference<PartSimulatorUnmanaged> simRef = pm.PartSimulator.UnmanagedRef;
			NativeList<Entity> simParts = simRef.Value.Parts;
			EntityManager em = scene.Unmanaged.EntityManager;
			yield return new WaitForSeconds(1.0f);
			Line("switch: unity t=1.0 " + DescribeSimParts(em, simParts, 4));
			Line("switch: world before " + DescribeSimulationWorld());
			EntityManager wem = World.DefaultGameObjectInjectionWorld.EntityManager;
			EntityQuery stepQuery = wem.CreateEntityQuery(ComponentType.ReadWrite<PhysicsStep>());
			int stepCount = stepQuery.CalculateEntityCount();
			if (stepCount != 1)
			{
				Line("switch: physicsStep count=" + stepCount);
				stepQuery.Dispose();
				pm.EndSimulation();
				UserSettings.Instance.SimulationSettings.SimulationType = original;
				yield break;
			}
			Entity stepEntity = stepQuery.GetSingletonEntity();
			PhysicsStep step = wem.GetComponentData<PhysicsStep>(stepEntity);
			step.SimulationType = SimulationType.HavokPhysics;
			wem.SetComponentData(stepEntity, step);
			stepQuery.Dispose();
			Line("switch: flipped PhysicsStep to HavokPhysics");
			yield return new WaitForSeconds(1.0f);
			Line("switch: havok t=2.0 " + DescribeSimParts(em, simParts, 4));
			Line("switch: world after " + DescribeSimulationWorld());
			yield return new WaitForSeconds(1.0f);
			Line("switch: havok t=3.0 " + DescribeSimParts(em, simParts, 4));
			pm.EndSimulation();
			UserSettings.Instance.SimulationSettings.SimulationType = original;
			Line("switch: done restored=" + original);
		}

		// Which simulation is actually driving the world, and is the Unity.Physics MotionData it
		// exports from advancing?  A Havok run that updates velocities but not MotionData means the
		// native step never got the world; a MotionData that advances while LocalTransform does not
		// means the export side is missing.
		private static string DescribeSimulationWorld()
		{
			EntityManager em = World.DefaultGameObjectInjectionWorld.EntityManager;
			StringBuilder sb = new StringBuilder();
			EntityQuery simQuery = em.CreateEntityQuery(ComponentType.ReadOnly<SimulationSingleton>());
			if (simQuery.CalculateEntityCount() == 1)
			{
				SimulationSingleton sim = simQuery.GetSingleton<SimulationSingleton>();
				sb.Append("simSingleton=").Append(sim.Type);
			}
			else
			{
				sb.Append("simSingleton=?");
			}
			simQuery.Dispose();
			EntityQuery stepQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsStep>());
			if (stepQuery.CalculateEntityCount() == 1)
			{
				PhysicsStep step = stepQuery.GetSingleton<PhysicsStep>();
				sb.Append(" step=").Append(step.SimulationType).Append(" gravity=").Append(Fmt(step.Gravity));
			}
			else
			{
				sb.Append(" step=?");
			}
			stepQuery.Dispose();
			EntityQuery worldQuery = em.CreateEntityQuery(ComponentType.ReadOnly<PhysicsWorldSingleton>());
			if (worldQuery.CalculateEntityCount() == 1)
			{
				PhysicsWorld pw = worldQuery.GetSingleton<PhysicsWorldSingleton>().PhysicsWorld;
				NativeArray<MotionData> motions = pw.MotionDatas;
				sb.Append(" motionDatas=").Append(motions.Length).Append(" dynamic=").Append(pw.NumDynamicBodies);
				for (int i = 0; i < motions.Length && i < 3; i++)
				{
					MotionData m = motions[i];
					sb.Append(" m").Append(i).Append('=').Append(Fmt(m.WorldFromMotion.pos))
						.Append(" v=").Append(Fmt(pw.MotionVelocities[i].LinearVelocity));
				}
			}
			else
			{
				sb.Append(" physicsWorld=?");
			}
			worldQuery.Dispose();
			return sb.ToString();
		}

		private static string DescribeSimParts(EntityManager em, NativeList<Entity> parts, int count)
		{
			StringBuilder sb = new StringBuilder();
			int n = math.min(count, parts.Length);
			for (int i = 0; i < n; i++)
			{
				Entity e = parts[i];
				string pos = "?";
				string vel = "?";
				string mass = "?";
				bool world = em.HasComponent<PhysicsWorldIndex>(e);
				bool joint = em.HasComponent<PartJointInfo>(e);
				try
				{
					pos = Fmt(em.GetComponentData<LocalTransform>(e).Position);
				}
				catch
				{
				}
				try
				{
					if (em.HasComponent<PhysicsVelocity>(e))
					{
						vel = Fmt(em.GetComponentData<PhysicsVelocity>(e).Linear);
					}
					else
					{
						vel = "none";
					}
				}
				catch
				{
				}
				try
				{
					if (em.HasComponent<PhysicsMass>(e))
					{
						PhysicsMass m = em.GetComponentData<PhysicsMass>(e);
						mass = (m.InverseMass > 0f) ? (1f / m.InverseMass).ToString("0.##") : "static";
					}
					else
					{
						mass = "none";
					}
				}
				catch
				{
				}
				if (i > 0)
				{
					sb.Append("; ");
				}
				sb.Append('[').Append(i).Append("] ").Append(pos).Append(" v=").Append(vel)
					.Append(" m=").Append(mass).Append(" world=").Append(world).Append(" joint=").Append(joint);
			}
			return sb.ToString();
		}

		private IEnumerator PhysicsProbe()
		{
			PartManager pm = PartManager.Instance;
			PartScene scene = pm.ActiveScene;
			if (scene == null)
			{
				Line("phys: no active scene");
				yield break;
			}
			Line("phys: before = " + DescribePhysics(scene));
			pm.StartSimulation();
			Line("phys: started, " + DescribePhysics(scene));
			for (int i = 1; i <= 4; i++)
			{
				yield return new WaitForSeconds(0.5f);
				Line("phys: t=" + (0.5f * i).ToString("0.0") + "s " + DescribePhysics(scene));
				Line("phys: t=" + (0.5f * i).ToString("0.0") + "s pos " + DescribePositions(scene));
			}
			if (pm.IsSimulating)
			{
				pm.EndSimulation();
			}
			Line("phys: end, IsSimulating=" + pm.IsSimulating);
		}

		// Build-time scene parts and simulator parts are two different entity sets: once the
		// simulation starts, the physics components live on the *simulator* clones.  Reading
		// scene.Unmanaged.Parts after StartSimulation therefore reports vel=0/mass=0 even on a
		// perfectly healthy backend, so every probe goes through this helper.
		private static NativeList<Entity> ProbeParts(PartScene scene)
		{
			PartManager pm = PartManager.Instance;
			if (pm != null && pm.IsSimulating)
			{
				try
				{
					return pm.PartSimulator.UnmanagedRef.Value.Parts;
				}
				catch (Exception)
				{
				}
			}
			return scene.Unmanaged.Parts;
		}

		private static string DescribePhysics(PartScene scene)
		{
			EntityManager em = scene.Unmanaged.EntityManager;
			NativeList<Entity> parts = ProbeParts(scene);
			int velocity = 0;
			int mass = 0;
			int collider = 0;
			int gravity = 0;
			int damping = 0;
			int worldIndex = 0;
			int constraint = 0;
			int joint = 0;
			for (int i = 0; i < parts.Length; i++)
			{
				Entity e = parts[i];
				if (em.HasComponent<PhysicsVelocity>(e)) velocity++;
				if (em.HasComponent<PhysicsMass>(e)) mass++;
				if (em.HasComponent<PhysicsCollider>(e)) collider++;
				if (em.HasComponent<PhysicsGravityFactor>(e)) gravity++;
				if (em.HasComponent<PhysicsDamping>(e)) damping++;
				if (em.HasComponent<PhysicsWorldIndex>(e)) worldIndex++;
				if (em.HasComponent<Constraint2D>(e)) constraint++;
				if (em.HasComponent<PartJointInfo>(e)) joint++;
			}
			int physicsWorldEntities = 0;
			int stepEntities = 0;
			try
			{
				EntityQuery q = em.CreateEntityQuery(new EntityQueryDesc
				{
					All = new ComponentType[] { ComponentType.ReadOnly<PhysicsWorldIndex>() },
					Any = new ComponentType[0],
					None = new ComponentType[0],
					Disabled = new ComponentType[0],
					Absent = new ComponentType[0]
				});
				physicsWorldEntities = q.CalculateEntityCount();
				q.Dispose();
				stepEntities = em.CreateEntityQuery(new EntityQueryDesc
				{
					All = new ComponentType[] { ComponentType.ReadOnly<PhysicsStep>() },
					Any = new ComponentType[0],
					None = new ComponentType[0],
					Disabled = new ComponentType[0],
					Absent = new ComponentType[0]
				}).CalculateEntityCount();
			}
			catch (Exception ex)
			{
				return "query failed: " + ex.GetType().Name + " " + ex.Message;
			}
			return "parts=" + parts.Length + " vel=" + velocity + " mass=" + mass + " collider=" + collider
				+ " gravity=" + gravity + " damping=" + damping + " worldIndex=" + worldIndex
				+ " constraint2D=" + constraint + " joints=" + joint
				+ " | worldEntities=" + physicsWorldEntities + " steps=" + stepEntities;
		}

		private static string DescribePositions(PartScene scene)
		{
			EntityManager em = scene.Unmanaged.EntityManager;
			NativeList<Entity> parts = ProbeParts(scene);
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < parts.Length && i < 8; i++)
			{
				Entity e = parts[i];
				float3 local = em.GetComponentData<LocalTransform>(e).Position;
				float3 world = em.GetComponentData<LocalToWorld>(e).Value.c3.xyz;
				float3 vel = em.HasComponent<PhysicsVelocity>(e) ? em.GetComponentData<PhysicsVelocity>(e).Linear.xyz : float3.zero;
				if (i > 0) sb.Append("; ");
				sb.Append('[').Append(i).Append("] lt=").Append(Fmt(local)).Append(" ltw=").Append(Fmt(world)).Append(" v=").Append(Fmt(vel));
			}
			return sb.ToString();
		}

		private static string Fmt(float3 v)
		{
			return "(" + v.x.ToString("0.00") + "," + v.y.ToString("0.00") + ")";
		}

		private static string DescribeTransforms(PartScene scene)
		{
			EntityManager em = scene.Unmanaged.EntityManager;
			NativeList<Entity> parts = scene.Unmanaged.Parts;
			int total = parts.Length;
			int localTransform = 0;
			int localToWorld = 0;
			int parent = 0;
			int typeValue = 0;
			for (int i = 0; i < total; i++)
			{
				Entity e = parts[i];
				if (em.HasComponent<LocalTransform>(e))
				{
					localTransform++;
				}
				if (em.HasComponent<LocalToWorld>(e))
				{
					localToWorld++;
				}
				if (em.HasComponent<Parent>(e))
				{
					parent++;
				}
				if (em.HasComponent<PartTypeValue>(e))
				{
					typeValue++;
				}
			}
			return "parts=" + total + " LocalTransform=" + localTransform + " LocalToWorld=" + localToWorld
				+ " Parent=" + parent + " PartTypeValue=" + typeValue;
		}

		private static string DescribeParts(PartScene scene)
		{
			EntityManager em = scene.Unmanaged.EntityManager;
			NativeList<Entity> parts = scene.Unmanaged.Parts;
			StringBuilder sb = new StringBuilder();
			for (int i = 0; i < parts.Length && i < 24; i++)
			{
				Entity e = parts[i];
				try
				{
					PartTypeValue type = em.GetComponentData<PartTypeValue>(e);
					PartTransform tr = em.GetComponentData<PartTransform>(e);
					if (i > 0)
					{
						sb.Append("; ");
					}
					sb.Append(type.Type).Append('#').Append(type.Index).Append('@').Append(tr.X).Append(',').Append(tr.Y);
				}
				catch (Exception ex)
				{
					sb.Append("<err ").Append(ex.GetType().Name).Append('>');
				}
			}
			return sb.ToString();
		}

		private static int CountManagedClones(PartManager pm)
		{
			Transform factory = pm.Factory.transform;
			return factory.GetComponentsInChildren<ManagedPart>(true).Length;
		}

		private IEnumerator Simulate()
		{
			PartManager pm = PartManager.Instance;
			PartScene scene = pm.ActiveScene;
			if (scene == null)
			{
				Line("sim: no active scene");
				yield break;
			}
			Line("sim: settings type=" + UserSettings.Instance.SimulationSettings.SimulationType
				+ " dt=" + UserSettings.Instance.SimulationSettings.FixedTimeStep
				+ " joints=" + UserSettings.Instance.SimulationSettings.CreateJoints
				+ " gravity=" + UserSettings.Instance.SimulationSettings.GravityY);

			NativeArray<float3> before = SnapshotPositions(scene);
			Line("sim: starting simulation with " + scene.Unmanaged.Parts.Length + " parts");
			try
			{
				pm.StartSimulation();
			}
			catch (Exception ex)
			{
				Line("sim: StartSimulation EXCEPTION " + ex);
				before.Dispose();
				yield break;
			}
			Line("sim: IsSimulating = " + pm.IsSimulating);
			yield return new WaitForSeconds(0.5f);
			yield return new WaitForSeconds(1.5f);
			Line("sim: after 2s IsSimulating = " + pm.IsSimulating);
			NativeArray<float3> after = SnapshotPositions(scene);
			Line("sim: " + DiffPositions(before, after));
			before.Dispose();
			after.Dispose();
			try
			{
				pm.EndSimulation();
				Line("sim: EndSimulation ok, IsSimulating=" + pm.IsSimulating);
			}
			catch (Exception ex)
			{
				Line("sim: EndSimulation EXCEPTION " + ex);
			}
			yield return null;
		}

		private static NativeArray<float3> SnapshotPositions(PartScene scene)
		{
			EntityManager em = scene.Unmanaged.EntityManager;
			NativeList<Entity> parts = scene.Unmanaged.Parts;
			NativeArray<float3> result = new NativeArray<float3>(parts.Length, Allocator.Persistent);
			for (int i = 0; i < parts.Length; i++)
			{
				try
				{
					result[i] = em.GetComponentData<LocalTransform>(parts[i]).Position;
				}
				catch
				{
					result[i] = new float3(float.NaN);
				}
			}
			return result;
		}

		private static string DiffPositions(NativeArray<float3> before, NativeArray<float3> after)
		{
			int moved = 0;
			int nan = 0;
			float maxDelta = 0f;
			StringBuilder sb = new StringBuilder();
			int n = Mathf.Min(before.Length, after.Length);
			for (int i = 0; i < n; i++)
			{
				float3 a = before[i];
				float3 b = after[i];
				if (float.IsNaN(b.x) || float.IsNaN(b.y))
				{
					nan++;
					continue;
				}
				float d = math.distance(a.xy, b.xy);
				if (d > 0.001f)
				{
					moved++;
				}
				if (d > maxDelta)
				{
					maxDelta = d;
				}
				if (i < 8)
				{
					sb.Append("[").Append(i).Append("] (").Append(a.x.ToString("F2")).Append(',').Append(a.y.ToString("F2"))
						.Append(")->(").Append(b.x.ToString("F2")).Append(',').Append(b.y.ToString("F2")).Append(") ");
				}
			}
			return "positions n=" + n + " moved=" + moved + " nan=" + nan + " maxDelta=" + maxDelta.ToString("F3") + "  " + sb;
		}

		private IEnumerator SaveLoad()
		{
			PartManager pm = PartManager.Instance;
			PartScene scene = pm.ActiveScene;
			if (scene == null)
			{
				Line("save: no active scene");
				yield break;
			}
			int count = scene.Unmanaged.Parts.Length;
			string path = Path.Combine(Application.persistentDataPath, "playqa-scene.csv");
			try
			{
				using (FileStream stream = File.Create(path))
				{
					pm.SaveScene(stream, scene.SceneID, SchematicsFormat.Csv);
				}
				string text = File.ReadAllText(path);
				Line("save: wrote " + count + " parts to " + path + " (" + text.Length + " bytes)");
				Line("save: header = " + (text.Length > 160 ? text.Substring(0, 160).Replace("\n", "\\n") : text.Replace("\n", "\\n")));
				using (FileStream stream = File.OpenRead(path))
				{
					pm.LoadScene(stream, "Reloaded", SchematicsFormat.Csv);
				}
				PartScene reloaded = pm.FindScene(scene.SceneID + 1);
				Line("save: reloaded scene parts = " + (reloaded != null ? reloaded.Unmanaged.Parts.Length.ToString() : "null"));
			}
			catch (Exception ex)
			{
				Line("save: EXCEPTION " + ex);
			}
			yield return null;
		}

		#endregion

		#region environment

		private static string ProbeEnvironment()
		{
			StringBuilder sb = new StringBuilder();
			sb.AppendLine("env: Unity " + Application.unityVersion + " / " + Application.platform + " / playing=" + Application.isPlaying);
			sb.AppendLine("env: product=" + Application.productName + " version=" + Application.version + " company=" + Application.companyName);
			sb.AppendLine("env: screen=" + Screen.width + "x" + Screen.height);
			sb.Append("env: CoreManager=").Append(CoreManager.Instance != null);
			sb.Append(" PartManager=").Append(PartManager.Instance != null);
			sb.Append(" UserInterface=").Append(UserInterface.Instance != null);
			sb.Append(" SimulationInterface=").Append(SimulationInterface.Instance != null);
			sb.AppendLine();
			sb.AppendLine("env: UserSettings version=" + (UserSettings.Instance != null ? UserSettings.Instance.Version.ToString() : "null"));
			World world = World.DefaultGameObjectInjectionWorld;
			sb.AppendLine("env: default world=" + (world != null ? world.Name : "null"));
			sb.AppendLine("env: input devices = " + DescribeDevices());
			sb.AppendLine("env: EventSystem = " + DescribeEventSystem());
			sb.AppendLine("env: graphics=" + SystemInfo.graphicsDeviceType + " " + SystemInfo.graphicsDeviceName);
			return sb.ToString();
		}

		private static string MissingScriptScan()
		{
			GameObject[] roots = FindAllRoots();
			int objects = 0;
			int behaviours = 0;
			List<string> offenders = new List<string>();
			foreach (GameObject root in roots)
			{
				Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
				foreach (Transform t in transforms)
				{
					objects++;
					MonoBehaviour[] comps = t.GetComponents<MonoBehaviour>();
					foreach (MonoBehaviour mb in comps)
					{
						behaviours++;
						if (mb == null)
						{
							offenders.Add(t.name);
						}
					}
				}
			}
			StringBuilder sb = new StringBuilder();
			sb.Append("missing-scripts: objects=").Append(objects).Append(" behaviours=").Append(behaviours)
				.Append(" missing=").Append(offenders.Count);
			if (offenders.Count > 0)
			{
				sb.Append(" on: ").Append(string.Join(", ", offenders));
			}
			return sb.ToString();
		}

		private static GameObject[] FindAllRoots()
		{
			List<GameObject> roots = new List<GameObject>();
			for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
			{
				UnityEngine.SceneManagement.Scene scene = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
				if (scene.isLoaded)
				{
					roots.AddRange(scene.GetRootGameObjects());
				}
			}
			return roots.ToArray();
		}

		#endregion

		// Reads back the BatchRendererGroup instance buffer that SceneRenderSystem uploads, so we can
		// tell a bad CPU-side copy apart from a bad GPU-side address.  The layout is structure of
		// arrays: a 48 byte header, then one contiguous array per metadata property, each array
		// m_instanceCapacity * propertySize bytes long (see SceneRenderSystem.PopulateBuffer).
		private static void BufferProbe()
		{
			World world = World.DefaultGameObjectInjectionWorld;
			Bpsim.Parts.SceneRenderSystem system = world.GetExistingSystemManaged<Bpsim.Parts.SceneRenderSystem>();
			if (system == null)
			{
				Line("buffer: SceneRenderSystem not found");
				return;
			}

			BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
			FieldInfo bufferField = typeof(Bpsim.Parts.SceneRenderSystem).GetField("m_buffer", flags);
			FieldInfo capacityField = typeof(Bpsim.Parts.SceneRenderSystem).GetField("m_instanceCapacity", flags);
			FieldInfo sizeField = typeof(Bpsim.Parts.SceneRenderSystem).GetField("m_sizeOfInstance", flags);
			FieldInfo countField = typeof(Bpsim.Parts.SceneRenderSystem).GetField("m_instanceCount", flags);

			GraphicsBuffer buffer = bufferField == null ? null : bufferField.GetValue(system) as GraphicsBuffer;
			if (buffer == null)
			{
				Line("buffer: m_buffer is null");
				return;
			}

			int capacity = (int)capacityField.GetValue(system);
			int sizeOfInstance = (int)sizeField.GetValue(system);
			int instanceCount = (int)countField.GetValue(system);
			int byteCount = buffer.count * buffer.stride;
			Line("buffer: count=" + instanceCount + " capacity=" + capacity + " sizeOfInstance=" + sizeOfInstance + " bytes=" + byteCount + " stride=" + buffer.stride);

			byte[] raw = new byte[byteCount];
			buffer.GetData(raw);

			string[] names = { "unity_ObjectToWorld", "unity_WorldToObject", "_Color", "_Rect", "_UVRect", "_BlendFactor" };
			int[] sizes = { 48, 48, 16, 16, 16, 16 };
			int offset = 48;
			for (int p = 0; p < names.Length; p++)
			{
				int size = sizes[p];
				int metadata = unchecked((int)(0x80000000u | (uint)offset));
				string head = "buffer[" + p + "] " + names[p] + " id=" + Shader.PropertyToID(names[p]) + " offset=" + offset + " metadata=0x" + ((uint)metadata).ToString("X8");
				for (int instance = 0; instance < 2 && instance < instanceCount; instance++)
				{
					int at = offset + instance * size;
					string values = "";
					for (int component = 0; component < size / 4; component++)
					{
						float value = BitConverter.ToSingle(raw, at + component * 4);
						values += (component == 0 ? "" : ", ") + value.ToString("0.#####", CultureInfo.InvariantCulture);
					}
					head += " | i" + instance + " = (" + values + ")";
				}
				Line(head);
				offset += capacity * size;
			}
		}

		#region Offscreen capture

		/// <summary>
		/// Renders the scene camera into a RenderTexture and writes it as PNG.  Unlike
		/// ScreenCapture / the desktop, this does not depend on the editor window being
		/// focused, which is what makes the automated visual checks reliable.
		///   invoke_static type=Bpsim.QA.BpsimPlayQA method=Capture args=shot.png,1280,960,1.5,0,6
		/// spec: file[,width,height,cameraX,cameraY,orthographicSize]
		/// (a single comma-separated string, because the bridge binds one argument per parameter)
		/// </summary>
		public static string Capture(string spec)
		{
			string[] args = string.IsNullOrEmpty(spec) ? new string[0] : spec.Split(',');
			string file = (args.Length > 0 && !string.IsNullOrEmpty(args[0])) ? args[0] : "qa-capture.png";
			int width = (args != null && args.Length > 1) ? ParseInt(args[1], 1280) : 1280;
			int height = (args != null && args.Length > 2) ? ParseInt(args[2], 960) : 960;
			Camera camera = Camera.main;
			if (camera == null)
			{
				Camera[] cameras = UnityEngine.Object.FindObjectsOfType<Camera>();
				if (cameras.Length > 0)
				{
					camera = cameras[0];
				}
			}
			if (camera == null)
			{
				return "error: no camera";
			}
			List<string> moved = new List<string>();
			if (args != null && args.Length > 5)
			{
				float cx = ParseFloat(args[3], camera.transform.position.x);
				float cy = ParseFloat(args[4], camera.transform.position.y);
				float size = ParseFloat(args[5], camera.orthographicSize);
				camera.transform.position = new Vector3(cx, cy, camera.transform.position.z);
				camera.orthographicSize = size;
				moved.Add("cam=(" + cx.ToString("0.###") + "," + cy.ToString("0.###") + ") size=" + size.ToString("0.###"));
			}
			RenderTexture previousTarget = camera.targetTexture;
			RenderTexture previousActive = RenderTexture.active;
			RenderTexture rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
			rt.Create();
			string path = null;
			try
			{
				camera.targetTexture = rt;
				camera.Render();
				RenderTexture.active = rt;
				Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
				tex.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
				tex.Apply();
				byte[] png = tex.EncodeToPNG();
				UnityEngine.Object.Destroy(tex);
				string dir = Path.GetDirectoryName(LogPath);
				path = Path.Combine(dir, file);
				File.WriteAllBytes(path, png);
			}
			catch (Exception ex)
			{
				return "error: " + ex.Message;
			}
			finally
			{
				camera.targetTexture = previousTarget;
				RenderTexture.active = previousActive;
				rt.Release();
				UnityEngine.Object.Destroy(rt);
			}
			return "captured " + path + " (" + width + "x" + height + ") " + string.Join(" ", moved.ToArray());
		}

		private static int ParseInt(string text, int fallback)
		{
			int value;
			return int.TryParse(text, out value) ? value : fallback;
		}

		private static float ParseFloat(string text, float fallback)
		{
			float value;
			return float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value) ? value : fallback;
		}

		#endregion

		private static void Line(string text)
		{
			s_report.AppendLine(text);
			Debug.Log("[BpsimQA] " + text);
		}

		private static void WriteFile()
		{
			try
			{
				string dir = Path.GetDirectoryName(LogPath);
				if (!string.IsNullOrEmpty(dir))
				{
					Directory.CreateDirectory(dir);
				}
				File.WriteAllText(LogPath, s_report.ToString());
			}
			catch (Exception ex)
			{
				Debug.LogError("[BpsimQA] cannot write log: " + ex);
			}
		}
	}
}
#endif
