using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Bpsim.Parts;

namespace Bpsim.QA
{
	/// <summary>
	/// Per-frame recorder for a free fall.  The point is to tell apart
	/// <list type="bullet">
	/// <item>physics jitter – the fixed tick itself overshoots and comes back (LocalTransform reverses),</item>
	/// <item>render-side judder – the drawn transform stalls for a frame and then jumps
	///       (LocalToWorld / batch-render instance buffer non-monotonic while LocalTransform is smooth).</item>
	/// </list>
	/// Samples the entity transforms *and* the float3 translations that
	/// <see cref="Bpsim.Parts.SceneRenderSystem"/> actually uploaded to the GPU, once per rendered frame.
	/// </summary>
	public class BpsimFallProbe : MonoBehaviour
	{
		private static BpsimFallProbe s_instance;
		private static readonly StringBuilder s_report = new StringBuilder();
		private static string s_status = "idle";
		private static bool s_running;

		public static string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs", "bpsim-fallprobe.log"));

		private static BpsimFallProbe Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject go = new GameObject("[BpsimFallProbe]");
			go.hideFlags = HideFlags.DontSave;
			UnityEngine.Object.DontDestroyOnLoad(go);
			s_instance = go.AddComponent<BpsimFallProbe>();
			return s_instance;
		}

