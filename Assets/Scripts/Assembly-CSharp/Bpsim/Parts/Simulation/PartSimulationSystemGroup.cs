using Unity.Entities;
using Unity.Physics.Systems;
using UnityEngine.Scripting;

namespace Bpsim.Parts.Simulation
{
	[UpdateAfter(typeof(PartSimulationSystem))]
	[UpdateAfter(typeof(MegaStructureMirrorSystem))]
	[UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
	internal class PartSimulationSystemGroup : ComponentSystemGroup
	{
		[Preserve]
		public PartSimulationSystemGroup()
		{
		}

		/// <summary>
		/// 在组内任何系统动手之前，先把已调度的 Job 全部完成。
		///
		/// <para>原因：<c>ExportPhysicsWorld.CheckIntegrity</c> 调度的
		/// <c>CheckDynamicBodyIntegrity</c> / <c>CheckColliderIntegrity</c> / <c>CheckTotalIntegrity</c>
		/// 是"构建物理世界时记下各 chunk 的版本号、Job 执行时再读一次"的比较。它们只是被调度，
		/// 真正执行要等下一次依赖同步，而本组（<c>AfterPhysicsSystemGroup</c>，即
		/// <c>ExportPhysicsWorld</c> 之后）里的系统紧接着就跑了，那时 Job 还没执行。
		/// 于是本组对动态实体写 <c>PhysicsVelocity</c> / <c>PhysicsCollider</c>（重力蛋施力、
		/// 关碰撞体、加减 <c>PhysicsGravityFactor</c>）就被判成"在物理步内改动态实体数据"，
		/// 抛 <c>InvalidOperationException</c>（Burst 下显示成 <c>System.InvalidOperationException: {0}</c>），
		/// 物理导出从此失效：之后所有场景读到的碰撞体都是 0，蛋直接穿过地面。</para>
		///
		/// <para>先完成这些 Job，让检查看到的是我们动手之前的版本，就不会误报。</para>
		/// </summary>
		protected override void OnUpdate()
		{
			EntityManager.CompleteAllTrackedJobs();
			base.OnUpdate();
		}
	}
}
