using Unity.Entities;

namespace Bpsim.Parts
{
	/// <summary>
	/// 实体上记录的部件身份：自己的 <c>(类型, 索引)</c>，外加它归属的基部件。
	/// </summary>
	/// <remarks>
	/// <para>
	/// <c>BaseType/BaseIndex</c> 是给 Burst job 用的：变体（索引 ≥ 9000）要按基部件的索引区间
	/// 归类（彩色框、Z 偏移、框架关节……），而 <see cref="PartVariants.Resolve"/> 查的是托管
	/// <c>Dictionary</c>，Burst 不支持（BC1016）。所以在建实体的那一刻就把解析结果烘进组件里，
	/// job 里只读这两个字段。
	/// </para>
	/// <para>
	/// 这两个字段留空（<see cref="PartType.Unknown"/>）时视为「没有变体归属」，
	/// <see cref="PartAspect.BaseTypeInfo"/> 会回退到 <c>Type/Index</c> —— 手工构造
	/// <c>new PartTypeValue { Type = ..., Index = ... }</c> 的老代码因此不用改。
	/// </para>
	/// </remarks>
	public struct PartTypeValue : IComponentData, IQueryTypeParameter
	{
		public PartType Type;

		public int Index;

		/// <summary>变体归属的基部件类型；非变体时等于 <see cref="Type"/>。</summary>
		public PartType BaseType;

		/// <summary>变体归属的基部件索引；非变体时等于 <see cref="Index"/>。</summary>
		public int BaseIndex;

		public PartTypeInfo Value => new PartTypeInfo(Type, Index);

		/// <summary>
		/// 归类用的基部件身份：变体返回基部件，原版部件返回自身。
		/// </summary>
		/// <remarks>
		/// <c>BaseType</c> 留空（<see cref="PartType.Unknown"/>）时回退到 <c>Type/Index</c>，
		/// 与 <see cref="PartAspect.BaseTypeInfo"/> 一致 —— 手工构造的
		/// <c>new PartTypeValue { Type = ..., Index = ... }</c> 因此照样正确。
		/// </remarks>
		public PartTypeInfo BaseValue =>
			BaseType == PartType.Unknown ? new PartTypeInfo(Type, Index) : new PartTypeInfo(BaseType, BaseIndex);
	}
}
