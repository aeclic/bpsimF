using System.Collections.Generic;
using Bpsim.Parts.Simulation;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	public struct BpleLightSourceTag : IComponentData { }

	/// <summary>BPLE's entity lights and ordinary/night lighting, backed by the ECS physics world.</summary>
	internal sealed partial class BpleLightRuntime : MonoBehaviour
	{
		internal sealed class Source
		{
			public Entity Part;
			public PartTypeInfo Info;
			public BpleLightProfile Profile;
			public readonly BpleLightEmitter Shape = new BpleLightEmitter();
			public bool Activated, EntityEnabled;
			public bool HasToggled, Flickering;
			public float SwitchedAt, Deficit, Capacity = 5000, Electricity = 5000;
			public int Component, ComponentSize = 1;
			public Vector2 Position, Direction, PreviousPosition, PreviousDirection;
			public Vector3 RootPosition;
			public float ScaleX = 1, ScaleY = 1;
			public Entity EnclosedInto;
			public Entity Query;
			public float QueryLength = -1, QueryWidth = -1;
			public Color Color;
			public GameObject Visual;
			public Mesh Mesh;
			public MaterialPropertyBlock Properties;
			public float MeshLength = -1, MeshWidth = -1;
		}

		private sealed class AlienGeometry
		{
			public Vector2 Center, PreviousCenter;
			public int Count, Lit;
			public float Farthest;
			public readonly List<Vector2> Positions = new List<Vector2>();
			public void Reset()
			{
				Center = PreviousCenter = Vector2.zero;
				Count = Lit = 0;
				Farthest = 0;
				Positions.Clear();
			}
		}

		private static BpleLightRuntime s_instance;
		private readonly Dictionary<Entity, Source> m_sources = new Dictionary<Entity, Source>();
		private readonly Dictionary<int, AlienGeometry> m_alienGeometry = new Dictionary<int, AlienGeometry>();
		private readonly List<AlienGeometry> m_alienGroups = new List<AlienGeometry>();
		private readonly Dictionary<Entity, Source> m_previews = new Dictionary<Entity, Source>();
		private readonly Dictionary<int, int> m_engineCounts = new Dictionary<int, int>();
		private readonly Dictionary<int, Gearbox> m_gearboxes = new Dictionary<int, Gearbox>();
		private readonly List<Entity> m_removed = new List<Entity>();
		private bool m_running;
		private bool m_nightVision;
		public static BpleLightRuntime Instance => s_instance;
		public static bool LightSystem => UserSettings.Instance?.SimulationSettings.BpleLightSystem ?? true;
		public bool NightVisionOn => m_nightVision;
		public int SourceCount => m_sources.Count;
		public int ContactCount { get; private set; }
		private static EntityManager Em => PartGameplayUtil.Em;

		public static BpleLightRuntime Ensure()
		{
			if (s_instance != null) return s_instance;
			var go = new GameObject("[BPLE Lights]") { hideFlags = HideFlags.DontSave };
			DontDestroyOnLoad(go);
			return s_instance = go.AddComponent<BpleLightRuntime>();
		}

		public void Begin()
		{
			End();
			m_running = true;
			ClearSources(m_previews);
			CaptureBodies();
		}

		public void End()
		{
			m_running = false;
			m_nightVision = false;
			ContactCount = 0;
			RestoreControlFrames();
			ClearSources(m_sources);
			m_bodies.Clear();
		}

		private static void ClearSources(Dictionary<Entity, Source> sources)
		{
			foreach (var source in sources.Values) DestroyVisual(source);
			sources.Clear();
		}

		private static void DestroyVisual(Source source)
		{
			if (source.Query != Entity.Null && PartManager.Instance?.System != null && Em.Exists(source.Query)) Em.DestroyEntity(source.Query);
			if (source.Visual != null) Destroy(source.Visual);
			if (source.Mesh != null) Destroy(source.Mesh);
		}

		public void Register(Entity part, PartTypeInfo info)
		{
			if (m_sources.ContainsKey(part)) return;
			var source = NewSource(part, info);
			RefreshPose(source);
			source.PreviousPosition = source.Position;
			source.PreviousDirection = source.Direction;
			m_sources.Add(part, source);
		}

		private static Source NewSource(Entity part, PartTypeInfo info) => new Source {
			Part = part, Info = PartVariants.Resolve(info), Profile = BplePartsProfiles.Light(info), SwitchedAt = Time.time };

		public void Unregister(Entity part)
		{
			if (!m_sources.TryGetValue(part, out var source)) return;
			if (source.Info.PartType == PartType.PointLight && source.Info.PartIndex == 4) m_nightVision = false;
			DestroyVisual(source);
			m_sources.Remove(part);
		}

		public Source Find(Entity part) => m_sources.TryGetValue(part, out var source) ? source : null;

		public void SetActivation(Entity part, bool enabled)
		{
			if (!m_sources.TryGetValue(part, out var source)) return;
			if (source.Activated != enabled) { source.SwitchedAt = Time.time; source.HasToggled = true; }
			source.Activated = enabled;
			if (source.Info.PartType == PartType.PointLight && source.Info.PartIndex == 4
				&& UserSettings.Instance.SimulationSettings.BpleDarkness)
				m_nightVision = enabled; // BPLE sets the global state to this lamp's state, not an unconditional XOR.
			RefreshPose(source);
			source.EntityEnabled = source.Shape.Type >= 0 && LightSystem && enabled && PartVariantPhysics.IsAssembly(part);
		}

		private static void RefreshPose(Source source)
		{
			var local = Em.GetComponentData<LocalTransform>(source.Part);
			float2 scale = Em.HasComponent<PartFreeTransform>(source.Part)
				? PartFreeTransform.Sanitize(Em.GetComponentData<PartFreeTransform>(source.Part)).Scale : new float2(1);
			scale *= local.Scale;
			source.ScaleX = scale.x;
			source.ScaleY = scale.y;
			source.RootPosition = local.Position;
			float3 up = math.rotate(local.Rotation, new float3(0, 1, 0));
			source.Direction = new Vector2(up.x, up.y);
			float3 origin = local.Position + .5f * scale.y * up;
			source.Position = new Vector2(origin.x, origin.y);
			source.Component = PartGameplayUtil.ConnectedComponent(source.Part);
			source.EnclosedInto = Em.HasComponent<PartContainerValue>(source.Part)
				? Em.GetComponentData<PartContainerValue>(source.Part).Value : Entity.Null;
			source.Shape.Type = source.Profile.entityType;
			source.Shape.Length = source.Profile.length * (source.Shape.Type <= 1 ? scale.y : math.min(scale.x, scale.y));
			source.Shape.HalfWidth = source.Profile.halfWidth * scale.x;
			source.Shape.Angle = source.Profile.angle;
		}

		internal static bool Profile;
		private static int s_profileSteps;

		internal static void SetProfiling(bool enabled)
		{
			Profile = enabled;
			s_profileSteps = 0;
		}

		public void Step(float dt)
		{
			if (!m_running || m_sources.Count == 0) return;
			long t0 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			m_removed.Clear();
			foreach (var pair in m_sources)
			{
				if (!Em.Exists(pair.Key)) { m_removed.Add(pair.Key); continue; }
				Source source = pair.Value;
				RefreshPose(source);
				source.EntityEnabled = source.Shape.Type >= 0 && LightSystem && source.Activated && PartVariantPhysics.IsAssembly(source.Part);
			}
			foreach (Entity part in m_removed) Unregister(part);
			if (m_sources.Count == 0) return;
			int active = 0;
			foreach (Source source in m_sources.Values) if (source.EntityEnabled) active++;
			if (active == 0)
			{
				// 所有灯都关着时，不需要跑引擎计数、外星灯几何、接触扫描或刚体缓存。
				// 这些循环每步都会遍历 1648 个部件，之前即使灯全灭也会把模拟拖到 ~2 FPS。
				foreach (Source source in m_sources.Values)
				{
					source.PreviousPosition = source.Position;
					source.PreviousDirection = source.Direction;
				}
				return;
			}
			long t1 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			RefreshEngineCounts();
			long t2 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			UpdateAlienLights(dt);
			long t3 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			UpdateEnergy();
			long t4 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			UpdateQueryPillars();
			long t5 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			ApplyLightContacts(dt);
			long t6 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			ApplyDecelerationLights(dt);
			long t7 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			UpdateControlLights(dt);
			long t8 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			CaptureBodies();
			long t9 = Profile ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
			foreach (Source source in m_sources.Values)
			{
				source.PreviousPosition = source.Position;
				source.PreviousDirection = source.Direction;
			}
			if (Profile)
			{
				long tEnd = System.Diagnostics.Stopwatch.GetTimestamp();
				if (s_profileSteps < 6 || s_profileSteps % 50 == 0)
				{
					double f = System.Diagnostics.Stopwatch.Frequency / 1000.0;
					Debug.Log($"[BplePerf] step={s_profileSteps} src={m_sources.Count} active={active} bodies={m_bodies.Count} pose={(t1 - t0) / f:F2} engine={(t2 - t1) / f:F2} alien={(t3 - t2) / f:F2} energy={(t4 - t3) / f:F2} query={(t5 - t4) / f:F2} contacts={(t6 - t5) / f:F2} decel={(t7 - t6) / f:F2} control={(t8 - t7) / f:F2} capture={(t9 - t8) / f:F2} prev={(tEnd - t9) / f:F2} total={(tEnd - t0) / f:F2}");
				}
				s_profileSteps++;
			}
		}

		private void RefreshEngineCounts()
		{
			m_engineCounts.Clear();
			m_gearboxes.Clear();
			foreach (Entity part in PartGameplayUtil.Simulator.Parts)
			{
				if (!Em.Exists(part)) continue;
				PartTypeInfo info = PartVariants.Resolve(PartGameplayUtil.TypeInfo(part));
				int component = PartGameplayUtil.ConnectedComponent(part);
				if (info.PartType == PartType.Engine || info.PartType == PartType.EngineSmall || info.PartType == PartType.EngineBig)
					m_engineCounts[component] = m_engineCounts.TryGetValue(component, out int count) ? count + 1 : 1;
				if (info.PartType == PartType.Gearbox && info.PartIndex != 0 && Em.HasComponent<Gearbox>(part))
					m_gearboxes[component] = Em.GetComponentData<Gearbox>(part);
			}
			foreach (Source source in m_sources.Values)
			{
				source.Shape.Sides = 0;
				if (source.Shape.Type > 1 || !m_gearboxes.TryGetValue(source.Component, out var gearbox)) continue;
				int rotation = Em.GetComponentData<PartTransform>(source.Part).Rotation;
				bool firstSide = rotation == 0 || rotation == 1 || rotation == 4 || rotation == 5;
				source.Shape.Sides = (gearbox.Activated ^ firstSide) ? 1 : 2;
			}
		}

		private void UpdateAlienLights(float dt)
		{
			foreach (AlienGeometry group in m_alienGeometry.Values) group.Reset();
			m_alienGroups.Clear();
			foreach (Source source in m_sources.Values)
			{
				if (source.Shape.Type != 4 || !source.EntityEnabled) continue;
				if (!m_alienGeometry.TryGetValue(source.Component, out AlienGeometry group))
					m_alienGeometry[source.Component] = group = new AlienGeometry();
				if (group.Lit == 0) m_alienGroups.Add(group);
				group.Lit++;
			}
			if (m_alienGroups.Count == 0) return;
			foreach (Entity part in PartGameplayUtil.Simulator.Parts)
			{
				if (!Em.Exists(part)) continue;
				int component = PartGameplayUtil.ConnectedComponent(part);
				if (!m_alienGeometry.TryGetValue(component, out AlienGeometry group) || group.Lit == 0) continue;
				Vector2 position = PartVariantPhysics.Position(part).xy;
				group.Center += position;
				group.PreviousCenter += position - (Vector2)PartGameplayUtil.LinearVelocity(part).xy * dt;
				group.Count++;
				group.Positions.Add(position);
			}
			foreach (AlienGeometry group in m_alienGroups)
			{
				if (group.Count == 0) continue;
				group.Center /= group.Count;
				group.PreviousCenter /= group.Count;
				foreach (Vector2 position in group.Positions)
					group.Farthest = Mathf.Max(group.Farthest, Vector2.Distance(group.Center, position));
			}
			foreach (Source source in m_sources.Values)
			{
				if (source.Shape.Type != 4 || !source.EntityEnabled) continue;
				AlienGeometry group = m_alienGeometry[source.Component];
				if (group.Count == 0) continue;
				source.Position = group.Center;
				source.PreviousPosition = group.PreviousCenter;
				source.Shape.Length = Mathf.Min(group.Farthest, 2 * Mathf.Sqrt(group.Count)) + 4;
				source.Shape.HalfWidth = (source.Shape.Length + 8) / 64;
				source.ComponentSize = Mathf.Max(1, group.Lit);
			}
		}

		private sealed class EnergyGroup
		{
			public readonly List<Source> Sources = new List<Source>();
			public float Capacity, Electricity;
		}

		private void UpdateEnergy()
		{
			var groups = new Dictionary<(int, int), EnergyGroup>();
			var componentCounts = new Dictionary<int, int>();
			foreach (Source source in m_sources.Values)
			{
				if (source.Shape.Type < 0) continue;
				var key = (source.Component, source.Shape.Type);
				if (!groups.TryGetValue(key, out var group)) groups[key] = group = new EnergyGroup();
				group.Sources.Add(source);
				group.Electricity += source.Deficit;
				componentCounts[source.Component] = componentCounts.TryGetValue(source.Component, out int count) ? count + 1 : 1;
			}
			foreach (var pair in groups)
			{
				EnergyGroup group = pair.Value;
				int engines = m_engineCounts.TryGetValue(pair.Key.Item1, out int count) ? count : 0;
				group.Capacity = group.Sources.Count * (5000 + 20000f * engines / componentCounts[pair.Key.Item1]);
				float previous = group.Electricity + group.Capacity;
				group.Electricity = Mathf.Min(previous + Mathf.Sqrt(group.Capacity), group.Capacity);
				float charge = (group.Electricity - previous) / group.Sources.Count;
				foreach (Source source in group.Sources) source.Deficit += charge;
			}
			// BPLE balances the stored deficit across the five light types at 4% per fixed step.
			foreach (int component in componentCounts.Keys)
			{
				var capacities = new float[5];
				float available = 0;
				for (int type = 0; type < 5; type++) if (groups.TryGetValue((component, type), out var group))
				{ capacities[type] = group.Capacity; available += group.Electricity; }
				int used = 0;
				for (int remaining = 5; remaining > 0; remaining--)
				{
					int type = -1;
					for (int i = 0; i < 5; i++) if ((used & (1 << i)) == 0 && (type < 0 || capacities[i] < capacities[type])) type = i;
					used |= 1 << type;
					float target = Mathf.Min(capacities[type], available / remaining);
					available -= target;
					if (!groups.TryGetValue((component, type), out var group)) continue;
					float deficit = (target - group.Capacity) / group.Sources.Count;
					foreach (Source source in group.Sources)
					{
						source.Deficit += (deficit - source.Deficit) * .04f;
						source.Capacity = group.Capacity;
						source.Electricity = group.Electricity;
					}
				}
			}
		}

		private void OnDestroy()
		{
			End();
			ClearSources(m_previews);
			ReleaseRendering();
			if (s_instance == this) s_instance = null;
		}
	}
}
