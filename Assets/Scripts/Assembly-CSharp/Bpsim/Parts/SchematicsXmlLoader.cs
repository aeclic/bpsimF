using System.IO;
using System.Xml.Serialization;

namespace Bpsim.Parts
{
	internal class SchematicsXmlLoader : ISchematicsLoader
	{
		protected XmlSerializer m_serializer;

		public static SchematicsXmlLoader Default { get; } = new SchematicsXmlLoader();

		public SchematicsXmlLoader()
		{
			m_serializer = new XmlSerializer(typeof(Schematics), CreateOverrides());
		}

		private static XmlAttributeOverrides CreateOverrides()
		{
			XmlAttributes xmlAttributes = new XmlAttributes();
			xmlAttributes.XmlRoot = new XmlRootAttribute("ContraptionDataset");
			XmlAttributes xmlAttributes2 = new XmlAttributes();
			xmlAttributes2.XmlArray = new XmlArrayAttribute("ContraptionDatasetList");
			xmlAttributes2.XmlArrayItems.Add(new XmlArrayItemAttribute("ContraptionDatasetUnit"));
			XmlAttributeOverrides xmlAttributeOverrides = new XmlAttributeOverrides();
			xmlAttributeOverrides.Add(typeof(Schematics), xmlAttributes);
			xmlAttributeOverrides.Add(typeof(Schematics), "Units", xmlAttributes2);
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "X", CreateMemberAttributes("x"));
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "Y", CreateMemberAttributes("y"));
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "Type", CreateMemberAttributes("partType"));
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "Index", CreateMemberAttributes("customPartIndex"));
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "Rotation", CreateMemberAttributes("rot"));
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "Flipped", CreateMemberAttributes("flipped"));
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "OffsetX", CreateMemberAttributes("gridlessX"));
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "OffsetY", CreateMemberAttributes("gridlessY"));
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "RotationDegrees", CreateMemberAttributes("fineAngle"));
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "ScaleX", CreateMemberAttributes("partScaleX"));
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "ScaleY", CreateMemberAttributes("partScaleY"));
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "Layer", CreateMemberAttributes("layer"));
			xmlAttributeOverrides.Add(typeof(Schematics.Unit), "FrameShape", CreateMemberAttributes("shape"));
			return xmlAttributeOverrides;
			static XmlAttributes CreateMemberAttributes(string name)
			{
				return new XmlAttributes
				{
					XmlAttribute = new XmlAttributeAttribute(name)
				};
			}
		}

		public virtual Schematics Read(Stream stream)
		{
			Schematics schematics = (Schematics)m_serializer.Deserialize(stream);
			int count = schematics.Units.Count;
			for (int i = 0; i < count; i++)
			{
				Schematics.Unit unit = schematics.Units[i];
				PartType type = ((LegacyPartType)unit.Type).ToPartType();
				schematics.Units[i] = unit.WithPartType((int)type, unit.Index);
			}
			schematics.Normalize();
			return schematics;
		}

		public virtual void Write(Stream stream, Schematics schematics)
		{
			Schematics schematics2 = new Schematics(schematics.Units.Count);
			foreach (Schematics.Unit unit in schematics.Units)
			{
				LegacyPartType type = ((PartType)unit.Type).ToLegacyPartType();
				schematics2.Units.Add(unit.WithPartType((int)type, unit.Index));
			}
			m_serializer.Serialize(stream, schematics2);
		}
	}
}
