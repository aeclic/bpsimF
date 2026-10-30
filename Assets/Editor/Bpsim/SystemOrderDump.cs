using System.Text;
using Unity.Entities;
using UnityEngine;

namespace BpsimProbe
{
    /// <summary>诊断用：按实际执行顺序打印 World 里的系统，确认 EggSystem 与物理步的先后。</summary>
    public static class SystemOrderDump
    {
        private static string s_last = "(not run)";

        public static string Dump()
        {
            World world = World.DefaultGameObjectInjectionWorld;
            if (world == null)
            {
                s_last = "no default world";
                return s_last;
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("world=" + world.Name);
            DumpGroup(world.GetExistingSystemManaged<SimulationSystemGroup>(), sb, 0);
            s_last = sb.ToString();
            return s_last;
        }

        public static string Last()
        {
            return s_last;
        }

        private static void DumpGroup(ComponentSystemGroup group, StringBuilder sb, int depth)
        {
            if (group == null)
            {
                return;
            }
            sb.Append(' ', depth * 2).AppendLine("[" + group.GetType().Name + "]");
            foreach (ComponentSystemBase system in group.Systems)
            {
                if (system is ComponentSystemGroup child)
                {
                    DumpGroup(child, sb, depth + 1);
                }
                else
                {
                    sb.Append(' ', (depth + 1) * 2).AppendLine(system.GetType().Name);
                }
            }
        }
    }
}
