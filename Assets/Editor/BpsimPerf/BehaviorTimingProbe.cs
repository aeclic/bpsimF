using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using Bpsim.Parts.Gameplay;
using Unity.Entities;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Bpsim.Perf
{
	/// <summary>
	/// QA-only: one clean pass over PartGameplayManager's behaviours, timing each
	/// PartBehavior.Tick independently, so we can see WHICH behaviour family eats the
	/// frame on a huge save.  Editor-assembly only; never shipped.
	///
	/// Usage (bridge): invoke_static Bpsim.Perf.BehaviorTimingProbe Run "" 
	/// The manager's own tick is paused for the duration of the measured pass, then
	/// restored, so the numbers are one pass and not two.
	/// </summary>
	public static class BehaviorTimingProbe
	{
		private static BehaviorTimingRunner s_runner;

		public static string Run(string spec)
		{
			if (s_runner == null)
			{
				var go = new GameObject("[BehaviorTiming]");
				go.hideFlags = HideFlags.DontSave;
				UnityEngine.Object.DontDestroyOnLoad(go);
				s_runner = go.AddComponent<BehaviorTimingRunner>();
			}
			s_runner.SchedMode = !string.IsNullOrEmpty(spec) && spec.IndexOf("sched", StringComparison.OrdinalIgnoreCase) >= 0;
			s_runner.Armed = true;
			s_runner.PassesDone = 0;
			s_runner.WaitFrames = s_runner.SchedMode ? 0 : 8;
			Debug.Log("[BehaviorTiming] armed sched=" + s_runner.SchedMode);
			return "armed sched=" + s_runner.SchedMode;
		}

		/// <summary>Caps fixed-step catch-up (Time.maximumDeltaTime). 0.02 = at most one step per frame.</summary>
		public static string SetMaxDelta(float value)
		{
			Time.maximumDeltaTime = value;
			return "maximumDeltaTime=" + Time.maximumDeltaTime.ToString("F3")
				+ " fixedDeltaTime=" + Time.fixedDeltaTime.ToString("F3");
		}

		public static string Status()
		{
			if (s_runner == null) return "none";
			return "armed=" + s_runner.Armed + " passes=" + s_runner.PassesDone;
		}

		/// <summary>A/B switch for the per-fixed-step power aggregation cache.</summary>
		public static string SetPowerCache(bool enabled)
		{
			Bpsim.Parts.Gameplay.ComponentPowerCache.SetEnabled(enabled);
			return Bpsim.Parts.Gameplay.ComponentPowerCache.Status();
		}
	}

	public sealed class BehaviorTimingRunner : MonoBehaviour
	{
		public bool Armed;
		public int PassesDone;
		public int WaitFrames;
		public bool SchedMode;

		private FieldInfo m_behaviorsField;
		private FieldInfo m_runningField;
		private int m_schedFrames;
		private float m_schedLastUnscaled;
		private float m_schedLastFixed;

		private void Update()
		{
			if (!Armed) return;
			if (SchedMode)
			{
				float nowUnscaled = Time.unscaledTime;
				float nowFixed = Time.fixedTime;
				if (m_schedFrames > 0)
				{
					float durMs = (nowUnscaled - m_schedLastUnscaled) * 1000f;
					float steps = (nowFixed - m_schedLastFixed) / Mathf.Max(1E-06f, Time.fixedDeltaTime);
					UnityEngine.Debug.Log("[BehaviorTiming] sched frame#=" + Time.frameCount
						+ " durMs=" + durMs.ToString("F0")
						+ " fixedSteps=" + steps.ToString("F2")
						+ " t=" + nowUnscaled.ToString("F2"));
				}
				m_schedLastUnscaled = nowUnscaled;
				m_schedLastFixed = nowFixed;
				m_schedFrames++;
				if (m_schedFrames > 12)
				{
					Armed = false;
				}
				return;
			}
			PartGameplayManager mgr = PartGameplayManager.Instance;
			if (mgr == null || !mgr.IsRunning) return;
			if (WaitFrames > 0)
			{
				WaitFrames--;
				return;
			}

			if (m_behaviorsField == null)
			{
				m_behaviorsField = typeof(PartGameplayManager).GetField("m_behaviors", BindingFlags.NonPublic | BindingFlags.Instance);
			}
			if (m_runningField == null)
			{
				m_runningField = typeof(PartGameplayManager).GetField("m_running", BindingFlags.NonPublic | BindingFlags.Instance);
			}
			IDictionary dict = m_behaviorsField == null ? null : (m_behaviorsField.GetValue(mgr) as IDictionary);
			if (dict == null)
			{
				Armed = false;
				return;
			}

			var items = new List<DictionaryEntry>();
			foreach (DictionaryEntry e in dict) items.Add(e);
			UnityEngine.Debug.Log("[BehaviorTiming] pass start: behaviours=" + items.Count);

			// Pause the manager's own FixedUpdate so this is ONE pass, not two.
			bool restoreRunning = false;
			if (m_runningField != null)
			{
				restoreRunning = (bool)m_runningField.GetValue(mgr);
				m_runningField.SetValue(mgr, false);
			}

			float dt = Time.fixedDeltaTime;
			var acc = new Dictionary<string, double[]>();
			int calls = 0;
			int fails = 0;
			int enabledCount = 0;
			double totalMs = 0;
			double maxSingleMs = 0;
			string maxSingleKey = "";
			var sw = new Stopwatch();

			for (int i = 0; i < items.Count; i++)
			{
				PartBehavior behavior = items[i].Value as PartBehavior;
				if (behavior == null) continue;
				string key = behavior.GetType().Name + "/" + behavior.Kind;
				if (behavior.Enabled) enabledCount++;

				sw.Restart();
				try
				{
					behavior.Tick(dt);
				}
				catch (Exception)
				{
					fails++;
				}
				sw.Stop();
				double ms = sw.Elapsed.TotalMilliseconds;
				totalMs += ms;
				calls++;
				if (ms > maxSingleMs)
				{
					maxSingleMs = ms;
					maxSingleKey = key;
				}

				double[] row;
				if (!acc.TryGetValue(key, out row))
				{
					row = new double[3];
					acc[key] = row;
				}
				row[0] += 1;
				row[1] += ms;
				if (ms > row[2]) row[2] = ms;
			}

			if (restoreRunning && m_runningField != null)
			{
				m_runningField.SetValue(mgr, true);
			}

			var sorted = new List<KeyValuePair<string, double[]>>(acc);
			sorted.Sort((a, b) => b.Value[1].CompareTo(a.Value[1]));

			var sb = new StringBuilder();
			sb.Append("pass done: calls=").Append(calls)
				.Append(" enabled=").Append(enabledCount)
				.Append(" fails=").Append(fails)
				.Append(" totalMs=").Append(totalMs.ToString("F1"))
				.Append(" maxSingle=").Append(maxSingleMs.ToString("F1")).Append("ms@").Append(maxSingleKey);
			for (int i = 0; i < sorted.Count && i < 14; i++)
			{
				KeyValuePair<string, double[]> kv = sorted[i];
				double share = totalMs > 0 ? kv.Value[1] * 100.0 / totalMs : 0;
				sb.Append("\n  ").Append(kv.Key)
					.Append(" n=").Append((int)kv.Value[0])
					.Append(" ms=").Append(kv.Value[1].ToString("F1"))
					.Append(" (").Append(share.ToString("F1")).Append("%)")
					.Append(" avg=").Append((kv.Value[1] / Math.Max(1, kv.Value[0])).ToString("F2"))
					.Append(" max=").Append(kv.Value[2].ToString("F1"));
			}
			UnityEngine.Debug.Log("[BehaviorTiming] " + sb);

			PassesDone++;
			Armed = false;
		}
	}
}
