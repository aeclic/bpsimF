using System;
using System.Collections.Generic;
using System.Globalization;

namespace Bpsim.Modding
{
	/// <summary>可调设置的类型常量（写进 <c>Mod.xml</c> 的 <c>type</c> 属性）。</summary>
	public static class ModSettingType
	{
		/// <summary>开关。</summary>
		public const string Bool = "bool";

		/// <summary>整数（可配 min / max）。</summary>
		public const string Int = "int";

		/// <summary>浮点数（可配 min / max）。</summary>
		public const string Float = "float";

		/// <summary>任意文本。</summary>
		public const string String = "string";

		/// <summary>下拉选择（用 &lt;Option value=".." label=".."/&gt; 声明候选）。</summary>
		public const string Enum = "enum";
	}

	/// <summary>
	/// Mod 自己声明的一项可调设置。宿主（<c>Bpsim.Mods.ModSettingsPanel</c>）据此生成设置行，
	/// 值由 <c>Bpsim.Mods.ModSettingsStore</c> 持久化，Mod 通过
	/// <see cref="IModSettings"/> 读取。
	/// </summary>
	/// <remarks>
	/// <c>Mod.xml</c> 里的写法：
	/// <code>
	/// &lt;Settings&gt;
	///   &lt;Setting key="autoSave" title="自动保存" type="bool" default="true" /&gt;
	///   &lt;Setting key="gridSize" title="网格边长" type="int" default="4" min="1" max="32" /&gt;
	///   &lt;Setting key="mode" title="模式" type="enum" default="fast"&gt;
	///     &lt;Option value="fast" label="快速" /&gt;
	///     &lt;Option value="precise" label="精确" /&gt;
	///   &lt;/Setting&gt;
	/// &lt;/Settings&gt;
	/// </code>
	/// </remarks>
	[Serializable]
	public sealed class ModSettingSpec
	{
		/// <summary>设置键（一个 Mod 内唯一，存盘用它）。</summary>
		public string key;

		/// <summary>行标题（可中文）。空则回退 <see cref="key"/>。</summary>
		public string title;

		/// <summary>行下面的一句说明（可选）。</summary>
		public string description;

		/// <summary>类型，见 <see cref="ModSettingType"/>。</summary>
		public string type = ModSettingType.Bool;

		/// <summary>默认值（一律以文本存，便于统一落盘）。</summary>
		public string value;

		/// <summary>数值型下限（仅 int/float，默认 0）。</summary>
		public float min;

		/// <summary>数值型上限（仅 int/float；<c>max &lt;= min</c> 视为不限制）。</summary>
		public float max;

		/// <summary>enum 型的候选值。</summary>
		public List<ModSettingOption> options = new List<ModSettingOption>();

		public string DisplayTitle => string.IsNullOrWhiteSpace(title) ? key : title;

		public bool IsBool => string.Equals(type, ModSettingType.Bool, StringComparison.OrdinalIgnoreCase);

		public bool IsEnum => string.Equals(type, ModSettingType.Enum, StringComparison.OrdinalIgnoreCase);

		public bool IsInt => string.Equals(type, ModSettingType.Int, StringComparison.OrdinalIgnoreCase);

		public bool IsFloat => string.Equals(type, ModSettingType.Float, StringComparison.OrdinalIgnoreCase);

		public bool HasRange => (IsInt || IsFloat) && max > min;

		/// <summary>把缺失字段补到可用状态（不抛异常）。</summary>
		public void Normalize(string fallbackKey)
		{
			if (string.IsNullOrWhiteSpace(key))
			{
				key = fallbackKey;
			}
			key = key.Trim();
			if (string.IsNullOrWhiteSpace(title))
			{
				title = key;
			}
			if (string.IsNullOrWhiteSpace(type))
			{
				type = ModSettingType.Bool;
			}
			type = type.Trim().ToLowerInvariant();
			if (options == null)
			{
				options = new List<ModSettingOption>();
			}
			for (int i = options.Count - 1; i >= 0; i--)
			{
				if (options[i] == null || string.IsNullOrWhiteSpace(options[i].value))
				{
					options.RemoveAt(i);
					continue;
				}
				options[i].Normalize();
			}
			if (IsEnum && options.Count == 0)
			{
				// enum 没声明候选就退化成开关，避免出现一张点不动的空下拉。
				type = ModSettingType.Bool;
			}
			if (value == null)
			{
				value = DefaultText();
			}
		}

		/// <summary>类型的默认值文本。</summary>
		public string DefaultText()
		{
			if (IsBool)
			{
				return "false";
			}
			if (IsEnum)
			{
				return options.Count > 0 ? options[0].value : string.Empty;
			}
			if (IsInt)
			{
				return Math.Round(min, MidpointRounding.AwayFromZero).ToString(CultureInfo.InvariantCulture);
			}
			if (IsFloat)
			{
				return min.ToString("0.###", CultureInfo.InvariantCulture);
			}
			return string.Empty;
		}

		/// <summary>把任意文本规整成本设置可接受的取值（越界/非法一律回默认）。</summary>
		public string Sanitize(string raw)
		{
			if (IsBool)
			{
				return ParseBool(raw) ? "true" : "false";
			}
			if (IsEnum)
			{
				if (!string.IsNullOrWhiteSpace(raw))
				{
					foreach (ModSettingOption option in options)
					{
						if (string.Equals(option.value, raw.Trim(), StringComparison.OrdinalIgnoreCase))
						{
							return option.value;
						}
					}
				}
				return options.Count > 0 ? options[0].value : string.Empty;
			}
			if (IsInt)
			{
				if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i))
				{
					return Clamp(i).ToString(CultureInfo.InvariantCulture);
				}
				return DefaultText();
			}
			if (IsFloat)
			{
				if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float f)
					&& !float.IsNaN(f) && !float.IsInfinity(f))
				{
					return ClampF(f).ToString("0.###", CultureInfo.InvariantCulture);
				}
				return DefaultText();
			}
			return raw ?? string.Empty;
		}

		private float Clamp(float v)
		{
			if (max > min)
			{
				v = Math.Max(min, Math.Min(max, v));
			}
			return v;
		}

		private float ClampF(float v) => Clamp(v);

		private static bool ParseBool(string raw)
		{
			if (string.IsNullOrWhiteSpace(raw))
			{
				return false;
			}
			switch (raw.Trim().ToLowerInvariant())
			{
			case "true":
			case "1":
			case "yes":
			case "on":
				return true;
			default:
				return false;
			}
		}
	}

	/// <summary>enum 型设置的一个候选值。</summary>
	[Serializable]
	public sealed class ModSettingOption
	{
		/// <summary>写进存档的值。</summary>
		public string value;

		/// <summary>显示名。空则回退 <see cref="value"/>。</summary>
		public string label;

		public string DisplayLabel => string.IsNullOrWhiteSpace(label) ? value : label;

		public void Normalize()
		{
			if (string.IsNullOrWhiteSpace(label))
			{
				label = value;
			}
		}
	}
}
