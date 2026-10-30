using System;
using System.Collections.Generic;
using Bpsim.Parts;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	public class PartButton : MonoBehaviour
	{
		protected RawImage m_partTexture;

		protected Text m_indexText;

		protected PartButtonList m_buttonList;

		protected PartButtonInfo m_info;

		protected PartType m_sortedPartType;

		protected List<Entity> m_parts;

		protected Vector2 m_averagePosition;

		protected Color m_disabledColor;

		protected Color m_enabledColor;

		public PartButtonInfo Info => m_info;

		public List<Entity> Parts => m_parts;

		public PartType SortedPartType => m_sortedPartType;

		public Vector2 AveragePosition => m_averagePosition;

		public virtual int SubButtonCount => 0;

		public virtual IEnumerable<PartButton> SubButtons { get; }

		protected virtual void Awake()
		{
			m_buttonList = PartButtonList.Instance;
			m_partTexture = base.transform.Find("PartTexture").GetComponent<RawImage>();
			m_indexText = base.transform.Find("Index").GetComponent<Text>();
			m_parts = new List<Entity>();
		}

		public void SetInfo(PartButtonInfo info)
		{
			m_info = info;
			m_sortedPartType = info.PartType;
		}

		public void SetSprite(bool enabled, Texture texture, Rect uvRect, Vector2 scale, Quaternion rotation)
		{
			RectTransform obj = (RectTransform)m_partTexture.transform;
			obj.sizeDelta = scale;
			obj.rotation = rotation;
			m_partTexture.enabled = enabled;
			m_partTexture.texture = texture;
			m_partTexture.uvRect = uvRect;
		}

		public void DisplayIndexText(string text)
		{
			m_indexText.text = text;
		}

		public virtual void Initialize()
		{
			float num = (float)m_info.ComponentRank / (float)(PartButtonList.Settings.MaxSeparationCount + 1);
			float num2 = 210f + num * 60f;
			float num3 = 2f - Math.Abs(num2 - 240f) / 60f;
			float num4 = 0.8f * (1.5f / num3);
			m_disabledColor = Color.HSVToRGB(num2 / 360f, num4, 0.6f);
			m_disabledColor.a = 0.7f;
			m_enabledColor = Color.HSVToRGB(num2 / 360f, 0.5f * num4, 0.75f);
			m_enabledColor.a = 0.7f;
			CalculateAveragePosition();
		}

		private void CalculateAveragePosition()
		{
			int count = m_parts.Count;
			if (count == 0)
			{
				m_averagePosition = Vector2.zero;
				return;
			}
			int num = 0;
			int num2 = 0;
			ComponentLookup<PartTransform> componentLookup = PartManager.Instance.System.GetComponentLookup<PartTransform>();
			foreach (Entity part in m_parts)
			{
				RefRO<PartTransform> refRO = componentLookup.GetRefRO(part);
				num += refRO.ValueRO.X;
				num2 += refRO.ValueRO.Y;
			}
			m_averagePosition = new Vector2((float)num / (float)count, (float)num2 / (float)count);
		}

		public virtual void Reset()
		{
			m_info = default(PartButtonInfo);
			m_sortedPartType = PartType.Unknown;
			m_averagePosition = Vector2.zero;
			m_parts.Clear();
			m_partTexture.texture = null;
			m_partTexture.uvRect = Rect.zero;
			m_partTexture.enabled = false;
		}
	}
}
