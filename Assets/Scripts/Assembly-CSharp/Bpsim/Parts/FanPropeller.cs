using Unity.Entities;

namespace Bpsim.Parts
{
	public struct FanPropeller : IComponentData, IQueryTypeParameter
	{
		public bool Enabled;

		public float Angle;

		public float AngularSpeed;

		public float TargetAngularSpeed;
	}
}
