using Bpsim.Serialization;
using Newtonsoft.Json;
using UnityEngine;

namespace Bpsim.Templates
{
	[JsonConverter(typeof(JsonAliasConverterLegacy<RigidbodyTemplate>))]
	public class RigidbodyTemplate : ComponentTemplate, ITemplate<Rigidbody>, ITemplate
	{
		public override ComponentType Type => ComponentType.Rigidbody;

		[JsonAlias("质量")]
		public float Mass { get; set; }

		[JsonAlias("阻力系数")]
		public float Drag { get; set; }

		[JsonAlias("角阻力系数")]
		public float AngularDrag { get; set; }

		[JsonAlias("启用重力")]
		public bool UseGravity { get; set; }

		[JsonAlias("关闭动力学模拟")]
		public bool IsKinematic { get; set; }

		[JsonAlias("插值")]
		public RigidbodyInterpolation Interpolation { get; set; }

		[JsonAlias("碰撞检测模式")]
		public CollisionDetectionMode CollisionDetectionMode { get; set; }

		[JsonAlias("约束")]
		public RigidbodyConstraints Constraints { get; set; }

		public static RigidbodyTemplate Create(Rigidbody rigidbody)
		{
			return new RigidbodyTemplate
			{
				Mass = rigidbody.mass,
				Drag = rigidbody.drag,
				AngularDrag = rigidbody.angularDrag,
				UseGravity = rigidbody.useGravity,
				IsKinematic = rigidbody.isKinematic,
				Interpolation = rigidbody.interpolation,
				CollisionDetectionMode = rigidbody.collisionDetectionMode,
				Constraints = rigidbody.constraints
			};
		}

		public override void Apply(GameObject gameObject, IResourceResolver resolver)
		{
			Apply(gameObject.AddOrGetComponent<Rigidbody>(), resolver);
		}

		public Rigidbody Apply(Rigidbody rigidbody, IResourceResolver resolver)
		{
			rigidbody.mass = Mass;
			rigidbody.drag = Drag;
			rigidbody.angularDrag = AngularDrag;
			rigidbody.useGravity = UseGravity;
			rigidbody.isKinematic = IsKinematic;
			rigidbody.interpolation = Interpolation;
			rigidbody.collisionDetectionMode = CollisionDetectionMode;
			rigidbody.constraints = Constraints;
			return rigidbody;
		}
	}
}
