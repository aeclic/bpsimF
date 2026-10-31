using System;
using Bpsim.Parts;
using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary>
	/// 给 Mod 用的部件扩展 API：把已有部件克隆一份、换索引、登记进 <c>PartCollection</c>，
	/// 于是零件选择器的「变体」列表里就多了一项。
	/// </summary>
	/// <remarks>
	/// <para>bpsim 的部件是 <c>(PartType, index)</c> 二元组，索引本身没有上限，所以「加部件」
	/// 不需要新 prefab —— 克隆基部件 + 换索引即可。这是这套体系能支撑 BPIR 那种
	/// 「变形框/三角框」扩展的关键。</para>
	/// <para>自定义索引从 <see cref="CustomIndexBase"/> 起，避开原版 0..~200 的占用区间，
	/// 也方便存档识别「这一件是 Mod 加的」。</para>
	/// </remarks>
	public static class ModParts
	{
		/// <summary>Mod 部件的索引起点。</summary>
		public const int CustomIndexBase = 9000;

		/// <summary>
		/// 注册一个部件变体。
		/// </summary>
		/// <param name="baseType">基部件类型（例如 <c>PartType.MetalFrame</c>）。</param>
		/// <param name="baseIndex">基部件索引（例如 12 = 彩色框第一件）。</param>
		/// <param name="newIndex">新索引，必须 ≥ <see cref="CustomIndexBase"/>。</param>
		/// <param name="displayName">显示名，同时作为 GameObject 名。</param>
		public static bool TryAddVariant(PartType baseType, int baseIndex, int newIndex, string displayName, out string error)
		{
			error = null;
			if (newIndex < CustomIndexBase)
			{
				error = "自定义索引必须 ≥ " + CustomIndexBase;
				return false;
			}

			PartFactory factory = GetFactory(out error);
			if (factory == null)
			{
				return false;
			}

			PartCollection<ManagedPart> collection = factory.PartCollection;
			if (collection == null)
			{
				error = "PartCollection 为空";
				return false;
			}

			if (collection.TryFindPart(new PartTypeInfo(baseType, newIndex), out ManagedPart _))
			{
				error = "索引 " + newIndex + " 已被占用";
				return false;
			}

			ManagedPart basePart = collection.FindPart(new PartTypeInfo(baseType, baseIndex));
			if (basePart == null)
			{
				error = "找不到基部件 " + baseType + "#" + baseIndex;
				return false;
			}

			try
			{
				ManagedPart clone = UnityEngine.Object.Instantiate(basePart);
				clone.PartIndex = newIndex;
				clone.gameObject.name = displayName;
				clone.name = displayName;
				// 跟着基部件挂在同一个容器下，避免模板对象散落在场景根节点。
				if (basePart.transform.parent != null)
				{
					clone.transform.SetParent(basePart.transform.parent, false);
				}
				collection.AddPart(new PartTypeInfo(baseType, newIndex), clone);
				// 登记「变体 → 基部件」：原版的索引区间判断（彩色框 12..129 等）靠它
				// 把变体认成基部件那一类，否则变体实体缺 ColoredFrame、质量查不到。
				PartVariants.Register(clone.TypeInfo, basePart.TypeInfo);
				// 关键：PartEntitySpawner 的预制体表是 PartManager.Start() 时一次性铺的，
				// 不在这里补登记的话 FindPrefab 对变体返回 Entity.Null，
				// 放置逻辑会直接跳过 —— 选择器里看得见，场上放不出来。
				PartEntitySpawner spawner = GetSpawner(out string spawnerError);
				if (spawner == null)
				{
					// 表还没建（PartManager 尚未 Start）：登记本身仍然有效，
					// 等 spawner 建表时会把 PartCollection 里已有的变体一起铺进去。
					Debug.LogWarning("[Mods] 部件变体 " + displayName + " 暂未建预制体：" + spawnerError);
				}
				else
				{
					spawner.AddPrefab(clone);
				}
				Debug.Log("[Mods] 已注册部件变体 " + displayName + " → " + baseType + "#" + newIndex);
				return true;
			}
			catch (Exception ex)
			{
				// 半途失败要把已写进 PartCollection / 登记表的痕迹清掉，否则这个索引
				// 以后再也登记不进来（AddPart 遇重复 key 会抛）。
				collection.RemovePart(new PartTypeInfo(baseType, newIndex));
				PartVariants.Unregister(new PartTypeInfo(baseType, newIndex));
				error = "克隆部件失败：" + ex.Message;
				return false;
			}
		}

		/// <summary>移除一个变体（Mod 卸载时用）。</summary>
		public static bool RemoveVariant(PartType baseType, int index)
		{
			PartFactory factory = GetFactory(out string _);
			PartCollection<ManagedPart> collection = factory != null ? factory.PartCollection : null;
			if (collection == null)
			{
				return false;
			}
			PartTypeInfo info = new PartTypeInfo(baseType, index);
			if (!collection.TryFindPart(info, out ManagedPart part))
			{
				return false;
			}
			// 只有 Mod 变体才归我们销毁：原版部件的 GameObject 是 PartGroups 里的模板，
			// 删掉会连带把整个模板目录搞坏。
			bool isVariant = PartVariants.IsVariant(info);
			PartEntitySpawner spawner = GetSpawner(out string _);
			if (spawner != null)
			{
				spawner.RemovePrefab(info);
			}
			PartVariants.Unregister(info);
			collection.RemovePart(info);
			if (isVariant && part != null)
			{
				UnityEngine.Object.Destroy(part.gameObject);
			}
			return true;
		}

		/// <summary>这个索引是不是 Mod 加的部件。</summary>
		public static bool IsCustomIndex(int index)
		{
			return index >= CustomIndexBase;
		}

		/// <summary>
		/// 「部件形状」特性是否开启（<see cref="FrameShapeSystem.FeatureEnabled"/>）。
		/// </summary>
		/// <remarks>
		/// 这个特性默认是关的：开着会让所有框类部件的碰撞体按形状重建，
		/// 原版流程用不到，所以由 <c>FrameShapeSystem.SetEnabled</c> 显式控制。
		/// Mod 想用形状就得自己打开 —— 之前框架没有对外暴露这个开关，
		/// 导致变体注册成功了却永远套不上形状（<see cref="FrameShapeSystem.SetShape"/>
		/// 第一行就因为 <c>!FeatureEnabled</c> 直接返回）。
		/// </remarks>
		public static bool FrameShapesEnabled => FrameShapeSystem.FeatureEnabled;

		/// <summary>
		/// 打开/关闭「部件形状」特性。开启后 <see cref="FrameShapeSystem.SetShape"/>
		/// 才会真正写组件并重建凸包，属性面板的「形状」按钮也会出现。
		/// </summary>
		/// <remarks>
		/// 模拟中调用会先把模拟停掉（改碰撞体拓扑必须重算质量与关节），
		/// 所以别在每帧里反复调用。
		/// </remarks>
		public static void SetFrameShapesEnabled(bool enabled)
		{
			FrameShapeSystem.SetEnabled(enabled);
		}

		private static PartFactory GetFactory(out string error)
		{
			error = null;
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				error = "PartManager 尚未初始化";
				return null;
			}
			PartFactory factory = manager.Factory;
			if (factory == null)
			{
				error = "PartFactory 尚未初始化";
				return null;
			}
			return factory;
		}

		private static PartEntitySpawner GetSpawner(out string error)
		{
			error = null;
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				error = "PartManager 尚未初始化";
				return null;
			}
			PartEntitySpawner spawner = manager.EntitySpawner;
			if (spawner == null)
			{
				error = "PartEntitySpawner 尚未初始化（PartManager.Start 还没跑）";
				return null;
			}
			return spawner;
		}
	}
}
