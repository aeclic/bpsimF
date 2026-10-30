using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Physics.Systems;
using Unity.Transforms;
using UnityEngine.Scripting;

namespace Bpsim.Physics
{
	[UpdateInGroup(typeof(AfterPhysicsSystemGroup))]
	internal partial class ConstraintSystem : SystemBase
	{
		[NoAlias]
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		private struct ConstraintSystem_18A742C8_LambdaJob_0_Job : IJobChunk
		{
			public ComponentTypeHandle<LocalTransform> __localTransformTypeHandle;

			public ComponentTypeHandle<PhysicsVelocity> __velocityTypeHandle;

			[ReadOnly]
			public ComponentTypeHandle<PhysicsMass> __massTypeHandle;

			[ReadOnly]
			public ComponentTypeHandle<Constraint2D> __constraintTypeHandle;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody([NoAlias] ref LocalTransform localTransform, [NoAlias] ref PhysicsVelocity velocity, [NoAlias] in PhysicsMass mass, [NoAlias] in Constraint2D constraint)
			{
				if (!mass.HasInfiniteMass)
				{
					localTransform.Position = new float3(localTransform.Position.xy, constraint.FixedPosition);
					localTransform.Rotation = quaternion.Euler(new float3(constraint.FixedAngle, localTransform.Rotation.ToEulerAngles().x));
					velocity.Linear = new float3(velocity.Linear.xy, 0f);
					velocity.Angular = new float3(float2.zero, velocity.Angular.z);
				}
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				IntPtr nativeArrayPtr = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr(chunk, ref __localTransformTypeHandle);
				IntPtr nativeArrayPtr2 = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr(chunk, ref __velocityTypeHandle);
				IntPtr nativeArrayPtr3 = InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr(chunk, ref __massTypeHandle);
				IntPtr nativeArrayPtr4 = InternalCompilerInterface.UnsafeGetChunkNativeArrayReadOnlyIntPtr(chunk, ref __constraintTypeHandle);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<LocalTransform>(nativeArrayPtr, i), ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PhysicsVelocity>(nativeArrayPtr2, i), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PhysicsMass>(nativeArrayPtr3, i), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<Constraint2D>(nativeArrayPtr4, i));
					}
					return;
				}
				if (math.countbits(chunkEnabledMask.ULong0 ^ (chunkEnabledMask.ULong0 << 1)) + math.countbits(chunkEnabledMask.ULong1 ^ (chunkEnabledMask.ULong1 << 1)) - 1 <= 4)
				{
					v128 mask = chunkEnabledMask;
					int j = 0;
					int endIndex = 0;
					while (EnabledBitUtility.GetNextRange(ref mask, ref j, ref endIndex))
					{
						for (; j < endIndex; j++)
						{
							OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<LocalTransform>(nativeArrayPtr, j), ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PhysicsVelocity>(nativeArrayPtr2, j), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PhysicsMass>(nativeArrayPtr3, j), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<Constraint2D>(nativeArrayPtr4, j));
						}
					}
					return;
				}
				ulong num = chunkEnabledMask.ULong0;
				int num2 = math.min(64, count);
				for (int k = 0; k < num2; k++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<LocalTransform>(nativeArrayPtr, k), ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PhysicsVelocity>(nativeArrayPtr2, k), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PhysicsMass>(nativeArrayPtr3, k), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<Constraint2D>(nativeArrayPtr4, k));
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<LocalTransform>(nativeArrayPtr, l), ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PhysicsVelocity>(nativeArrayPtr2, l), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<PhysicsMass>(nativeArrayPtr3, l), in InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<Constraint2D>(nativeArrayPtr4, l));
					}
					num >>= 1;
				}
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}
		}

		private ComponentTypeHandle<LocalTransform> __Unity_Transforms_LocalTransform_RW_ComponentTypeHandle;

		private ComponentTypeHandle<PhysicsVelocity> __Unity_Physics_PhysicsVelocity_RW_ComponentTypeHandle;

		private ComponentTypeHandle<PhysicsMass> __Unity_Physics_PhysicsMass_RO_ComponentTypeHandle;

		private ComponentTypeHandle<Constraint2D> __Bpsim_Physics_Constraint2D_RO_ComponentTypeHandle;

		private EntityQuery __query_1824520476_0;

		[Preserve]
		protected override void OnUpdate()
		{
			ConstraintSystem_18A742C8_LambdaJob_0_Execute();
		}

		private void ConstraintSystem_18A742C8_LambdaJob_0_Execute()
		{
			__Unity_Transforms_LocalTransform_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Physics_PhysicsVelocity_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Physics_PhysicsMass_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Physics_Constraint2D_RO_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			ConstraintSystem_18A742C8_LambdaJob_0_Job jobData = new ConstraintSystem_18A742C8_LambdaJob_0_Job
			{
				__localTransformTypeHandle = __Unity_Transforms_LocalTransform_RW_ComponentTypeHandle,
				__velocityTypeHandle = __Unity_Physics_PhysicsVelocity_RW_ComponentTypeHandle,
				__massTypeHandle = __Unity_Physics_PhysicsMass_RO_ComponentTypeHandle,
				__constraintTypeHandle = __Bpsim_Physics_Constraint2D_RO_ComponentTypeHandle
			};
			base.Dependency = InternalCompilerInterface.JobChunkInterface.Schedule(jobData, __query_1824520476_0, base.Dependency);
		}

		private void __AssignHandlesImpl(ref SystemState state)
		{
			__query_1824520476_0 = state.GetEntityQuery(new EntityQueryDesc
			{
				All = new ComponentType[4]
				{
					ComponentType.ReadOnly<PhysicsMass>(),
					ComponentType.ReadOnly<Constraint2D>(),
					ComponentType.ReadWrite<LocalTransform>(),
					ComponentType.ReadWrite<PhysicsVelocity>()
				},
				Any = new ComponentType[0],
				None = new ComponentType[0],
				Disabled = new ComponentType[0],
				Absent = new ComponentType[0],
				Options = EntityQueryOptions.Default
			});
			__Unity_Transforms_LocalTransform_RW_ComponentTypeHandle = state.GetComponentTypeHandle<LocalTransform>();
			__Unity_Physics_PhysicsVelocity_RW_ComponentTypeHandle = state.GetComponentTypeHandle<PhysicsVelocity>();
			__Unity_Physics_PhysicsMass_RO_ComponentTypeHandle = state.GetComponentTypeHandle<PhysicsMass>(isReadOnly: true);
			__Bpsim_Physics_Constraint2D_RO_ComponentTypeHandle = state.GetComponentTypeHandle<Constraint2D>(isReadOnly: true);
		}

		protected override void OnCreateForCompiler()
		{
			base.OnCreateForCompiler();
			__AssignHandlesImpl(ref base.CheckedStateRef);
		}

		[Preserve]
		public ConstraintSystem()
		{
		}
	}
}
