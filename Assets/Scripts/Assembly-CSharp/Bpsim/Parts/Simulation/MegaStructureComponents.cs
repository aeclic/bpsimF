using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 巨构优化：部件在整车刚体系里的「本地姿态」（建造空间下的位姿）与质量。
	/// <para>
	/// 巨构模式下部件不再有独立刚体，它的 <c>LocalTransform</c> 每步由整车刚体的位姿
	/// 反算得到：<c>world = body * local</c>。所以这个本地姿态是**建造时**就定死的常量，
	/// 掉落时用它把自己从复合碰撞体里摘出来。
	/// </para>
	/// </summary>
	public struct MegaPartOffset : IComponentData
	{
		public float3 Position;

		public quaternion Rotation;

		public float Mass;
	}

	/// <summary>巨构模式下部件累积的受击强度（接触法向接近速度，单位 m/s）。</summary>
	public struct MegaPartDamage : IComponentData
	{
		public float PeakSpeed;
	}

	/// <summary>单例：巨构载具的整车刚体实体。</summary>
	public struct MegaVehicle : IComponentData
	{
		public Entity Body;

		public float TotalMass;
	}

	/// <summary>一次接触事件压缩出来的「谁被打、多快」。</summary>
	public struct MegaHit
	{
		public Entity Part;

		public float Speed;
	}
}
