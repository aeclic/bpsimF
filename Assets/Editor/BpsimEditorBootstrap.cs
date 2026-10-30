// Editor-side runtime bootstrap for bpsim-main.
//
// WHY THIS EXISTS
// ---------------
// bpsim-main renders and simulates through the player-built DOTS assemblies in
// Assets/Plugins (Unity.Collections / Unity.Entities / Unity.Physics /
// Unity.Transforms / Havok.Physics). Those assemblies were taken out of a release player
// build, where ENABLE_UNITY_COLLECTIONS_CHECKS is off, so their container types
// (ComponentTypeHandle<T>, ComponentLookup<T>, NativeParallelHashMap<,>, ...) embed no
// AtomicSafetyHandle even though they are still marked [NativeContainer].
//
// The Editor always runs its native job system with safety checks on, so scheduling a
// IJobChunk declared in Assembly-CSharp or in those assemblies used to fail with
//
//   InvalidOperationException: ConstraintSystem_..._Job.JobData.__localTransformTypeHandle
//   uses the [NativeContainer] but has no AtomicSafetyHandle embedded, this is an
//   internal error in the container type.
//
// The validation itself is done by native code and ignores JobsUtility.JobDebuggerEnabled;
// what actually removes it is Tools/BPSim/StripNativeContainerForEditor.ps1, which drops the
// now-meaningless [NativeContainer] markers from the two assemblies that carry them.
//
// Turning the job debugger off is still worth doing here: with no embedded safety handles
// there is nothing for it to track, and the Editor otherwise keeps paying for the bookkeeping
// on every schedule. Assembly-CSharp-Editor is Editor-only, so player builds are unaffected.
using Unity.Jobs.LowLevel.Unsafe;
using UnityEditor;
using UnityEngine;

internal static class BpsimEditorBootstrap
{
    [InitializeOnLoadMethod]
    private static void OnEditorDomainLoad()
    {
        JobsUtility.JobDebuggerEnabled = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void OnEnterPlayMode()
    {
        JobsUtility.JobDebuggerEnabled = false;
    }
}
