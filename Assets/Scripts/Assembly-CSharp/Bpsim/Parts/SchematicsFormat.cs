namespace Bpsim.Parts
{
	public enum SchematicsFormat
	{
		None = 0,
		Csv = 1,
		Json = 2,
		Xml = 3,
		EncryptedXml = 4,

		/// <summary>
		/// BPIR（Bad Piggies Innovation Rebuild）导出/导入用的 14 列 CSV：
		/// <c>type,index,x,y,rotation,flipped,mass,gravity,drag,size,power,suction,members,layer</c>。
		/// 与 <see cref="Csv" /> 的 13 列格式语义完全不同，只能靠列数区分，所以单独成一种格式。
		/// </summary>
		BpirCsv = 5
	}
}
