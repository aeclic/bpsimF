using Bpsim.Parts.Simulation;
using Unity.Mathematics;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// 燃料箱（BPSS <c>FuelBox.cs</c>，274 行）。
	/// </summary>
	/// <remarks>
	/// 原版做的事可以分成四块，这里逐块给出取舍：
	/// <list type="bullet">
	/// <item>供油 / 回油：由 <c>FuelSystem</c> 统一按连通组结算，behavior 只负责
	/// 给出容器数据（容量、上限）并刷新液面，不做分油数学。</item>
	/// <item>容量按朝向分支：<c>Deg_0/315</c> 可爆、<c>Deg_45/90</c> 不可爆、
	/// <c>Deg_135/180/225/270</c> 容量 <c>1E+10</c> 且标成无限；225/270 不可爆。
	/// 碰撞爆炸尚未接入 FuelBox；此处的“可爆”只用于模式日志。</item>
	/// <item>液面 sprite：<c>SetFuelLevel</c> 写 <c>PostTransformScale</c> +
	/// <c>LocalTransform.Position.y</c>，与原版 localScale/localPosition 一致。</item>
	/// <item>刚体质量随油量变化（<c>2 + min(min(fuel,8),4)</c>）：未复刻 —— bpsim 改质量要
	/// 同时重算 <c>PhysicsMass.InverseInertia</c>，而燃料箱通常焊在载具上，质量变化会被
	/// 焊接约束吃掉，收益远小于风险。</item>
	/// </list>
	/// </remarks>
	public sealed class FuelBoxBehavior : PartBehavior
	{
		private const float DefaultCapacity = 4f;

		private const float InfiniteCapacity = 1E+10f;

		private const float LevelEpsilon = 0.002f;

		private float m_lastRatio = -1f;

		private int m_writes;

		public override void Init()
		{
			PartGameplayUtil.TryGetGrid(Part, out int _, out int __, out int rotation, out bool _);
			float capacity = DefaultCapacity;
			bool infinite = false;
			bool explosive = true;
			// Both projects serialize 0/90/180/270/45/135/225/315, not consecutive 45-degree steps.
			switch (rotation)
			{
			case 1:
			case 4:
				// Deg_45 / Deg_90：立着放的油箱不可爆。
				explosive = false;
				break;
			case 5:
			case 2:
				// Deg_135 / Deg_180：容量拉到 1E+10（原版就是这么做"接近无限"的）。
				capacity = InfiniteCapacity;
				infinite = true;
				break;
			case 6:
			case 3:
				// Deg_225 / Deg_270：容量 1E+10 且标记为无限。
				capacity = InfiniteCapacity;
				infinite = true;
				explosive = false;
				break;
			}
			FuelContainer container = new FuelContainer
			{
				Amount = capacity,
				Capacity = capacity,
				SuppliedStep = 0f,
				Infinite = (byte)(infinite ? 1 : 0)
			};
			if (Em.HasComponent<FuelContainer>(Part))
			{
				Em.SetComponentData(Part, container);
			}
			else
			{
				Em.AddComponentData(Part, container);
			}
			Enabled = true;
			FuelSystem.MarkDirty();
			m_lastRatio = -1f;
			Manager.Log("fuelbox#" + Info.PartIndex + ": capacity=" + capacity.ToString("F1")
				+ " infinite=" + infinite + " explosive=" + explosive);
			UpdateVisual(force: true);
		}

		public override void Trigger()
		{
			// BPSS FuelBox 没有 OnTouch（IsTriggerable 也是 false），点它不该有反应。
		}

		public override void Tick(float deltaTime)
		{
			UpdateVisual(force: false);
		}

		private void UpdateVisual(bool force)
		{
			if (!Em.HasComponent<FuelContainer>(Part))
			{
				return;
			}
			FuelContainer container = Em.GetComponentData<FuelContainer>(Part);
			float ratio = (container.Capacity > 0f) ? math.saturate(container.Amount / container.Capacity) : 0f;
			if (!force && math.abs(ratio - m_lastRatio) < LevelEpsilon)
			{
				return;
			}
			m_lastRatio = ratio;
			m_writes += PartGameplayUtil.SetFuelLevel(Part, ratio);
		}

		public override string Describe()
		{
			FuelContainer container = Em.HasComponent<FuelContainer>(Part) ? Em.GetComponentData<FuelContainer>(Part) : default(FuelContainer);
			float ratio = (container.Capacity > 0f) ? math.saturate(container.Amount / container.Capacity) : 0f;
			return "fuelbox#" + Info.PartIndex + " amount=" + container.Amount.ToString("F3")
				+ " capacity=" + container.Capacity.ToString("F1")
				+ " ratio=" + ratio.ToString("F3")
				+ " infinite=" + (container.Infinite != 0)
				+ " supplied=" + container.SuppliedStep.ToString("F4")
				+ " levelWrites=" + m_writes;
		}
	}

	/// <summary>
	/// 燃料管（BPSS <c>FuelTube.cs</c>，297 行）。
	/// </summary>
	/// <remarks>
	/// 原版三种管由 <c>customPartIndex</c> 决定：2 = 普通管、3 = 自动管、5 = 阀门
	/// （其它值直接抛异常）。bpsim 的 prefab 有 6 个索引，这里按同样的编号映射，
	/// 其余索引按普通管处理，避免手滑放错索引时零件变成空壳。
	/// <para>
	/// 只有阀门可点：<c>Open</c> 翻转后置脏 <c>FuelSystem</c> 让它重算拓扑，
	/// 于是"关掉阀门 = 断油"立刻生效。
	/// </para>
	/// <para>
	/// 未复刻：自动管按邻居组合换图（参考工程保留了完整 <c>s_rotationMap</c>），
	/// 以及阀门的原始开/关贴图 —— 这里用 <c>FuelTube_Sprite_1</c>（通管造型）与
	/// <c>FuelTube_Sprite_5</c>（阀门造型）作为可见反馈，属于明确标注的近似实现。
	/// </para>
	/// </remarks>
	public sealed class FuelTubeBehavior : PartBehavior
	{
		private FuelTubeKind m_kind;

		private int m_spriteSwaps;

		public override void Init()
		{
			// 索引映射照 BPSS CurrentTubeType。
			m_kind = Info.PartIndex switch
			{
				3 => FuelTubeKind.Auto,
				5 => FuelTubeKind.Valve,
				_ => FuelTubeKind.Common
			};
			FuelTubeLink link = new FuelTubeLink
			{
				Kind = (byte)m_kind,
				Open = 1
			};
			if (Em.HasComponent<FuelTubeLink>(Part))
			{
				Em.SetComponentData(Part, link);
			}
			else
			{
				Em.AddComponentData(Part, link);
			}
			Enabled = m_kind == FuelTubeKind.Valve;
			FuelSystem.MarkDirty();
			SyncSprite();
			Manager.Log("fueltube#" + Info.PartIndex + ": kind=" + m_kind);
		}

		public override void Trigger()
		{
			if (m_kind != FuelTubeKind.Valve || !Em.HasComponent<FuelTubeLink>(Part))
			{
				return;
			}
			FuelTubeLink link = Em.GetComponentData<FuelTubeLink>(Part);
			link.Open = (byte)((link.Open != 0) ? 0 : 1);
			Em.SetComponentData(Part, link);
			Enabled = link.Open != 0;
			FuelSystem.MarkDirty();
			// 立刻重建：同一帧里紧接着点击发动机时，HasFuelSource() 必须已经看到
			// 「阀门关掉 → 组里没有燃料箱」，否则发动机会带着空油箱启动一次。
			FuelSystem system = FuelSystem.Instance;
			if (system != null)
			{
				system.RebuildNow();
			}
			SyncSprite();
			Manager.Log("fueltube#" + Info.PartIndex + ": valve " + ((link.Open != 0) ? "open" : "closed"));
		}

		private void SyncSprite()
		{
			if (m_kind != FuelTubeKind.Valve)
			{
				return;
			}
			bool open = !Em.HasComponent<FuelTubeLink>(Part) || Em.GetComponentData<FuelTubeLink>(Part).Open != 0;
			m_spriteSwaps += PartGameplayUtil.SetFuelTubeSprite(Part, open ? "FuelTube_Sprite_1" : "FuelTube_Sprite_5");
		}

		public override string Describe()
		{
			bool open = !Em.HasComponent<FuelTubeLink>(Part) || Em.GetComponentData<FuelTubeLink>(Part).Open != 0;
			return "fueltube#" + Info.PartIndex + " kind=" + m_kind
				+ " open=" + open
				+ " enabled=" + Enabled
				+ " spriteSwaps=" + m_spriteSwaps;
		}
	}
}
