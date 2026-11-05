using System;
using Bpsim.Modding;
using Bpsim.Parts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Bpsim.LevelEditor
{
	public sealed class LevelDocument
	{
		public const string RequiredMod = "e47b018a-61a6-4e79-a789-c5f627a00aa4";
		public readonly JObject Data;
		public Guid Id { get { return Guid.Parse((string)Data["id"]); } }
		public string FileName { get { return Id.ToString("N") + ".bpslevel"; } }

		private LevelDocument(JObject data) { Data = data; }

		public static LevelDocument FromLevel(ModLevelDefinition definition)
		{
			if (definition == null) throw new ArgumentException("没有可编辑的关卡。请先导入一个内置关卡。");
			JObject data = string.IsNullOrEmpty(definition.ExtensionData) ? new JObject() : JObject.Parse(definition.ExtensionData);
			if (data["id"] == null) data["id"] = Guid.NewGuid().ToString("D");
			data["format"] = "bpsim.level";
			data["version"] = 1;
			data["title"] = definition.Title ?? "未命名关卡";
			data["base_scene"] = definition.BaseScene ?? definition.Key;
			data["coordinate_space"] = "bple";
			data["required_mods"] = data["required_mods"] ?? new JArray(RequiredMod);
			data["prefabs"] = JArray.FromObject(definition.Prefabs ?? new string[0]);
			data["references"] = JArray.FromObject(definition.References ?? new string[0]);
			data["level_bytes"] = Convert.ToBase64String(definition.LevelBytes ?? new byte[0]);
			data["starter"] = definition.Starter ?? string.Empty;
			data["starter_format"] = FormatName(definition.StarterFormat);
			data["water_bodies"] = data["water_bodies"] ?? new JArray();
			data["extensions"] = data["extensions"] ?? new JObject();
			LevelDocument document = new LevelDocument(data);
			document.ValidateWater();
			return document;
		}

		public static LevelDocument Parse(string json)
		{
			if (string.IsNullOrEmpty(json) || json.Length > 32 * 1024 * 1024) throw new FormatException("关卡文档为空或过大。");
			JObject data = JsonConvert.DeserializeObject<JObject>(json, new JsonSerializerSettings
			{
				MaxDepth = 160, DateParseHandling = DateParseHandling.None, TypeNameHandling = TypeNameHandling.None
			});
			{
				if (data == null) throw new FormatException("关卡文档无效。");
				if ((string)data["format"] != "bpsim.level" || (int?)data["version"] != 1)
					throw new FormatException("不支持的关卡包格式或版本。");
				Guid id;
				if (!Guid.TryParse((string)data["id"], out id) || id == Guid.Empty) throw new FormatException("关卡缺少有效 ID。");
				if ((string)data["coordinate_space"] != "bple") throw new FormatException("本版本仅支持 BPLE 坐标空间。");
				JArray mods = data["required_mods"] as JArray;
				bool dependency = false;
				if (mods != null) foreach (JToken mod in mods) if (string.Equals((string)mod, RequiredMod, StringComparison.OrdinalIgnoreCase)) dependency = true;
				if (!dependency) throw new FormatException("关卡包未声明关卡编辑器 Mod 依赖。");
				string title = (string)data["title"];
				if (string.IsNullOrWhiteSpace(title) || title.Length > 160) throw new FormatException("关卡名称须为 1–160 个字符。");
				data["id"] = id.ToString("D");
				LevelDocument document = new LevelDocument(data);
				document.ValidateWater();
				ModLevels.Validate(document.Definition());
				return document;
			}
		}

		public ModLevelDefinition Definition()
		{
			string bytes = (string)Data["level_bytes"];
			if (bytes == null || bytes.Length > 24 * 1024 * 1024) throw new FormatException("关卡二进制数据缺失或过大。");
			JObject extension = (JObject)Data.DeepClone();
			extension.Remove("level_bytes");
			extension.Remove("starter");
			return new ModLevelDefinition
			{
				Key = Id.ToString("N"),
				BaseScene = (string)Data["base_scene"],
				Title = (string)Data["title"],
				Prefabs = StringArray(Data["prefabs"]),
				References = StringArray(Data["references"]),
				LevelBytes = Convert.FromBase64String(bytes),
				Starter = (string)Data["starter"] ?? string.Empty,
				StarterFormat = ReadFormat((string)Data["starter_format"]),
				ExtensionData = extension.ToString(Formatting.None)
			};
		}

		private static string[] StringArray(JToken token)
		{
			JArray array = token as JArray;
			if (array == null || array.Count > 65535) throw new FormatException("资源映射表无效。");
			string[] values = new string[array.Count];
			for (int i = 0; i < values.Length; i++)
			{
				if (array[i].Type != JTokenType.String || ((string)array[i]).Length > 512) throw new FormatException("资源映射名称无效。");
				values[i] = (string)array[i];
			}
			return values;
		}

		public void ValidateWater()
		{
			int schema = (int?)Data["water_schema_version"] ?? 1;
			if (schema < 1 || schema > 2) throw new FormatException("不支持的水域数据版本。");
			Data["water_schema_version"] = 2;
			if (Data["parallax_enabled"] == null) Data["parallax_enabled"] = true;
			WaterSettings.Boolean(Data, "parallax_enabled");
			JArray bodies = Data["water_bodies"] as JArray;
			if (bodies == null || bodies.Count > 64) throw new FormatException("水域列表无效，最多支持 64 片水域。");
			System.Collections.Generic.HashSet<Guid> ids = new System.Collections.Generic.HashSet<Guid>();
			foreach (JToken token in bodies)
			{
				JObject body = token as JObject;
				Guid id;
				if (body == null || !Guid.TryParse((string)body["id"], out id) || id == Guid.Empty || !ids.Add(id))
					throw new FormatException("水域 ID 缺失或重复。");
				string name = (string)body["name"];
				if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new FormatException("水域名称须为 1–80 个字符。");
				WaterSettings.Read(body);
			}
		}

		public static string FormatName(SchematicsFormat format)
		{
			switch (format)
			{
			case SchematicsFormat.Csv: return "csv";
			case SchematicsFormat.Json: return "json";
			case SchematicsFormat.Xml: return "xml";
			default: throw new FormatException("本版本仅支持 CSV、JSON 或明文 XML 起始载具。");
			}
		}

		private static SchematicsFormat ReadFormat(string format)
		{
			switch (format)
			{
			case "csv": return SchematicsFormat.Csv;
			case "json": return SchematicsFormat.Json;
			case "xml": return SchematicsFormat.Xml;
			default: throw new FormatException("不支持的起始载具格式。");
			}
		}
	}
}
