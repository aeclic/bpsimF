using Bpsim.Serialization;
using Newtonsoft.Json;
using UnityEngine;

namespace Bpsim.Templates
{
	[JsonConverter(typeof(JsonAliasConverterLegacy<MaterialTemplate>))]
	public class MaterialTemplate : ITemplate<Material>, ITemplate
	{
		[JsonAlias("名称")]
		public string Name { get; set; }

		[JsonAlias("着色器")]
		public string Shader { get; set; }

		[JsonAlias("贴图")]
		public string Texture { get; set; }

		[JsonAlias("颜色")]
		public HexColor Color { get; set; }

		public MaterialTemplate()
		{
			Color = HexColor.White;
		}

		public static MaterialTemplate Create(Material material)
		{
			return new MaterialTemplate
			{
				Name = material.name,
				Shader = material.shader.name,
				Texture = material.mainTexture?.name,
				Color = (HexColor)material.color
			};
		}

		public Material Apply(Material material, IResourceResolver resolver)
		{
			return Apply(material, null, resolver);
		}

		public Material Apply(Material material, Renderer renderer, IResourceResolver resolver)
		{
			bool num = !string.IsNullOrEmpty(Name);
			bool flag = !string.IsNullOrEmpty(Shader);
			bool flag2 = !string.IsNullOrEmpty(Texture);
			bool flag3 = Color != HexColor.White;
			if (num)
			{
				Material material2 = resolver.ResolveMaterial(Name);
				if (!flag && !flag2 && !flag3)
				{
					return material2;
				}
				if (!flag && !flag2 && material2.enableInstancing && renderer != null)
				{
					MaterialPropertyBlock materialPropertyBlock = new MaterialPropertyBlock();
					materialPropertyBlock.SetColor("_Color", (Color)Color);
					renderer.SetPropertyBlock(materialPropertyBlock);
					return material2;
				}
				material = new Material(material2);
				if (flag)
				{
					material.shader = resolver.ResolveShader(Shader);
				}
				if (flag2)
				{
					material.mainTexture = resolver.ResolveTexture(Texture);
				}
				if (flag3)
				{
					material.color = (Color)Color;
				}
				return material;
			}
			if (!flag && material == null)
			{
				throw new TemplateException("Cannot resolve material.");
			}
			if (flag)
			{
				if (material == null)
				{
					material = new Material(resolver.ResolveShader(Shader));
				}
				else
				{
					material.shader = resolver.ResolveShader(Shader);
				}
			}
			if (flag2)
			{
				material.mainTexture = resolver.ResolveTexture(Texture);
			}
			if (flag3)
			{
				material.color = (Color)Color;
			}
			return material;
		}
	}
}