		/// <summary>Starts recording. Returns immediately; poll <see cref="Status"/>.</summary>
		public static string Run(string frames)
		{
			if (s_running)
			{
				return "busy: " + s_status;
			}
			int n = 180;
			if (!string.IsNullOrEmpty(frames) && int.TryParse(frames, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
			{
				n = Mathf.Clamp(parsed, 10, 1200);
			}
			s_running = true;
			s_status = "running";
			s_report.Length = 0;
			BpsimFallProbe instance = Ensure();
			Application.logMessageReceived -= instance.OnLog;
			Application.logMessageReceived += instance.OnLog;
			instance.StartCoroutine(instance.RunProbe(n));
			return "started frames=" + n;
		}

		public static string Status()
		{
			return s_status + "\n" + s_report;
		}

		private static void Line(string text)
		{
			s_report.AppendLine(text);
		}

		private struct Sample
		{
			public float LocalY;
			public float WorldY;
			public float MatrixY;
			public float VelocityY;
			public float DrawnY;
			public float CamY;
			public float DeltaTime;
			public bool HasDrawn;
		}

		private IEnumerator RunProbe(int frames)
		{
			Line("=== bpsim fall probe @ " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===");
			yield return null;

			PartManager pm = PartManager.Instance;
			if (pm == null)
			{
				Finish("fail: PartManager missing");
				yield break;
			}
			if (!pm.HasActiveScene())
			{
				pm.LoadEmptyScene("FallProbe");
				yield return null;
			}
			PartScene scene = pm.ActiveScene;
			if (scene == null)
			{
				Finish("fail: no scene");
				yield break;
			}

			if (scene.Unmanaged.Parts.Length == 0)
			{
				Place(scene, pm, PartType.MetalFrame, 0, 12);
				Place(scene, pm, PartType.Pig, 3, 12);
				Place(scene, pm, PartType.NormalWheel, -3, 12);
				scene.Unmanaged.Update();
				yield return null;
			}

			// 物理组件（PhysicsMass / PhysicsVelocity）是 PartSimulator 开跑时用 ECB 挂上去的：
			// 建造状态下部件只有 LocalTransform + collider，所以必须先 StartSimulation 再等几帧，
			// 才可能找到“动态部件”（否则永远 fail: no dynamic part）。
			pm.StartSimulation();
			for (int warm = 0; warm < 3; warm++)
			{
				yield return new WaitForEndOfFrame();
			}

			EntityManager em = scene.Unmanaged.EntityManager;
			List<Entity> tracked = new List<Entity>();
			for (int attempt = 0; attempt < 60 && tracked.Count == 0; attempt++)
			{
				NativeList<Entity> parts = scene.Unmanaged.Parts;
				tracked.Clear();
				for (int i = 0; i < parts.Length && tracked.Count < 3; i++)
				{
					Entity e = parts[i];
					if (!em.Exists(e) || !em.HasComponent<PhysicsVelocity>(e) || !em.HasComponent<PhysicsMass>(e))
					{
						continue;
					}
					if (!em.GetComponentData<PhysicsMass>(e).HasInfiniteMass)
					{
						tracked.Add(e);
					}
				}
				if (tracked.Count == 0)
				{
					yield return new WaitForEndOfFrame();
				}
			}
			if (tracked.Count == 0)
			{
				StringBuilder sb = new StringBuilder();
				sb.Append("fail: no dynamic part (parts=").Append(scene.Unmanaged.Parts.Length)
					.Append(", simulating=").Append(pm.IsSimulating).Append(')');
				NativeList<Entity> dbg = scene.Unmanaged.Parts;
				for (int i = 0; i < dbg.Length; i++)
				{
					Entity e = dbg[i];
					sb.Append("\n  [").Append(i).Append("] ");
					if (!em.Exists(e))
					{
						sb.Append("gone");
						continue;
					}
					sb.Append(e.Index).Append(':').Append(e.Version);
					if (em.HasComponent<PartTypeValue>(e))
					{
						sb.Append(' ').Append(em.GetComponentData<PartTypeValue>(e).Value);
					}
					sb.Append(" collider=").Append(em.HasComponent<PhysicsCollider>(e) ? "y" : "n");
					sb.Append(" mass=").Append(em.HasComponent<PhysicsMass>(e)
						? (em.GetComponentData<PhysicsMass>(e).HasInfiniteMass ? "static" : "dynamic")
						: "none");
					sb.Append(" velocity=").Append(em.HasComponent<PhysicsVelocity>(e) ? "y" : "n");
					sb.Append(" local=").Append(em.HasComponent<LocalTransform>(e) ? "y" : "n");
				}
				Finish(sb.ToString());
				yield break;
			}

			Line("fall: parts=" + scene.Unmanaged.Parts.Length + " tracked=" + tracked.Count
				+ " dt_settings=" + UserSettings.Instance.SimulationSettings.FixedTimeStep.ToString("0.####", CultureInfo.InvariantCulture)
				+ " gravity=" + UserSettings.Instance.SimulationSettings.GravityY.ToString("0.##", CultureInfo.InvariantCulture)
				+ " solver=" + UserSettings.Instance.SimulationSettings.SolverIterationCount);
			for (int i = 0; i < tracked.Count; i++)
			{
				Line("fall: track[" + i + "] " + DescribePart(em, tracked[i]));
			}

			Line("fall: simulating=" + pm.IsSimulating);

			List<Sample>[] samples = new List<Sample>[tracked.Count];
			for (int i = 0; i < tracked.Count; i++)
			{
				samples[i] = new List<Sample>(frames);
			}

			for (int frame = 0; frame < frames; frame++)
			{
				yield return new WaitForEndOfFrame();
				float dt = Time.deltaTime;
				NativeArray<float4> drawn = ReadDrawnTranslations();
				for (int p = 0; p < tracked.Count; p++)
				{
					Entity e = tracked[p];
					Sample s = default;
					s.DeltaTime = dt;
					if (!em.Exists(e))
					{
						continue;
					}
					s.LocalY = em.GetComponentData<LocalTransform>(e).Position.y;
					s.MatrixY = em.GetComponentData<LocalToWorld>(e).Value.c3.y;
					s.WorldY = em.GetComponentData<WorldTransform>(e).Position.y;
					// 玩家真正看到的是“画面位置 − 镜头位置”：相机跟错量导致的抽搐只会显现在这个差里。
					s.CamY = Bpsim.SceneCamera.Instance != null ? Bpsim.SceneCamera.Instance.transform.position.y : 0f;
					s.VelocityY = em.GetComponentData<PhysicsVelocity>(e).Linear.y;
					if (drawn.IsCreated && frame < drawn.Length)
					{
						// The batch renderer keeps instances in upload order; the first few are a
						// stable enough sample to compare "what is drawn" against "what physics has".
						s.DrawnY = drawn[frame % drawn.Length].y;
						s.HasDrawn = true;
					}
					if (drawn.IsCreated)
					{
						// pick the drawn instance closest to this part's matrix to follow one instance
						float best = float.MaxValue;
						for (int k = 0; k < drawn.Length; k++)
						{
							float d = math.abs(drawn[k].y - s.MatrixY);
							if (d < best)
							{
								best = d;
								s.DrawnY = drawn[k].y;
								s.HasDrawn = true;
							}
						}
					}
					samples[p].Add(s);
				}
				if (drawn.IsCreated)
				{
					drawn.Dispose();
				}
			}

			for (int p = 0; p < tracked.Count; p++)
			{
				Report(p, samples[p]);
			}

			if (pm.IsSimulating)
			{
				pm.EndSimulation();
			}
			Finish("done");
		}

		private static void Place(PartScene scene, PartManager pm, PartType type, int x, int y)
		{
			PartTypeInfo info = new PartTypeInfo(type, 0);
			Entity prefab = pm.EntitySpawner.FindPrefab(info);
			if (prefab == Entity.Null)
			{
				Line("fall: no prefab for " + info.PartType);
				return;
			}
			PartCreationRequest request = new PartCreationRequest(prefab, scene.SceneID, info, x, y, 0, false);
			if (!scene.Unmanaged.PlacePartAt(request, overlay: false))
			{
				Line("fall: rejected " + info.PartType + " at " + x + "," + y);
			}
		}

		private static string DescribePart(EntityManager em, Entity e)
		{
			StringBuilder sb = new StringBuilder();
			sb.Append(e.Index).Append(':').Append(e.Version);
			if (em.HasComponent<PartTypeValue>(e))
			{
				sb.Append(' ').Append(em.GetComponentData<PartTypeValue>(e).Value);
			}
			sb.Append(" mass=").Append(em.GetComponentData<PhysicsMass>(e).InverseMass.ToString("0.###", CultureInfo.InvariantCulture));
			sb.Append(" collider=").Append(em.HasComponent<PhysicsCollider>(e) ? "y" : "n");
			sb.Append(" damping=").Append(em.HasComponent<PhysicsDamping>(e) ? "y" : "n");
			sb.Append(" smooth=").Append(em.HasComponent<Unity.Physics.GraphicsIntegration.PhysicsGraphicalSmoothing>(e) ? "y" : "n");
			sb.Append(" buffer=").Append(em.HasComponent<Unity.Physics.GraphicsIntegration.PhysicsGraphicalInterpolationBuffer>(e) ? "y" : "n");
			return sb.ToString();
		}

		private static void Report(int index, List<Sample> samples)
		{
			int n = samples.Count;
			if (n < 4)
			{
				Line("fall[" + index + "]: not enough samples");
				return;
			}

			StringBuilder deltas = new StringBuilder();
			int shown = 0;
			for (int i = 1; i < n && shown < 60; i++)
			{
				float d = samples[i].WorldY - samples[i - 1].WorldY;
				deltas.Append(d.ToString("0.####", CultureInfo.InvariantCulture)).Append(' ');
				shown++;
			}

			Line("fall[" + index + "] matrix deltas: " + deltas);
			Line("fall[" + index + "] local deltas : " + Deltas(samples, s => s.LocalY, 60));

			Line("fall[" + index + "] matrix  " + Stats(samples, s => s.WorldY));
			Line("fall[" + index + "] local   " + Stats(samples, s => s.LocalY));
			Line("fall[" + index + "] velocity" + Stats(samples, s => s.VelocityY));
			if (samples[0].HasDrawn)
			{
				Line("fall[" + index + "] buffer  " + Stats(samples, s => s.DrawnY));
			}
			Line("fall[" + index + "] cam     " + Stats(samples, s => s.CamY));
			Line("fall[" + index + "] rel(matrix-cam) " + Stats(samples, s => s.MatrixY - s.CamY));
			Line("fall[" + index + "] rel(local-cam)  " + Stats(samples, s => s.LocalY - s.CamY));
			Line("fall[" + index + "] rel deltas: " + Deltas(samples, s => s.MatrixY - s.CamY, 60));
		}

		private static string Deltas(List<Sample> samples, Func<Sample, float> get, int max)
		{
			StringBuilder sb = new StringBuilder();
			for (int i = 1; i < samples.Count && i <= max; i++)
			{
				sb.Append((get(samples[i]) - get(samples[i - 1])).ToString("0.####", CultureInfo.InvariantCulture)).Append(' ');
			}
			return sb.ToString();
		}

		/// <summary>
		/// A body in free fall has a strictly monotonic position: every frame it moves down a little
		/// more.  A reversal of the per-frame delta is therefore a visible twitch, and a zero delta
		/// followed by a double delta is judder from the tick/render beat.
		/// </summary>
		private static string Stats(List<Sample> samples, Func<Sample, float> get)
		{
			int n = samples.Count;
			int reversals = 0;
			int stalls = 0;
			int doubles = 0;
			float sum = 0f;
			float min = float.MaxValue;
			float max = float.MinValue;
			int counted = 0;
			float previousDelta = 0f;
			float previousPrevious = 0f;
			for (int i = 1; i < n; i++)
			{
				float d = get(samples[i]) - get(samples[i - 1]);
				sum += d;
				counted++;
				if (d < min)
				{
					min = d;
				}
				if (d > max)
				{
					max = d;
				}
				if (i > 1 && previousDelta != 0f && Mathf.Sign(d) != Mathf.Sign(previousDelta))
				{
					reversals++;
				}
				if (Mathf.Abs(d) < 1e-4f)
				{
					stalls++;
				}
				if (i > 2 && previousPrevious != 0f && Mathf.Abs(d) > 1.6f * Mathf.Abs(previousDelta) && Mathf.Abs(previousDelta) < 1e-4f)
				{
					doubles++;
				}
				previousPrevious = previousDelta;
				previousDelta = d;
			}
			float mean = counted == 0 ? 0f : sum / counted;
			float variance = 0f;
			for (int i = 1; i < n; i++)
			{
				float d = (get(samples[i]) - get(samples[i - 1])) - mean;
				variance += d * d;
			}
			variance = counted == 0 ? 0f : variance / counted;
			return "mean=" + mean.ToString("0.######", CultureInfo.InvariantCulture)
				+ " min=" + min.ToString("0.######", CultureInfo.InvariantCulture)
				+ " max=" + max.ToString("0.######", CultureInfo.InvariantCulture)
				+ " sigma=" + Mathf.Sqrt(variance).ToString("0.######", CultureInfo.InvariantCulture)
				+ " reversals=" + reversals + " stalls=" + stalls + " jumpAfterStall=" + doubles;
		}

		private static NativeArray<float4> ReadDrawnTranslations()
		{
			try
			{
				World world = World.DefaultGameObjectInjectionWorld;
				Bpsim.Parts.SceneRenderSystem system = world.GetExistingSystemManaged<Bpsim.Parts.SceneRenderSystem>();
				if (system == null)
				{
					return default;
				}
				BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
				FieldInfo bufferField = typeof(Bpsim.Parts.SceneRenderSystem).GetField("m_buffer", flags);
				FieldInfo capacityField = typeof(Bpsim.Parts.SceneRenderSystem).GetField("m_instanceCapacity", flags);
				FieldInfo countField = typeof(Bpsim.Parts.SceneRenderSystem).GetField("m_instanceCount", flags);
				GraphicsBuffer buffer = bufferField == null ? null : bufferField.GetValue(system) as GraphicsBuffer;
				if (buffer == null)
				{
					return default;
				}
				int capacity = (int)capacityField.GetValue(system);
				int count = Mathf.Min((int)countField.GetValue(system), capacity);
				if (count <= 0 || capacity <= 0)
				{
					return default;
				}
				int byteCount = buffer.count * buffer.stride;
				byte[] raw = new byte[byteCount];
				buffer.GetData(raw);
				NativeArray<float4> result = new NativeArray<float4>(count, Allocator.Temp);
				// Structure of arrays: 48 byte header, then unity_ObjectToWorld as
				// capacity * 48 bytes; the translation lives at float components 12..14.
				const int matrixOffset = 48;
				for (int i = 0; i < count; i++)
				{
					int at = matrixOffset + i * 48;
					if (at + 60 > raw.Length)
					{
						break;
					}
					result[i] = new float4(
						BitConverter.ToSingle(raw, at + 48),
						BitConverter.ToSingle(raw, at + 52),
						BitConverter.ToSingle(raw, at + 56),
						0f);
				}
				return result;
			}
			catch (Exception ex)
			{
				Line("fall: buffer read failed: " + ex.Message);
				return default;
			}
		}

		private void Finish(string status)
		{
			s_status = status;
			s_running = false;
			Application.logMessageReceived -= OnLog;
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
				File.WriteAllText(LogPath, s_report.ToString());
			}
			catch (Exception ex)
			{
				Debug.LogWarning("[BpsimFallProbe] log write failed: " + ex.Message);
			}
			Debug.Log("[BpsimFallProbe] " + status + " -> " + LogPath);
		}

		private void OnLog(string message, string stackTrace, LogType type)
		{
			if (type == LogType.Exception || type == LogType.Error)
			{
				s_report.AppendLine("[log:" + type + "] " + message);
			}
		}
	}
}
