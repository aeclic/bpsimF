#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

namespace Bpsim.EditorTools
{
	/// <summary>
	/// Editor-side switches that the automated QA passes need.
	///
	/// WHY THIS EXISTS
	/// ---------------
	/// Unity stops advancing the play-mode loop while the Editor window is unfocused unless
	/// PlayerSettings.runInBackground is on.  The symptoms are easy to misread:
	///   * coroutines simply never resume after their first `yield return null`,
	///   * the Game view keeps showing its last frame,
	///   * DshBridge still answers every command, because it is driven by
	///     EditorApplication.update (the *editor* loop keeps running).
	/// So an automated run looks like "started" forever with no console output at all - which
	/// is exactly what happened to BpsimPlayQA.Run("place") at 2026-10-20 23:05 until the
	/// Unity window was activated again.
	///
	/// Drive it from the bridge:
	///   invoke_static type=Bpsim.EditorTools.BpsimQaEditor method=SetRunInBackground args=["1"]
	///   invoke_static type=Bpsim.EditorTools.BpsimQaEditor method=GetRunInBackground args=[]
	/// </summary>
	public static class BpsimQaEditor
	{
		public static string GetRunInBackground()
		{
			return "runInBackground=" + PlayerSettings.runInBackground;
		}

		public static string SetRunInBackground(string value)
		{
			bool on = !(value == "0" || string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
				|| string.Equals(value, "off", StringComparison.OrdinalIgnoreCase));
			PlayerSettings.runInBackground = on;
			AssetDatabase.SaveAssets();
			Debug.Log("[BpsimQaEditor] PlayerSettings.runInBackground = " + PlayerSettings.runInBackground);
			return GetRunInBackground();
		}

		/// <summary>Convenience menu for humans; same effect as SetRunInBackground("1").</summary>
		[MenuItem("Tools/BPSim/QA/Allow Play Mode In Background")]
		public static void AllowPlayModeInBackground()
		{
			SetRunInBackground("1");
		}
	}
}
#endif
