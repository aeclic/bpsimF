using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Bpsim.Parts.Gameplay
{
	internal struct UmbrellaVisual : IComponentData { public float3 BasePosition; }
	public sealed partial class WingBehavior
	{
		private static readonly float[] WingAngles = { -180,-135,-90,-45,-10,10,15,19,22,45,90,135,180 };
		private static readonly float[] WingLift = { 0,-.2f,0,-.2f,0,1.5f,1.75f,.8f,.1f,.2f,0,-.2f,0 };
		private static readonly float[] TailAngles = { -180,-135,-90,-45,-10,10,45,90,135,180 };
		private static readonly float[] TailLift = { 0,-1.5f,0,-1.5f,0,1,1.5f,0,-1.5f,0 };
		public bool IsTail => Info.PartType == PartType.Tailplane || Info.PartType == PartType.MetalTail;
		public float LastAngle { get; private set; }
		public float LastCoefficient { get; private set; }
		public float3 LastForce { get; private set; }
		public override void Init()
		{
			Enabled = true;
			if (Em.HasComponent<PhysicsDamping>(Part)) Em.SetComponentData(Part, new PhysicsDamping { Linear = 1f, Angular = .2f });
		}
		public override void Trigger() => Enabled = !Enabled;
		internal static float Coefficient(float angle, bool tail)
		{
			var x = tail ? TailAngles : WingAngles;
			var y = tail ? TailLift : WingLift;
			for (int i = 1; i < x.Length; i++)
				if (angle <= x[i]) return math.lerp(y[i - 1], y[i], math.saturate((angle - x[i - 1]) / (x[i] - x[i - 1])));
			return y[y.Length - 1];
		}
		public override void Tick(float dt)
		{
			LastForce = float3.zero;
			if (!Enabled) return;
			float3 air = PartVariantPhysics.AirVelocity(Part);
			float speed = math.length(air);
			if (speed < 1E-5f) return;
			var rotation = PartVariantPhysics.Rotation(Part);
			float3 chord = math.rotate(rotation, new float3(1,0,0));
			float3 normal = math.rotate(rotation, new float3(0,0,1));
			PartGameplayUtil.TryGetGrid(Part, out _, out _, out _, out bool flipped);
			float sign = flipped ? -1f : 1f;
			if (IsTail)
			{
				float roll = math.degrees(math.acos(math.clamp(sign * chord.x, -1f, 1f))) * (chord.y >= 0 ? 1 : -1);
				chord = math.rotate(quaternion.AxisAngle(normal, math.radians(.4f * (roll - 30f))), chord);
			}
			float angle = sign * (math.cross(air, chord).z >= 0 ? 1 : -1)
				* math.degrees(math.acos(math.clamp(math.dot(air / speed, chord), -1f, 1f)));
			LastAngle = angle;
			LastCoefficient = Coefficient(angle, IsTail);
			float lift = IsTail ? (Info.PartType == PartType.MetalTail ? 1f : .2f) : (Info.PartType == PartType.MetalWing ? 1.5f : .8f);
			float3 force = lift * speed * speed * LastCoefficient * math.cross(normal, air / speed);
			LastForce = force * math.min(1f, 100f / math.max(math.length(force), 1E-5f));
			PartVariantPhysics.Force(Part, LastForce, dt);
		}
		public override string Describe() => (IsTail ? "tail" : "wing") + " enabled=" + Enabled + " angle=" + LastAngle.ToString("F2")
			+ " coeff=" + LastCoefficient.ToString("F2") + " force=" + LastForce;
	}

	public sealed partial class UmbrellaBehavior
	{
		private bool m_open = true;
		private float m_timer;
		private float m_angle;
		private float3 m_anchor;
		public float DeltaAngle { get; private set; }
		public bool Open => m_open;
		public float3 LastForce { get; private set; }
		public int Mode => PartVariantPhysics.IsAssembly(Part) ? Info.PartIndex : 0;
		public override void Init()
		{
			Enabled = false;
			RememberPose();
			UpdateVisual(0);
		}
		private void RememberPose()
		{
			float3 up = math.rotate(PartVariantPhysics.Rotation(Part), new float3(0,1,0));
			m_angle = math.atan2(up.y, up.x);
			m_anchor = PartVariantPhysics.Position(Part);
		}
		public void SetAngle(float degrees) => DeltaAngle = math.radians(math.clamp(degrees, -180, 180));
		public override void Trigger()
		{
			if (PartVariantPhysics.IsAssembly(Part) || Enabled) Enabled = !Enabled;
		}
		public override void Tick(float dt)
		{
			float3 up = math.rotate(PartVariantPhysics.Rotation(Part), new float3(0,1,0));
			float3 right = math.rotate(PartVariantPhysics.Rotation(Part), new float3(1,0,0));
			float omega = PartVariantPhysics.AngularSpeed(Part);
			float angle = math.atan2(up.y, up.x);
			LastForce = float3.zero;
			if (m_open)
			{
				int mode = Mode;
				if (mode == 1 || mode == 6)
				{
					float error = PartVariantPhysics.Wrap(angle - m_angle);
					float force = -16f * omega;
					if (mode == 6)
					{
						force = -32f * omega - 400f * error;
						m_angle = PartVariantPhysics.Wrap(m_angle + error * math.saturate(10f * math.abs(error) - .2f));
					}
					else m_angle = angle;
					LastForce = PartVariantPhysics.Perpendicular(up) * math.clamp(force, -150, 150);
					PartVariantPhysics.Torque(Part, -64 * omega - 1024 * error, dt, true);
				}
				else if (mode == 2 || mode == 4)
				{
					float3 velocity = PartGameplayUtil.LinearVelocity(Part); velocity.z = 0;
					float ratio = math.min(1, 4f / math.max(math.length(velocity), 1E-5f));
					PartGameplayUtil.AddVelocity(Part, -velocity * ratio);
					m_anchor = math.lerp(PartVariantPhysics.Position(Part), m_anchor, ratio);
					float3 correction = (m_anchor - PartVariantPhysics.Position(Part)) / dt; correction.z = 0;
					PartGameplayUtil.AddVelocity(Part, correction * math.min(1, 4 / math.max(math.length(correction), 1E-5f)));
					PartVariantPhysics.Torque(Part, -64 * omega, dt, true);
				}
				else if (mode == 3 || mode == 5)
				{
					float desired = m_angle + DeltaAngle;
					float error = PartVariantPhysics.Wrap(desired - angle);
					float force = (-32 * omega + 400 * error) * (mode == 3 ? 1f : 1.5f);
					float3 direction = mode == 3 ? up : new float3(math.cos(desired), math.sin(desired), 0);
					LastForce = PartVariantPhysics.Perpendicular(direction) * math.clamp(force, -150, 150);
					PartVariantPhysics.Torque(Part, -64 * omega + 1024 * error, dt, true);
				}
				else
				{
					float3 air = PartVariantPhysics.AirVelocity(Part);
					float vertical = math.dot(up, air), lateral = math.dot(right, air);
					// The BPSS force signs oppose the two local components, including a flipped canopy.
					LastForce = -up * math.clamp(vertical * math.abs(vertical), -60, 60)
						- right * math.clamp(.5f * lateral * math.abs(lateral), -60, 60);
				}
				PartVariantPhysics.Force(Part, LastForce, dt, (mode == 0 ? .2f * up : float3.zero));
			}
			if (!Enabled) return;
			m_timer += dt;
			UpdateVisual(math.saturate(m_timer / .25f));
			PartVariantPhysics.Force(Part, up * (m_open ? 20f : -4f), dt);
			if (m_timer < .25f) return;
			m_timer = 0;
			m_open = !m_open;
			Enabled = false;
			if (m_open && Mode != 3 && Mode != 5) RememberPose();
			if (Bpsim.UI.PartButtonList.Instance != null) Bpsim.UI.PartButtonList.Instance.NeedsUpdate = true;
		}
		private void UpdateVisual(float progress)
		{
			if (!Em.HasBuffer<LinkedEntityGroup>(Part)) return;
			int count = Em.GetBuffer<LinkedEntityGroup>(Part).Length;
			for (int i = 0; i < count; i++)
			{
				Entity child = Em.GetBuffer<LinkedEntityGroup>(Part)[i].Value;
				if (!Em.HasComponent<UmbrellaVisual>(child)) continue;
				float blend = m_open ? 1 - math.pow(progress, 3) : math.pow(progress, 6);
				float yScale = math.lerp(2f, 1f, blend);
				var transform = Em.GetComponentData<LocalTransform>(child);
				transform.Position = Em.GetComponentData<UmbrellaVisual>(child).BasePosition;
				transform.Position.y = math.lerp(-.05f, 0, m_open ? 1 - progress : progress) - .45f * (yScale - 1);
				transform.Scale = 1;
				Em.SetComponentData(child, transform);
				var scale = new PostTransformScale { Value = float3x3.Scale(new float3(math.lerp(.4f, 1, blend), yScale, 1)) };
				if (Em.HasComponent<PostTransformScale>(child)) Em.SetComponentData(child, scale);
				else Em.AddComponentData(child, scale);
			}
		}
		public override string Describe() => "umbrella open=" + m_open + " moving=" + Enabled + " mode=" + Mode + " angle=" + math.degrees(DeltaAngle).ToString("F1") + " force=" + LastForce;
	}
}
