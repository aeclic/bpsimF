using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

public static class HavokProbe
{
    public static void Probe()
    {
        Debug.Log("[HavokProbe] unityVersion=" + Application.unityVersion + " platform=" + Application.platform);
        try
        {
            Assembly asm = null;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (a.GetName().Name == "Havok.Physics") { asm = a; break; }
            }
            if (asm == null)
            {
                asm = Assembly.Load("Havok.Physics");
            }
            Debug.Log("[HavokProbe] Havok.Physics loaded: " + (asm != null));
            Type plugin = asm.GetType("Havok.Physics.Plugin", true);
            MethodInfo m = plugin.GetMethod("HP_GetPluginVersionString", BindingFlags.NonPublic | BindingFlags.Static);
            Debug.Log("[HavokProbe] method found: " + (m != null));
            object ret = m.Invoke(null, null);
            IntPtr ip = (IntPtr)ret;
            Debug.Log("[HavokProbe] HP_GetPluginVersionString ptr=" + ip + " str=" + (ip == IntPtr.Zero ? "<null>" : Marshal.PtrToStringAnsi(ip)));
        }
        catch (Exception e)
        {
            Debug.LogError("[HavokProbe] EXCEPTION " + e.GetType().Name + ": " + e.Message);
            var inner = e.InnerException;
            while (inner != null) { Debug.LogError("[HavokProbe]  inner " + inner.GetType().Name + ": " + inner.Message); inner = inner.InnerException; }
        }
        Debug.Log("[HavokProbe] SimulationType values: " + string.Join(",", Enum.GetNames(typeof(Unity.Physics.SimulationType))));
        Debug.Log("[HavokProbe] done");
    }
}
