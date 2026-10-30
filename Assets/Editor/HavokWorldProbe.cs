using System;
using Unity.Entities;
using Unity.Physics;
using Unity.Physics.Systems;
using UnityEngine;

public static class HavokWorldProbe
{
    public static void Run()
    {
        World world = null;
        try
        {
            world = DefaultWorldInitialization.Initialize("HavokProbeWorld", false);
            var em = world.EntityManager;

            Entity stepEntity = em.CreateEntity();
            em.AddComponentData(stepEntity, new PhysicsStep
            {
                SimulationType = SimulationType.HavokPhysics,
                Gravity = new Unity.Mathematics.float3(0f, -9.81f, 0f),
                SolverIterationCount = 4
            });

            for (int i = 0; i < 5; i++)
            {
                world.Update();
                using (var q = em.CreateEntityQuery(typeof(SimulationSingleton)))
                {
                    if (!q.IsEmpty)
                    {
                        var ss = q.GetSingleton<SimulationSingleton>();
                        Debug.Log("[HavokWorldProbe] frame " + i + " SimulationSingleton.Type=" + ss.Type);
                    }
                    else
                    {
                        Debug.Log("[HavokWorldProbe] frame " + i + " no SimulationSingleton");
                    }
                }
            }
            Debug.Log("[HavokWorldProbe] OK");
        }
        catch (Exception e)
        {
            Debug.LogError("[HavokWorldProbe] EXCEPTION " + e);
        }
        finally
        {
            if (world != null) { try { world.Dispose(); } catch (Exception e2) { Debug.LogError("[HavokWorldProbe] dispose " + e2.Message); } }
            Debug.Log("[HavokWorldProbe] done");
        }
    }
}
