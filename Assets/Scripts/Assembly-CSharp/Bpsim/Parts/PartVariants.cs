using System;
using System.Collections.Generic;

namespace Bpsim.Parts
{
	/// <summary>
	/// Mod 变体 → 基部件 的登记表。
	/// </summary>
	/// <remarks>
	/// <para>bpsim 的部件是 <c>(PartType, index)</c> 二元组，索引本身没有上限，所以「加部件」
	/// 不需要新 prefab —— 克隆基部件 + 换索引即可（见 <c>Bpsim.Mods.ModParts</c>）。但原版
	/// 大量逻辑是按「索引区间」判断部件类别的（<c>BasePart.ColoredFrames</c> 是
	/// MetalFrame 12..129、<c>BasePart.TransparentFrames</c> 是 132..133……），
	/// 变体的索引（9000+）落在所有区间之外，于是会被当成「不属于彩色框」：
	/// 实体缺 <c>ColoredFrame</c> 组件、Z 偏移算错、<c>PartExtensionMap</c> 查不到质量。
	/// </para>
	/// <para>这张表让「区间判断」能顺着变体找回它的基部件，从而继承基部件的类别。
	/// 只供托管侧使用（Burst job 里只能做 <c>BelongsTo(PartTypeInfo)</c> 这种等值比较）。</para>
	/// </remarks>
	public static class PartVariants
	{
		private static readonly Dictionary<PartTypeInfo, PartTypeInfo> s_bases = new Dictionary<PartTypeInfo, PartTypeInfo>();

		/// <summary>
		/// 变体自己的显示名（「异形木框」「磁轨炮」……）。
		/// </summary>
		/// <remarks>
		/// 建造列表的变体弹窗原来一律写 <c>partType.GetAliasName()</c>，同一个基类型下的
		/// 十几个变体于是全叫「木框」「铁框」，玩家根本分不出哪件是哪件 —— 看上去就像
		/// 「Mod 的部件没显示出来」。这张表让每个变体带上自己的名字。
		/// </remarks>
		private static readonly Dictionary<PartTypeInfo, string> s_names = new Dictionary<PartTypeInfo, string>();

		/// <summary>
		/// 登记表变了（<see cref="Register"/>/<see cref="Unregister"/>/<see cref="Clear"/>）。
		/// </summary>
		/// <remarks>
		/// 建造列表的 <c>PartSelector.Start()</c> 很早就会跑完（它比 <c>ModManager</c> 晚不了多少），
		/// 而 Mod 的变体是在之后才注册进来的 —— 主列表那一行的名字于是永远停在「木框」，
		/// 玩家看到的就是一份和原版一模一样的清单，自然觉得「Mod 没装上」。
		/// 这个事件让 UI 在变体真正登记之后重算一次标签。
		/// </remarks>
		public static event Action Changed;

		/// <summary>所有已登记的变体。</summary>
		public static IEnumerable<KeyValuePair<PartTypeInfo, PartTypeInfo>> All => s_bases;

		/// <summary>这个 <c>(类型, 索引)</c> 是不是 Mod 加的变体。</summary>
		public static bool IsVariant(PartTypeInfo info)
		{
			return s_bases.ContainsKey(info);
		}

		/// <summary>取变体的基部件；不是变体时返回 false。</summary>
		public static bool TryGetBase(PartTypeInfo info, out PartTypeInfo baseInfo)
		{
			return s_bases.TryGetValue(info, out baseInfo);
		}

		/// <summary>变体返回它的基部件，原版部件原样返回。</summary>
		public static PartTypeInfo Resolve(PartTypeInfo info)
		{
			if (s_bases.TryGetValue(info, out PartTypeInfo baseInfo))
			{
				return baseInfo;
			}
			return info;
		}

		/// <summary>登记一个变体。重复登记以最后一次为准。</summary>
		public static void Register(PartTypeInfo variant, PartTypeInfo baseInfo)
		{
			s_bases[variant] = baseInfo;
			RaiseChanged();
		}

		/// <summary>登记一个变体，并记住它自己的显示名。</summary>
		public static void Register(PartTypeInfo variant, PartTypeInfo baseInfo, string displayName)
		{
			s_bases[variant] = baseInfo;
			if (!string.IsNullOrEmpty(displayName))
			{
				s_names[variant] = displayName;
			}
			else
			{
				s_names.Remove(variant);
			}
			RaiseChanged();
		}

		/// <summary>
		/// 取部件在建造列表里该显示的名字：变体用自己的名字，原版部件用类型别名。
		/// </summary>
		public static string GetDisplayName(PartTypeInfo info)
		{
			if (s_names.TryGetValue(info, out string name) && !string.IsNullOrEmpty(name))
			{
				return name;
			}
			return info.PartType.GetAliasName();
		}

		/// <summary>
		/// 这个类型下有几个变体（建造列表用它提示「这一行点进去还有东西」）。
		/// </summary>
		public static int CountVariants(PartType partType)
		{
			int count = 0;
			foreach (PartTypeInfo variant in s_bases.Keys)
			{
				if (variant.PartType == partType)
				{
					count++;
				}
			}
			return count;
		}

		/// <summary>注销一个变体（Mod 卸载时用）。</summary>
		public static bool Unregister(PartTypeInfo variant)
		{
			s_names.Remove(variant);
			bool removed = s_bases.Remove(variant);
			if (removed)
			{
				RaiseChanged();
			}
			return removed;
		}

		/// <summary>
		/// 清空登记表。部件目录重建（<c>PartFactory.BuildParts</c>）时调用：
		/// 那一刻 <c>PartCollection</c> 是从 Resources 重新实例化的，旧变体已经不存在了。
		/// </summary>
		public static void Clear()
		{
			s_bases.Clear();
			s_names.Clear();
			RaiseChanged();
		}

		private static void RaiseChanged()
		{
			Changed?.Invoke();
		}
	}
}
