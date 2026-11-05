using System;
using System.Collections.Generic;
using Bpsim.Parts;
using Bpsim.Parts.Simulation;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Extensions;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Modding
{
	/// <summary>Opt-in 2D fluid parameters, in game units. No registered volumes means no system/jobs.</summary>
	public struct ModFluidVolume
	{
		public float Left, Surface, Width, Depth;
		public float Density, BuoyancyMultiplier, LinearDrag, AngularDrag;
		public float2 Flow;
		public float FlowAcceleration;
		public float WaveAmplitude, WaveLength, WaveSpeed;
		public byte PhysicsEnabled, CaptureContacts;
	}

	public struct ModFluidContact
	{
		public float2 Position;
		public float Speed, Radius;
		public bool Entering;
	}

	/// <summary>One owner-scoped session; volumes may be replaced on level changes without retaining old handles.</summary>
	public sealed class ModFluidSession : IDisposable
	{
		public static int ActiveVolumeCount => ModFluidRegistry.Count;
		public static int QueriedBodyCount => ModFluidRegistry.BodyCount;
		public static int ExecutedChunksLastStep => ModFluidRegistry.ExecutedChunks;
		private readonly ModEntryPoint m_owner;
		private readonly List<ModFluidHandle> m_handles = new List<ModFluidHandle>();
		private bool m_disposed;
		private ModFluidSession(ModEntryPoint owner) { m_owner = owner; }

		public static ModFluidSession Open(ModEntryPoint owner)
		{
			ModHostResources.CheckOwner(owner);
			return ModHostResources.Own(owner, new ModFluidSession(owner));
		}

		public ModFluidHandle Add(ModFluidVolume volume, Action<ModFluidContact> onContact)
		{
			if (m_disposed) throw new ObjectDisposedException(nameof(ModFluidSession));
			ModHostResources.CheckOwner(m_owner);
			ModFluidMath.Validate(volume);
			ModFluidHandle handle = new ModFluidHandle(this, volume, onContact);
			ModFluidRegistry.Add(handle);
			m_handles.Add(handle);
			return handle;
		}

		internal void Remove(ModFluidHandle handle)
		{
			m_handles.Remove(handle);
			ModFluidRegistry.Remove(handle);
		}

		public void Dispose()
		{
			if (m_disposed) return;
			m_disposed = true;
			while (m_handles.Count != 0) m_handles[m_handles.Count - 1].Dispose();
		}
	}

	public sealed class ModFluidHandle : IDisposable
	{
		public int DeliveredContacts { get; internal set; }
		public int SimulationSteps { get; internal set; }
		internal ModFluidVolume Data;
		internal Action<ModFluidContact> Contact;
		private ModFluidSession m_session;
		internal ModFluidHandle(ModFluidSession session, ModFluidVolume data, Action<ModFluidContact> contact)
		{
			m_session = session; Data = data; Contact = contact;
		}
		public void Update(ModFluidVolume volume)
		{
			if (m_session == null) throw new ObjectDisposedException(nameof(ModFluidHandle));
			ModFluidMath.Validate(volume);
			Data = volume;
		}
		public void Dispose()
		{
			ModFluidSession session = m_session;
			m_session = null; Contact = null;
			if (session != null) session.Remove(this);
		}
	}

	public static class ModFluidMath
	{
		public static void Validate(ModFluidVolume v)
		{
			float4 bounds = new float4(v.Left, v.Surface, v.Width, v.Depth);
			float4 force = new float4(v.Density, v.BuoyancyMultiplier, v.LinearDrag, v.AngularDrag);
			float4 waves = new float4(v.WaveAmplitude, v.WaveLength, v.WaveSpeed, v.FlowAcceleration);
			if (!math.all(math.isfinite(bounds)) || !math.all(math.isfinite(force)) ||
				!math.all(math.isfinite(waves)) || !math.all(math.isfinite(v.Flow)) ||
				v.Width <= 0 || v.Depth <= 0 || v.WaveLength <= 0 || math.any(force < 0) ||
				math.any(force > 1000) || math.any(waves < 0) || v.Width > 80000 || v.Depth > 80000 ||
				math.any(math.abs(v.Flow) > 10000) || v.FlowAcceleration > 10000 || v.WaveSpeed > 1000 ||
				v.PhysicsEnabled > 1 || v.CaptureContacts > 1)
				throw new ArgumentException("Invalid fluid parameters.");
		}

		public static float SurfaceAt(ModFluidVolume v, float x, float time)
		{
			float phase = (x - v.Left) * (2f * math.PI / math.max(v.WaveLength, .01f)) - time * v.WaveSpeed;
			float amplitude = math.min(v.WaveAmplitude, v.Depth * .45f);
			return v.Surface + amplitude * (math.sin(phase) + .35f * math.sin(phase * 1.73f + time * v.WaveSpeed * .27f)) / 1.35f;
		}

		public static float ShapeFill(float3 boundsSize, float volume)
		{
			float boundsVolume = math.max(boundsSize.x, .001f) * math.max(boundsSize.y, .001f) * math.max(boundsSize.z, .001f);
			return volume > 0 && math.isfinite(volume) ? math.saturate(volume / boundsVolume) : 1f;
		}

		/// <summary>Clip an oriented collider's projected bounds; returns area and center of displaced water.</summary>
		public static float SubmergedArea(float2 center, float2 halfSize, quaternion rotation,
			float left, float right, float bottom, float surface, out float2 centroid, out float totalArea)
		{
			float2 axisX = math.rotate(rotation, new float3(halfSize.x, 0, 0)).xy;
			float2 axisY = math.rotate(rotation, new float3(0, halfSize.y, 0)).xy;
			FixedList128Bytes<float2> polygon = default;
			polygon.Add(center - axisX - axisY);
			polygon.Add(center + axisX - axisY);
			polygon.Add(center + axisX + axisY);
			polygon.Add(center - axisX + axisY);
			float2 unused;
			totalArea = Area(polygon, out unused);
			Clip(ref polygon, new float2(1, 0), left);
			Clip(ref polygon, new float2(-1, 0), -right);
			Clip(ref polygon, new float2(0, 1), bottom);
			Clip(ref polygon, new float2(0, -1), -surface);
			return Area(polygon, out centroid);
		}

		private static void Clip(ref FixedList128Bytes<float2> polygon, float2 normal, float distance)
		{
			FixedList128Bytes<float2> output = default;
			if (polygon.Length == 0) return;
			float2 previous = polygon[polygon.Length - 1];
			float previousDistance = math.dot(previous, normal) - distance;
			for (int i = 0; i < polygon.Length; i++)
			{
				float2 current = polygon[i];
				float currentDistance = math.dot(current, normal) - distance;
				if ((currentDistance >= 0) != (previousDistance >= 0))
					output.Add(math.lerp(previous, current, previousDistance / (previousDistance - currentDistance)));
				if (currentDistance >= 0) output.Add(current);
				previous = current; previousDistance = currentDistance;
			}
			polygon = output;
		}

		private static float Area(FixedList128Bytes<float2> polygon, out float2 centroid)
		{
			float twiceArea = 0;
			float2 sum = float2.zero;
			float2 origin = polygon.Length == 0 ? float2.zero : polygon[0];
			for (int i = 0; i < polygon.Length; i++)
			{
				float2 a = polygon[i] - origin, b = polygon[(i + 1) % polygon.Length] - origin;
				float cross = a.x * b.y - b.x * a.y;
				twiceArea += cross;
				sum += (a + b) * cross;
			}
			centroid = math.abs(twiceArea) > 1e-8f ? origin + sum / (3f * twiceArea) : origin;
			return math.abs(twiceArea) * .5f;
		}

		public static PhysicsVelocity Apply(in ModFluidVolume water, PhysicsVelocity velocity, in PhysicsMass mass,
			float submergedArea, float totalArea, float2 gravity, float deltaTime, float bodyCenterY = float.NegativeInfinity)
		{
			return Apply(water, velocity, mass, submergedArea, totalArea, gravity, deltaTime, bodyCenterY, float2.zero, float2.zero);
		}

		public static PhysicsVelocity Apply(in ModFluidVolume water, PhysicsVelocity velocity, in PhysicsMass mass,
			float submergedArea, float totalArea, float2 gravity, float deltaTime, float bodyCenterY,
			float2 bodyCenter, float2 buoyancyCenter)
		{
			if (water.PhysicsEnabled == 0 || mass.InverseMass <= 0 || deltaTime <= 0 || submergedArea <= 0 || totalArea <= 1e-8f) return velocity;
			float fraction = math.saturate(submergedArea / totalArea);
			float bodyMass = 1f / mass.InverseMass;
			float2 buoyancy = -gravity * water.Density * water.BuoyancyMultiplier * submergedArea * deltaTime;
			float dragFraction = fraction;
			float decay = 1f - math.exp(-water.LinearDrag * dragFraction * deltaTime / bodyMass);
			float2 drag = (water.Flow - velocity.Linear.xy) * (bodyMass * decay);
			float2 current = math.normalizesafe(water.Flow) * (water.FlowAcceleration * fraction * bodyMass * deltaTime);
			float3 impulse = new float3(buoyancy + drag + current, 0);
			if (!math.all(math.isfinite(impulse))) return velocity;
			float maxImpulse = bodyMass * 20f;
			float magnitude = math.length(impulse);
			float impulseScale = magnitude > maxImpulse ? maxImpulse / magnitude : 1f;
			if (impulseScale < 1f) impulse *= impulseScale;
			velocity.Linear += impulse * mass.InverseMass;
			float torqueImpulse = (buoyancyCenter.x - bodyCenter.x) * buoyancy.y - (buoyancyCenter.y - bodyCenter.y) * buoyancy.x;
			velocity.Angular.z += torqueImpulse * mass.InverseInertia.z * impulseScale;
			velocity.Angular *= math.exp(-water.AngularDrag * fraction * deltaTime);
			return velocity;
		}
	}

	internal static class ModFluidRegistry
	{
		internal static readonly List<ModFluidHandle> Active = new List<ModFluidHandle>();
		private static ModFluidSystem s_system;
		public static int Count => Active.Count;
		public static int BodyCount => s_system == null || s_system.World == null || !s_system.World.IsCreated ? 0 : s_system.BodyCount;
		public static int ExecutedChunks => s_system == null ? 0 : s_system.ExecutedChunks;

		internal static void Add(ModFluidHandle handle)
		{
			if (Active.Count >= 64) throw new InvalidOperationException("At most 64 active Mod fluid volumes are supported.");
			World world = World.DefaultGameObjectInjectionWorld;
			if (world == null || !world.IsCreated) throw new InvalidOperationException("The part physics world is not ready.");
			if (s_system == null || s_system.World != world)
			{
				s_system = world.GetOrCreateSystemManaged<ModFluidSystem>();
				PartSimulationSystemGroup group = world.GetOrCreateSystemManaged<PartSimulationSystemGroup>();
				group.AddSystemToUpdateList(s_system);
				group.SortSystems();
			}
			Active.Add(handle);
		}

		internal static void Remove(ModFluidHandle handle)
		{
			Active.Remove(handle);
			if (Active.Count != 0 || s_system == null) return;
			ModFluidSystem system = s_system;
			s_system = null;
			if (system.World == null || !system.World.IsCreated) return;
			PartSimulationSystemGroup group = system.World.GetExistingSystemManaged<PartSimulationSystemGroup>();
			if (group != null) group.RemoveSystemFromUpdateList(system);
			system.World.DestroySystemManaged(system);
		}

		internal static void Destroyed(ModFluidSystem system)
		{
			if (s_system == system) { s_system = null; Active.Clear(); }
		}
	}

	[DisableAutoCreation]
	[UpdateInGroup(typeof(PartSimulationSystemGroup), OrderLast = true)]
	internal partial class ModFluidSystem : SystemBase
	{
		private const int ContactsPerChunk = 8;
		private EntityQuery m_parts;
		private NativeArray<ModFluidVolume> m_volumes;
		private NativeArray<ContactSlot> m_contacts;
		private NativeArray<int> m_counts;
		internal int BodyCount => m_parts.CalculateEntityCount();
		internal int ExecutedChunks { get; private set; }

		protected override void OnCreate()
		{
			m_parts = GetEntityQuery(ComponentType.ReadOnly<PartTypeValue>(), ComponentType.ReadOnly<LocalTransform>(),
				ComponentType.ReadOnly<PhysicsCollider>(), ComponentType.ReadOnly<PhysicsMass>(), ComponentType.ReadWrite<PhysicsVelocity>());
			m_volumes = new NativeArray<ModFluidVolume>(64, Allocator.Persistent);
		}

		protected override void OnDestroy()
		{
			Dependency.Complete();
			if (m_volumes.IsCreated) m_volumes.Dispose();
			if (m_contacts.IsCreated) m_contacts.Dispose();
			if (m_counts.IsCreated) m_counts.Dispose();
			ModFluidRegistry.Destroyed(this);
		}

		protected override void OnUpdate()
		{
			PartManager manager = PartManager.Instance;
			int volumeCount = ModFluidRegistry.Active.Count;
			float dt = World.Time.DeltaTime;
			if (manager == null || !manager.IsSimulating || manager.PartSimulator == null ||
				volumeCount == 0 || dt <= 0 || UnityEngine.Time.timeScale <= 0 || m_parts.IsEmptyIgnoreFilter) return;
			// This group is after MegaStructureMirrorSystem and completes physics integrity jobs.
			// Part impulses therefore survive until next step's MegaStructureDriveSystem.
			Dependency.Complete();
			float2 aggregateMin = new float2(float.MaxValue), aggregateMax = new float2(float.MinValue);
			for (int i = 0; i < volumeCount; i++)
			{
				ModFluidVolume volume = ModFluidRegistry.Active[i].Data;
				ModFluidRegistry.Active[i].SimulationSteps++;
				m_volumes[i] = volume;
				aggregateMin = math.min(aggregateMin, new float2(volume.Left, volume.Surface - volume.Depth));
				aggregateMax = math.max(aggregateMax, new float2(volume.Left + volume.Width, volume.Surface + volume.WaveAmplitude));
			}
			// IJobChunk indexes the unfiltered chunk cache, including holes left by filters.
			int chunks = m_parts.CalculateChunkCountWithoutFiltering();
			if (!m_counts.IsCreated || m_counts.Length < chunks)
			{
				if (m_counts.IsCreated) m_counts.Dispose();
				if (m_contacts.IsCreated) m_contacts.Dispose();
				int capacity = math.ceilpow2(math.max(1, chunks));
				m_counts = new NativeArray<int>(capacity, Allocator.Persistent);
				m_contacts = new NativeArray<ContactSlot>(capacity * ContactsPerChunk, Allocator.Persistent);
			}
			for (int i = 0; i < chunks; i++) m_counts[i] = -1;
			SimulationSettings settings = UserSettings.Instance.SimulationSettings;
			FluidJob job = new FluidJob
			{
				Transforms = GetComponentTypeHandle<LocalTransform>(true),
				Colliders = GetComponentTypeHandle<PhysicsCollider>(true),
				Masses = GetComponentTypeHandle<PhysicsMass>(true),
				Velocities = GetComponentTypeHandle<PhysicsVelocity>(false),
				Volumes = m_volumes, VolumeCount = volumeCount,
				Contacts = m_contacts, Counts = m_counts,
				Gravity = new float2(settings.GravityX, settings.GravityY), DeltaTime = dt,
				Time = UnityEngine.Time.time, Minimum = aggregateMin, Maximum = aggregateMax
			};
			if (Application.isEditor || !BurstCompiler.IsEnabled)
			{
				// This project's imported player DOTS assemblies have a managed Editor
				// compatibility path. New scheduled chunk jobs may be silently skipped there.
				// Execute the same batch body explicitly, once per matching chunk.
				NativeArray<ArchetypeChunk> batches = m_parts.ToArchetypeChunkArray(Allocator.Temp);
				try
				{
					for (int i = 0; i < batches.Length; i++)
					{
						ArchetypeChunk batch = batches[i];
						job.Execute(batch, i, false, default);
					}
				}
				finally { batches.Dispose(); }
			}
			else
			{
				Dependency = job.ScheduleParallel(m_parts, Dependency);
				Dependency.Complete();
			}
			ExecutedChunks = 0;
			for (int i = 0; i < chunks; i++) if (m_counts[i] >= 0) ExecutedChunks++;
			int delivered = 0;
			for (int chunk = 0; chunk < chunks && delivered < 128; chunk++)
			{
				for (int i = 0; i < m_counts[chunk] && delivered < 128; i++)
				{
					ContactSlot slot = m_contacts[chunk * ContactsPerChunk + i];
					if (slot.Volume >= ModFluidRegistry.Active.Count) continue;
					ModFluidHandle handle = ModFluidRegistry.Active[slot.Volume];
					Action<ModFluidContact> callback = handle.Contact;
					if (callback == null) continue;
					handle.DeliveredContacts++;
					delivered++;
					try { callback(slot.Contact); }
					catch (Exception ex) { Debug.LogWarning("[Mods] Fluid contact callback: " + ex.Message); }
				}
			}
		}

		private struct ContactSlot
		{
			public int Volume;
			public ModFluidContact Contact;
		}

		[BurstCompile]
		private struct FluidJob : IJobChunk
		{
			[ReadOnly] public ComponentTypeHandle<LocalTransform> Transforms;
			[ReadOnly] public ComponentTypeHandle<PhysicsCollider> Colliders;
			[ReadOnly] public ComponentTypeHandle<PhysicsMass> Masses;
			public ComponentTypeHandle<PhysicsVelocity> Velocities;
			[ReadOnly] public NativeArray<ModFluidVolume> Volumes;
			[NativeDisableParallelForRestriction] public NativeArray<ContactSlot> Contacts;
			public NativeArray<int> Counts;
			public int VolumeCount;
			public float2 Gravity, Minimum, Maximum;
			public float DeltaTime, Time;

			public void Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				var transforms = chunk.GetNativeArray(ref Transforms);
				var colliders = chunk.GetNativeArray(ref Colliders);
				var masses = chunk.GetNativeArray(ref Masses);
				var velocities = chunk.GetNativeArray(ref Velocities);
				int contactCount = 0;
				// Match the host's explicit chunk iteration, rather than depending on
				// ChunkEntityEnumerator implementations in imported player DOTS assemblies.
				for (int i = 0; i < chunk.Count; i++)
				{
					if (useEnabledMask && ((i < 64 ? chunkEnabledMask.ULong0 >> i : chunkEnabledMask.ULong1 >> (i - 64)) & 1UL) == 0) continue;
					PhysicsMass mass = masses[i];
					PhysicsCollider collider = colliders[i];
					if (mass.InverseMass <= 0 || !collider.Value.IsCreated) continue;
					LocalTransform transform = transforms[i];
					Aabb aabb = collider.Value.Value.CalculateAabb(new RigidTransform(transform.Rotation, transform.Position), transform.Scale);
					if (aabb.Max.x < Minimum.x || aabb.Min.x > Maximum.x ||
						aabb.Max.y < Minimum.y || aabb.Min.y > Maximum.y + math.abs(velocities[i].Linear.y * DeltaTime)) continue;
					Aabb local = collider.Value.Value.CalculateAabb();
					float3 localSize = math.max(local.Max - local.Min, new float3(.001f));
					float3 localCenter = (local.Min + local.Max) * .5f;
					float2 shapeCenter = (transform.Position + math.rotate(transform.Rotation, localCenter * transform.Scale)).xy;
					float2 bodyCenter = (transform.Position + math.rotate(transform.Rotation, mass.Transform.pos)).xy;
					PhysicsVelocity velocity = velocities[i];
					bool applied = false;
					for (int volumeIndex = 0; volumeIndex < VolumeCount; volumeIndex++)
					{
						ModFluidVolume water = Volumes[volumeIndex];
						if (aabb.Max.x < water.Left || aabb.Min.x > water.Left + water.Width ||
							aabb.Max.y < water.Surface - water.Depth) continue;
						float surface = ModFluidMath.SurfaceAt(water, shapeCenter.x, Time);
						if (water.CaptureContacts != 0 && contactCount < ContactsPerChunk)
						{
							float previousMin = aabb.Min.y - velocity.Linear.y * DeltaTime;
							float previousMax = aabb.Max.y - velocity.Linear.y * DeltaTime;
							float previousSurface = ModFluidMath.SurfaceAt(water, shapeCenter.x - velocity.Linear.x * DeltaTime, Time - DeltaTime);
							bool entering = velocity.Linear.y < water.Flow.y;
							bool crossed = entering
								? previousMin > previousSurface && aabb.Min.y <= surface
								: previousMax < previousSurface && aabb.Max.y >= surface;
							float speed = math.abs(velocity.Linear.y - water.Flow.y);
							if (crossed && speed > .3f)
							{
								Contacts[unfilteredChunkIndex * ContactsPerChunk + contactCount++] = new ContactSlot
								{
									Volume = volumeIndex,
									Contact = new ModFluidContact
									{
										Position = new float2(math.clamp(shapeCenter.x, water.Left, water.Left + water.Width), surface),
										Speed = math.min(speed, 40f), Radius = math.min(aabb.Max.x - aabb.Min.x, 8f),
										Entering = entering
									}
								};
							}
						}
						if (water.PhysicsEnabled == 0 || applied || aabb.Min.y > surface) continue;
						float2 centroid;
						float total;
						float area = ModFluidMath.SubmergedArea(shapeCenter, localSize.xy * (.5f * transform.Scale), transform.Rotation,
							water.Left, water.Left + water.Width, water.Surface - water.Depth, surface, out centroid, out total);
						float fill = ModFluidMath.ShapeFill(localSize, collider.Value.Value.MassProperties.Volume);
						area *= fill;
						total *= fill;
						if (area <= 1e-8f) continue;
						ModFluidVolume localWater = water;
						localWater.Surface = surface;
						velocity = ModFluidMath.Apply(localWater, velocity, mass, area, total, Gravity, DeltaTime,
							bodyCenter.y, bodyCenter, centroid);
						applied = true; // Overlapping pools do not multiply displacement/drag.
					}
					if (applied) velocities[i] = velocity;
				}
				Counts[unfilteredChunkIndex] = contactCount;
			}
		}
	}
}
