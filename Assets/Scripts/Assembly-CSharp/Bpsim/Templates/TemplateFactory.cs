using Bpsim.Parts;
using Bpsim.Rendering;
using UnityEngine;

namespace Bpsim.Templates
{
	public static class TemplateFactory
	{
		public static GameObjectTemplate GameObject(GameObject gameObject)
		{
			return GameObjectTemplate.Create(gameObject);
		}

		public static TransformTemplate Transform(Transform transform)
		{
			return TransformTemplate.Create(transform);
		}

		public static ColliderTemplate Collider(Collider collider)
		{
			return ColliderTemplate.Create(collider);
		}

		public static RendererTemplate Renderer(Renderer renderer)
		{
			return RendererTemplate.Create(renderer);
		}

		public static RigidbodyTemplate Rigidbody(Rigidbody rigidbody)
		{
			return RigidbodyTemplate.Create(rigidbody);
		}

		public static SpriteTemplate Sprite(SpriteBase sprite)
		{
			return SpriteTemplate.Create(sprite);
		}

		public static PartTemplate Part(ManagedPart part)
		{
			return PartTemplate.Create(part);
		}

		public static MaterialTemplate Material(Material material)
		{
			return MaterialTemplate.Create(material);
		}
	}
}
