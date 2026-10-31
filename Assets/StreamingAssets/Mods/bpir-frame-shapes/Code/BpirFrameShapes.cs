// BPIR 变形框 —— 运行时编译的源码 Mod 样例。
//
// 作用：
//   1. 打开宿主内置的「框形状」特性（FrameShapeSystem）。之后任何容器框
//      （木框 / 铁框 / 彩色框 / 透明框）的部件属性面板里会多出「形状」一行，
//      点它循环 长方形 → 直角三角 → 半圆 → 圆；
//   2. 存档里记录着非长方形形状时，认为载具依赖本 Mod，宿主会把 GUID 记进载具；
//   3. 演示 ModEntryPoint 的 OnLoad / OnUnload / IsRequiredForMachine 三个钩子。
//
// 形状的数据与外观全部由 bpsim 已有的 FrameShapeSystem 提供
// （0=长方形 1=直角三角 2=半圆 3=圆）：实体上的 FrameShape 组件决定凸包碰撞体，
// _BlendFactor.z 决定 shader 里的程序化裁剪。
// 本 Mod 不新增部件、不往建造列表里塞任何条目 —— 建造列表和原版完全一致。

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

		/// <summary>特性是否由本 Mod 打开（卸载时只关自己打开的那次）。</summary>
		private bool m_enabled;

		public override void OnLoad()
		{
			// Mod 在 AfterSceneLoad 加载，此时世界 / PartManager 未必就绪，
			// 直接开特性可能撞上 World.DefaultGameObjectInjectionWorld 为 null，
			// 所以交给 enabler 每 0.5 秒重试一次。
			m_host = new GameObject("BpirFrameShapes.Enabler");
			UnityEngine.Object.DontDestroyOnLoad(m_host);
			m_enabler = m_host.AddComponent<ShapeFeatureEnabler>();

			if (Log != null)
			{
				Log.Info("BPIR 变形框已挂载：容器框的属性面板里会多出「形状」一行");
			}
		}

		public override void OnUnload()
		{
			if (m_enabler != null)
			{
				m_enabled = m_enabler.Applied;
			}
			if (m_host != null)
			{
				UnityEngine.Object.Destroy(m_host);
				m_host = null;
				m_enabler = null;
			}
			if (m_enabled)
			{
				// 关掉特性会把所有框恢复成方框外观与碰撞，存档里的形状值保留。
				ModParts.SetFrameShapesEnabled(false);
				m_enabled = false;
			}
		}

		/// <summary>
		/// 存档里出现非长方形形状（JSON 的 "FrameShape":1..3）就当「依赖本 Mod」。
		/// CSV / XML 的对应列名分别是第 13 列与 shape 属性，这里只做 JSON 的粗判，
		/// 判不出来时返回 false，代价只是清单里少一条依赖记录。
		/// </summary>
		public override bool IsRequiredForMachine(string machineJson)
		{
			if (string.IsNullOrEmpty(machineJson))
			{
				return false;
			}
			for (int shape = FrameShape.Triangle; shape < FrameShape.Count; shape++)
			{
				if (machineJson.IndexOf("\"FrameShape\":" + shape, StringComparison.Ordinal) >= 0)
				{
					return true;
				}
			}
			return false;
		}
	}

	/// <summary>
	/// 等到宿主世界建好再打开形状特性。<see cref="ModParts.SetFrameShapesEnabled"/>
	/// 需要 DefaultGameObjectInjectionWorld 已经存在，太早调用会抛异常，
	/// 所以这里每 0.5 秒重试，成功后自停。
	/// </summary>
	public sealed class ShapeFeatureEnabler : MonoBehaviour
	{
		private float m_nextTry;

		public bool Applied { get; private set; }

		private void Update()
		{
			if (Applied)
			{
				return;
			}
			if (Time.unscaledTime < m_nextTry)
			{
				return;
			}
			m_nextTry = Time.unscaledTime + 0.5f;

			if (ModParts.FrameShapesEnabled)
			{
				Applied = true;
				return;
			}
			try
			{
				ModParts.SetFrameShapesEnabled(true);
			}
			catch (Exception)
			{
				// 世界还没建好，下一轮再试。
				return;
			}
			if (ModParts.FrameShapesEnabled)
			{
				Applied = true;
				Debug.Log("[BPIR 变形框] 形状特性已开启：容器框的属性面板里可以切换形状了");
			}
		}
	}
}
