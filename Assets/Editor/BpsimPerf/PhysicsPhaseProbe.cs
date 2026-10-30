using System.Diagnostics;
using Unity.Entities;
using Unity.Physics.Systems;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Bpsim.Perf
{
    /// <summary>
    /// 把 Unity.Physics 的一个物理步拆成 5 段实测计时：
    ///   build     = BuildPhysicsWorld（ECS -> PhysicsWorld，含 AABB/关节数据准备）
    ///   broadphase= BroadphaseSystem（BVH 重建 + 静态/动态树遍历，产 body pair）
    ///   contacts  = NarrowphaseSystem（碰撞体距离计算，产 contact）
    ///   jacobians = CreateJacobiansSystem（contact/joint -> Jacobian）
    ///   solve     = SolveAndIntegrateSystem（迭代求解 + 积分）
    ///
    /// 关键：Unity.Physics 各系统只是「调度 job 后立刻返回」，主线程打点间隔恒为 0。
    /// 所以每个标记点先 CompleteAllTrackedJobs() 强制等完上一阶段的 job，再取时间差，
    /// 这样每段就是该阶段的真实墙钟耗时（含并行等待）。
    ///
    /// 用法（Play Mode 内）：invoke_static Bpsim.Perf.PhysicsPhaseProbe Enable / Status / Reset / Disable
    /// 采满 30 帧自动停止，日志行前缀 [PhysicsPhase]。
    /// </summary>
    public static class PhysicsPhaseProbe
    {
        public const int SlotCount = 5;
        public static readonly string[] SlotLabels = { "build", "broadphase", "contacts", "jacobians", "solve" };

        internal static bool s_active;
        internal static long s_last;
        internal static readonly double[] s_sums = new double[SlotCount];
        internal static readonly double[] s_max = new double[SlotCount];
        internal static int s_frames;
        internal static int s_maxFrames = 30;

        public static bool Active => s_active;
        public static int Frames => s_frames;

        public static string Enable()
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return "error: no default world";
            s_active = false;
            world.GetOrCreateSystemManaged<MarkBegin>();
            world.GetOrCreateSystemManaged<MarkAfterBuild>();
            world.GetOrCreateSystemManaged<MarkAfterBroadphase>();
            world.GetOrCreateSystemManaged<MarkAfterContacts>();
            world.GetOrCreateSystemManaged<MarkAfterJacobians>();
            world.GetOrCreateSystemManaged<MarkEnd>();
            Reset();
            s_active = true;
            return "enabled";
        }

        public static string Disable()
        {
            s_active = false;
            return "disabled";
        }

        public static void Reset()
        {
            s_frames = 0;
            s_last = 0;
            for (int i = 0; i < SlotCount; i++) { s_sums[i] = 0; s_max[i] = 0; }
        }

        public static string Status()
        {
            if (s_frames == 0) return "idle (frames=0, sums cleared)";
            var sb = new System.Text.StringBuilder();
            sb.Append("frames=").Append(s_frames).Append(" ms/frame:");
            for (int i = 0; i < SlotCount; i++)
                sb.Append(' ').Append(SlotLabels[i]).Append('=').Append((s_sums[i] / s_frames).ToString("F2"));
            sb.Append(" | max:");
            for (int i = 0; i < SlotCount; i++)
                sb.Append(' ').Append(SlotLabels[i]).Append('=').Append(s_max[i].ToString("F1"));
            return sb.ToString();
        }

        /// <summary>标记点：先强制等完上一阶段的 job，再结算到上一个 slot。</summary>
        internal static void OnMarker(int slot)
        {
            // 未启用时完全不做同步，避免影响正常帧时间测量
            if (!s_active) { s_last = 0; return; }
            var world = World.DefaultGameObjectInjectionWorld;
            if (world != null) world.EntityManager.CompleteAllTrackedJobs();
            long now = Stopwatch.GetTimestamp();

            long prev = s_last;
            s_last = now;
            if (prev == 0) return;

            if (slot >= 0 && slot < SlotCount)
            {
                double ms = (now - prev) * 1000.0 / Stopwatch.Frequency;
                s_sums[slot] += ms;
                if (ms > s_max[slot]) s_max[slot] = ms;
            }
            if (slot == SlotCount - 1)
            {
                s_frames++;
                if (s_frames % 5 == 0) Debug.Log("[PhysicsPhase] " + Status());
                if (s_frames >= s_maxFrames) s_active = false;
            }
        }

        /// <summary>消融实验：整组开关物理（PhysicsSimulationGroup = build + broadphase + contacts + jacobians + solve）。</summary>
        public static string SetPhysics(bool enabled)
        {
            var world = World.DefaultGameObjectInjectionWorld;
            if (world == null) return "error: no default world";
            var group = world.GetExistingSystemManaged<PhysicsSimulationGroup>();
            if (group == null) return "error: no PhysicsSimulationGroup";
            group.Enabled = enabled;
            return "physics=" + enabled;
        }
    }

    /// <summary>标记系统基类：Slot = -1 表示只做同步打点。</summary>
    public abstract partial class PhaseMarker : SystemBase
    {
        protected abstract int Slot { get; }
        protected override void OnUpdate() => PhysicsPhaseProbe.OnMarker(Slot);
    }

    // 全部挂在 PhysicsSimulationGroup 下，用 UpdateAfter/UpdateBefore 夹在各系统组之间。
    // 注意：BuildPhysicsWorld 属于 PhysicsInitializeGroup，而 PhysicsSimulationGroup 本身带
    // [UpdateAfter(typeof(PhysicsInitializeGroup))]，所以“在 build 之后”由组序天然保证。
    // 一旦写成 [UpdateAfter(typeof(BuildPhysicsWorld))]，Entities 会报
    // "Ignoring invalid [UpdateAfterAttribute] ... can only order systems that are members of
    // the same ComponentSystemGroup instance"（跨组约束非法），所以这里不引用 BuildPhysicsWorld。
    [UpdateInGroup(typeof(PhysicsSimulationGroup), OrderFirst = true)]
    public partial class MarkBegin : PhaseMarker { protected override int Slot => -1; }

    [UpdateInGroup(typeof(PhysicsSimulationGroup))]
    [UpdateAfter(typeof(MarkBegin))]
    [UpdateBefore(typeof(PhysicsCreateBodyPairsGroup))]
    public partial class MarkAfterBuild : PhaseMarker { protected override int Slot => 0; }

    [UpdateInGroup(typeof(PhysicsSimulationGroup))]
    [UpdateAfter(typeof(PhysicsCreateBodyPairsGroup))]
    [UpdateBefore(typeof(PhysicsCreateContactsGroup))]
    public partial class MarkAfterBroadphase : PhaseMarker { protected override int Slot => 1; }

    [UpdateInGroup(typeof(PhysicsSimulationGroup))]
    [UpdateAfter(typeof(PhysicsCreateContactsGroup))]
    [UpdateBefore(typeof(PhysicsCreateJacobiansGroup))]
    public partial class MarkAfterContacts : PhaseMarker { protected override int Slot => 2; }

    [UpdateInGroup(typeof(PhysicsSimulationGroup))]
    [UpdateAfter(typeof(PhysicsCreateJacobiansGroup))]
    [UpdateBefore(typeof(PhysicsSolveAndIntegrateGroup))]
    public partial class MarkAfterJacobians : PhaseMarker { protected override int Slot => 3; }

    [UpdateInGroup(typeof(PhysicsSimulationGroup), OrderLast = true)]
    [UpdateAfter(typeof(PhysicsSolveAndIntegrateGroup))]
    public partial class MarkEnd : PhaseMarker { protected override int Slot => 4; }
}
