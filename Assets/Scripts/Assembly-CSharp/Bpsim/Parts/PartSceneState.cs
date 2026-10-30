using UnityEngine;

namespace Bpsim.Parts
{
	public class PartSceneState
	{
		public float LastSelectedTime { get; set; }

		public Vector2 CameraPosition { get; set; }

		public float CameraSize { get; set; }

		public bool HasSelection
		{
			get
			{
				if (Selection.width > 0)
				{
					return Selection.height > 0;
				}
				return false;
			}
		}

		public RectInt Selection { get; set; }
	}
}
