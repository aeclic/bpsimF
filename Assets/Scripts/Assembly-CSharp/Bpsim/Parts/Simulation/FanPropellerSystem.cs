using System;
using System.Runtime.CompilerServices;
using AOT;
using Bpsim.Physics;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Physics.Aspects;
using Unity.Transforms;
using UnityEngine.Scripting;

namespace Bpsim.Parts.Simulation
{
	[UpdateInGroup(typeof(PartSimulationSystemGroup))]
	internal partial class FanPropellerSystem : SystemBase
	{
		[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
		[NoAlias]
		private struct FanPropellerSystem_23E6237B_LambdaJob_0_Job : IJobChunk
		{
			public delegate void RunWithoutJobSystem_000006E1_0024PostfixBurstDelegate(ref EntityQuery query, IntPtr jobPtr);

			internal static class RunWithoutJobSystem_000006E1_0024BurstDirectCall
			{
				private static IntPtr Pointer;

				private static IntPtr DeferredCompilation;

				[BurstDiscard]
				private unsafe static void GetFunctionPointerDiscard(ref IntPtr P_0)
				{
#if !UNITY_EDITOR
					if (Pointer == (IntPtr)0)
					{
						Pointer = (nint)BurstCompiler.GetILPPMethodFunctionPointer2(DeferredCompilation, default(RuntimeMethodHandle), typeof(RunWithoutJobSystem_000006E1_0024PostfixBurstDelegate).TypeHandle);
					}
#endif
					P_0 = Pointer;
				}

				private static IntPtr GetFunctionPointer()
				{
					nint result = 0;
					GetFunctionPointerDiscard(ref result);
					return result;
				}

				public static void Constructor()
				{
#if !UNITY_EDITOR
					// 反编译把 Burst ILPP 生成的 metadata handle 丢了（原来这里是 ldtoken 该方法），
					// 于是这里传进去的 default(RuntimeMethodHandle) 是 null，真机构建启动时
					// BurstCompiler.CompileILPPMethod2 直接抛
					//   ArgumentNullException: Value cannot be null. Parameter name: burstMethodHandle
					// 而且它是静态构造链（RuntimeInitializeOnLoadMethod → Initialize() → cctor），
					// 异常会包成 TypeInitializationException 打到启动日志里。
					// 没有 handle 就拿不到 Burst 函数指针，Invoke() 里本来就有「指针为 0 就退回
					// RunWithoutJobSystem_0024BurstManaged」的分支，所以这里只要不炸即可。
					try
					{
						DeferredCompilation = BurstCompiler.CompileILPPMethod2(default(RuntimeMethodHandle));
					}
					catch (Exception)
					{
						DeferredCompilation = IntPtr.Zero;
					}
#endif
				}

				public static void Initialize()
				{
				}

				static RunWithoutJobSystem_000006E1_0024BurstDirectCall()
				{
					Constructor();
				}

				public unsafe static void Invoke(ref EntityQuery query, IntPtr jobPtr)
				{
					if (BurstCompiler.IsEnabled)
					{
						IntPtr functionPointer = GetFunctionPointer();
						if (functionPointer != (IntPtr)0)
						{
							((delegate* unmanaged[Cdecl]<ref EntityQuery, IntPtr, void>)functionPointer)(ref query, jobPtr);
							return;
						}
					}
					RunWithoutJobSystem_0024BurstManaged(ref query, jobPtr);
				}
			}

			internal static InternalCompilerInterface.JobChunkRunWithoutJobSystemDelegate FunctionPtrFieldNoBurst;

			internal static InternalCompilerInterface.JobChunkRunWithoutJobSystemDelegate FunctionPtrFieldBurst;

			public Reference<PartSimulatorUnmanaged> simulator;

			[NativeDisableParallelForRestriction]
			public ComponentLookup<LocalTransform> localTransformLookup;

			public ComponentTypeHandle<FanPropeller> __fanPropellerTypeHandle;

			public RigidBodyAspect.TypeHandle __rigidBodyAspectTypeHandle;

			[ReadOnly]
			public PartAspect.TypeHandle __partAspectTypeHandle;

			public BufferTypeHandle<LinkedEntityGroup> __linkedEntityGroupTypeHandle;

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			private void OriginalLambdaBody([NoAlias] ref FanPropeller fanPropeller, RigidBodyAspect rigidBodyAspect, PartAspect partAspect, DynamicBuffer<LinkedEntityGroup> linkedEntityGroup)
			{
				float3 axis = math.right();
				if (partAspect.PartType == PartType.Fan)
				{
					axis = math.left();
				}
				else if (partAspect.PartType == PartType.Propeller)
				{
					axis = math.right();
				}
				else if (partAspect.PartType == PartType.Rotor)
				{
					axis = math.up();
				}
				if (fanPropeller.Enabled)
				{
					float num = 0f;
					float num2 = 0f;
					float2 @float = float2.zero;
					if (partAspect.PartType == PartType.Fan)
					{
						num = ((partAspect.PartIndex == 5) ? 60f : 7f);
						num2 = ((partAspect.PartIndex == 5) ? 540f : 63f);
						@float = -rigidBodyAspect.Rotation.ToRightDirection();
					}
					else if (partAspect.PartType == PartType.Propeller)
					{
						num = 37f;
						num2 = float.PositiveInfinity;
						@float = rigidBodyAspect.Rotation.ToRightDirection();
					}
					else if (partAspect.PartType == PartType.Rotor)
					{
						num = 120f;
						num2 = 840f;
						@float = rigidBodyAspect.Rotation.ToUpDirection();
						float2 float2 = new float2(0f, 1f);
						float num3 = math.dot(@float, float2);
						@float = math.select(@float, 0.5f * (@float + float2), num3 > 0f);
					}
					float powerFactor = GetPowerFactor(partAspect.ConnectedComponent, simulator);
					float num4 = 2f * num / 50f * powerFactor;
					float num5 = num4 * math.dot(@float, rigidBodyAspect.LinearVelocity.xy);
					num4 = math.select(num4, num4 * num2 / num5, num5 > num2);
					rigidBodyAspect.ApplyLinearImpulseWorldSpace(new float3(num4 * @float, 0f));
					fanPropeller.TargetAngularSpeed = MathF.PI / 180f * (1000f * powerFactor + 700f);
					fanPropeller.AngularSpeed = math.clamp(fanPropeller.TargetAngularSpeed, fanPropeller.AngularSpeed + MathF.PI / 2f, fanPropeller.AngularSpeed - MathF.PI / 2f);
					fanPropeller.Angle += fanPropeller.AngularSpeed * 0.02f;
				}
				else
				{
					fanPropeller.AngularSpeed *= math.exp(-0.1f);
					fanPropeller.Angle += fanPropeller.AngularSpeed * 0.02f;
				}
				localTransformLookup.GetRefRW(linkedEntityGroup[1].Value, isReadOnly: false).ValueRW.Rotation = quaternion.AxisAngle(axis, fanPropeller.Angle);
			}

			[CompilerGenerated]
			public void Execute(in ArchetypeChunk chunk, int batchIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				IntPtr nativeArrayPtr = InternalCompilerInterface.UnsafeGetChunkNativeArrayIntPtr(chunk, ref __fanPropellerTypeHandle);
				RigidBodyAspect.ResolvedChunk resolvedChunk = __rigidBodyAspectTypeHandle.Resolve(chunk);
				PartAspect.ResolvedChunk resolvedChunk2 = __partAspectTypeHandle.Resolve(chunk);
				BufferAccessor<LinkedEntityGroup> bufferAccessor = chunk.GetBufferAccessor(ref __linkedEntityGroupTypeHandle);
				int count = chunk.Count;
				if (!useEnabledMask)
				{
					for (int i = 0; i < count; i++)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<FanPropeller>(nativeArrayPtr, i), resolvedChunk[i], resolvedChunk2[i], bufferAccessor[i]);
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
							OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<FanPropeller>(nativeArrayPtr, j), resolvedChunk[j], resolvedChunk2[j], bufferAccessor[j]);
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
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<FanPropeller>(nativeArrayPtr, k), resolvedChunk[k], resolvedChunk2[k], bufferAccessor[k]);
					}
					num >>= 1;
				}
				num = chunkEnabledMask.ULong1;
				for (int l = 64; l < count; l++)
				{
					if ((num & 1) != 0L)
					{
						OriginalLambdaBody(ref InternalCompilerInterface.UnsafeGetRefToNativeArrayPtrElement<FanPropeller>(nativeArrayPtr, l), resolvedChunk[l], resolvedChunk2[l], bufferAccessor[l]);
					}
					num >>= 1;
				}
			}

			[MonoPInvokeCallback(typeof(InternalCompilerInterface.JobChunkRunWithoutJobSystemDelegate))]
			[BurstCompile(FloatMode = FloatMode.Default, FloatPrecision = FloatPrecision.Standard, CompileSynchronously = false)]
			public static void RunWithoutJobSystem(ref EntityQuery query, IntPtr jobPtr)
			{
				RunWithoutJobSystem_000006E1_0024BurstDirectCall.Invoke(ref query, jobPtr);
			}

			void IJobChunk.Execute(in ArchetypeChunk chunk, int unfilteredChunkIndex, bool useEnabledMask, in v128 chunkEnabledMask)
			{
				Execute(in chunk, unfilteredChunkIndex, useEnabledMask, in chunkEnabledMask);
			}

			[MethodImpl(MethodImplOptions.AggressiveInlining)]
			[MonoPInvokeCallback(typeof(InternalCompilerInterface.JobChunkRunWithoutJobSystemDelegate))]
			public static void RunWithoutJobSystem_0024BurstManaged(ref EntityQuery query, IntPtr jobPtr)
			{
				InternalCompilerInterface.JobChunkInterface.RunWithoutJobsInternal(ref InternalCompilerInterface.UnsafeAsRef<FanPropellerSystem_23E6237B_LambdaJob_0_Job>(jobPtr), ref query);
			}
		}

		private ComponentTypeHandle<FanPropeller> __Bpsim_Parts_FanPropeller_RW_ComponentTypeHandle;

		private RigidBodyAspect.TypeHandle __Unity_Physics_Aspects_RigidBodyAspect_RW_AspectTypeHandle;

		private PartAspect.TypeHandle __Bpsim_Parts_PartAspect_RO_AspectTypeHandle;

		private BufferTypeHandle<LinkedEntityGroup> __Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle;

		private ComponentLookup<LocalTransform> __Unity_Transforms_LocalTransform_RW_ComponentLookup;

		private EntityQuery __query_1178201653_0;

		[Preserve]
		protected override void OnUpdate()
		{
			// The default world ticks these systems before PartManager.Awake (and for one frame
			// after it is destroyed on play-mode exit); Instance is null then, and touching it
			// threw NullReferenceException once per system per frame.
			if (PartManager.Instance == null)
			{
				return;
			}
			if (PartManager.Instance.IsSimulating)
			{
				Reference<PartSimulatorUnmanaged> simulator = PartManager.Instance.PartSimulator.UnmanagedRef;
				__Unity_Transforms_LocalTransform_RW_ComponentLookup.Update(ref base.CheckedStateRef);
				ComponentLookup<LocalTransform> localTransformLookup = __Unity_Transforms_LocalTransform_RW_ComponentLookup;
				FanPropellerSystem_23E6237B_LambdaJob_0_Execute(ref simulator, ref localTransformLookup);
			}
		}

		private static float GetPowerFactor(int index, Reference<PartSimulatorUnmanaged> simulator)
		{
			ConnectedComponentInfo connectedComponentInfo = simulator.Value.ConnectedComponents[index];
			float enginePower = connectedComponentInfo.EnginePower;
			float powerConsumption = connectedComponentInfo.PowerConsumption;
			float num = 0f;
			if (powerConsumption > 1f)
			{
				num = math.min(enginePower / powerConsumption, 20f);
			}
			else if (enginePower > 0f)
			{
				num = 1f;
			}
			return math.pow(num, (num > 1f) ? 0.585f : 0.75f);
		}

		private void FanPropellerSystem_23E6237B_LambdaJob_0_Execute(ref Reference<PartSimulatorUnmanaged> simulator, ref ComponentLookup<LocalTransform> localTransformLookup)
		{
			__Bpsim_Parts_FanPropeller_RW_ComponentTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Physics_Aspects_RigidBodyAspect_RW_AspectTypeHandle.Update(ref base.CheckedStateRef);
			__Bpsim_Parts_PartAspect_RO_AspectTypeHandle.Update(ref base.CheckedStateRef);
			__Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle.Update(ref base.CheckedStateRef);
			FanPropellerSystem_23E6237B_LambdaJob_0_Job jobData = new FanPropellerSystem_23E6237B_LambdaJob_0_Job
			{
				simulator = simulator,
				localTransformLookup = localTransformLookup,
				__fanPropellerTypeHandle = __Bpsim_Parts_FanPropeller_RW_ComponentTypeHandle,
				__rigidBodyAspectTypeHandle = __Unity_Physics_Aspects_RigidBodyAspect_RW_AspectTypeHandle,
				__partAspectTypeHandle = __Bpsim_Parts_PartAspect_RO_AspectTypeHandle,
				__linkedEntityGroupTypeHandle = __Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle
			};
			CompleteDependency();
			InternalCompilerInterface.JobChunkRunWithoutJobSystemDelegate functionPointer = (JobsUtility.JobCompilerEnabled ? FanPropellerSystem_23E6237B_LambdaJob_0_Job.FunctionPtrFieldBurst : FanPropellerSystem_23E6237B_LambdaJob_0_Job.FunctionPtrFieldNoBurst);
			InternalCompilerInterface.UnsafeRunJobChunk(ref jobData, __query_1178201653_0, functionPointer);
			simulator = jobData.simulator;
			localTransformLookup = jobData.localTransformLookup;
		}

		private void __AssignHandlesImpl(ref SystemState state)
		{
			EntityQueryBuilder entityQueryBuilder = new EntityQueryBuilder(Allocator.Temp);
			entityQueryBuilder = entityQueryBuilder.WithAllRW<FanPropeller>();
			entityQueryBuilder = entityQueryBuilder.WithAll<LinkedEntityGroup>();
			entityQueryBuilder = entityQueryBuilder.WithAspect<RigidBodyAspect>();
			entityQueryBuilder = entityQueryBuilder.WithAspect<PartAspect>();
			__query_1178201653_0 = entityQueryBuilder.Build(ref state);
			__Bpsim_Parts_FanPropeller_RW_ComponentTypeHandle = state.GetComponentTypeHandle<FanPropeller>();
			__Unity_Physics_Aspects_RigidBodyAspect_RW_AspectTypeHandle = new RigidBodyAspect.TypeHandle(ref state, isReadOnly: false);
			__Bpsim_Parts_PartAspect_RO_AspectTypeHandle = new PartAspect.TypeHandle(ref state, isReadOnly: true);
			__Unity_Entities_LinkedEntityGroup_RO_BufferTypeHandle = state.GetBufferTypeHandle<LinkedEntityGroup>(isReadOnly: true);
			__Unity_Transforms_LocalTransform_RW_ComponentLookup = state.GetComponentLookup<LocalTransform>();
		}

		protected override void OnCreateForCompiler()
		{
			base.OnCreateForCompiler();
			__AssignHandlesImpl(ref base.CheckedStateRef);
			FanPropellerSystem_23E6237B_LambdaJob_0_Job.FunctionPtrFieldNoBurst = FanPropellerSystem_23E6237B_LambdaJob_0_Job.RunWithoutJobSystem;
			FanPropellerSystem_23E6237B_LambdaJob_0_Job.FunctionPtrFieldBurst = InternalCompilerInterface.BurstCompile(FanPropellerSystem_23E6237B_LambdaJob_0_Job.FunctionPtrFieldNoBurst);
		}

		[Preserve]
		public FanPropellerSystem()
		{
		}

		public static void Initialize_0024FanPropellerSystem_23E6237B_LambdaJob_0_Job_RunWithoutJobSystem_000006E1_0024BurstDirectCall()
		{
			FanPropellerSystem_23E6237B_LambdaJob_0_Job.RunWithoutJobSystem_000006E1_0024BurstDirectCall.Initialize();
		}
	}
}
