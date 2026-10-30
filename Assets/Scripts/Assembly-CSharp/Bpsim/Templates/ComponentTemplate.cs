using System;
using System.Text.Json.Serialization;
using Bpsim.Parts;
using Bpsim.Rendering;
using Newtonsoft.Json;
using UnityEngine;

namespace Bpsim.Templates
{
	[Newtonsoft.Json.JsonConverter(typeof(ComponentConverterLegacy))]
	[System.Text.Json.Serialization.JsonConverter(typeof(ComponentConverter))]
	public abstract class ComponentTemplate : ITemplate
	{
		public abstract ComponentType Type { get; }

		public abstract void Apply(GameObject gameObject, IResourceResolver resolver);

		public static bool IsSupported(Component component)
		{
			if (!(component is Transform))
			{
				if (!(component is Collider))
				{
					if (!(component is Renderer))
					{
						if (!(component is Rigidbody))
						{
							if (!(component is SpriteBase))
							{
								if (component is ManagedPart)
								{
									return true;
								}
								return false;
							}
							return true;
						}
						return true;
					}
					return true;
				}
				return true;
			}
			return true;
		}

		public static Type Resolve(ComponentType type)
		{
			return type switch
			{
				ComponentType.Transform => typeof(TransformTemplate), 
				ComponentType.Collider => typeof(ColliderTemplate), 
				ComponentType.Renderer => typeof(RendererTemplate), 
				ComponentType.Sprite => typeof(SpriteTemplate), 
				ComponentType.Part => typeof(PartTemplate), 
				_ => throw new InvalidOperationException(), 
			};
		}

		public static ComponentTemplate Create(ComponentType type)
		{
			return type switch
			{
				ComponentType.Transform => new TransformTemplate(), 
				ComponentType.Collider => new ColliderTemplate(), 
				ComponentType.Renderer => new RendererTemplate(), 
				ComponentType.Sprite => new SpriteTemplate(), 
				ComponentType.Part => new PartTemplate(), 
				_ => throw new InvalidOperationException(), 
			};
		}

		public static ComponentTemplate Create(Component component)
		{
			if (!(component is Transform transform))
			{
				if (!(component is Collider collider))
				{
					if (!(component is Renderer renderer))
					{
						if (!(component is Rigidbody rigidbody))
						{
							if (!(component is SpriteBase sprite))
							{
								if (component is ManagedPart part)
								{
									return PartTemplate.Create(part);
								}
								throw new InvalidOperationException();
							}
							return SpriteTemplate.Create(sprite);
						}
						return RigidbodyTemplate.Create(rigidbody);
					}
					return RendererTemplate.Create(renderer);
				}
				return ColliderTemplate.Create(collider);
			}
			return TransformTemplate.Create(transform);
		}
	}
}
