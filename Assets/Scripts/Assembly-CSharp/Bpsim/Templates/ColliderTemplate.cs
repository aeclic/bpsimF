using System;
using Bpsim.Serialization;
using Newtonsoft.Json;
using UnityEngine;

namespace Bpsim.Templates
{
	[JsonConverter(typeof(JsonAliasConverterLegacy<ColliderTemplate>))]
	public class ColliderTemplate : ComponentTemplate, ITemplate<Collider>, ITemplate, ITemplate<BoxCollider>, ITemplate<CapsuleCollider>, ITemplate<SphereCollider>
	{
		public override ComponentType Type => ComponentType.Collider;

		[JsonAlias("碰撞器类型")]
		public ColliderType ColliderType { get; set; }

		[JsonAlias("是否启用")]
		public bool Enabled { get; set; }

		[JsonAlias("中心")]
		public Vector3 Center { get; set; }

		[JsonAlias("大小")]
		public Vector3 Size { get; set; }

		[JsonAlias("半径")]
		public float Radius { get; set; }

		[JsonAlias("高度")]
		public float Height { get; set; }

		[JsonAlias("弹性系数")]
		public float Bounciness { get; set; }

		[JsonAlias("动摩擦系数")]
		public float DynamicFriction { get; set; }

		[JsonAlias("静摩擦系数")]
		public float StaticFriction { get; set; }

		[JsonAlias("弹性组合模式")]
		public PhysicMaterialCombine BounceCombine { get; set; }

		[JsonAlias("摩擦组合模式")]
		public PhysicMaterialCombine FrictionCombine { get; set; }

		public static ColliderTemplate Create(Collider collider)
		{
			ColliderTemplate colliderTemplate;
			if (collider is BoxCollider boxCollider)
			{
				colliderTemplate = new ColliderTemplate
				{
					ColliderType = ColliderType.Box,
					Center = boxCollider.center,
					Size = boxCollider.size
				};
			}
			else if (collider is SphereCollider sphereCollider)
			{
				colliderTemplate = new ColliderTemplate
				{
					ColliderType = ColliderType.Sphere,
					Center = sphereCollider.center,
					Radius = sphereCollider.radius
				};
			}
			else
			{
				if (!(collider is CapsuleCollider capsuleCollider))
				{
					throw new InvalidOperationException();
				}
				colliderTemplate = new ColliderTemplate
				{
					ColliderType = ColliderType.Capsule,
					Center = capsuleCollider.center,
					Radius = capsuleCollider.radius,
					Height = capsuleCollider.height
				};
			}
			colliderTemplate.Enabled = collider.enabled;
			colliderTemplate.Bounciness = collider.material.bounciness;
			colliderTemplate.DynamicFriction = collider.material.dynamicFriction;
			colliderTemplate.StaticFriction = collider.material.staticFriction;
			colliderTemplate.BounceCombine = collider.material.bounceCombine;
			colliderTemplate.FrictionCombine = collider.material.frictionCombine;
			return colliderTemplate;
		}

		public override void Apply(GameObject gameObject, IResourceResolver resolver)
		{
			switch (ColliderType)
			{
			case ColliderType.Box:
				Apply(gameObject.AddOrGetComponent<BoxCollider>(), resolver);
				break;
			case ColliderType.Sphere:
				Apply(gameObject.AddOrGetComponent<SphereCollider>(), resolver);
				break;
			case ColliderType.Capsule:
				Apply(gameObject.AddOrGetComponent<CapsuleCollider>(), resolver);
				break;
			default:
				throw new InvalidOperationException();
			}
		}

		public Collider Apply(Collider collider, IResourceResolver resolver)
		{
			return ColliderType switch
			{
				ColliderType.Box => Apply((BoxCollider)collider, resolver), 
				ColliderType.Sphere => Apply((SphereCollider)collider, resolver), 
				ColliderType.Capsule => Apply((CapsuleCollider)collider, resolver), 
				_ => throw new InvalidOperationException(), 
			};
		}

		public BoxCollider Apply(BoxCollider boxCollider, IResourceResolver resolver)
		{
			boxCollider.enabled = Enabled;
			boxCollider.center = Center;
			boxCollider.size = Size;
			ApplyMaterial(boxCollider);
			return boxCollider;
		}

		public CapsuleCollider Apply(CapsuleCollider capsuleCollider, IResourceResolver resolver)
		{
			capsuleCollider.enabled = Enabled;
			capsuleCollider.center = Center;
			capsuleCollider.radius = Radius;
			capsuleCollider.height = Height;
			ApplyMaterial(capsuleCollider);
			return capsuleCollider;
		}

		public SphereCollider Apply(SphereCollider sphereCollider, IResourceResolver resolver)
		{
			sphereCollider.enabled = Enabled;
			sphereCollider.center = Center;
			sphereCollider.radius = Radius;
			ApplyMaterial(sphereCollider);
			return sphereCollider;
		}

		private void ApplyMaterial(Collider collider)
		{
			collider.material.bounciness = Bounciness;
			collider.material.dynamicFriction = DynamicFriction;
			collider.material.staticFriction = StaticFriction;
			collider.material.bounceCombine = BounceCombine;
			collider.material.frictionCombine = FrictionCombine;
		}
	}
}
