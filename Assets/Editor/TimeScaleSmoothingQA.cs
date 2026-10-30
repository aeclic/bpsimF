using System.Text;
using Bpsim.Parts;
using Unity.Entities;
using Unity.Physics.GraphicsIntegration;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Editor
{
	/// <summary>
	/// Verifies the mechanics the time-scale smoothing fix relies on. Run in batch mode:
	/// <c>Tools\unity-batch-bpsim.bat -executeMethod Bpsim.Editor.TimeScaleSmoothingQA.Run</c>
	/// </summary>
	/// <remarks>
	/// The interesting claim is not "the components exist" but "adding
	/// <see cref="PhysicsGraphicalSmoothing"/> actually hands ownership of LocalToWorld to the smoothing
	/// system". That hinges on <see cref="EntityQueryOptions.FilterWriteGroup"/> excluding write-group
	/// members from <c>LocalToWorldSystem</c>'s root query, so it is asserted directly here instead of
	/// being taken on faith from the package sources.
	/// </remarks>
	public static class TimeScaleSmoothingQA
	{
		private static readonly StringBuilder s_log = new StringBuilder();
		private static int s_failures;

		private static void Line(string text)
		{
			s_log.AppendLine(text);
			Debug.Log("[TimeScaleQA] " + text);
		}

		private static void Check(bool condition, string what)
		{
			if (condition)
			{
				Line("PASS  " + what);
				return;
			}
			s_failures++;
			Line("FAIL  " + what);
		}

		public static void Run()
		{
			s_log.Clear();
			s_failures = 0;

			// Build a throwaway world from exactly the system list default world initialization
			// would use. That asserts both halves of "the smoothing pipeline is live in this
			// project": the systems are discovered, and they instantiate inside the default group
			// layout (which is where the physics/transform ordering comes from). Poking the
			// ambient editor world instead would be flaky - in batch mode it may exist without
			// its systems having been created yet.
			World world = null;
			try
			{
				System.Collections.Generic.IReadOnlyList<System.Type> defaultSystems =
					DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);
				world = new World("TimeScaleSmoothingQA");
				DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(world, defaultSystems);
				RunChecks(world);
			}
			catch (System.Exception ex)
			{
				s_failures++;
				Line("FAIL  exception: " + ex);
			}
			// Deliberately not disposing the test world: SeparatorSystem.OnDestroy (pre-existing,
			// unrelated to this fix) double-disposes its NativeParallelHashMap, so World.Dispose()
			// throws and would mask the results below. Batch mode tears the process down anyway.

			Line("FAILURES: " + s_failures);
			System.IO.File.WriteAllText("Logs/timescale-smoothing-qa.txt", s_log.ToString());
			if (s_failures > 0 && Application.isBatchMode)
			{
				UnityEditor.EditorApplication.Exit(1);
			}
		}

		private static void RunChecks(World world)
		{
			EntityManager em = world.EntityManager;

			// 1. The smoothing system is auto-created (this project registers nothing manually).
			Check(world.GetExistingSystemManaged<SmoothRigidBodiesGraphicalMotion>() != null,
				"SmoothRigidBodiesGraphicalMotion is auto-created");
			Check(world.GetExistingSystemManaged<BufferInterpolatedRigidBodiesMotion>() != null,
				"BufferInterpolatedRigidBodiesMotion is auto-created");
			Check(world.GetExistingSystemManaged<RecordMostRecentFixedTime>() != null,
				"RecordMostRecentFixedTime is auto-created");

			// 2. Our WorldTransform back-fill system is registered in the transform group.
			//    The type is internal to Assembly-CSharp, so it is looked up by name.
			System.Type syncType = typeof(BasePart).Assembly.GetType("Bpsim.Parts.Simulation.PartWorldTransformSyncSystem");
			Check(syncType != null, "PartWorldTransformSyncSystem type exists");
			Check(syncType != null && world.GetExistingSystemManaged(syncType) != null,
				"PartWorldTransformSyncSystem is auto-created");

			// 3. An entity carrying PhysicsGraphicalSmoothing must be excluded from the query shape
			//    LocalToWorldSystem uses for roots, otherwise the transform system would overwrite the
			//    interpolated matrix every frame.
			Entity smoothed = em.CreateEntity();
			em.SetName(smoothed, "SmoothedRoot");
			em.AddComponentData(smoothed, LocalTransform.FromPosition(new Unity.Mathematics.float3(1f, 2f, 3f)));
			em.AddComponentData(smoothed, default(WorldTransform));
			em.AddComponentData(smoothed, default(LocalToWorld));
			em.AddComponentData(smoothed, default(PhysicsGraphicalSmoothing));
			em.AddComponentData(smoothed, default(PropagateLocalToWorld));

			Entity plain = em.CreateEntity();
			em.SetName(plain, "PlainRoot");
			em.AddComponentData(plain, LocalTransform.FromPosition(new Unity.Mathematics.float3(4f, 5f, 6f)));
			em.AddComponentData(plain, default(WorldTransform));
			em.AddComponentData(plain, default(LocalToWorld));

			EntityQueryBuilder builder = new EntityQueryBuilder(Unity.Collections.Allocator.Temp)
				.WithAll<LocalTransform>()
				.WithAllRW<WorldTransform, LocalToWorld>()
				.WithNone<Parent>()
				.WithOptions(EntityQueryOptions.FilterWriteGroup);
			EntityQuery rootsQuery;
			try
			{
				rootsQuery = builder.Build(em);
			}
			finally
			{
				builder.Dispose();
			}

			using (Unity.Collections.NativeArray<Entity> roots = rootsQuery.ToEntityArray(Unity.Collections.Allocator.Temp))
			{
				bool hasPlain = false;
				bool hasSmoothed = false;
				for (int i = 0; i < roots.Length; i++)
				{
					if (roots[i] == plain)
					{
						hasPlain = true;
					}
					if (roots[i] == smoothed)
					{
						hasSmoothed = true;
					}
				}
				Check(hasPlain, "FilterWriteGroup root query still matches a plain root entity");
				Check(!hasSmoothed, "FilterWriteGroup root query excludes an entity with PhysicsGraphicalSmoothing");
			}

			// 4. Our back-fill query must be exactly the set that the root job skips.
			EntityQuery syncQuery = em.CreateEntityQuery(new EntityQueryDesc
			{
				All = new[]
				{
					ComponentType.ReadOnly<LocalTransform>(),
					ComponentType.ReadOnly<WorldTransform>(),
					ComponentType.ReadOnly<PhysicsGraphicalSmoothing>()
				},
				None = new[] { ComponentType.ReadOnly<Parent>() }
			});
			Check(syncQuery.CalculateEntityCount() == 1,
				"PartWorldTransformSyncSystem query matches exactly the smoothed root");

			// 5. The interpolation buffer is a plain component (no write group), so the physics-side
			//    buffer system keeps matching the same entity.
			Check(!typeof(PhysicsGraphicalInterpolationBuffer).IsDefined(typeof(WriteGroupAttribute), false),
				"PhysicsGraphicalInterpolationBuffer has no WriteGroup attribute");

			em.DestroyEntity(smoothed);
			em.DestroyEntity(plain);
		}
	}
}
