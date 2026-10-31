using System;
using System.Security.Cryptography;
using System.Text;

namespace Bpsim.Modding
{
	/// <summary>
	/// Mod 的稳定标识。抄 Besiege 的 <c>&lt;ID&gt;</c> 设计：每个 Mod 有一个 GUID，
	/// 存档里只记 GUID，不记名字/路径。这样玩家改文件夹名、换版本、重装都不会让存档失效。
	/// </summary>
	/// <remarks>
	/// 老 Mod（只有 <c>manifest.json</c>、没有 GUID）用 <see cref="Derive"/> 从 id 字符串派生：
	/// 同一个 id 永远得到同一个 GUID，所以老存档也能继续认出来。
	/// </remarks>
	public static class ModGuid
	{
		/// <summary>空 GUID（表示「没有」）。</summary>
		public static readonly Guid Empty = Guid.Empty;

		/// <summary>从任意字符串派生一个稳定的 GUID（MD5，前 16 字节）。</summary>
		public static Guid Derive(string seed)
		{
			if (string.IsNullOrEmpty(seed))
			{
				return Empty;
			}
			using (MD5 md5 = MD5.Create())
			{
				byte[] hash = md5.ComputeHash(Encoding.UTF8.GetBytes("bpsim:" + seed.ToLowerInvariant()));
				return new Guid(hash);
			}
		}

		/// <summary>解析清单里的 GUID 字符串；失败时回退到 <see cref="Derive"/>。</summary>
		public static Guid Parse(string text, string fallbackSeed)
		{
			if (!string.IsNullOrWhiteSpace(text) && Guid.TryParse(text.Trim(), out Guid parsed) && parsed != Empty)
			{
				return parsed;
			}
			return Derive(fallbackSeed);
		}

		/// <summary>存档/配置里统一用大写带连字符的形式。</summary>
		public static string ToStorage(Guid id)
		{
			return id == Empty ? string.Empty : id.ToString("D").ToUpperInvariant();
		}

		/// <summary>日志/UI 里用的短形式（前 8 位）。</summary>
		public static string ToShort(Guid id)
		{
			return id == Empty ? "-" : id.ToString("N").Substring(0, 8);
		}
	}
}
