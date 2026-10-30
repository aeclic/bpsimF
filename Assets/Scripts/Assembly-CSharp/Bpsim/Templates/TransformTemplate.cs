using Bpsim.Serialization;
using Newtonsoft.Json;
using UnityEngine;

namespace Bpsim.Templates
{
	[JsonConverter(typeof(JsonAliasConverterLegacy<TransformTemplate>))]
	public class TransformTemplate : ComponentTemplate, ITemplate<Transform>, ITemplate
	{
		public override ComponentType Type => ComponentType.Transform;

		[JsonAlias("位置")]
		public Vector3 LocalPosition { get; set; }

		[JsonAlias("旋转")]
		public Quaternion LocalRotation { get; set; }

		[JsonAlias("缩放")]
		public Vector3 LocalScale { get; set; }

		public TransformTemplate()
		{
			LocalPosition = Vector3.zero;
			LocalRotation = Quaternion.identity;
			LocalScale = Vector3.one;
		}

		public static TransformTemplate Create(Transform transform)
		{
			return new TransformTemplate
			{
				LocalPosition = transform.localPosition,
				LocalRotation = transform.localRotation,
				LocalScale = transform.localScale
			};
		}

		public override void Apply(GameObject gameObject, IResourceResolver resolver)
		{
			Apply(gameObject.transform, resolver);
		}

		public Transform Apply(Transform transform, IResourceResolver resolver)
		{
			transform.localPosition = LocalPosition;
			transform.localRotation = LocalRotation;
			transform.localScale = LocalScale;
			return transform;
		}
	}
}
