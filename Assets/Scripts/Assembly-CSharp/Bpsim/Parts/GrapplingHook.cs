using System.Runtime.InteropServices;
using Unity.Entities;

namespace Bpsim.Parts
{
	[StructLayout(LayoutKind.Sequential, Size = 1)]
	public struct GrapplingHook : IComponentData, IQueryTypeParameter
	{
	}
}
