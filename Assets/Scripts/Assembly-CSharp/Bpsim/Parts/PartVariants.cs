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
		}

		/// <summary>注销一个变体（Mod 卸载时用）。</summary>
		public static bool Unregister(PartTypeInfo variant)
		{
			return s_bases.Remove(variant);
		}

		/// <summary>
		/// 清空登记表。部件目录重建（<c>PartFactory.BuildParts</c>）时调用：
		/// 那一刻 <c>PartCollection</c> 是从 Resources 重新实例化的，旧变体已经不存在了。
		/// </summary>
		public static void Clear()
		{
			s_bases.Clear();
		}
	}
}
