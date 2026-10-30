using Bpsim.Parts;

namespace Bpsim.UI
{
	public struct PartSliderButtonInfo
	{
		public PartButtonInfo Value;

		public PartSliderButton.Range Range;

		public PartSliderButtonInfo(PartButtonInfo value, PartSliderButton.Range range)
		{
			Value = value;
			Range = range;
		}

		public PartSliderButtonInfo(PartButtonType buttonType, int buttonIndex, PartType partType, int partIndex, int componentIndex, PartSliderButton.Range range)
		{
			Value = new PartButtonInfo(buttonType, buttonIndex, partType, partIndex, componentIndex);
			Range = range;
		}
	}
}
