using System.Collections;
using System.Collections.Generic;
using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>BPSS 901-17 TNT.cs, AlienTNT.cs and BlasterTNT.cs, indexed by the seven stock skins.</summary>
	public sealed class TNTBehavior : PartBehavior
	{
		private static readonly Queue<TNTBehavior> Pending = new Queue<TNTBehavior>();
		private static bool s_draining;
		private bool m_queued, m_spent, m_released, m_dud, m_destroyCorpse;
		private float m_cooldown, m_age, m_radius, m_radiusSpeed;
		private float3 m_center, m_centerVelocity, m_previousVelocity;
		private LineRenderer m_ring;
		private readonly Dictionary<Entity, float> m_alpha = new Dictionary<Entity, float>();
		public int Detonations { get; private set; }
		public bool Spent => m_spent;
		public bool Released => m_released;
		public bool Dud => m_dud;
		public int BurntJoints { get; private set; }
		private SimulationSettings Settings => UserSettings.Instance.SimulationSettings;
		private int GridRotation => Em.HasComponent<PartTransform>(Part) ? Em.GetComponentData<PartTransform>(Part).Rotation : 0;
		private bool Diagonal => GridRotation >= 4;
		private float3 VisualRight => math.mul(Rotation(), Diagonal ? new float3(0.70710678f, 0.70710678f, 0f) : new float3(1f, 0f, 0f));
		public float Radius => 8f * RadiusScale(Info.PartIndex) * (Info.PartIndex == 5 ? Settings.AlienTNTExplosionRadius : Settings.TNTExplosionRadius);
		public static float RadiusScale(int skin) => skin == 1 ? 1.13f : skin == 2 ? 2f : skin == 3 ? 0.25f : skin == 4 ? 3f : 1f;

		public override void Init()
		{
			m_released = Info.PartIndex == 6 && Diagonal;
			Enabled = m_released;
			if (Info.PartIndex != 1 && Info.PartIndex != 5) TNTCollisionSetup.EnableEvents(Part);
		}

		public override void Trigger()
		{
			if (m_spent || Info.PartIndex == 3) return;
			if (Info.PartIndex == 6 && !m_released)
			{
				m_released = Enabled = true;
				RefreshButtons();
				return;
			}
			Explode();
		}

		public void OnImpact(float relativeSpeed)
		{
			float threshold = Info.PartIndex == 0 ? (Diagonal ? 96f : 4f)
				: Info.PartIndex == 2 || Info.PartIndex == 4 ? 96f : Info.PartIndex == 3 ? 240f
				: Info.PartIndex == 6 ? 32f : float.PositiveInfinity;
			if (relativeSpeed > threshold) Explode();
		}

		public void Explode()
		{
			if (m_spent || m_queued || !PartGameplayUtil.Alive(Part) || (Info.PartIndex == 5 && m_cooldown > 0f)
				|| (Info.PartIndex == 6 && !m_released)) return;
			m_queued = true;
			Pending.Enqueue(this);
			if (s_draining) return;
			s_draining = true;
			try
			{
				while (Pending.Count > 0)
				{
					TNTBehavior next = Pending.Dequeue();
					next.m_queued = false;
					if (!next.m_spent && PartGameplayUtil.Alive(next.Part)) next.Detonate();
				}
			}
			finally
			{
				s_draining = false;
				while (Pending.Count > 0) Pending.Dequeue().m_queued = false;
			}
		}

		private void Detonate()
		{
			Detonations++;
			if (Info.PartIndex == 6) { StartWave(); return; }
			bool alien = Info.PartIndex == 5;
			m_spent = !alien;
			if (m_spent) TNTCollisionSetup.StopEvents(Part);
			m_cooldown = math.max(Settings.AlienTNTExplosionCoolingTime, Time.fixedDeltaTime);
			float3 center = Position();
			float force = alien ? Settings.AlienTNTExplosionForce : Settings.TNTExplosionForce;
			ref PartSimulatorUnmanaged simulator = ref PartGameplayUtil.Simulator;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity target = simulator.Parts[i];
				if (target == Part || !PartGameplayUtil.Alive(target)) continue;
				float3 delta = PartGameplayUtil.Position(target) - center;
				delta.z = 0f;
				if (math.lengthsq(delta) > Radius * Radius) continue;
				ApplyImpulse(target, CalculateImpulse(Info.PartIndex, GridRotation, VisualRight, delta, force, Radius, m_dud));
				if (Manager.Find(target) is TNTBehavior tnt)
				{
					if ((!alien && (tnt.Info.PartIndex == 0 || (Info.PartIndex == 4 && tnt.Info.PartIndex == 4)))
						|| (tnt.Info.PartIndex == 6 && math.lengthsq(delta) < 4f)) tnt.Explode();
				}
				if (alien && Settings.AlienTNTTriggerGun && Manager.Find(target) is ManualGunBehavior
					&& math.distancesq(PartGameplayUtil.Position(target) - math.mul(PartGameplayUtil.Rotation(target), new float3(1, 0, 0)), center) < 0.78f)
					Manager.StartCoroutine(FireGun(target));
			}
			if (Info.PartIndex == 0 && Diagonal) Burn(center);
			BpsimExplosionVisual.Spawn(center);
			Enabled = false;
			RefreshButtons();
			if (!alien) Manager.Retire(Part);
		}

		private IEnumerator FireGun(Entity gun)
		{
			yield return new WaitForSeconds(0.2f);
			if (Manager != null && Manager.IsRunning && PartGameplayUtil.Alive(gun)) Manager.Trigger(gun);
		}

		/// <summary>Returns impulse, before BPSS light-body compensation and inverse mass.</summary>
		public static float3 CalculateImpulse(int skin, int rotation, float3 right, float3 delta, float force, float radius, bool dud = false)
		{
			float distance = math.max(math.length(delta), 1f);
			float3 radial = math.normalizesafe(delta);
			float impulse = 25f * force;
			bool diagonal = rotation >= 4;
			switch (skin)
			{
			case 0: return radial * (impulse / math.pow(distance, 1.5f) * (diagonal ? 0.05f : 1f));
			case 1:
				float3 down = new float3(right.y, -right.x, 0f);
				float axial = math.dot(delta, down);
				return axial < 0.25f || axial > 9f || math.length(delta - down * axial) > 0.9f ? float3.zero : down * (16f * impulse);
			case 2: return radial * (10f * impulse / math.pow(distance, 1.5f) * (diagonal || rotation == 2 ? 1f : -1f));
			case 3:
				float3 up = new float3(-right.y, right.x, 0f);
				return dud || math.dot(radial, up) < 0.2f ? float3.zero : up * (200f * impulse);
			case 4: return radial * (8f * impulse * math.saturate(1f - distance / math.max(radius, 0.0001f)) * (diagonal || rotation == 2 ? -1f : 1f));
			case 5: return radial * (impulse / math.pow(distance, 1.5f) * (diagonal ? -10f : 1f));
			default: return float3.zero;
			}
		}

		private void ApplyImpulse(Entity target, float3 impulse)
		{
			if (!Em.HasComponent<PhysicsMass>(target)) return;
			float inverseMass = Em.GetComponentData<PhysicsMass>(target).InverseMass;
			if (inverseMass <= 0f) return;
			float mass = 1f / inverseMass;
			if (Info.PartIndex == 1) impulse *= mass < 5.5f ? mass : 1f;
			else impulse *= mass < 0.1f ? mass : mass < 0.4f ? mass / 0.4f : 1f;
			TNTImpulse.Apply(target, impulse);
		}

		public override void Tick(float dt)
		{
			m_cooldown = math.max(0f, m_cooldown - dt);
			if (!m_spent && Info.PartIndex == 3)
			{
				float3 velocity = PartGameplayUtil.LinearVelocity(Part);
				if (math.length(velocity - m_previousVelocity) / dt > 600f) { m_dud = true; Explode(); }
				m_previousVelocity = velocity;
			}
			if (Info.PartIndex == 6 && m_spent && m_age < 2f) TickWave(dt);
		}

		private void StartWave()
		{
			m_spent = true;
			TNTCollisionSetup.StopEvents(Part);
			Enabled = false;
			RefreshButtons();
			m_destroyCorpse = Settings.BlasterTNTDestroy;
			m_center = Position();
			m_centerVelocity = PartGameplayUtil.LinearVelocity(Part);
			m_radius = 0.5f;
			m_radiusSpeed = 160f;
			m_ring = PartGameplayUtil.CreateLine("[TNT shockwave]", new Color(1f, 1f, 1f, 0.25f), 0.06f);
			m_ring.loop = true;
			m_ring.positionCount = 96;
			if (m_destroyCorpse)
			{
				PartGameplayUtil.SetAlpha(Part, 0f, m_alpha);
				Manager.Detach(Part);
				TNTCollisionSetup.DisableCollision(Part);
			}
			else TintCorpse();
			ref PartSimulatorUnmanaged simulator = ref PartGameplayUtil.Simulator;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity target = simulator.Parts[i];
				if (target != Part && math.distancesq(Position().xy, PartGameplayUtil.Position(target).xy) < 64f
					&& Manager.Find(target) is TNTBehavior other && other.Info.PartIndex == 6) other.Explode();
			}
			DrawWave();
		}

		private static float WaveIntegral(float time) => time <= 0f ? 0f : time >= 0.02f ? 80000f * 0.02f / math.E : 80000f * time * math.exp(-time / 0.02f);
		private void TickWave(float dt)
		{
			float3 oldCenter = m_center;
			float oldRadius = m_radius, oldSpeed = m_radiusSpeed;
			m_age += dt;
			m_centerVelocity *= math.max(1f - 0.2f * dt, 0f);
			m_center += m_centerVelocity * dt;
			m_radiusSpeed *= math.max(1f - 0.2f * dt, 0f);
			m_radius += m_radiusSpeed * dt;
			ref PartSimulatorUnmanaged simulator = ref PartGameplayUtil.Simulator;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity target = simulator.Parts[i];
				if (target == Part || !PartGameplayUtil.Alive(target)) continue;
				float3 delta = PartGameplayUtil.Position(target) - m_center; delta.z = 0f;
				float before = math.distance(PartGameplayUtil.Position(target).xy, oldCenter.xy), after = math.length(delta);
				if (before < 0.5f || after < 0.5f) continue;
				float amplitude = math.abs(WaveIntegral((m_radius - after) / m_radiusSpeed) - WaveIntegral((oldRadius - before) / oldSpeed));
				// BlasterTNT applies physical impulse directly, without TNT's light-body compensation.
				if (Em.HasComponent<PhysicsMass>(target)) TNTImpulse.Apply(target,
					math.normalizesafe(delta) * (amplitude / math.max(after * after, 16f)));
			}
			DrawWave();
			if (m_age >= 2f)
			{
				PartGameplayUtil.DestroyLine(m_ring); m_ring = null;
				if (m_destroyCorpse) Manager.Retire(Part);
			}
		}

		private void DrawWave()
		{
			if (m_ring == null) return;
			m_ring.enabled = true;
			m_ring.startColor = m_ring.endColor = new Color(1, 1, 1, math.min(64f / (m_radius * m_radius), 0.25f));
			for (int i = 0; i < 96; i++)
			{
				float angle = i * (2f * math.PI / 96);
				m_ring.SetPosition(i, new Vector3(m_center.x + math.cos(angle) * m_radius, m_center.y + math.sin(angle) * m_radius, -0.8f));
			}
		}

		private void TintCorpse()
		{
			if (!Em.HasBuffer<LinkedEntityGroup>(Part)) return;
			var group = Em.GetBuffer<LinkedEntityGroup>(Part);
			for (int i = 0; i < group.Length; i++)
			{
				Entity child = group[i].Value;
				if (!Em.HasComponent<Unity.Rendering.MaterialColor>(child)) continue;
				var color = Em.GetComponentData<Unity.Rendering.MaterialColor>(child);
				float grey = math.dot(color.Value.xyz, new float3(0.299f, 0.587f, 0.114f)) * 0.5f;
				color.Value.xyz = new float3(grey);
				Em.SetComponentData(child, color);
			}
		}

		private void Burn(float3 center)
		{
			var targets = new List<(Entity part, float distance, int bucket)>();
			ref PartSimulatorUnmanaged simulator = ref PartGameplayUtil.Simulator;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity part = simulator.Parts[i];
				if (part == Part || !PartGameplayUtil.Alive(part) || !Em.HasComponent<PhysicsMass>(part)
					|| Em.GetComponentData<PhysicsMass>(part).InverseMass <= 0f) continue;
				PartTypeInfo type = PartGameplayUtil.TypeInfo(part);
				if (type.PartType == PartType.Pumpkin && type.PartIndex == 0) continue;
				float3 delta = PartGameplayUtil.Position(part) - center;
				float distance = math.length(delta.xy);
				if (distance > 12f) continue;
				int bucket = ((int)math.floor((math.atan2(delta.y, delta.x) + math.PI) * (16f / (2f * math.PI)))) % 16;
				targets.Add((part, distance, bucket));
			}
			targets.Sort((a, b) => a.distance.CompareTo(b.distance));
			float[] occlusion = new float[16];
			for (int i = 0; i < 16; i++) occlusion[i] = 1f;
			foreach (var target in targets)
			{
				PartTypeInfo type = PartGameplayUtil.TypeInfo(target.part);
				float strength = Em.HasComponent<PartExtensionComponent>(target.part)
					? Em.GetComponentData<PartExtensionComponent>(target.part).Value.ConnectionStrength : 0f;
				float defense = type.Equals(BasePart.MetalBox) ? 1f
					: type.PartType == PartType.JetEngine || Manager.Find(target.part) is FuelTubeBehavior ? 0.7f
					: strength >= 1200f ? 0.6f : strength >= 900f ? 0.5f : strength >= 600f ? 0.4f : 0.3f;
				float ratio = 0.16f * (1f - math.pow(target.distance / 12f, 3f)) * occlusion[target.bucket] / defense;
				if (ratio > 0.025f)
				{
					BurntJoints += TNTDamage.BurnJoints(target.part, ratio - 0.025f);
					TNTImpulse.Apply(target.part, math.normalizesafe(PartGameplayUtil.Position(target.part) - center, new float3(0, 1, 0))
						* (1000f * (ratio - 0.025f) * Time.fixedDeltaTime));
				}
				float bodyRadius = 0.7f;
				if (Em.HasComponent<PhysicsCollider>(target.part))
				{
					var collider = Em.GetComponentData<PhysicsCollider>(target.part);
					if (collider.Value.IsCreated) bodyRadius = math.length(collider.Value.Value.CalculateAabb().Extents.xy) * 0.5f;
				}
				float halfAngle = bodyRadius >= target.distance ? math.PI : math.asin(bodyRadius / math.max(target.distance, 0.01f));
				int spread = math.max(1, (int)math.ceil(halfAngle / (2f * math.PI / 16f)));
				for (int i = -spread; i <= spread; i++) occlusion[(target.bucket + i + 32) % 16] *= 1f - 0.5f * defense * (1f - math.abs(i) / (float)spread);
			}
		}

		/// <summary>Projectile explosions trigger all non-Epic TNT; ordinary TNT only chains the default skin.</summary>
		public static void FromProjectile(float3 center, float radius)
		{
			var manager = PartGameplayManager.Instance;
			if (manager == null || !manager.IsRunning) return;
			ref PartSimulatorUnmanaged simulator = ref PartGameplayUtil.Simulator;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity target = simulator.Parts[i];
				if (!(manager.Find(target) is TNTBehavior tnt)) continue;
				float d = math.distancesq(center.xy, PartGameplayUtil.Position(target).xy);
				if (d <= radius * radius && (tnt.Info.PartIndex == 0 || tnt.Info.PartIndex == 1 || tnt.Info.PartIndex == 2 || tnt.Info.PartIndex == 5
					|| (tnt.Info.PartIndex == 6 && d < 4f))) tnt.Explode();
			}
		}

		private static void RefreshButtons()
		{
			if (Bpsim.UI.PartButtonList.Instance != null) Bpsim.UI.PartButtonList.Instance.NeedsUpdate = true;
		}
		public override void Dispose() { PartGameplayUtil.DestroyLine(m_ring); m_ring = null; }
		public override string Describe() => "tnt#" + Info.PartIndex + " spent=" + m_spent + " released=" + m_released + " dud=" + m_dud + " detonations=" + Detonations;
	}
}
