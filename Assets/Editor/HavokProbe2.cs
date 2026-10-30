using System;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

public static class HavokProbe2
{
    public static void Probe()
    {
        try
        {
            Assembly asm = Assembly.Load("Havok.Physics");
            Type plugin = asm.GetType("Havok.Physics.Plugin", true);

            MethodInfo ver = plugin.GetMethod("HP_GetPluginVersionString", BindingFlags.NonPublic | BindingFlags.Static);
            IntPtr ip = (IntPtr)ver.Invoke(null, null);
            Debug.Log("[HavokProbe2] version=" + Marshal.PtrToStringAnsi(ip));

            Type tci = plugin.GetNestedType("TypeCheckInformation", BindingFlags.NonPublic);
            Debug.Log("[HavokProbe2] TypeCheckInformation=" + (tci != null));
            object inst = Activator.CreateInstance(tci, true);
            MethodInfo chk = plugin.GetMethod("HP_CheckCompatibility", BindingFlags.NonPublic | BindingFlags.Static);
            object[] args = new object[] { inst };
            object result = chk.Invoke(null, args);
            Debug.Log("[HavokProbe2] HP_CheckCompatibility(uninitialised) => " + result
                + "   (false = native validation active, true = bypassed)");
        }
        catch (Exception e)
        {
            Debug.LogError("[HavokProbe2] EXCEPTION " + e);
        }
    }
}
