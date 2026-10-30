using System;

namespace Bpsim.Templates
{
	public class TemplateException : Exception
	{
		public TemplateException(string message)
			: base(message)
		{
		}
	}
}
