using Bpsim.Serialization;
using Newtonsoft.Json;
using UnityEngine;

namespace Bpsim.Templates
{
	[JsonConverter(typeof(JsonAliasConverterLegacy<RendererTemplate>))]
	public class RendererTemplate : ComponentTemplate, ITemplate<Renderer>, ITemplate, ITemplate<MeshRenderer>
	{
		public override ComponentType Type => ComponentType.Renderer;

		[JsonAlias("是否启用")]
		public bool Enabled { get; set; }

		[JsonAlias("材质")]
		public MaterialTemplate Material { get; set; }

		public static RendererTemplate Create(Renderer renderer)
		{
			return new RendererTemplate
			{
				Enabled = renderer.enabled,
				Material = MaterialTemplate.Create(renderer.material)
			};
		}

		public override void Apply(GameObject gameObject, IResourceResolver resolver)
		{
			MeshRenderer meshRenderer = gameObject.AddOrGetComponent<MeshRenderer>();
			MeshFilter meshFilter = gameObject.AddOrGetComponent<MeshFilter>();
			Apply(meshRenderer, resolver);
			meshFilter.mesh = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
		}

		public Renderer Apply(Renderer renderer, IResourceResolver resolver)
		{
			return Apply((MeshRenderer)renderer, resolver);
		}

		public MeshRenderer Apply(MeshRenderer meshRenderer, IResourceResolver resolver)
		{
			meshRenderer.enabled = Enabled;
			meshRenderer.material = Material.Apply(meshRenderer.material, meshRenderer, resolver);
			return meshRenderer;
		}
	}
}
