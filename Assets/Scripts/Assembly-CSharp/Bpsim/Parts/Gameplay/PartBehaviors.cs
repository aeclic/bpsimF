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

		private readonly HashSet<Entity> m_voided = new HashSet<Entity>();

		private readonly Dictionary<Entity, CollisionFilter> m_filters = new Dictionary<Entity, CollisionFilter>();

		private float m_radius = 2.5f;

		private int m_voidPasses;

		private int m_ticks;

		private int m_lastForeign;

		private int m_lastFrames;

		private bool m_lastDetected;

		private int m_colliderChanges;

		private bool m_visualInit;

		private bool m_visualOn;

		private bool m_visualFull;

		private int m_visualSwaps;

		public override void Trigger()
		{
			Enabled = !Enabled;
			if (!Enabled)
			{
				RestoreAll();
			}
			// BPSS AutoControlLight.OnTouch 翻转 activated 的同时也换图，这里保持一致。
			SyncVisual(force: true);
			Manager.Log("voidlight: " + (Enabled ? "enabled" : "disabled"));
		}

		/// <summary>
		/// 按「开关状态 + 朝向」更新灯图（BPSS AutoControlLight 的 active/inactive sprite，
		/// 以及斜向型号专用的 *Full 那一对）。
		/// </summary>
		/// <remarks>
		/// 原版 <c>CurrentType</c> = <c>m_gridRotation &gt;= Deg_45 ? 1 : 0</c>；type 0 用
		/// <c>activeSprite/inactiveSprite</c>，type 1 用 <c>m_activeFullSprite/m_inactiveFullSprite</c>，
		/// 另一对永远关掉。熄灭状态也要保持正确的那一对，所以 Tick 里在早退之前先同步一次。
		/// </remarks>
		private void SyncVisual(bool force)
		{
			int gridX;
			int gridY;
			int rotation;
			bool flipped;
			if (!PartGameplayUtil.TryGetGrid(Part, out gridX, out gridY, out rotation, out flipped))
			{
				rotation = 0;
			}
			bool full = rotation >= 4;
			if (!force && m_visualInit && m_visualOn == Enabled && m_visualFull == full)
			{
				return;
			}
			m_visualSwaps += PartGameplayUtil.SetPointLightVisual(Part, Enabled, full);
			m_visualInit = true;
			m_visualOn = Enabled;
			m_visualFull = full;
		}

		public override void Tick(float deltaTime)
		{
			SyncVisual(force: false);
			if (!Enabled)
			{
				return;
			}
			m_ticks++;
			EntityManager em = PartGameplayUtil.Em;
			ref PartSimulatorUnmanaged simulator = ref PartManager.Instance.PartSimulator.Unmanaged;
			float3 origin = Position();
			float3 down = Down();
			if (math.lengthsq(down) < 1E-06f)
			{
				down = new float3(0f, -1f, 0f);
			}
			down = math.normalize(down);
			float3 side = new float3(down.y, 0f - down.x, 0f);
			int rotation;
			int gridX;
			int gridY;
			bool flipped;
			PartGameplayUtil.TryGetGrid(Part, out gridX, out gridY, out rotation, out flipped);
			bool diagonal = rotation >= 4;
			bool detected = false;
			int foreign = 0;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity candidate = simulator.Parts[i];
				if (candidate == Part || !PartGameplayUtil.Alive(candidate) || !PartGameplayManager.IsForeign(Part, candidate))
				{
					continue;
				}
				foreign++;
				float3 delta = PartGameplayUtil.Position(candidate) - origin;
				float x = math.dot(delta, down);
				float y = math.dot(delta, side);
				bool inside = diagonal
					? (math.abs(x) <= m_radius && math.abs(y) <= m_radius)
					: (x >= -2f && x <= 6.5f && math.abs(y) <= 2.5f);
				if (inside)
				{
					detected = true;
					break;
				}
			}
			if (!detected)
			{
				m_lastForeign = foreign;
				m_lastDetected = false;
				m_lastFrames = 0;
				RestoreAll();
				return;
			}
			m_lastForeign = foreign;
			m_lastDetected = true;
			m_lastFrames = 0;
			HashSet<Entity> current = new HashSet<Entity>();
			for (int j = 0; j < simulator.Parts.Length; j++)
			{
				Entity candidate2 = simulator.Parts[j];
				if (!PartGameplayUtil.Alive(candidate2) || PartGameplayManager.IsForeign(Part, candidate2))
				{
					continue;
				}
				PartTypeInfo info = PartGameplayUtil.TypeInfo(candidate2);
				if (info.PartType != PartType.WoodenFrame && info.PartType != PartType.MetalFrame)
				{
					continue;
				}
				float3 delta2 = PartGameplayUtil.Position(candidate2) - origin;
				float x2 = math.dot(delta2, down);
				float y2 = math.dot(delta2, side);
				if (math.abs(x2) <= m_radius && math.abs(y2) <= m_radius)
				{
					current.Add(candidate2);
					m_lastFrames++;
				}
			}
			List<Entity> restore = new List<Entity>();
			foreach (Entity item2 in m_voided)
			{
				if (!current.Contains(item2))
				{
					restore.Add(item2);
				}
			}
			for (int k = 0; k < restore.Count; k++)
			{
				RestorePart(restore[k]);
			}
			foreach (Entity item3 in current)
			{
				if (m_voided.Contains(item3))
				{
					continue;
				}
				m_voided.Add(item3);
				m_colliderChanges += PartGameplayUtil.SetCollisionEnabled(item3, false, m_filters);
				PartGameplayUtil.SetAlpha(item3, 0.25f, AlphaRestore);
				if (em.HasComponent<PartTypeValue>(item3))
				{
					m_voidPasses++;
				}
			}
		}

		private void RestorePart(Entity part)
		{
			PartGameplayUtil.SetCollisionEnabled(part, true, m_filters);
			PartGameplayUtil.RestoreAlphaFor(part, AlphaRestore);
			m_voided.Remove(part);
		}

		private void RestoreAll()
		{
			foreach (Entity item in m_voided)
			{
				PartGameplayUtil.SetCollisionEnabled(item, true, m_filters);
				PartGameplayUtil.RestoreAlphaFor(item, AlphaRestore);
			}
			m_voided.Clear();
		}

		public override void Dispose()
		{
			RestoreAll();
		}

		public override string Describe()
		{
			return "voidlight enabled=" + Enabled + " voided=" + m_voided.Count + " passes=" + m_voidPasses
				+ " ticks=" + m_ticks + " foreign=" + m_lastForeign + " detected=" + m_lastDetected
				+ " frames=" + m_lastFrames + " colliderChanges=" + m_colliderChanges
				+ " full=" + m_visualFull + " spriteSwaps=" + m_visualSwaps;
		}
	}

	/// <summary>
	/// BPSS DecelerationLight (减速灯): a foreign body passing through the beam
	/// is slowed down instead of being pulled.
	/// </summary>
	public sealed class DecelerationLightBehavior : PartBehavior
	{
		private float m_radius = 3f;

		private bool m_visualInit;

		private bool m_visualOn;

		private int m_visualSwaps;

		public override void Init()
		{
			// BPSS DecelerationLight 继承 PointLight，熄灭时显示 inactive 图。
			SyncVisual(force: true);
		}

		public override void Trigger()
		{
			Enabled = !Enabled;
			SyncVisual(force: true);
			Manager.Log("decelerationlight: " + (Enabled ? "enabled" : "disabled"));
		}

		/// <summary>继承自 PointLight 的那半行为：换 active/inactive 图（没有 Full 那一对）。</summary>
		private void SyncVisual(bool force)
		{
			if (!force && m_visualInit && m_visualOn == Enabled)
			{
				return;
			}
			m_visualSwaps += PartGameplayUtil.SetPointLightVisual(Part, Enabled, false);
			m_visualInit = true;
			m_visualOn = Enabled;
		}

		public override void Tick(float deltaTime)
		{
			SyncVisual(force: false);
			if (!Enabled)
			{
				return;
			}
			PartSimulatorUnmanaged simulator = PartManager.Instance.PartSimulator.Unmanaged;
			float3 origin = Position();
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity candidate = simulator.Parts[i];
				if (candidate == Part || !PartGameplayUtil.Alive(candidate) || !PartGameplayManager.IsForeign(Part, candidate))
				{
					continue;
				}
				float3 delta = PartGameplayUtil.Position(candidate) - origin;
				if (math.length(delta) > m_radius)
				{
					continue;
				}
				float3 velocity = PartGameplayUtil.LinearVelocity(candidate);
				PartGameplayUtil.AddVelocity(candidate, -velocity * math.saturate(deltaTime * 3f));
			}
		}

		public override string Describe()
		{
			return "decelerationlight enabled=" + Enabled + " spriteSwaps=" + m_visualSwaps;
		}
	}

}
