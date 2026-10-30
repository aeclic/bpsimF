using System.Runtime.InteropServices;
using Unity.Entities;

namespace Bpsim
{
	[StructLayout(LayoutKind.Sequential, Size = 1)]
	public struct PartTag : IComponentData, IQueryTypeParameter
	{
	}
}
