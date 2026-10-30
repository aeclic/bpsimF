using Bpsim.Parts;
using Bpsim.Serialization;
using Newtonsoft.Json;
using UnityEngine;

namespace Bpsim.Templates
{
	[JsonConverter(typeof(JsonAliasConverterLegacy<PartTemplate>))]
	public class PartTemplate : ComponentTemplate, ITemplate<ManagedPart>, ITemplate
	{
		public override ComponentType Type => ComponentType.Part;

		[JsonAlias("Unity类型")]
		public string UnityType { get; set; }

		[JsonAlias("部件类型")]
		public PartType PartType { get; set; }

		[JsonAlias("部件材质类型")]
		public PartTier PartTier { get; set; }

		[JsonAlias("部件材质序号")]
		public int PartIndex { get; set; }

		[JsonAlias("基础部件类型")]
		public PartType UnderlyingPartType { get; set; }

		[JsonAlias("基础部件材质序号")]
		public int UnderlyingPartIndex { get; set; }

		[JsonAlias("质量")]
		public float Mass { get; set; }

		[JsonAlias("动力消耗值")]
		public float PowerConsumption { get; set; }

		[JsonAlias("动力值")]
		public float EnginePower { get; set; }

		[JsonAlias("图标渲染器模板")]
		public RendererTemplate IconRendererTemplate { get; set; }

		public static PartTemplate Create(ManagedPart part)
		{
			return new PartTemplate
			{
				PartType = part.PartType,
				PartTier = part.PartTier,
				PartIndex = part.PartIndex
			};
		}

		public override void Apply(GameObject gameObject, IResourceResolver resolver)
		{
			Apply(gameObject.AddOrGetComponent<ManagedPart>(), resolver);
		}

		public ManagedPart Apply(ManagedPart part, IResourceResolver resolver)
		{
			part.PartType = PartType;
			part.PartTier = PartTier;
			part.PartIndex = PartIndex;
			return part;
		}
	}
}
