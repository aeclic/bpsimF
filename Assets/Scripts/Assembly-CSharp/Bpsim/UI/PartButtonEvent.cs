using Unity.Entities;

namespace Bpsim.UI
{
	public readonly struct PartButtonEvent
	{
		public readonly Entity Part;

		public readonly PartButtonType PartButtonType;

		public readonly float SliderValue;

		private PartButtonEvent(Entity part, PartButtonType partButtonType, float sliderValue)
		{
			Part = part;
			PartButtonType = partButtonType;
			SliderValue = sliderValue;
		}

		public static PartButtonEvent CreateTriggerEvent(Entity part)
		{
			return new PartButtonEvent(part, PartButtonType.Trigger, 0f);
		}

		public static PartButtonEvent CreateSliderEvent(Entity part, float value)
		{
			return new PartButtonEvent(part, PartButtonType.Slider, value);
		}
	}
}
