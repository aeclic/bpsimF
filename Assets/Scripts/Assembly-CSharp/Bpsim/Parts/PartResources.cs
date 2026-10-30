using System;
using System.Collections.Generic;
using UnityEngine;

namespace Bpsim.Parts
{
	[Serializable]
	[CreateAssetMenu(fileName = "PartResources", menuName = "ScriptableObjects/PartResources", order = 1)]
	public class PartResources : ScriptableObject
	{
		[Serializable]
		public class PartRange
		{
			[SerializeField]
			private PartType m_partType;

			[SerializeField]
			private int m_partIndex;

			[SerializeField]
			private int m_startIndex;

			[SerializeField]
			private int m_endIndex;

			[SerializeField]
			private string m_nameFormat;

			public PartType PartType => m_partType;

			public int PartIndex => m_partIndex;

			public int StartIndex => m_startIndex;

			public int EndIndex => m_endIndex;

			public string NameFormat => m_nameFormat;
		}

		[SerializeField]
		private List<PartRange> m_ranges;

		[SerializeField]
		private List<GameObject> m_partGroups;

		[SerializeField]
		private List<TextAsset> m_templateGroups;

		[SerializeField]
		private TextAsset m_extensionMap;

		public List<PartRange> Ranges => m_ranges;

		public List<GameObject> PartGroups => m_partGroups;

		public List<TextAsset> TemplateGroups => m_templateGroups;

		public TextAsset ExtensionMap => m_extensionMap;
	}
}
