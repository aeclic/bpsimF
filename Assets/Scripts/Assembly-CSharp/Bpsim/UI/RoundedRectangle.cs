using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class RoundedRectangle : MonoBehaviour
	{
		private bool m_dirty;

		[SerializeField]
		private float m_width;

		[SerializeField]
		private float m_height;

		[SerializeField]
		private float m_radius;

		private static readonly int m_widthID;

		private static readonly int m_heightID;

		private static readonly int m_radiusID;

		public float Width
		{
			get
			{
				return m_width;
			}
			set
			{
				if (m_width != value)
				{
					m_width = value;
					SetDirty();
				}
			}
		}

		public float Height
		{
			get
			{
				return m_height;
			}
			set
			{
				if (m_height != value)
				{
					m_height = value;
					SetDirty();
				}
			}
		}

		public float Radius
		{
			get
			{
				return m_radius;
			}
			set
			{
				if (m_radius != value)
				{
					m_radius = value;
					SetDirty();
				}
			}
		}

		static RoundedRectangle()
		{
			m_widthID = Shader.PropertyToID("_Width");
			m_heightID = Shader.PropertyToID("_Height");
			m_radiusID = Shader.PropertyToID("_Radius");
		}

		public void SetDirty()
		{
			m_dirty = true;
		}

		private void OnEnable()
		{
			m_dirty = true;
		}

		private void Update()
		{
			if (m_dirty)
			{
				m_dirty = false;
				UpdateMaterial();
			}
		}

		private void UpdateMaterial()
		{
			RectTransform component = GetComponent<RectTransform>();
			Graphic component2 = GetComponent<Graphic>();
			Material material = null;
			if (component2 is RawImage rawImage)
			{
				material = rawImage.material;
				if (material == null || material.name.StartsWith("Default UI Material"))
				{
					material = (rawImage.material = new Material(Shader.Find("Bpsim/RoundedRectangle")));
				}
			}
			else if (component2 is Image image)
			{
				material = image.material;
				if (material == null || material.name.StartsWith("Default UI Material"))
				{
					material = (image.material = new Material(Shader.Find("Bpsim/RoundedRectangle")));
				}
			}
			if (material != null)
			{
				material.SetFloat(m_widthID, (m_width > 0f) ? m_width : component.rect.width);
				material.SetFloat(m_heightID, (m_height > 0f) ? m_height : component.rect.height);
				material.SetFloat(m_radiusID, m_radius);
			}
		}
	}
}
