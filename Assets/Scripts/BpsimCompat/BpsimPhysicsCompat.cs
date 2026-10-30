#if UNITY_EDITOR
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

namespace Bpsim.Compat
{
	/// <summary>
	/// Editor-only compatibility switches for the player-built DOTS assemblies this project
	/// ships in Assets/Plugins (Unity.Burst, Unity.Collections, Unity.Entities, Unity.Physics,
	/// Havok.Physics, ...).
	///
	/// WHY: those DLLs come from the shipping player build, and the project has no
	/// com.unity.burst package, so `Unity.Burst.BurstCompiler.IsEnabled` is forced to false
	/// (see Tools/BPSim/PatchBurstForEditor.ps1) and every [BurstCompile] job falls back to
	/// managed execution in the Editor.  In that configuration Unity.Physics' job safety
	/// bookkeeping is not consistent with what the physics systems expect: the first
	/// simulation tick dies with
	///
	///   IndexOutOfRangeException: Index 2 is out of restricted IJobParallelFor range [0...0]
	///   in ReadWriteBuffer.
	///     at Unity.Physics.Systems.PhysicsWorldExporter+ExportDynamicBodiesJob.Execute
	///
	/// followed by a native crash while scheduling PartSimulationSystem.ImpulseEventJob.
	/// The restricted-range bookkeeping is exactly what Unity's job debugger installs on a
	/// NativeArray when it is scheduled, so turning the job debugger off for this automated
	/// managed path removes the restriction (and the throwing check) and lets the simulation
	/// step.  This is an Editor-only workaround: a real build of the project with the DOTS
	/// packages installed runs Burst-compiled and does not need it.
	/// </summary>
	public static class BpsimPhysicsCompat
	{
		private const string Prefix = "[BpsimCompat] ";

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Install()
		{
			if (!Application.isEditor)
			{
				return;
			}
			// The player-built DOTS assemblies rely on job safety state the Editor job
			// debugger does not set up for managed (non-Burst) execution - see class comment.
			JobsUtility.JobDebuggerEnabled = false;
			Debug.Log(Prefix + "JobsUtility.JobDebuggerEnabled = " + JobsUtility.JobDebuggerEnabled
				+ " (managed DOTS fallback)");
		}

		public static string SetJobDebugger(string value)
		{
			bool on = !(value == "0" || string.Equals(value, "false", System.StringComparison.OrdinalIgnoreCase));
			JobsUtility.JobDebuggerEnabled = on;
			Debug.Log(Prefix + "JobsUtility.JobDebuggerEnabled = " + JobsUtility.JobDebuggerEnabled);
			return "JobDebuggerEnabled=" + JobsUtility.JobDebuggerEnabled;
		}

		public static string Status()
		{
			return "JobDebuggerEnabled=" + JobsUtility.JobDebuggerEnabled;
		}
	}
}
#endif
