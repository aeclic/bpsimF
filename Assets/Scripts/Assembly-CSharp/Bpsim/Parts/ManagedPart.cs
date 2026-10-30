using UnityEngine;

namespace Bpsim.Parts
{
	public class ManagedPart : MonoBehaviour
	{
		[SerializeField]
		protected PartType m_partType;

		[SerializeField]
		protected PartTier m_partTier;

		[SerializeField]
		protected int m_partIndex;

		public PartType PartType
		{
			get
			{
				return m_partType;
			}
			set
			{
				m_partType = value;
			}
		}

		public PartTier PartTier
		{
			get
			{
				return m_partTier;
			}
			set
			{
				m_partTier = value;
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
				m_partIndex = value;
			}
		}

		public PartTypeInfo TypeInfo
		{
			get
			{
				return new PartTypeInfo(m_partType, m_partIndex);
			}
			set
			{
				PartTypeInfo partTypeInfo = value;
				partTypeInfo.Deconstruct(out m_partType, out m_partIndex);
			}
		}
	}
}
