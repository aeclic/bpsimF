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
		BpirCsv = 5,

		/// <summary>
		/// 按文件内容判定格式（见 <see cref="Schematics.DetectFormat" />）。只能在加载时用：
		/// 保存必须先落到具体格式。旧档里从来没有这个值，所以不会破坏既有存档。
		/// </summary>
		Auto = 6
	}
}
