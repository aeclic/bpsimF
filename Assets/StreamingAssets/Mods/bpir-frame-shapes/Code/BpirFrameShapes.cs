using System;
using Bpsim.Modding;
using Bpsim.Mods;
using Bpsim.Parts;
using UnityEngine;

namespace BpirFrameShapes
{
	public sealed class Entry : ModEntryPoint
	{
		private GameObject m_host;
		private ShapeFeatureEnabler m_enabler;

		public override void OnLoad()
		{
			ModParts.BpirCompatibilityToolsEnabled = true;
			m_host = new GameObject("BpirCompatibility.Enabler");
			UnityEngine.Object.DontDestroyOnLoad(m_host);
			m_enabler = m_host.AddComponent<ShapeFeatureEnabler>();
			Log?.Info("BPIR 兼容 Mod 已启用");
		}

		public override void OnUnload()
		{
			ModParts.BpirCompatibilityToolsEnabled = false;
			bool ownsShapes = m_enabler != null && m_enabler.EnabledShapes;
			if (m_enabler != null) m_enabler.enabled = false;
			if (m_host != null) UnityEngine.Object.Destroy(m_host);
			m_host = null;
			m_enabler = null;
			if (ownsShapes) ModParts.SetFrameShapesEnabled(false);
		}

		public override bool IsRequiredForMachine(string machineJson)
		{
			if (string.IsNullOrEmpty(machineJson)) return false;
			for (int shape = FrameShape.Triangle; shape < FrameShape.Count; shape++)
				if (machineJson.IndexOf("\"FrameShape\":" + shape, StringComparison.Ordinal) >= 0) return true;
			return false;
		}
	}

	public sealed class ShapeFeatureEnabler : MonoBehaviour
	{
		public bool EnabledShapes { get; private set; }

		private void Update()
		{
			if (PartManager.Instance == null || PartManager.Instance.IsSimulating) return;
			if (!ModParts.FrameShapesEnabled)
			{
				ModParts.SetFrameShapesEnabled(true);
				EnabledShapes = true;
			}
			enabled = false;
		}
	}
}
