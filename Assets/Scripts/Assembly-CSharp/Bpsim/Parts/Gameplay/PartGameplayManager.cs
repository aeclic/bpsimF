using System.Collections.Generic;
using System.Text;
using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// Runtime driver for the BPSS part behaviours that bpsim 2023.0.2 shipped
	/// without (its GrapplingHookSystem / RocketSystem / PartPowerSystem were
	/// empty).  Lives for the whole process; Begin() is called by
	/// PartManager.StartSimulation and End() by PartManager.EndSimulation.
	/// </summary>
	public sealed class PartGameplayManager : MonoBehaviour
	{
		private static PartGameplayManager s_instance;

		private readonly Dictionary<Entity, PartBehavior> m_behaviors = new Dictionary<Entity, PartBehavior>();

		private readonly List<Entity> m_dead = new List<Entity>();
		private readonly List<Entity> m_spawnedParts = new List<Entity>();

		// Spawning can happen while Tick enumerates m_behaviors. Initialize after that iteration.
		internal void RegisterSpawnedPart(Entity part) => m_spawnedParts.Add(part);

		private bool m_running;

		private StringBuilder m_log = new StringBuilder();

		public static PartGameplayManager Instance => s_instance;

		public bool IsRunning => m_running;

		public int BehaviorCount => m_behaviors.Count;

		public static EntityManager Em
		{
			get
			{
				PartManager manager = PartManager.Instance;
				if (manager == null || manager.System == null)
				{
					return default(EntityManager);
				}
				return manager.System.EntityManager;
			}
		}

		public static PartGameplayManager Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject gameObject = new GameObject("[PartGameplay]");
			gameObject.hideFlags = HideFlags.DontSave;
			UnityEngine.Object.DontDestroyOnLoad(gameObject);
			s_instance = gameObject.AddComponent<PartGameplayManager>();
			return s_instance;
		}

		private void Awake()
		{
			if (s_instance != null && s_instance != this)
			{
				UnityEngine.Object.Destroy(base.gameObject);
				return;
			}
			s_instance = this;
		}

		public void Begin()
		{
			End();
			PartManager manager = PartManager.Instance;
			if (manager == null || manager.System == null || manager.PartSimulator == null)
			{
				m_running = false;
				return;
			}
			m_running = true;
			m_log.Length = 0;
			EntityManager em = Em;
			ref PartSimulatorUnmanaged simulator = ref manager.PartSimulator.Unmanaged;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity part = simulator.Parts[i];
				if (!em.Exists(part) || !em.HasComponent<PartTypeValue>(part))
				{
					continue;
				}
				PartTypeValue type = em.GetComponentData<PartTypeValue>(part);
				PartBehavior behavior = CreateBehavior(type.Value);
				if (behavior == null)
				{
					continue;
				}
				behavior.Part = part;
				behavior.Info = type.Value;
				m_behaviors[part] = behavior;
				try
				{
					behavior.Init();
				}
				catch (System.Exception exception)
				{
					Debug.LogWarning("[PartGameplay] init " + behavior.Describe() + " failed: " + exception);
				}
			}
			Log("begin: parts=" + simulator.Parts.Length + " behaviors=" + m_behaviors.Count + " " + DescribeKinds());
		}

		public void End()
		{
			m_running = false;
			TNTDamage.Clear();
			TNTImpulse.Clear();
			m_spawnedParts.Clear();
			TNTCollisionSetup.ActiveCount = 0;
			BpsimExplosionVisual.Clear();
			foreach (KeyValuePair<Entity, PartBehavior> item in m_behaviors)
			{
				try
				{
					item.Value.Dispose();
				}
				catch (System.Exception exception)
				{
					Debug.LogWarning("[PartGameplay] dispose " + item.Key + " failed: " + exception);
				}
			}
			m_behaviors.Clear();
			m_dead.Clear();
			if (PartManager.Instance != null && PartManager.Instance.PartSimulator != null)
			{
				PartGameplayUtil.RestoreAlpha(VoidLightBehavior.AlphaRestore);
			}
		}

		private static PartBehavior Make(PartGameplayKind kind, PartBehavior behavior)
		{
			behavior.Kind = kind;
			return behavior;
		}

		private static PartBehavior CreateBehavior(PartTypeInfo info)
		{
			switch (info.PartType)
			{
			case PartType.GrapplingHook:
				if (info.PartIndex == 5)
				{
					return Make(PartGameplayKind.ManualGun, new ManualGunBehavior());
				}
				if (info.PartIndex == 6)
				{
					return Make(PartGameplayKind.AutoGun, new AutoGunBehavior());
				}
				if (info.PartIndex >= 8 && info.PartIndex <= 10)
				{
					return Make(PartGameplayKind.Generator, new GeneratorBehavior());
				}
				if (info.PartIndex >= 0 && info.PartIndex <= 4)
				{
					return Make(PartGameplayKind.GrappleHook, new GrappleHookBehavior());
				}
				return null;
			case PartType.PointLight:
				// BPSS 灯族按 prefab 编号 _01.._07 对应 PartIndex 0..6：
				// 0=_01 PointLight、1=_02 Mushroom、2=_03 Lantern、3=_04 PointLight、
				// 4=_05 AlienPointLight、5=_06 DecelerationLight、6=_07 AutoControlLight。
				// 0-4 只是「换图开关」，5 额外减速（DecelerationLightManager），
				// 6 是虚化灯（AutoControlLightManager）。
				if (info.PartIndex == 6)
				{
					return Make(PartGameplayKind.AutoControlLight, new VoidLightBehavior());
				}
				if (info.PartIndex == 5)
				{
					return Make(PartGameplayKind.DecelerationLight, new DecelerationLightBehavior());
				}
				if (info.PartIndex >= 0 && info.PartIndex <= 4)
				{
					return Make(PartGameplayKind.PointLight, new PointLightBehavior());
				}
				return null;
			case PartType.SpotLight:
				// BPSS SpotLight：点击开关，光锥容器在 0.001 与 (3,1,1) 之间插值。
				return Make(PartGameplayKind.SpotLight, new SpotLightBehavior());
			case PartType.Rocket:
			case PartType.RedRocket:
			case PartType.CokeBottle:
			case PartType.SodaBottle:
				// BPSS 这四族共用一个 Rocket.cs（BasePropulsion），差别全在 prefab 字段与
				// Initialize() 的 INSettings 倍率上；型号表见 LaunchProfile。
				return Make(PartGameplayKind.Rocket, new LaunchBehavior());
			case PartType.TNT:
				return Make(PartGameplayKind.TNT, new TNTBehavior());
			case PartType.Balloon:
			case PartType.Balloons2:
			case PartType.Balloons3:
				return Make(PartGameplayKind.Balloon, new BalloonBehavior());
			case PartType.Umbrella:
				return Make(PartGameplayKind.Umbrella, new UmbrellaBehavior());
			case PartType.PoweredUmbrella:
				return Make(PartGameplayKind.PoweredUmbrella, new PoweredUmbrellaBehavior());
			case PartType.Spring:
			case PartType.SpringBoxingGlove:
				return Make(PartGameplayKind.Spring, new SpringBehavior());
			case PartType.Kicker:
				return Make(PartGameplayKind.Kicker, new KickerBehavior());
			case PartType.TimeBomb:
				return Make(PartGameplayKind.TimeBomb, new TimeBombBehavior());
			case PartType.Bellows:
				return Make(PartGameplayKind.Bellows, new BellowsBehavior());
			case PartType.MotorWheel:
				// 索引 7 是 BPSS 的 OffRoadWheel（Innovation 模板把它收在 MotorWheel 下）。
				// 推力/射线/视觉滚动由 ECS 系统 WheelSystem 负责，这里只管开关。
				return Make((info.PartIndex == 7) ? PartGameplayKind.OffRoadWheel : PartGameplayKind.MotorWheel, new WheelBehavior());
			case PartType.SmallWheel:
				// SmallWheel_08_SET uses MotorWheel.cs; the other small wheels are passive.
				return info.PartIndex == 7 ? Make(PartGameplayKind.MotorWheel, new WheelBehavior()) : null;
			case PartType.StickyWheel:
				return Make(PartGameplayKind.StickyWheel, new WheelBehavior());
			case PartType.Gearbox:
				return Make(PartGameplayKind.Gearbox, new GearboxBehavior());
			case PartType.ElectricalPart:
				if (info.PartIndex == 4 || info.PartIndex == 5 || info.PartIndex == 11 || info.PartIndex == 21 ||
					(info.PartIndex >= 42 && info.PartIndex <= 44) || info.PartIndex == 46 || info.PartIndex == 47)
				{
					return Make(PartGameplayKind.CircuitSwitch, new CircuitSwitchBehavior());
				}
				return null;
			case PartType.MechanicalPart:
				// 0-4 = MechanicalGate1-5，点击开合。
				return Make(PartGameplayKind.CircuitSwitch, new CircuitSwitchBehavior());
			case PartType.Sandbag:
			case PartType.Sandbag2:
			case PartType.Sandbag3:
				// 点击放手（BPSS Sandbag.OnTouch → Drop）；悬挂弹簧由 SandbagSystem 每物理帧施加。
				return Make(PartGameplayKind.Sandbag, new SandbagBehavior());
			case PartType.Rope:
				// 只有 0-3 是绳索（4-7 是铰接板 HingePlate，没有点击行为）。
				// 点击在最近节点处砍断（BPSS Rope.OnTouch）。
				return (Rope.IsRope(info) ? Make(PartGameplayKind.Rope, new RopeBehavior()) : null);
			case PartType.Egg:
				// 0 是普通蛋（无效果）；1-5 是特殊蛋（BPSS Egg.cs 的 1-4 + AlienEgg 的 05）。
				// 点击翻转 Egg.Enabled，效果由 EggSystem 每物理帧调和。
				return (Egg.GetKind(info) != EggKind.Plain ? Make(PartGameplayKind.Egg, new EggBehavior()) : null);
			case PartType.JetEngine:
				// BPSS 的 JetEngine 预制体组是混合组：_01/_02_SET 才是引擎本体，
				// _03/_04/_06_SET 挂的是 FuelTube.cs（普通管/自动管/阀门），_05_SET 挂的是
				// FuelBox.cs（燃料箱 + 液面）。原版因此根本没有独立的燃料件 PartType，
				// 三种件只能靠 customPartIndex 区分。bpsim 照旧按 PartIndex 分派：
				// 2/3/5 → 燃料管（普通/自动/阀门），4 → 燃料箱，0/1 与其余 → 引擎。
				// FuelTubeBehavior 内部同样按 3=Auto、5=Valve、其余=Common 映射，
				// 所以这里的索引直接沿用，不需要另做一张表。
				switch (info.PartIndex)
				{
				case 2:
				case 3:
				case 5:
					return Make(PartGameplayKind.FuelTube, new FuelTubeBehavior());
				case 4:
					// BPSS FuelBox：容器（不响应点击），每步由 FuelSystem 汇总并回油，
					// 这里只负责给容器数据 + 刷新液面。
					return Make(PartGameplayKind.FuelBox, new FuelBoxBehavior());
				default:
					return Make(PartGameplayKind.JetEngine, new JetEngineBehavior());
				}
			case PartType.Pumpkin:
				// BPSS Pumpkin：放下即「固定件」，点击把整个焊接组件冻成静态体
				// （FixedPumpkinManager），再点松开。冻结在 FixedPumpkinSystem 里做。
				return Make(PartGameplayKind.Pumpkin, new PumpkinBehavior());
			case PartType.Pig:
			case PartType.KingPig:
			case PartType.GoldenPig:
				// 猪族三份 BPSS 脚本只有阻尼模型不同（普通猪速度相关、猪王/金猪常数），
				// 表情/音效/粒子在 bpsim 没有对应资源，按降级处理。
				return Make(PartGameplayKind.Pig, new PigBehavior());
			case PartType.Wings:
			case PartType.Tailplane:
			case PartType.MetalWing:
			case PartType.MetalTail:
				return Make(PartGameplayKind.Wing, new WingBehavior());
			default:
				return null;
			}
		}

		private void FixedUpdate()
		{
			if (!m_running)
			{
				return;
			}
			PartManager manager = PartManager.Instance;
			if (manager == null || !manager.IsSimulating || manager.PartSimulator == null)
			{
				End();
				return;
			}
			float deltaTime = Mathf.Max(Time.fixedDeltaTime, 1E-05f);
			m_dead.Clear();
			// 推力（汽水/可乐/火箭）按连通块汇总后统一施加，见 LaunchBehavior.ThrustBatch：
			// 一辆车上百瓶汽水同时开着时，逐瓶遍历全部部件的写法是 O(瓶数 × 部件数)。
			LaunchBehavior.ThrustBatch.Begin();
			// 每固定步只重建一次“按连通块聚合的电力数据”，否则每个动力伞都要全量扫一遍部件
			// （92 个伞 × 3 遍 × 1 万部件 = 每步约 280 万次托管组件查询）。
			ComponentPowerCache.AdvanceToken();
			MarkerTeams.Refresh();
			foreach (KeyValuePair<Entity, PartBehavior> item in m_behaviors)
			{
				PartBehavior behavior = item.Value;
				if (!Em.Exists(item.Key))
				{
					m_dead.Add(item.Key);
					continue;
				}
				try
				{
					behavior.Tick(deltaTime);
				}
				catch (System.Exception exception)
				{
					Debug.LogWarning("[PartGameplay] tick " + behavior.Describe() + " failed: " + exception);
					behavior.Enabled = false;
				}
			}
			LaunchBehavior.ThrustBatch.Apply();
			// Every generated skin follows the same dispatch as a part present at simulation start.
			for (int i = 0; i < m_spawnedParts.Count; i++)
			{
				Entity part = m_spawnedParts[i];
				if (!Em.Exists(part) || m_behaviors.ContainsKey(part)) continue;
				PartTypeInfo info = PartGameplayUtil.TypeInfo(part);
				PartBehavior behavior = CreateBehavior(info);
				if (behavior == null) continue;
				behavior.Part = part;
				behavior.Info = info;
				m_behaviors.Add(part, behavior);
				try { behavior.Init(); }
				catch (System.Exception exception)
				{
					Debug.LogWarning("[PartGameplay] init generated " + behavior.Describe() + " failed: " + exception);
				}
			}
			if (m_spawnedParts.Count > 0)
			{
				// A standalone generated part has no new joint to make PartSimulationSystem dirty.
				// Give it its own component now instead of inheriting prefab component zero.
				Em.CompleteAllTrackedJobs();
				using (var set = PartSimulation.FindConnectedComponents(manager.PartSimulator.UnmanagedRef,
					manager.System.GetComponentLookup<Unity.Physics.PhysicsConstrainedBodyPair>(true)))
					PartSimulation.UpdateConnectedComponents(manager.PartSimulator.UnmanagedRef,
						manager.System.GetComponentLookup<PartConnectedComponent>(),
						manager.System.GetComponentLookup<PartExtensionComponent>(true), set);
				if (!UserSettings.Instance.SimulationSettings.PartSelfCollision) PartCollisionFilter.Apply(Em, manager.PartSimulator.Unmanaged);
				FuelSystem.MarkDirty();
				if (Bpsim.UI.PartButtonList.Instance != null) Bpsim.UI.PartButtonList.Instance.NeedsUpdate = true;
			}
			m_spawnedParts.Clear();
			TNTDamage.Flush();
			for (int i = 0; i < m_dead.Count; i++)
			{
				PartBehavior behavior;
				if (m_behaviors.TryGetValue(m_dead[i], out behavior))
				{
					behavior.Dispose();
					m_behaviors.Remove(m_dead[i]);
				}
			}
			m_dead.Clear();
		}

		private void OnDestroy()
		{
			End();
			if (s_instance == this)
			{
				s_instance = null;
			}
		}

		public bool HasBehavior(Entity part)
		{
			return m_behaviors.ContainsKey(part);
		}

		public PartBehavior Find(Entity part)
		{
			PartBehavior behavior;
			m_behaviors.TryGetValue(part, out behavior);
			return behavior;
		}

		public void Retire(Entity part) => TNTDamage.Retire(part);
		public void Detach(Entity part) => TNTDamage.Detach(part);

		public void Trigger(Entity part)
		{
			PartBehavior behavior = Find(part);
			if (behavior == null)
			{
				return;
			}
			Debug.Log("[PartGameplay] trigger " + behavior.Describe());
			behavior.Trigger();
		}

		public bool IsEnabled(Entity part)
		{
			PartBehavior behavior = Find(part);
			if (behavior == null)
			{
				return false;
			}
			return behavior.Enabled;
		}

		public string Describe()
		{
			StringBuilder stringBuilder = new StringBuilder();
			stringBuilder.Append("running=").Append(m_running).Append(" behaviors=").Append(m_behaviors.Count);
			foreach (KeyValuePair<Entity, PartBehavior> item in m_behaviors)
			{
				stringBuilder.Append("\n  ").Append(item.Key.Index).Append(": ").Append(item.Value.Describe());
			}
			return stringBuilder.ToString();
		}

		public string LogText()
		{
			return m_log.ToString();
		}

		public void Log(string message)
		{
			m_log.AppendLine(message);
			Debug.Log("[PartGameplay] " + message);
		}

		private string DescribeKinds()
		{
			int hooks = 0;
			int guns = 0;
			int generators = 0;
			int lights = 0;
			int rockets = 0;
			int tnts = 0;
			int balloons = 0;
			int umbrellas = 0;
			int springs = 0;
			int kickers = 0;
			int timebombs = 0;
			int bellows = 0;
			int wheels = 0;
			int gearboxes = 0;
			int switches = 0;
			int sandbags = 0;
			int ropes = 0;
			int eggs = 0;
			foreach (KeyValuePair<Entity, PartBehavior> item in m_behaviors)
			{
				switch (item.Value.Kind)
				{
				case PartGameplayKind.GrappleHook:
					hooks++;
					break;
				case PartGameplayKind.AutoGun:
				case PartGameplayKind.ManualGun:
					guns++;
					break;
				case PartGameplayKind.Generator:
					generators++;
					break;
				case PartGameplayKind.AutoControlLight:
				case PartGameplayKind.DecelerationLight:
					lights++;
					break;
				case PartGameplayKind.Rocket:
					rockets++;
					break;
				case PartGameplayKind.TNT:
					tnts++;
					break;
				case PartGameplayKind.Balloon:
					balloons++;
					break;
				case PartGameplayKind.Umbrella:
				case PartGameplayKind.PoweredUmbrella:
					umbrellas++;
					break;
				case PartGameplayKind.Spring:
					springs++;
					break;
				case PartGameplayKind.Kicker:
					kickers++;
					break;
				case PartGameplayKind.TimeBomb:
					timebombs++;
					break;
				case PartGameplayKind.Bellows:
					bellows++;
					break;
				case PartGameplayKind.MotorWheel:
				case PartGameplayKind.StickyWheel:
				case PartGameplayKind.OffRoadWheel:
					wheels++;
					break;
				case PartGameplayKind.Gearbox:
					gearboxes++;
					break;
				case PartGameplayKind.CircuitSwitch:
					switches++;
					break;
				case PartGameplayKind.Sandbag:
					sandbags++;
					break;
				case PartGameplayKind.Rope:
					ropes++;
					break;
				case PartGameplayKind.Egg:
					eggs++;
					break;
				}
			}
			return "hook=" + hooks + " gun=" + guns + " generator=" + generators + " light=" + lights
				+ " rocket=" + rockets + " tnt=" + tnts + " balloon=" + balloons + " umbrella=" + umbrellas
				+ " spring=" + springs + " kicker=" + kickers + " timebomb=" + timebombs + " bellows=" + bellows
				+ " wheel=" + wheels + " gearbox=" + gearboxes + " switch=" + switches
				+ " sandbag=" + sandbags + " rope=" + ropes + " egg=" + eggs;
		}

		// ------------------------------------------------------------------
		// Queries used by the behaviour implementations
		// ------------------------------------------------------------------

		public static bool IsForeign(Entity source, Entity target)
		{
			if (source == target)
			{
				return false;
			}
			int a = PartGameplayUtil.ConnectedComponent(source);
			int b = PartGameplayUtil.ConnectedComponent(target);
			return a < 0 || b < 0 || a != b;
		}

		public static Entity FindTargetInCone(Entity source, float3 forward, float maxDistance, float minDot)
		{
			PartSimulatorUnmanaged simulator = PartManager.Instance.PartSimulator.Unmanaged;
			float3 origin = PartGameplayUtil.Position(source);
			Entity best = Entity.Null;
			float bestDistance = maxDistance;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity candidate = simulator.Parts[i];
				if (candidate == source || !PartGameplayUtil.Alive(candidate) || !IsForeign(source, candidate))
				{
					continue;
				}
				float3 delta = PartGameplayUtil.Position(candidate) - origin;
				float distance = math.length(delta);
				if (distance < 0.05f || distance > bestDistance)
				{
					continue;
				}
				if (math.dot(delta / distance, forward) < minDot)
				{
					continue;
				}
				best = candidate;
				bestDistance = distance;
			}
			return best;
		}

		public static void ApplyRadialImpulse(float3 center, float radius, float strength, Entity ignoreA, Entity ignoreB)
		{
			PartSimulatorUnmanaged simulator = PartManager.Instance.PartSimulator.Unmanaged;
			for (int i = 0; i < simulator.Parts.Length; i++)
			{
				Entity part = simulator.Parts[i];
				if (part == ignoreA || part == ignoreB || !PartGameplayUtil.Alive(part))
				{
					continue;
				}
				float3 delta = PartGameplayUtil.Position(part) - center;
				float distance = math.length(delta);
				if (distance > radius)
				{
					continue;
				}
				float falloff = 1f - distance / radius;
				float3 direction = (distance > 1E-04f) ? (delta / distance) : new float3(0f, 1f, 0f);
				PartGameplayUtil.AddVelocity(part, direction * (strength * falloff));
			}
		}
	}
}
