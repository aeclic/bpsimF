using Bpsim.ComponentModel;
using UnityEngine;

namespace Bpsim.Parts
{
	public class PartDrawingSettings : ObservableObject
	{
		private Vector2 m_brushSize;

		private PartDrawer.Shape m_brushShape;

		private bool m_overlay;

		private PartType m_partType;

		private int m_partIndex;

		public Vector2 BrushSize
		{
			get
			{
				return m_brushSize;
			}
			set
			{
				if (value.x >= 0f && value.y >= 0f)
				{
					SetProperty(ref m_brushSize, value, "BrushSize");
				}
			}
		}

		public PartDrawer.Shape BrushShape
		{
			get
			{
				return m_brushShape;
			}
			set
			{
				SetProperty(ref m_brushShape, value, "BrushShape");
			}
		}

		public bool Overlay
		{
			get
			{
				return m_overlay;
			}
			set
			{
				SetProperty(ref m_overlay, value, "Overlay");
			}
		}

		public PartType PartType
		{
			get
			{
				return m_partType;
			}
			set
			{
				SetProperty(ref m_partType, value, "PartType");
			}
		}

		public int PartIndex
		{
			get
			{
				return m_partIndex;
			}
			set
			{
				SetProperty(ref m_partIndex, value, "PartIndex");
			}
		}

		public PartDrawingSettings()
		{
			Reset();
		}

		public void Reset()
		{
			BrushSize = new Vector2(1f, 1f);
			BrushShape = PartDrawer.Shape.Circle;
			Overlay = false;
			PartType = PartType.All;
			PartIndex = -1;
		}
	}
}
