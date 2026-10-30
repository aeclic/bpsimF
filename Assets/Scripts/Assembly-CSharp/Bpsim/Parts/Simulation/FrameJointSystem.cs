using Bpsim.Collections;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine.Scripting;

namespace Bpsim.Parts.Simulation
{
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class FrameJointSystem : SystemBase
	{
		[BurstCompile]
		private struct BuildFrameJointsJob : IJob
		{
			public EntityCommandBuffer CommandBuffer;

			[ReadOnly]
			public ComponentLookup<WorldTransform> WorldTransformLookup;

			[ReadOnly]
			public ComponentLookup<PartTypeValue> PartTypeLookup;

			[ReadOnly]
			public ComponentLookup<PartTransform> PartTransformLookup;

			[ReadOnly]
			public PartAspect.Lookup PartAspectLookup;

			public Reference<PartSimulatorUnmanaged> Simulator;

			public float ConnectionStrength;

			public void Execute()
			{
				NativeList<(Entity, byte)> nativeList = new NativeList<(Entity, byte)>(256, Allocator.Temp);
				NativeList<Entity> nativeList2 = new NativeList<Entity>(256, Allocator.Temp);
				NativeParallelHashMap<Entity, int> nativeParallelHashMap = new NativeParallelHashMap<Entity, int>(256, Allocator.Temp);
				foreach (Entity part in Simulator.Value.Parts)
				{
					Entity value = part;
					PartAspect partAspect = PartAspectLookup[value];
					PartTypeInfo typeInfo = partAspect.TypeInfo;
					bool flag = false;
					if (partAspect.ContainedPart != Entity.Null)
					{
						flag = PartTypeLookup[partAspect.ContainedPart].Value.BelongsTo(new PartTypeInfo(PartType.SpringBoxingGlove, 4));
					}
					byte b = 0;
					if (typeInfo.PartType == PartType.MetalFrame && flag)
					{
						b |= 1;
					}
					if (typeInfo.PartType == PartType.WoodenFrame && flag)
					{
						b |= 2;
					}
					if (typeInfo.BelongsTo(BasePart.LightFrame))
					{
						b |= 4;
					}
					if (typeInfo.BelongsTo(BasePart.BracketFrame))
					{
						b |= 8;
						nativeList2.Add(in value);
					}
					if (b > 0)
					{
						nativeList.Add((value, b));
					}
				}
				int length = nativeList.Length;
				for (int i = 0; i < length; i++)
				{
					nativeParallelHashMap[nativeList[i].Item1] = i;
				}
				int length2 = nativeList2.Length;
				NativeDisjointSet disjointSet = new NativeDisjointSet(length2, Allocator.Temp);
				for (int j = 0; j < length2; j++)
				{
					for (int k = j + 1; k < length2; k++)
					{
						Entity entity = nativeList2[j];
						Entity entity2 = nativeList2[k];
						PartTransform partTransform = PartTransformLookup[entity];
						PartTransform partTransform2 = PartTransformLookup[entity2];
						int num = partTransform2.X - partTransform.X;
						int num2 = partTransform2.Y - partTransform.Y;
						if (num * num + num2 * num2 == 1)
						{
							disjointSet.Union(j, k);
						}
					}
				}
				int componentCount;
				NativeArray<int> componentIndexes = disjointSet.GetComponentIndexes(Allocator.Temp, out componentCount);
				NativeArray<int> nativeArray = new NativeArray<int>(length, Allocator.Temp);
				for (int l = 0; l < length2; l++)
				{
					int index = nativeParallelHashMap[nativeList2[l]];
					int value2 = componentIndexes[l];
					nativeArray[index] = value2;
				}
				NativeArray<NativeHeap<ComparableTuple<float, int>>> nativeArray2 = new NativeArray<NativeHeap<ComparableTuple<float, int>>>(length, Allocator.Temp);
				for (int m = 0; m < length; m++)
				{
					nativeArray2[m] = new NativeHeap<ComparableTuple<float, int>>(64, Allocator.Temp);
				}
				for (int n = 0; n < length; n++)
				{
					for (int num3 = n + 1; num3 < length; num3++)
					{
						(Entity, byte) tuple = nativeList[n];
						(Entity, byte) tuple2 = nativeList[num3];
						Entity item = tuple.Item1;
						Entity item2 = tuple2.Item1;
						PartAspect partAspect2 = PartAspectLookup[item];
						PartAspect partAspect3 = PartAspectLookup[item2];
						byte b2 = (byte)(tuple.Item2 & tuple2.Item2);
						if (b2 == 0 || !(((b2 & 8) > 0) ? (nativeArray[n] == nativeArray[num3]) : (partAspect2.ConnectedComponent == partAspect3.ConnectedComponent)))
						{
							continue;
						}
						float num4 = math.distancesq(new float2(partAspect2.CoordX, partAspect2.CoordY), new float2(partAspect3.CoordX, partAspect3.CoordY));
						int num5 = (((b2 & 3) > 0) ? 64 : 32);
						float num6 = (((b2 & 3) > 0) ? 32f : 16f);
						if (num4 >= 2f && num4 <= num6 * num6)
						{
							NativeHeap<ComparableTuple<float, int>> nativeHeap = nativeArray2[n];
							if (nativeHeap.Count < num5)
							{
								nativeHeap.Push(new ComparableTuple<float, int>(0f - num4, num3));
							}
							else if (num4 < 0f - nativeHeap.Peek().Item1)
							{
								nativeHeap.PopAndPush(new ComparableTuple<float, int>(0f - num4, num3));
							}
							NativeHeap<ComparableTuple<float, int>> nativeHeap2 = nativeArray2[num3];
							if (nativeHeap2.Count < num5)
							{
								nativeHeap2.Push(new ComparableTuple<float, int>(0f - num4, n));
							}
							else if (num4 < 0f - nativeHeap.Peek().Item1)
							{
								nativeHeap2.PopAndPush(new ComparableTuple<float, int>(0f - num4, n));
							}
						}
					}
				}
				NativeParallelHashSet<ComparableTuple<Entity, Entity>> nativeParallelHashSet = new NativeParallelHashSet<ComparableTuple<Entity, Entity>>(256, Allocator.Temp);
				for (int num7 = 0; num7 < length; num7++)
				{
					foreach (ComparableTuple<float, int> unorderedItem in nativeArray2[num7].UnorderedItems)
					{
						Entity item3 = nativeList[num7].Item1;
						Entity item4 = nativeList[unorderedItem.Item2].Item1;
						nativeParallelHashSet.Add((item3.Index < item4.Index) ? new ComparableTuple<Entity, Entity>(item3, item4) : new ComparableTuple<Entity, Entity>(item4, item3));
					}
				}
				foreach (ComparableTuple<Entity, Entity> item5 in nativeParallelHashSet)
				{
					float connectionStrength = ConnectionStrength;
					PartSimulatorUnmanaged.AddFixedJoint(CommandBuffer, WorldTransformLookup, item5.Item1, item5.Item2, connectionStrength, enableCollision: false);
				}
				nativeParallelHashSet.Dispose();
				foreach (NativeHeap<ComparableTuple<float, int>> item6 in nativeArray2)
				{
					item6.Dispose();
				}
				nativeList.Dispose();
				nativeParallelHashMap.Dispose();
				nativeList2.Dispose();
				componentIndexes.Dispose();
				nativeArray.Dispose();
				nativeArray2.Dispose();
			}
		}

		private bool m_dirty;

		private EntityCommandBufferSystem m_commandBufferSystem;

		private ComponentLookup<WorldTransform> __Unity_Transforms_WorldTransform_RO_ComponentLookup;

		private ComponentLookup<PartTypeValue> __Bpsim_Parts_PartTypeValue_RO_ComponentLookup;

		private ComponentLookup<PartTransform> __Bpsim_Parts_PartTransform_RO_ComponentLookup;

		public void SetDirty()
		{
			m_dirty = true;
		}

		[Preserve]
		protected override void OnCreate()
		{
			m_commandBufferSystem = base.World.GetOrCreateSystemManaged<EndFixedStepSimulationEntityCommandBufferSystem>();
		}

		[Preserve]
		protected override void OnUpdate()
		{
			if (m_dirty)
			{
				m_dirty = false;
				SimulationSettings simulationSettings = UserSettings.Instance.SimulationSettings;
				float num = (simulationSettings.InfiniteConnectionStrength ? float.PositiveInfinity : simulationSettings.ConnectionStrengthFactor);
				__Bpsim_Parts_PartTransform_RO_ComponentLookup.Update(ref base.CheckedStateRef);
				__Bpsim_Parts_PartTypeValue_RO_ComponentLookup.Update(ref base.CheckedStateRef);
				__Unity_Transforms_WorldTransform_RO_ComponentLookup.Update(ref base.CheckedStateRef);
				base.Dependency = IJobExtensions.Schedule(new BuildFrameJointsJob
				{
					CommandBuffer = m_commandBufferSystem.CreateCommandBuffer(),
					WorldTransformLookup = __Unity_Transforms_WorldTransform_RO_ComponentLookup,
					PartTypeLookup = __Bpsim_Parts_PartTypeValue_RO_ComponentLookup,
					PartTransformLookup = __Bpsim_Parts_PartTransform_RO_ComponentLookup,
					PartAspectLookup = new PartAspect.Lookup(ref base.CheckedStateRef, isReadOnly: true),
					Simulator = PartManager.Instance.PartSimulator.UnmanagedRef,
					ConnectionStrength = num * 24f
				}, base.Dependency);
				m_commandBufferSystem.AddJobHandleForProducer(base.Dependency);
			}
		}

		private void __AssignHandlesImpl(ref SystemState state)
		{
			__Unity_Transforms_WorldTransform_RO_ComponentLookup = state.GetComponentLookup<WorldTransform>(isReadOnly: true);
			__Bpsim_Parts_PartTypeValue_RO_ComponentLookup = state.GetComponentLookup<PartTypeValue>(isReadOnly: true);
			__Bpsim_Parts_PartTransform_RO_ComponentLookup = state.GetComponentLookup<PartTransform>(isReadOnly: true);
		}

		protected override void OnCreateForCompiler()
		{
			base.OnCreateForCompiler();
			__AssignHandlesImpl(ref base.CheckedStateRef);
		}

		[Preserve]
		public FrameJointSystem()
		{
		}
	}
}
