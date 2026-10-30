namespace Bpsim.Templates
{
	public interface ITemplate
	{
	}
	public interface ITemplate<T> : ITemplate
	{
		T Apply(T unityObject, IResourceResolver resolver);
	}
}
