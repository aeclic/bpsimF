// Anvil Physics (box3d) — 接触事件通道。
//
// 背景：Unity.Physics / Havok 的 ICollisionEventsJob 走 SimulationSingleton，而所有扩展方法
// （IICollisionEventJobExtensions.Schedule / IHavokCollisionEventsJobExtensions.Schedule）都会在
// SimulationSingleton.Type 不是它们认识的值时直接 return inputDeps —— 也就是「静默跳过」。
// Anvil 后端把 Type 设成 AnvilPhysics，因此这些 job 一条都不会跑；下游的 TNT / 电路压力 /
// 巨型结构损伤就全部失效。
//
// 这一层提供等价通道：AnvilStepSystem 每步把 box3d 的接触读进 AnvilContactEvents 单例，
// 下游系统在 Anvil 模式下改读这个单例即可。
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Physics.Anvil
{
	/// <summary>一条 box3d 接触（已从 body 索引映射回 Entity）。</summary>
	public struct AnvilContactEvent
	{
		public Entity EntityA;
		public Entity EntityB;
		/// <summary>接触点世界坐标（相对各自 body 质心的锚点已换算成世界点）。</summary>
		public float3 Position;
		/// <summary>manifold 法线，由 A 指向 B。</summary>
		public float3 Normal;
		/// <summary>本步该接触点沿法线的冲量。</summary>
		public float Impulse;

		/// <summary>ECS 的 CollisionEvent 里 ColliderKey.Empty 等价物；Anvil 目前不拆 compound 子形状。</summary>
		public const uint k_EmptyKey = 0xFFFFFFFFu;
	}
}
