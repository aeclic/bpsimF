using Bpsim.Templates;
using UnityEngine;

namespace Bpsim
{
	public class ResourceResolver : IResourceResolver
	{
		public static ResourceResolver Default { get; private set; } = new ResourceResolver();

		public Texture2D ResolveTexture(string path)
		{
			return CoreManager.Instance.Resources.LoadAsset<Texture2D>(path);
		}

		public AudioClip ResolveAudio(string path)
		{
			return null;
		}

		public Shader ResolveShader(string path)
		{
			return Shader.Find(path);
		}

		public Material ResolveMaterial(string path)
		{
			return CoreManager.Instance.Resources.LoadAsset<Material>(path);
		}
	}
}
