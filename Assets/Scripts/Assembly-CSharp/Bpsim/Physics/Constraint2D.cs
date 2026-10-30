using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace Bpsim.Physics
{
	public struct Constraint2D : IComponentData, IQueryTypeParameter
	{
		public float FixedPosition;

		public float2 FixedAngle;

		public static Constraint2D CreateFromTransform(LocalTransform transform)
		{
			return new Constraint2D
			{
				FixedPosition = transform.Position.z,
				FixedAngle = transform.Rotation.ToEulerAngles().yz
			};
		}

		public static Constraint2D CreateFromTransform(WorldTransform transform)
		{
			return new Constraint2D
			{
				FixedPosition = transform.Position.z,
				FixedAngle = transform.Rotation.ToEulerAngles().yz
			};
		}
	}
}
