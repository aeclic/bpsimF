using Bpsim.Collections;
using Bpsim.UI;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Parts.Simulation
{
	public static class PartSimulation
	{
		public static void SendButtonEvent(PartButtonEvent partButtonEvent)
		{
			if (partButtonEvent.PartButtonType == PartButtonType.Trigger)
			{
				ProcessTouch(partButtonEvent.Part);
			}
			else if (partButtonEvent.PartButtonType == PartButtonType.Slider
				&& Bpsim.Parts.Gameplay.PartGameplayManager.Instance?.Find(partButtonEvent.Part) is Bpsim.Parts.Gameplay.UmbrellaBehavior umbrella)
			{
				umbrella.SetAngle(partButtonEvent.SliderValue);
			}
		}

		public static void ProcessTouch(Entity part)
		{
			EntityManager entityManager = PartManager.Instance.System.EntityManager;
			Bpsim.Parts.Gameplay.PartGameplayManager gameplay = Bpsim.Parts.Gameplay.PartGameplayManager.Instance;
			if (gameplay != null && gameplay.HasBehavior(part))
			{
				gameplay.Trigger(part);
				return;
			}
			if (entityManager.HasComponent<FanPropeller>(part))
			{
				FanPropeller componentData = entityManager.GetComponentData<FanPropeller>(part);
				componentData.Enabled = !componentData.Enabled;
				entityManager.SetComponentData(part, componentData);
			}
		}

		public static void ProcessTouch(Reference<PartSimulatorUnmanaged> simulator, Vector2 position)
		{
			float3 @float = SceneCamera.Instance.Camera.ScreenToWorldPoint(position);
			EntityManager entityManager = PartManager.Instance.System.EntityManager;
			foreach (Entity part in simulator.Value.Parts)
			{
				if (math.distancesq(entityManager.GetComponentData<LocalTransform>(part).Position.xy, @float.xy) < 0.5f)
				{
					ProcessTouch(part);
				}
			}
		}

		public static NativeDisjointSet FindConnectedComponents(Reference<PartSimulatorUnmanaged> simulator, ComponentLookup<PhysicsConstrainedBodyPair> constrainedBodyPairLookup)
		{
			NativeList<Entity> parts = simulator.Value.Parts;
			using NativeParallelHashMap<Entity, int> nativeParallelHashMap = new NativeParallelHashMap<Entity, int>(parts.Length, Allocator.Temp);
			for (int i = 0; i < parts.Length; i++)
			{
				nativeParallelHashMap.Add(parts[i], i);
			}
			NativeDisjointSet result = new NativeDisjointSet(parts.Length, Allocator.Temp);
			foreach (Entity joint in simulator.Value.Joints)
			{
				PhysicsConstrainedBodyPair physicsConstrainedBodyPair = constrainedBodyPairLookup[joint];
				result.Union(nativeParallelHashMap[physicsConstrainedBodyPair.EntityA], nativeParallelHashMap[physicsConstrainedBodyPair.EntityB]);
			}
			return result;
		}

		public static void UpdateConnectedComponents(Reference<PartSimulatorUnmanaged> simulator, ComponentLookup<PartConnectedComponent> connectedComponentLookup, ComponentLookup<PartExtensionComponent> extensionDataLookup, NativeDisjointSet disjointSet)
		{
			int componentCount;
			using NativeArray<int> nativeArray = disjointSet.GetComponentIndexes(Allocator.Temp, out componentCount);
			UpdateConnectedComponents(simulator, connectedComponentLookup, extensionDataLookup, nativeArray, componentCount);
		}

		/// <summary>
		/// 用预先算好的连通块索引写回部件，索引数组与 <see cref="PartCollisionFilter"/> 共用，
		/// 避免「同一个 job 里刚写完又从 ComponentLookup 读回来」这种读写可见性问题。
		/// </summary>
		public static void UpdateConnectedComponents(Reference<PartSimulatorUnmanaged> simulator, ComponentLookup<PartConnectedComponent> connectedComponentLookup, ComponentLookup<PartExtensionComponent> extensionDataLookup, NativeArray<int> nativeArray, int componentCount)
		{
			NativeList<Entity> parts = simulator.Value.Parts;
			for (int i = 0; i < parts.Length; i++)
			{
				connectedComponentLookup[parts[i]] = new PartConnectedComponent
				{
					Index = nativeArray[i]
				};
			}
			NativeList<ConnectedComponentInfo> connectedComponents = simulator.Value.ConnectedComponents;
			connectedComponents.Length = componentCount;
			for (int j = 0; j < componentCount; j++)
			{
				connectedComponents[j] = default(ConnectedComponentInfo);
			}
			for (int k = 0; k < parts.Length; k++)
			{
				ref ConnectedComponentInfo reference = ref connectedComponents.ElementAt(nativeArray[k]);
				RefRO<PartExtensionComponent> refRO = extensionDataLookup.GetRefRO(parts[k]);
				reference.PartCount++;
				reference.EnginePower += refRO.ValueRO.Value.EnginePower;
				reference.PowerConsumption += refRO.ValueRO.Value.PowerConsumption;
			}
		}

		public static void UpdateConnectedComponents(Reference<PartSimulatorUnmanaged> simulator, PartAspect.Lookup partAspectLookup, NativeDisjointSet disjointSet)
		{
			NativeList<Entity> parts = simulator.Value.Parts;
			int componentCount;
			using NativeArray<int> nativeArray = disjointSet.GetComponentIndexes(Allocator.Temp, out componentCount);
			NativeList<ConnectedComponentInfo> connectedComponents = simulator.Value.ConnectedComponents;
			connectedComponents.Length = componentCount;
			for (int i = 0; i < componentCount; i++)
			{
				connectedComponents[i] = default(ConnectedComponentInfo);
			}
			for (int j = 0; j < parts.Length; j++)
			{
				PartAspect partAspect = partAspectLookup[parts[j]];
				ref ConnectedComponentInfo reference = ref connectedComponents.ElementAt(nativeArray[j]);
				partAspect.ConnectedComponent = nativeArray[j];
				reference.PartCount++;
				reference.EnginePower += partAspect.ExtensionData.EnginePower;
				reference.PowerConsumption += partAspect.ExtensionData.PowerConsumption;
			}
		}
	}
}
