using System.Collections.Generic;
using System.Text;
using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// BPSS GrapplingHook (吸盘枪): fires at a part that is not part of the same
	/// connected component, tethers it with a spring and winches the vehicle in.
	/// </summary>
	public sealed class GrappleHookBehavior : PartBehavior
	{
		private GeneratorBehavior m_generator;

		public override void Init()
		{
			// BPSS PartGenerator is enabled in ISettingsB. Enclosing skins 1-4 switches
			// their Shoot path from a tether to a single-part copier; free hooks remain hooks.
			if (Info.PartIndex >= 1 && Info.PartIndex <= 4 && Em.HasComponent<PartContainerValue>(Part)
				&& PartGameplayUtil.Alive(Em.GetComponentData<PartContainerValue>(Part).Value))
			{
				Kind = PartGameplayKind.Generator;
				m_generator = new GeneratorBehavior { Part = Part, Info = Info, Kind = Kind };
				m_generator.Init();
			}
		}
		private Entity m_anchor;

		private float3 m_anchorPoint;

		private bool m_attached;

		private float m_limit;

		private float m_winchSpeed = 3.5f;

		private float m_minimumDistance = 1.2f;

		private float m_springStrength = 32f;

		private float m_springDamping = 10f;

		private float m_cooldown;

		private LineRenderer m_line;

		public override void Trigger()
		{
			if (m_generator != null) { m_generator.Trigger(); return; }
			if (m_attached)
			{
				Release("manual release");
				return;
			}
			if (m_cooldown > 0f)
			{
				return;
			}
			float3 forward = Forward();
			Entity target = PartGameplayManager.FindTargetInCone(Part, forward, 9f, 0.55f);
			if (target == Entity.Null)
			{
				Manager.Log("hook: no target in cone");
				m_cooldown = 0.2f;
				return;
			}
			m_anchor = target;
			m_anchorPoint = PartGameplayUtil.Position(target);
			float distance = math.distance(Position(), m_anchorPoint);
			m_limit = math.max(distance, m_minimumDistance);
			m_attached = true;
			Enabled = true;
			m_cooldown = 0.35f;
			if (m_line == null)
			{
				m_line = PartGameplayUtil.CreateLine("GrappleTether", new Color(0.85f, 0.9f, 0.95f, 0.95f), 0.07f);
			}
			m_line.enabled = true;
			Manager.Log("hook: attached to " + target.Index + " distance=" + distance.ToString("F2"));
		}

		public override void Tick(float deltaTime)
		{
			if (m_generator != null) { m_generator.Tick(deltaTime); return; }
			if (m_cooldown > 0f)
			{
				m_cooldown -= deltaTime;
			}
			if (!m_attached)
			{
				return;
			}
			if (!PartGameplayUtil.Alive(m_anchor))
			{
				Release("anchor destroyed");
				return;
			}
			m_anchorPoint = PartGameplayUtil.Position(m_anchor);
			float3 position = Position();
			float3 delta = m_anchorPoint - position;
			float distance = math.length(delta);
			if (distance > 14f)
			{
				Release("out of range");
				return;
			}
			if (distance > 1E-04f)
			{
				m_limit = math.max(m_minimumDistance, m_limit - m_winchSpeed * deltaTime);
				if (distance > m_limit)
				{
					float3 direction = delta / distance;
					float radialSpeed = math.dot(PartGameplayUtil.LinearVelocity(Part), direction);
					float stretch = distance - m_limit;
					float acceleration = m_springStrength * stretch - m_springDamping * radialSpeed;
					acceleration = math.clamp(acceleration, -90f, 90f);
					PartGameplayUtil.AddVelocity(Part, direction * (acceleration * deltaTime));
				}
			}
			if (m_line != null)
			{
				PartGameplayUtil.SetLine(m_line, position, m_anchorPoint, new Color(0.85f, 0.9f, 0.95f, 0.95f), -0.45f);
			}
		}

		private void Release(string reason)
		{
			if (m_line != null)
			{
				m_line.enabled = false;
			}
			m_attached = false;
			m_anchor = Entity.Null;
			Enabled = false;
			Manager.Log("hook: " + reason);
		}

		public override void Dispose()
		{
			PartGameplayUtil.DestroyLine(m_line);
			m_line = null;
			m_attached = false;
			m_anchor = Entity.Null;
		}

		public override string Describe()
		{
			if (m_generator != null) return m_generator.Describe();
			return "hook#" + Info.PartIndex + " attached=" + m_attached + " anchor=" + (m_anchor != Entity.Null) + " limit=" + m_limit.ToString("F2");
		}
	}

	/// <summary>
	/// BPRC ExplodingGrapplingHook: yellow alien skin (index 5), one shot per touch.
	/// Hook_6 has mass 0.5, impulse 20, no gravity, TTL 1 and explosion radius 8.
	/// </summary>
	public sealed class ManualGunBehavior : PartBehavior
	{
		private const float CoolingTime = 1f;
		private Entity m_projectile;
		private float m_cooldown;
		private int m_shots;

		public override void Trigger()
		{
			RefreshReady();
			if (Enabled)
			{
				return;
			}
			float3 forward = Forward();
			m_projectile = AutoGunProjectileSpawner.Spawn(Part, Position() + forward * 0.55f, forward,
				radius: 8f, strength: 25f, speed: 40f, gravity: false, inheritVelocity: false, impulseFalloff: true);
			if (m_projectile == Entity.Null)
			{
				return;
			}
			Enabled = true;
			m_cooldown = CoolingTime;
			m_shots++;
		}

		public override void Tick(float deltaTime)
		{
			m_cooldown = math.max(0f, m_cooldown - deltaTime);
			RefreshReady();
		}

		private void RefreshReady()
		{
			// BPRC releases the firing lock on impact, or after GunProjectileCoolingTime.
			if (m_cooldown <= 0f || m_projectile == Entity.Null || !Em.Exists(m_projectile))
			{
				Enabled = false;
			}
		}

		public override void Dispose()
		{
			if (m_projectile != Entity.Null && Em.Exists(m_projectile))
			{
				Em.DestroyEntity(m_projectile);
			}
			m_projectile = Entity.Null;
			Enabled = false;
		}

		public override string Describe()
		{
			return "manualgun cooling=" + Enabled + " shots=" + m_shots;
		}
	}

	/// <summary>
	/// BPSS AutoGun (机炮): toggles on and automatically fires an exploding
	/// projectile at the nearest foreign part inside a forward cone.
	/// </summary>
	/// <remarks>
	/// <para>
	/// 对应 BPSS <c>AutoGun : ExplodingGrapplingHook</c>。原版 <c>FixedUpdate</c> 里先用
	/// <c>AutoGunAngle</c>/<c>AutoGunMaxDistance</c> 扫一遍有没有异队部件（只用于「要不要开火」），
	/// 然后以 <c>AutoGunRandomProbability</c>（默认 0.1，50 Hz 下平均 0.2 s 一发）调用
	/// <c>Shoot()</c>。<c>Shoot()</c> 生成的是<b>真弹丸</b>
	/// <c>ExplodingGrapplingHookProjectile</c>：带刚体、沿机炮<b>正前方</b>飞（不做任何追踪），
	/// 撞到东西或 TTL 到期就 <c>Explode()</c>（<c>AddExplosionForce</c>）。
	/// </para>
	/// <para>
	/// 这里的实现同样打真弹丸：<see cref="AutoGunProjectileSpawner.Spawn" /> 出一发 ECS 弹丸实体，
	/// 由 <see cref="AutoGunProjectileSystem" /> 负责飞行、碰撞与爆炸。
	/// <b>但与初版不同：开火不再要求在前方找到目标</b>——只要机炮是开启状态就按
	/// <see cref="FireInterval" /> 持续朝正前方射击（用户实测原版就是这个观感：
	/// 点一下之后一直突突，不看有没有目标）。目标扫描只保留在日志里，用于解释命中来源。
	/// </para>
	/// </remarks>
	public sealed class AutoGunBehavior : PartBehavior
	{
		/// <summary>
		/// 开火间隔（秒）。
		/// </summary>
		/// <remarks>
		/// BPSS 的 <c>GunProjectileForcedCoolingTime</c> 默认 0（没有硬冷却），实际射速来自
		/// <c>AutoGunRandomProbability</c> = 0.1：每个 FixedUpdate（50 Hz）有 10 % 概率
		/// <c>Shoot()</c>，平均 0.2 s 一发。这里用等价的固定间隔，行为可预测、也方便回归。
		/// </remarks>
		private const float FireInterval = 0.2f;

		/// <summary>一帧最多补几发，避免卡顿后瞬间倾泻。</summary>
		private const int MaxShotsPerTick = 3;

		/// <summary>炮口相对部件中心的正前方偏移（格），避免弹丸生成在自身碰撞体里。</summary>
		private const float MuzzleOffset = 0.55f;

		private float m_timer;

		private int m_shots;

		private int m_hits;

		public override void Trigger()
		{
			Enabled = !Enabled;
			m_timer = 0f;
			Manager.Log("autogun: " + (Enabled ? "enabled" : "disabled"));
		}

		public override void Tick(float deltaTime)
		{
			if (!Enabled)
			{
				m_timer = 0f;
				return;
			}
			m_timer += deltaTime;
			int fired = 0;
			while (m_timer >= FireInterval && fired < MaxShotsPerTick)
			{
				m_timer -= FireInterval;
				Fire();
				fired++;
			}
			if (m_timer >= FireInterval)
			{
				// 掉帧太久时不累积欠账，否则恢复瞬间会连喷一串。
				m_timer = FireInterval;
			}
		}

		/// <summary>
		/// 朝机炮正前方发射一发弹丸（不需要目标，只要机炮开着就一直打）。
		/// </summary>
		/// <remarks>
		/// 弹丸方向永远是机炮的 <see cref="PartBehavior.Forward" />，不做任何追踪或提前量，
		/// 与原版 <c>AutoGun</c> 一致（打偏就打偏）。
		/// </remarks>
		private void Fire()
		{
			float3 forward = Forward();
			float3 origin = Position() + forward * MuzzleOffset;
			Entity projectile = AutoGunProjectileSpawner.Spawn(Part, origin, forward);
			m_shots++;
			if (projectile == Entity.Null)
			{
				Manager.Log("autogun: shot #" + m_shots + " failed (no ECS world)");
			}
		}

		/// <summary>由 <see cref="AutoGunProjectileSystem" /> 在弹丸爆炸时回报命中。</summary>
		public void ReportHit()
		{
			m_hits++;
		}

		public override string Describe()
		{
			return "autogun enabled=" + Enabled + " shots=" + m_shots + " hits=" + m_hits;
		}
	}

	/// <summary>
	/// BPSS 生成器（<c>GrapplingHook</c> 家族的 1-4 单部件生成器 / 8-10 多部件生成器）。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版行为（<c>GrapplingHook.Shoot()</c> + <c>PartGeneratorManager.GeneratePart</c>）：
	/// 模板取生成器<b>正后方</b> 1..<c>MaxCopyDistance</c> 格；复制体落在<b>正前方</b>
	/// <c>CopyDistance(+DetectionDistance)</c> 格处，相对偏移保持不变（所以后方连续 3 格
	/// 的模板会在前方变成连续 3 格的一串）。
	/// </para>
	/// <para>
	/// 距离取 <c>INDeclarationSettingsExp.json</c> 的默认值：
	/// <c>PartGeneratorDistances = [4,5,6,7]</c>（单部件，按 <c>customPartIndex-1</c> 取）、
	/// <c>MultipartGeneratorDistances = [7,8,9]</c>（多部件，按 <c>customPartIndex-8</c> 取）、
	/// <c>MultipartGeneratorDetectionDistance = 3</c>。
	/// 多部件的落地距离 = <c>CopyDistance + DetectionDistance</c>，单部件不叠加检测距离。
	/// </para>
	/// <para>
	/// 复制体之间按「相邻 1 格」焊接，但<b>不与生成器焊接</b> —— 原版同样只把生成体并进
	/// 运行时载具的连通块，中间那段空格没有任何连接。
	/// </para>
	/// </remarks>
	public sealed class GeneratorBehavior : PartBehavior
	{
		/// <summary>BPSS <c>PartGeneratorDistances</c>（单部件生成器，索引 1-4）。</summary>
		private static readonly int[] s_singleCopyDistances = new int[4] { 4, 5, 6, 7 };

		/// <summary>BPSS <c>MultipartGeneratorDistances</c>（多部件生成器，索引 8-10）。</summary>
		private static readonly int[] s_multiCopyDistances = new int[3] { 7, 8, 9 };

		/// <summary>BPSS <c>MultipartGeneratorDetectionDistance</c>：向后找模板的格数。</summary>
		private const int MultiDetectionDistance = 3;

		/// <summary>BPSS <c>PartGeneratorCoolingTime</c>：0.2 s 冷却。</summary>
		private const float Cooldown = 0.2f;

		private float m_cooldown;

		private int m_generated;

		private string m_lastReason = "idle";

		public override void Trigger()
		{
			if (m_cooldown > 0f)
			{
				return;
			}
			m_cooldown = Cooldown;
			int created = Generate();
			m_generated += created;
			Manager.Log("generator#" + Info.PartIndex + ": generated=" + created + " total=" + m_generated + " (" + m_lastReason + ")");
		}

		public override void Tick(float deltaTime)
		{
			if (m_cooldown > 0f)
			{
				m_cooldown -= deltaTime;
			}
		}

		/// <summary>索引 8-10 是多部件生成器（BPSS <c>BasePart.MultipartGenerators</c>）。</summary>
		private bool IsMultipart => Info.PartIndex >= 8;

		private int Generate()
		{
			EntityManager em = PartGameplayUtil.Em;
			ref PartSimulatorUnmanaged simulator = ref PartManager.Instance.PartSimulator.Unmanaged;
			if (!PartGameplayUtil.TryGetGrid(Part, out int gridX, out int gridY, out int rotation, out bool flipped))
			{
				m_lastReason = "no grid";
				return 0;
			}
			Vector3 direction = PartGameplayUtil.DirectionFromRotation(rotation, BasePart.IsEightWay(Info));
			int dirX = Mathf.RoundToInt(direction.x);
			int dirY = Mathf.RoundToInt(direction.y);
			if (dirX == 0 && dirY == 0)
			{
				m_lastReason = "no direction";
				return 0;
			}
			// 生成器所在建造层（PartMap 的第三维是层号，公共层是 PartBuildLayer.MinLayer = 1，
			// 不是 0 —— 用 0 查表永远查不到任何部件）。
			byte layer = PartBuildLayerUtility.GetLayer(em, Part);
			int detection = IsMultipart ? MultiDetectionDistance : 1;
			// 模板：生成器正后方 1..detection 格（近 → 远）。
			List<Entity> templates = new List<Entity>(detection);
			for (int i = 1; i <= detection; i++)
			{
				int x = gridX - dirX * i;
				int y = gridY - dirY * i;
				if (!simulator.PartMap.TryGet(x, y, layer, out var grid))
				{
					continue;
				}
				Entity candidate = grid.HasPart ? grid.Part : grid.PartContainer;
				if (candidate != Entity.Null && candidate != Part && PartGameplayUtil.Alive(candidate))
				{
					templates.Add(candidate);
				}
			}
			if (templates.Count == 0)
			{
				m_lastReason = "no template behind (layer " + layer + ")";
				return 0;
			}
			int created = 0;
			Entity previous = Entity.Null;
			float3 previousPosition = default(float3);
			quaternion previousRotation = quaternion.identity;
			for (int i = 0; i < templates.Count; i++)
			{
				Entity template = templates[i];
				// 落地偏移：后方第 (i+1) 格 → 前方 offset 格。
				int offset;
				if (IsMultipart)
				{
					int copyDistance = s_multiCopyDistances[Mathf.Clamp(Info.PartIndex - 8, 0, 2)];
					offset = copyDistance + detection - (i + 1);
				}
				else
				{
					// 单部件生成器只复制最近的一个模板。
					if (i > 0)
					{
						break;
					}
					offset = s_singleCopyDistances[Mathf.Clamp(Info.PartIndex - 1, 0, 3)];
				}
				int x = gridX + dirX * offset;
				int y = gridY + dirY * offset;
				if (simulator.PartMap.TryGet(x, y, layer, out var occupied) && !occupied.IsEmpty)
				{
					continue;
				}
				PartTypeInfo info = PartGameplayUtil.TypeInfo(template);
				if (!PartGameplayUtil.TryGetGrid(template, out _, out _, out var tRotation, out var tFlipped))
				{
					continue;
				}
				Entity copy = PartGameplayUtil.SpawnPart(info, x, y, tRotation, tFlipped, ref simulator, layer);
				if (copy == Entity.Null)
				{
					continue;
				}
				created++;
				// 复制体只和「相邻一格」的上一个复制体焊接；与生成器之间隔着 CopyDistance 格空格，
				// 原版同样不建立连接。
				float3 copyPosition = new float3(x, y, -0.09f);
				quaternion copyRotation = PartGameplayUtil.GridRotation(tRotation, BasePart.IsEightWay(info));
				if (previous != Entity.Null && math.abs(x - (int)math.round(previousPosition.x)) + math.abs(y - (int)math.round(previousPosition.y)) == 1)
				{
					PartGameplayUtil.CreateFixedJoint(previous, previousPosition, previousRotation, copy, copyPosition, copyRotation, 1200f);
				}
				previous = copy;
				previousPosition = copyPosition;
				previousRotation = copyRotation;
			}
			m_lastReason = created > 0 ? ("copied " + created + " at layer " + layer) : "blocked";
			return created;
		}

		public override string Describe()
		{
			return "generator#" + Info.PartIndex + " generated=" + m_generated + " (" + m_lastReason + ")";
		}
	}

	/// <summary>
	/// BPSS AutoControlLight (虚化灯): while active, a foreign part inside the
	/// detection area makes the light phase the frames of its own contraption
	/// inside the void radius (collision filter off + 25% alpha), like
	/// AutoControlLightManager does.
	/// </summary>
	public sealed class VoidLightBehavior : PartBehavior
	{
		public static readonly Dictionary<Entity, float> AlphaRestore = new Dictionary<Entity, float>();
		private readonly Dictionary<Entity, CollisionFilter> m_selfFilter = new Dictionary<Entity, CollisionFilter>();
		private bool m_detected, m_collided;
		private float m_delay, m_maxDelay = .1f;
		public bool Detected => m_detected;
		public bool Collided => m_collided;
		public bool Full => Em.GetComponentData<PartTransform>(Part).Rotation >= 4;
		public override void Init()
		{
			Enabled = false;
			BpleLightRuntime.Ensure().Register(Part, Info);
			Entity enclosure = Em.GetComponentData<PartContainerValue>(Part).Value;
			if (enclosure != Entity.Null && PartGameplayUtil.TypeInfo(enclosure).Equals(BasePart.AlienMetalFrame)) m_maxDelay = 0;
			SyncVisual();
		}
		public override void Trigger()
		{
			if (Enabled && m_collided) return;
			Enabled = !Enabled;
			BpleLightRuntime.Instance.SetActivation(Part, Enabled);
			if (!Enabled) SetDetected(false, true, 0);
			SyncVisual();
		}
		private void SyncVisual() => PartGameplayUtil.SetPointLightVisual(Part, Enabled, Full);
		public override void Tick(float dt) => SyncVisual();
		internal void SetCollided(bool value) => m_collided = value;
		internal void SetDetected(bool value, bool force, float dt)
		{
			if (value == m_detected) return;
			m_delay += dt;
			if (!force && m_delay < m_maxDelay) return;
			m_delay = 0; m_detected = value;
			PartGameplayUtil.SetCollisionEnabled(Part, !m_detected, m_selfFilter);
		}
		public override void Dispose()
		{
			if (Em.Exists(Part)) PartGameplayUtil.SetCollisionEnabled(Part, true, m_selfFilter);
			BpleLightRuntime.Instance?.Unregister(Part);
		}
		public override string Describe() => "autocontrollight enabled=" + Enabled + " detected=" + m_detected + " collided=" + m_collided;
	}

	public sealed class DecelerationLightBehavior : PartBehavior
	{
		public override void Init()
		{
			Enabled = false;
			BpleLightRuntime.Ensure().Register(Part, Info);
			PartGameplayUtil.SetPointLightVisual(Part, false, false);
		}
		public override void Trigger()
		{
			if (!Enabled && !PartVariantPhysics.IsAssembly(Part)) return;
			Enabled = !Enabled;
			PartGameplayUtil.SetPointLightVisual(Part, Enabled, false);
			BpleLightRuntime.Instance.SetActivation(Part, Enabled);
		}
		public override void Dispose() => BpleLightRuntime.Instance?.Unregister(Part);
		public override string Describe() => "decelerationlight enabled=" + Enabled + " innerRadius=4 outerRadius=8 maxForce=150";
	}

}
