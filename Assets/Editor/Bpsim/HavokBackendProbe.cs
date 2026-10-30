using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Unity.Physics;
using UnityEngine;

namespace Bpsim.QA
{
    /// <summary>
    /// Havok 后端接入自检：确认 Havok.Physics 程序集与原生 HavokNative.dll 都能加载、
    /// 原生版本串能取到、且 HP_CheckCompatibility 已被补丁改成恒通过。
    /// 批量调用：
    ///   Unity.exe -batchmode -quit -executeMethod Bpsim.QA.HavokBackendProbe.Run
    /// 期望输出（三行都以 [HavokProbe] 开头）：
    ///   version=Havok native plugin built from 'master' branch; revision '...'
    ///   HP_CheckCompatibility(uninitialised) => True   （False 表示原生校验仍然生效 = 补丁没上）
    ///   SimulationType values=NoPhysics,UnityPhysics,HavokPhysics
    /// 见 BPSIM_HAVOK_PORT.md。
    /// </summary>
    public static class HavokBackendProbe
    {
        public static void Run()
        {
            try
            {
                Assembly asm = Assembly.Load("Havok.Physics");
                Type plugin = asm.GetType("Havok.Physics.Plugin", true);

                MethodInfo ver = plugin.GetMethod("HP_GetPluginVersionString", BindingFlags.NonPublic | BindingFlags.Static);
                IntPtr ip = (IntPtr)ver.Invoke(null, null);
                Debug.Log("[HavokProbe] version=" + Marshal.PtrToStringAnsi(ip));

                Type tci = plugin.GetNestedType("TypeCheckInformation", BindingFlags.NonPublic);
                object inst = Activator.CreateInstance(tci, true);
                MethodInfo chk = plugin.GetMethod("HP_CheckCompatibility", BindingFlags.NonPublic | BindingFlags.Static);
                object result = chk.Invoke(null, new object[] { inst });
                Debug.Log("[HavokProbe] HP_CheckCompatibility(uninitialised) => " + result
                    + "   (false = native validation active, true = bypassed)");

                Debug.Log("[HavokProbe] SimulationType values=" + string.Join(",", Enum.GetNames(typeof(SimulationType))));
            }
            catch (Exception e)
            {
                Debug.LogError("[HavokProbe] EXCEPTION " + e);
            }
        }
    }
}
