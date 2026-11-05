using EPOOutline;
using UnityEngine;

namespace Bpsim.Factions
{
	public static class EpoFactionStyle
	{
		public static void Apply(Outliner outliner,float width)
		{
			outliner.RenderStage = RenderStage.AfterTransparents;
			outliner.PrimaryBufferSizeMode = BufferSizeMode.Native;
			outliner.DilateQuality = DilateQuality.High;
			outliner.DilateIterations = Mathf.CeilToInt(width);
			outliner.DilateShift = width/outliner.DilateIterations;
			outliner.BlurType = BlurType.Gaussian5x5;
			outliner.BlurIterations = 1; outliner.BlurShift = .65f;
			outliner.OutlineLayerMask = 1L << EpoFactionMask.OutlineLayer;
		}
	}
}
