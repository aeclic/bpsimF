using Bpsim.Parts.Simulation;
using UnityEngine;

internal static class _0024BurstDirectCallInitializer
{
	[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
	private static void Initialize()
	{
		// Burst direct-call pre-registration is only meaningful in a Burst-enabled
		// player build. In the editor it calls into the editor's Burst service and
		// crashes natively (SIGSEGV in BurstCompilerService::CompileAsync), so it
		// is skipped here. See the README for details.
#if !UNITY_EDITOR
		FanPropellerSystem.Initialize_0024FanPropellerSystem_23E6237B_LambdaJob_0_Job_RunWithoutJobSystem_000006E1_0024BurstDirectCall();
#endif
	}
}
