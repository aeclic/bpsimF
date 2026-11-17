using Unity.Entities;
using UnityEngine;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>BPLE's cached 2D bounds/material, retained when authoring GameObjects become entities.</summary>
	internal struct BpleLightBodyBounds : IComponentData
	{
		public BpleLightBounds Bounds;
		public float Friction, Bounciness;
		public int FrictionMode, BounceMode;

		public static BpleLightBodyBounds Create(GameObject root)
		{
			var result = new BpleLightBodyBounds();
			float minX = 0, minY = 0, maxX = 0, maxY = 0;
			foreach (var collider in root.GetComponentsInChildren<UnityEngine.Collider>())
			{
				var material = collider.sharedMaterial;
				float friction = material != null ? material.dynamicFriction : .6f;
				float bounce = material != null ? material.bounciness : 0;
				int fm = material != null ? (int)material.frictionCombine : 0;
				int bm = material != null ? (int)material.bounceCombine : 0;
				if (fm > result.FrictionMode || (fm == result.FrictionMode && friction > result.Friction))
				{ result.FrictionMode = fm; result.Friction = friction; }
				if (bm > result.BounceMode || (bm == result.BounceMode && bounce > result.Bounciness))
				{ result.BounceMode = bm; result.Bounciness = bounce; }
				if (!collider.enabled || collider.isTrigger) continue;
				Vector2 center, half;
				if (collider is UnityEngine.BoxCollider box) { center = box.center; half = (Vector2)box.size * .5f; }
				else if (collider is UnityEngine.CapsuleCollider capsule) { center = capsule.center; half = new Vector2(capsule.radius, capsule.height * .5f); }
				else if (collider is UnityEngine.SphereCollider sphere)
				{ center = sphere.center; half = Vector2.one * sphere.radius; result.Bounds.Type = 1; }
				else continue;
				Transform node = collider.transform;
				while (node != null && node != root.transform)
				{
					Vector3 right = node.localRotation * Vector3.right;
					center = (Vector2)node.localPosition + new Vector2(right.x * center.x - right.y * center.y, right.x * center.y + right.y * center.x);
					node = node.parent;
				}
				minX = Mathf.Min(minX, center.x - half.x); maxX = Mathf.Max(maxX, center.x + half.x);
				minY = Mathf.Min(minY, center.y - half.y); maxY = Mathf.Max(maxY, center.y + half.y);
			}
			result.Bounds.X = (minX + maxX) * .5f;
			result.Bounds.Y = (minY + maxY) * .5f;
			result.Bounds.A = (maxX - minX) * .5f;
			result.Bounds.B = (maxY - minY) * .5f;
			result.Bounds.R = Mathf.Sqrt(result.Bounds.A * result.Bounds.A + result.Bounds.B * result.Bounds.B);
			if (result.Bounds.Type == 1)
			{ result.Bounds.R = Mathf.Max(result.Bounds.A, result.Bounds.B); result.Bounds.A = result.Bounds.B = result.Bounds.R; }
			return result;
		}

		public float CombineFriction(float value, bool minimum) => Combine(Mathf.Max(FrictionMode, minimum ? (int)PhysicMaterialCombine.Minimum : 0), Friction, value);
		public float CombineBounce(float value) => Combine(BounceMode, Bounciness, value);
		private static float Combine(int mode, float a, float b)
		{
			switch ((PhysicMaterialCombine)mode)
			{
			case PhysicMaterialCombine.Minimum: return Mathf.Min(a, b);
			case PhysicMaterialCombine.Maximum: return Mathf.Max(a, b);
			case PhysicMaterialCombine.Multiply: return a * b;
			default: return (a + b) * .5f;
			}
		}
	}
}
