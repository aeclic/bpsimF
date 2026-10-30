using System;

namespace Bpsim
{
	[AttributeUsage(AttributeTargets.Method)]
	public class RequiresSyncPointAttribute : Attribute
	{
	}
}
