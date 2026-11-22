using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	internal struct HingePlate : IComponentData
	{
		public Entity Left;
		public Entity Right;

		public const float Friction = 0.7f;
		public const float HalfMass = 1f;
		public const float HalfOffset = 0.25f;

		public static readonly int2[] Neighbours =
		{
			new int2(1, 0), new int2(1, 1), new int2(0, 1), new int2(-1, 1),
			new int2(-1, 0), new int2(-1, -1), new int2(0, -1), new int2(1, -1)
		};

		public Entity Half(bool right) => right ? Right : Left;

		public static int NormalizeLayout(int rotation) => (rotation % 6 + 6) % 6;

		public static int QuarterTurns(int layout) => layout < 2 ? layout : layout - 2;

		public static float Height(int layout) => layout < 2 ? 0f : -0.5f;

		public static bool IsTrack(PartTypeInfo type) => type.PartIndex == 5 || type.PartIndex == 7;

		public static bool TryConnect(PartTypeInfo source, int layout, PartTypeInfo target, int targetLayout,
			int2 offset, bool right, out bool targetRight, out int angle)
		{
			layout = NormalizeLayout(layout);
			int2 local = QuarterTurns(layout) switch
			{
				1 => new int2(offset.y, -offset.x),
				2 => -offset,
				3 => new int2(-offset.y, offset.x),
				_ => offset
			};
			bool atEnd = local.x == (right ? 1 : -1) && local.y == 0;
			targetRight = !right;
			angle = 0;
			if (!target.BelongsTo(BasePart.HingePlates))
				return !IsTrack(source) && atEnd && (target.PartType == PartType.WoodenFrame ||
					target.PartType == PartType.MetalFrame || target.PartType == PartType.Kicker);

			targetLayout = NormalizeLayout(targetLayout);
			if (layout < 2) return atEnd && layout == targetLayout;

			int phase = (targetLayout - layout + 4) % 4;
			(bool connected, bool half, int limitCenter) = (right, local.x, local.y, phase) switch
			{
				(false, -1,  0, 0) => (true, true, 0),
				(false, -1,  0, 1) => (true, false, -90),
				(false, -1, -1, 1) => (true, true, 90),
				(false, -1, -1, 2) => (true, false, 0),
				(false,  0, -1, 3) => (true, false, 90),
				(true,   0, -1, 1) => (true, true, -90),
				(true,   1, -1, 2) => (true, true, 0),
				(true,   1, -1, 3) => (true, false, -90),
				(true,   1,  0, 0) => (true, false, 0),
				(true,   1,  0, 3) => (true, true, 90),
				_ => (false, false, 0)
			};
			targetRight = half;
			angle = limitCenter;
			return connected;
		}
	}
}
