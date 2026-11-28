using System.Collections.Generic;
using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// 轮子的开关行为。真正的推力、射线与视觉滚动在 ECS 系统
	/// <c>Bpsim.Parts.Simulation.WheelSystem</c> 里，这里只负责「玩家点击 → 切换 <see cref="Wheel.Enabled" />」。
	/// </summary>
	/// <remarks>
	/// BPSS 的 <c>MotorWheel.OnTouch()</c> 就是 <c>SetEnabled(!m_enabled)</c>；
	/// <c>StickyWheel</c> / <c>OffRoadWheel</c> 继承同一套。<c>CartWheel</c> / <c>NormalWheel</c> /
	/// <c>SmallWheel</c>（除动力款 #7）没有 <c>HasOnOffToggle</c>，所以它们不会走到这里。
	/// </remarks>
	public sealed class WheelBehavior : PartBehavior
	{
		private const int IronBracketWheelIndex = 9000;
		private readonly Dictionary<Entity, Entity> m_rotorLinks = new Dictionary<Entity, Entity>();
		private readonly HashSet<int> m_linkedComponents = new HashSet<int>();

		public override void Init()
		{
			// 开局默认关闭，与 BPSS 的 m_enabled 默认 false 一致。
			if (Em.HasComponent<Wheel>(Part))
			{
				Wheel wheel = Em.GetComponentData<Wheel>(Part);
				Enabled = wheel.Enabled;
			}
			if (IsIronBracketWheel) CreateRotorLinks();
		}

		private bool IsIronBracketWheel => Info.PartType == PartType.MotorWheel && Info.PartIndex == IronBracketWheelIndex;

		private void CreateRotorLinks()
		{
			if (!PartGameplayUtil.TryGetGrid(Part, out int gridX, out int gridY, out int rotation, out _)) return;
			int ownComponent = PartGameplayUtil.ConnectedComponent(Part);
			byte layer = PartBuildLayerUtility.GetLayer(Em, Part);
			BitDirection top = BitDirection.Up.Rotate(rotation);
			for (int i = 0; i < 4; i++)
			{
				BitDirection direction = BitDirectionExtensions.FromIndex(i);
				if (direction == top) continue;
				(int dx, int dy) = direction.ToVector();
				if (!PartGameplayUtil.Simulator.PartMap.TryGet(gridX + dx, gridY + dy, layer, out PartGridInfo grid)) continue;
				TryCreateRotorLink(grid.PartContainer, direction, ownComponent, rotation);
				TryCreateRotorLink(grid.Part, direction, ownComponent, rotation);
			}
			PartCollisionFilter.ApplyIronBracketWheelFilters(Em, PartGameplayUtil.Simulator,
				UserSettings.Instance.SimulationSettings.PartSelfCollision);
		}

		private void TryCreateRotorLink(Entity target, BitDirection wheelDirection, int ownComponent, int wheelRotation)
		{
			if (!CanConnectRotorTarget(target, wheelDirection, wheelRotation)) return;
			int component = PartGameplayUtil.ConnectedComponent(target);
			if (component < 0 || component == ownComponent || !m_linkedComponents.Add(component)) return;
			bool frameOnly = IsFrameOnlyComponent(component);
			uint rotorCategory = frameOnly ? 1u << 31 : 0u;

			Entity joint = CreateRotorJoint(target);
			if (joint == Entity.Null)
			{
				m_linkedComponents.Remove(component);
				return;
			}
			m_rotorLinks.Add(target, joint);
			MarkRotorComponent(component, frameOnly, rotorCategory);
			if (frameOnly) MarkVehicleComponent(ownComponent);
		}

		private bool CanConnectRotorTarget(Entity target, BitDirection wheelDirection, int wheelRotation)
		{
			if (target == Entity.Null || target == Part || !PartGameplayUtil.Alive(target)
				|| !Em.HasComponent<PhysicsMass>(target) || !Em.HasComponent<LocalTransform>(target)
				|| !Em.HasComponent<PhysicsWorldIndex>(target) || !Em.HasComponent<PartTransform>(target)
				|| !Em.HasComponent<PartExtensionComponent>(target) || !Em.HasComponent<PartExtensionComponent>(Part)) return false;
			PartExtensionData source = Em.GetComponentData<PartExtensionComponent>(target).Value;
			PartExtensionData wheel = Em.GetComponentData<PartExtensionComponent>(Part).Value;
			int targetRotation = Em.GetComponentData<PartTransform>(target).Rotation;
			return source.IsConnectionSource
				&& (source.ConnectionDirection.Rotate(targetRotation) & wheelDirection.Reverse()) != 0
				&& (wheel.ConnectionDirection.Rotate(wheelRotation) & wheelDirection) != 0;
		}

		private Entity CreateRotorJoint(Entity target)
		{
			float3 anchorWorld = PartGameplayUtil.Position(Part);
			float3 sourcePosition = PartGameplayUtil.Position(Part);
			float3 targetPosition = PartGameplayUtil.Position(target);
			quaternion sourceRotation = PartGameplayUtil.Rotation(Part);
			quaternion targetRotation = PartGameplayUtil.Rotation(target);
			RigidTransform sourceTransform = new RigidTransform(sourceRotation, sourcePosition);
			RigidTransform targetTransform = new RigidTransform(targetRotation, targetPosition);
			RigidTransform targetFromSource = math.mul(math.inverse(targetTransform), sourceTransform);
			BodyFrame frameA = new BodyFrame
			{
				Position = math.transform(math.inverse(sourceTransform), anchorWorld),
				Axis = new float3(0f, 0f, 1f),
				PerpendicularAxis = new float3(1f, 0f, 0f)
			};
			BodyFrame frameB = new BodyFrame
			{
				Position = math.transform(targetFromSource, frameA.Position),
				Axis = math.rotate(targetFromSource.rot, frameA.Axis),
				PerpendicularAxis = math.rotate(targetFromSource.rot, frameA.PerpendicularAxis)
			};
			Entity joint = Em.CreateEntity();
			Em.SetName(joint, "IronBracketWheelRotorJoint");
			Em.AddSharedComponent(joint, new PhysicsWorldIndex(0u));
			Em.AddComponentData(joint, PhysicsJoint.CreateHinge(frameA, frameB));
			Em.AddComponentData(joint, new PhysicsConstrainedBodyPair(Part, target, false));
			Em.AddComponentData(joint, new PartJointInfo { Type = PartJointType.Normal, State = 1 });
			PartGameplayUtil.Simulator.Joints.Add(joint);
			TNTDamage.RefreshTopology();
			return joint;
		}

		private bool IsFrameOnlyComponent(int component)
		{
			foreach (Entity member in PartGameplayUtil.Simulator.Parts)
			{
				if (PartGameplayUtil.ConnectedComponent(member) != component || !Em.HasComponent<PartTypeValue>(member)) continue;
				PartType type = Em.GetComponentData<PartTypeValue>(member).Type;
				if (type != PartType.MetalFrame && type != PartType.WoodenFrame) return false;
			}
			return true;
		}

		private void MarkRotorComponent(int component, bool frameOnly, uint rotorCategory)
		{
			foreach (Entity member in PartGameplayUtil.Simulator.Parts)
			{
				if (member == Part || !Em.Exists(member) || PartGameplayUtil.ConnectedComponent(member) != component
					|| !Em.HasComponent<PhysicsVelocity>(member) || Em.HasComponent<MotorWheelRotorMember>(member)) continue;
				Em.AddComponentData(member, new MotorWheelRotorMember
				{
					Wheel = Part,
					FrameOnly = frameOnly,
					RotorCategory = rotorCategory
				});
			}
		}

		private void MarkVehicleComponent(int component)
		{
			foreach (Entity member in PartGameplayUtil.Simulator.Parts)
			{
				if (PartGameplayUtil.ConnectedComponent(member) != component || !Em.HasComponent<PhysicsCollider>(member)
					|| Em.HasComponent<MotorWheelRotorVehicleMember>(member)) continue;
				Em.AddComponentData(member, new MotorWheelRotorVehicleMember { Wheel = Part });
			}
		}

		public override void Trigger()
		{
			if (!Em.HasComponent<Wheel>(Part))
			{
				return;
			}
			Wheel wheel = Em.GetComponentData<Wheel>(Part);
			wheel.Enabled = !wheel.Enabled;
			// BPSS 在 SetEnabled 里同时把推力斜坡清零，重新开启时从 0 爬升。
			wheel.ThrottleTimer = 0f;
			wheel.Throttle = 0f;
			Em.SetComponentData(Part, wheel);
			Enabled = wheel.Enabled;
		}

		public override void Dispose()
		{
			foreach (Entity joint in m_rotorLinks.Values) TNTDamage.BreakJoint(joint);
			if (m_rotorLinks.Count > 0) TNTDamage.RefreshTopology();
			m_rotorLinks.Clear();
			m_linkedComponents.Clear();
		}

		public override string Describe()
		{
			if (!Em.HasComponent<Wheel>(Part))
			{
				return "wheel enabled=" + Enabled + " (component missing)";
			}
			Wheel wheel = Em.GetComponentData<Wheel>(Part);
			return "wheel " + wheel.Kind + " enabled=" + wheel.Enabled
				+ " power=" + wheel.PowerFactor.ToString("F2")
				+ " maxSpeed=" + wheel.MaximumSpeed.ToString("F2")
				+ " throttle=" + wheel.Throttle.ToString("F2")
				+ " contact=" + wheel.HasContact
				+ " spin=" + wheel.SpinSpeed.ToString("F2");
		}
	}

	/// <summary>
	/// 齿轮箱的开关行为。BPSS 的 <c>Gearbox.OnTouch()</c> 只翻转 <c>activated</c> 并换贴图，
	/// 倒车由同一焊接组件里的轮子读取状态后自行取反。
	/// </summary>
	public sealed class GearboxBehavior : PartBehavior
	{
		public override void Init()
		{
			if (Em.HasComponent<Gearbox>(Part))
			{
				Enabled = Em.GetComponentData<Gearbox>(Part).Activated;
			}
		}

		public override void Trigger()
		{
			if (!Em.HasComponent<Gearbox>(Part))
			{
				return;
			}
			Gearbox gearbox = Em.GetComponentData<Gearbox>(Part);
			gearbox.Activated = !gearbox.Activated;
			gearbox.AchievementSent = true;
			Em.SetComponentData(Part, gearbox);
			Enabled = gearbox.Activated;
		}

		public override string Describe()
		{
			if (!Em.HasComponent<Gearbox>(Part))
			{
				return "gearbox activated=" + Enabled + " (component missing)";
			}
			return "gearbox activated=" + Em.GetComponentData<Gearbox>(Part).Activated;
		}
	}
}
