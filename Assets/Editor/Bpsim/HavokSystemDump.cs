using System;
using System.Text;
using Unity.Entities;
using UnityEngine;

namespace Bpsim.EditorTools
{
    /// <summary>
    /// 诊断 Havok 后端时用来回答一个关键问题：Unity.Physics 自带的仿真系统组
    /// （Broadphase/Narrowphase/CreateJacobians/SolveAndIntegrate）到底有没有跟 Havok 的那一套
    /// **同时存在**。如果两套都在跑，Unity 的求解器积分出来的位置会被 Havok 的
    /// ExtractMotionsJob 用它自己那份「没前进」的数据覆盖回去 —— 正好符合实测
    /// 「速度按重力累积、位置却一直停在建造位置」。
    /// <para>用反射按类型名查，避免编译期依赖（类型被 #if !HAVOK_PHYSICS_EXISTS 编译掉时也能跑）。</para>
    /// </summary>
    public static class HavokSystemDump
    {
        private static readonly string[] Candidates =
        {
            "Unity.Physics.Systems.PhysicsSimulationPickerSystem, Unity.Physics",
            "Unity.Physics.Systems.BroadphaseSystem, Unity.Physics",
            "Unity.Physics.Systems.NarrowphaseSystem, Unity.Physics",
            "Unity.Physics.Systems.CreateJacobiansSystem, Unity.Physics",
            "Unity.Physics.Systems.SolveAndIntegrateSystem, Unity.Physics",
            "Unity.Physics.Systems.PhysicsSimulationGroup, Unity.Physics",
            "Havok.Physics.PhysicsSimulationPickerSystem, Havok.Physics",
            "Havok.Physics.HavokBroadphaseSystem, Havok.Physics",
            "Havok.Physics.HavokNarrowphaseSystem, Havok.Physics",
            "Havok.Physics.HavokCreateJacobiansSystem, Havok.Physics",
            "Havok.Physics.HavokSolveAndIntegrateSystem, Havok.Physics",
            "Havok.Physics.HavokBroadphaseData, Havok.Physics",
            "Unity.Physics.Systems.BuildPhysicsWorld, Unity.Physics",
            "Unity.Physics.Systems.ExportPhysicsWorld, Unity.Physics"
        };

        public static string Dump()
        {
            StringBuilder sb = new StringBuilder();
            World world = World.DefaultGameObjectInjectionWorld;
            sb.Append("world=").Append(world == null ? "null" : world.Name).Append('\n');
            if (world == null)
            {
                return sb.ToString();
            }

            foreach (string spec in Candidates)
            {
                Type type = Type.GetType(spec);
                if (type == null)
                {
                    sb.Append("MISSING  ").Append(spec).Append('\n');
                    continue;
                }
                ComponentSystemBase system = null;
                try
                {
                    system = world.GetExistingSystemManaged(type);
                }
                catch (Exception ex)
                {
                    sb.Append("ERR      ").Append(spec).Append(" : ").Append(ex.GetType().Name).Append('\n');
                    continue;
                }
                sb.Append(system == null ? "ABSENT   " : "present  ").Append(spec);
                if (system != null)
                {
                    sb.Append("  enabled=").Append(system.Enabled)
                      .Append(" type=").Append(system.GetType().Name);
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>把某个系统按类型名开/关，用来做「只留 Havok」或「只留 Unity」的对照实验。</summary>
        public static string Toggle(string typeSpec, bool enabled)
        {
            World world = World.DefaultGameObjectInjectionWorld;
            Type type = Type.GetType(typeSpec);
            if (world == null || type == null)
            {
                return "bad world/type: " + typeSpec;
            }
            ComponentSystemBase system = world.GetExistingSystemManaged(type);
            if (system == null)
            {
                return "absent: " + typeSpec;
            }
            system.Enabled = enabled;
            return type.Name + ".Enabled=" + system.Enabled;
        }
    }
}
