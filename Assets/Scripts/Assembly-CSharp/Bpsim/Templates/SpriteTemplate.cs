using Bpsim.Rendering;
using Bpsim.Serialization;
using Newtonsoft.Json;
using UnityEngine;

namespace Bpsim.Templates
{
	[JsonConverter(typeof(JsonAliasConverterLegacy<SpriteTemplate>))]
	public class SpriteTemplate : ComponentTemplate, ITemplate<SpriteBase>, ITemplate
	{
		public override ComponentType Type => ComponentType.Sprite;

		[JsonAlias("贴图类型")]
		public SpriteType SpriteType { get; set; }

		[JsonAlias("名称")]
		public string Name { get; set; }

		public static SpriteTemplate Create(SpriteBase sprite)
		{
			SpriteType spriteType = ((sprite is MeshSprite) ? SpriteType.Mesh : ((sprite is MaterialSprite) ? SpriteType.Material : ((sprite is UISprite) ? SpriteType.UI : SpriteType.None)));
			SpriteType spriteType2 = spriteType;
			return new SpriteTemplate
			{
				Name = sprite.SpriteName,
				SpriteType = spriteType2
			};
		}

		public override void Apply(GameObject gameObject, IResourceResolver resolver)
		{
			switch (SpriteType)
			{
			case SpriteType.Mesh:
				Apply(gameObject.AddOrGetComponent<MeshSprite>(), resolver);
				break;
			case SpriteType.Material:
				Apply(gameObject.AddOrGetComponent<MaterialSprite>(), resolver);
				break;
			case SpriteType.UI:
				Apply(gameObject.AddOrGetComponent<UISprite>(), resolver);
				break;
			default:
				Apply(gameObject.AddOrGetComponent<MaterialSprite>(), resolver);
				break;
			}
		}

		public SpriteBase Apply(SpriteBase sprite, IResourceResolver resolver)
		{
			return SpriteType switch
			{
				SpriteType.Mesh => Apply((MeshSprite)sprite, resolver), 
				SpriteType.Material => Apply((MaterialSprite)sprite, resolver), 
				SpriteType.UI => Apply((UISprite)sprite, resolver), 
				_ => Apply((MaterialSprite)sprite, resolver), 
			};
		}

		public MeshSprite Apply(MeshSprite sprite, IResourceResolver resolver)
		{
			sprite.SpriteName = Name;
			return sprite;
		}

		public MaterialSprite Apply(MaterialSprite sprite, IResourceResolver resolver)
		{
			sprite.SpriteName = Name;
			return sprite;
		}

		public UISprite Apply(UISprite sprite, IResourceResolver resolver)
		{
			sprite.SpriteName = Name;
			return sprite;
		}
	}
}
