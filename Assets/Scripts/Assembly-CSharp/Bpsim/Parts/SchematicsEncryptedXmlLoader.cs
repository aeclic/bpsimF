using System.IO;
using System.Security.Cryptography;

namespace Bpsim.Parts
{
	internal class SchematicsEncryptedXmlLoader : SchematicsXmlLoader
	{
		private byte[] m_keyBytes;

		private byte[] m_ivBytes;

		public new static SchematicsEncryptedXmlLoader Default { get; } = new SchematicsEncryptedXmlLoader();

		public SchematicsEncryptedXmlLoader()
		{
			byte[] salt = new byte[13]
			{
				82, 166, 66, 87, 146, 51, 179, 108, 242, 110,
				98, 237, 124
			};
			Rfc2898DeriveBytes rfc2898DeriveBytes = new Rfc2898DeriveBytes("3b91A049Ca7HvSjhxT35", salt);
			m_keyBytes = rfc2898DeriveBytes.GetBytes(32);
			m_ivBytes = rfc2898DeriveBytes.GetBytes(16);
		}

		public override Schematics Read(Stream stream)
		{
			using MemoryStream memoryStream = new MemoryStream();
			Decrypt(stream, memoryStream);
			memoryStream.Seek(0L, SeekOrigin.Begin);
			Schematics schematics = (Schematics)m_serializer.Deserialize(memoryStream);
			int count = schematics.Units.Count;
			for (int i = 0; i < count; i++)
			{
				Schematics.Unit unit = schematics.Units[i];
				PartType type = ((LegacyPartType)unit.Type).ToPartType();
				schematics.Units[i] = unit.WithPartType((int)type, unit.Index);
			}
			return schematics;
		}

		public override void Write(Stream stream, Schematics schematics)
		{
			Schematics schematics2 = new Schematics(schematics.Units.Count);
			foreach (Schematics.Unit unit in schematics.Units)
			{
				LegacyPartType type = ((PartType)unit.Type).ToLegacyPartType();
				schematics2.Units.Add(unit.WithPartType((int)type, unit.Index));
			}
			using MemoryStream memoryStream = new MemoryStream();
			m_serializer.Serialize(memoryStream, schematics2);
			memoryStream.Seek(0L, SeekOrigin.Begin);
			Encrypt(memoryStream, stream);
		}

		private void Encrypt(Stream input, Stream output)
		{
			using Aes aes = Aes.Create();
			aes.Key = m_keyBytes;
			aes.IV = m_ivBytes;
			using ICryptoTransform transform = aes.CreateEncryptor();
			using CryptoStream cryptoStream = new CryptoStream(input, transform, CryptoStreamMode.Read, leaveOpen: false);
			cryptoStream.CopyTo(output);
		}

		private void Decrypt(Stream input, Stream output)
		{
			using Aes aes = Aes.Create();
			aes.Key = m_keyBytes;
			aes.IV = m_ivBytes;
			using ICryptoTransform transform = aes.CreateDecryptor();
			using CryptoStream cryptoStream = new CryptoStream(input, transform, CryptoStreamMode.Read, leaveOpen: false);
			cryptoStream.CopyTo(output);
		}
	}
}
