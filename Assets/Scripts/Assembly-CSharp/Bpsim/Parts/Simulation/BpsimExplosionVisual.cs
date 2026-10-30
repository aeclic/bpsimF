using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.Parts.Simulation
{
	/// <summary>
	/// 复用 BPRC 的 TNT_SmokeCloud 粒子预制体。所有爆炸共用少量实例，避免每次爆炸创建
	/// GameObject 和材质；粒子本身使用原版图集动画，表现与 BPRC 保持一致。
	/// </summary>
	internal sealed class BpsimExplosionVisual : MonoBehaviour
	{
		private static BpsimExplosionVisual s_instance;
		private readonly List<ParticleSystem> m_pool = new List<ParticleSystem>();
		private GameObject m_prefab;

		internal static void Spawn(float3 position, float scale = 1f)
		{
			Ensure().SpawnInternal(new Vector3(position.x, position.y, -0.78f), scale);
		}

		private static BpsimExplosionVisual Ensure()
		{
			if (s_instance != null)
			{
				return s_instance;
			}
			GameObject host = new GameObject("[BpsimExplosionVisual]");
			host.hideFlags = HideFlags.DontSave;
			Object.DontDestroyOnLoad(host);
			s_instance = host.AddComponent<BpsimExplosionVisual>();
			s_instance.m_prefab = Resources.Load<GameObject>("BpsimExplosion/TNT_SmokeCloud");
			return s_instance;
		}

		private void SpawnInternal(Vector3 position, float scale)
		{
			if (m_prefab == null)
			{
				return;
			}
			ParticleSystem root = null;
			for (int i = 0; i < m_pool.Count; i++)
			{
				if (m_pool[i] != null && !m_pool[i].IsAlive(true))
				{
					root = m_pool[i];
					break;
				}
			}
			if (root == null)
			{
				root = Object.Instantiate(m_prefab, transform).GetComponent<ParticleSystem>();
				m_pool.Add(root);
			}
			root.transform.SetPositionAndRotation(position, Quaternion.identity);
			root.transform.localScale = Vector3.one * Mathf.Clamp(scale, 0.55f, 1.35f);
			root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
			root.Play(true);
		}

		internal static void Clear()
		{
			if (s_instance == null)
			{
				return;
			}
			foreach (ParticleSystem root in s_instance.m_pool)
			{
				if (root != null)
				{
					root.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
				}
			}
		}

		private void OnDestroy()
		{
			if (s_instance == this)
			{
				s_instance = null;
			}
		}
	}
}
