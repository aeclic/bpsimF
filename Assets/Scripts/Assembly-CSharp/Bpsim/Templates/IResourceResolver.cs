using UnityEngine;

namespace Bpsim.Templates
{
	public interface IResourceResolver
	{
		Texture2D ResolveTexture(string path);

		AudioClip ResolveAudio(string path);

		Shader ResolveShader(string path);

		Material ResolveMaterial(string path);
	}
}
