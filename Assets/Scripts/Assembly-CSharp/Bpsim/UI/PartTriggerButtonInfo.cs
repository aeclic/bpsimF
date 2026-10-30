using Bpsim.Parts;

namespace Bpsim.UI
{
	public struct PartTriggerButtonInfo
	{
		public PartButtonInfo Value;

		public bool Consistent;

		public bool Multiple;

		public PartTriggerButtonInfo(PartButtonInfo value, bool consistent = false, bool multiple = true)
		{
			Value = value;
			Consistent = consistent;
			Multiple = multiple;
		}

		public PartTriggerButtonInfo(PartButtonType buttonType, int buttonIndex, PartType partType, int partIndex, int componentIndex, bool consistent = false, bool multiple = true)
		{
			Value = new PartButtonInfo(buttonType, buttonIndex, partType, partIndex, componentIndex);
			Consistent = consistent;
			Multiple = multiple;
		}
	}
}
