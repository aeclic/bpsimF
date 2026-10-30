namespace Bpsim.Parts
{
	public struct PartExtensionPair
	{
		public PartRangeInfo.Writable Key;

		public PartExtensionData Value;

		public PartExtensionPair(PartRangeInfo.Writable key, PartExtensionData value)
		{
			Key = key;
			Value = value;
		}
	}
}
