using System.Collections.Generic;
using UnityEngine;

namespace Bpsim.Rendering
{
	/// <summary>
	/// 部件图集注册表：图集名 → (序号, 材质)。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版只有一个图集 <c>Part_Texture_0</c>（序号 0，材质也来自同名资源）。Mod 通过
	/// <see cref="Bpsim.Mods.ModSprites"/> 注册自己的贴图后，这里为它复制一份
	/// <c>Part_Texture_0.mat</c> 并换上新的 <c>_MainTex</c>，得到一张独立材质；
	/// <c>SceneRenderSystem</c> 按实体上的 <see cref="PartAtlasIndex"/> 选择材质。
	/// </para>
	/// <para>
	/// 注册只能发生在渲染系统第一次注册资源之前（实际就是部件注册阶段），
	/// 因为批处理的材质表与图集序号列表在那一刻定下来。
	/// </para>
	/// </remarks>
	public static class PartAtlasRegistry
	{
		/// <summary>原版图集名。</summary>
		public const string BaseAtlasName = "Part_Texture_0";

		private static readonly List<string> s_names = new List<string> { BaseAtlasName };

		// s_names 预置了原版图集，s_materials / s_textures 也要预置一个占位项，
		// 否则 Mod 图集拿到的序号（从 1 开始）会与列表下标错位，GetTexture(1) 永远拿到别的图集。
		private static readonly List<Material> s_materials = new List<Material> { null };

		private static readonly List<Texture2D> s_textures = new List<Texture2D> { null };

		private static readonly Dictionary<string, int> s_indices = new Dictionary<string, int> { { BaseAtlasName, 0 } };

		/// <summary>已注册的图集数量（含原版）。</summary>
		public static int Count => s_names.Count;

		/// <summary>该图集是否有独立材质（没有就回退原版材质）。</summary>
		public static bool IsRegistered(string atlasName)
		{
			return !string.IsNullOrEmpty(atlasName) && s_indices.ContainsKey(atlasName);
		}

		/// <summary>图集序号，未注册返回 <see cref="PartAtlasIndex.Default"/>。</summary>
		public static int IndexOf(string atlasName)
		{
			if (string.IsNullOrEmpty(atlasName))
			{
				return PartAtlasIndex.Default;
			}
			return s_indices.TryGetValue(atlasName, out int index) ? index : PartAtlasIndex.Default;
		}

		/// <summary>按序号取材质；<paramref name="fallback"/> 用于尚未建出材质的情形。</summary>
		public static Material GetMaterial(int index, Material fallback)
		{
			if (index > 0 && index < s_materials.Count)
			{
				Material material = s_materials[index];
				if (material != null)
				{
					return material;
				}
			}
			return fallback;
		}

		/// <summary>按序号取贴图（UGUI 图标要直接换 texture 时用）。</summary>
		public static Texture2D GetTexture(int index)
		{
			if (index >= 0 && index < s_textures.Count)
			{
				return s_textures[index];
			}
			return null;
		}

		/// <summary>按图集名取贴图。</summary>
		public static Texture2D GetTexture(string atlasName)
		{
			return GetTexture(IndexOf(atlasName));
		}

		/// <summary>
		/// 注册图集。重复注册同名图集直接返回已有序号。
		/// </summary>
		/// <param name="atlasName">图集名（与精灵表首列一致）。</param>
		/// <param name="texture">贴图；null 表示暂时只占位，稍后 <see cref="SetTexture"/>。</param>
		/// <param name="index">分配的序号。</param>
		public static bool Register(string atlasName, Texture2D texture, out int index)
		{
			if (string.IsNullOrEmpty(atlasName))
			{
				index = PartAtlasIndex.Default;
				return false;
			}
			if (s_indices.TryGetValue(atlasName, out index))
			{
				if (texture != null)
				{
					SetTexture(index, texture);
				}
				return true;
			}
			index = s_names.Count;
			s_names.Add(atlasName);
			s_indices[atlasName] = index;
			s_textures.Add(texture);
			s_materials.Add(texture != null ? CreateMaterial(atlasName, texture) : null);
			return true;
		}

		/// <summary>替换某个图集序号的贴图（会重建材质）。</summary>
		public static bool SetTexture(int index, Texture2D texture)
		{
			if (index <= 0 || index >= s_names.Count || texture == null)
			{
				return false;
			}
			while (s_textures.Count <= index)
			{
				s_textures.Add(null);
			}
			s_textures[index] = texture;
			Material material = s_materials[index];
			if (material != null)
			{
				material.mainTexture = texture;
				return true;
			}
			s_materials[index] = CreateMaterial(s_names[index], texture);
			return true;
		}

		/// <summary>诊断用：把内部状态拼成一行文本（编辑器 QA 探针调用）。</summary>
		public static string DumpState()
		{
			System.Text.StringBuilder builder = new System.Text.StringBuilder();
			builder.Append("Count=").Append(s_names.Count);
			builder.Append(" names=[");
			for (int i = 0; i < s_names.Count; i++)
			{
				if (i > 0) { builder.Append(", "); }
				builder.Append(s_names[i]);
				builder.Append("#tex=").Append(i < s_textures.Count ? (s_textures[i] != null ? s_textures[i].name : "null") : "<missing>");
				builder.Append("#mat=").Append(i < s_materials.Count ? (s_materials[i] != null ? s_materials[i].name : "null") : "<missing>");
			}
			builder.Append("] textures=").Append(s_textures.Count);
			builder.Append(" materials=").Append(s_materials.Count);
			return builder.ToString();
		}

		/// <summary>清空 Mod 注册的图集（退出游戏 / 重载 Mod 时用）。</summary>
		public static void Clear()
		{
			for (int i = s_materials.Count - 1; i > 0; i--)
			{
				Material material = s_materials[i];
				if (material != null)
				{
					if (Application.isPlaying)
					{
						Object.Destroy(material);
					}
					else
					{
						Object.DestroyImmediate(material);
					}
				}
			}
			s_names.RemoveRange(1, s_names.Count - 1);
			s_materials.RemoveRange(1, s_materials.Count - 1);
			if (s_textures.Count > 1)
			{
				s_textures.RemoveRange(1, s_textures.Count - 1);
			}
			s_indices.Clear();
			s_indices[BaseAtlasName] = 0;
		}

		private static Material CreateMaterial(string atlasName, Texture2D texture)
		{
			Material template = null;
			if (CoreManager.Instance != null && CoreManager.Instance.Resources != null)
			{
				CoreManager.Instance.Resources.TryLoadAsset<Material>(BaseAtlasName, out template);
			}
			if (template == null)
			{
				Debug.LogWarning("[PartAtlasRegistry] 找不到模板材质 " + BaseAtlasName + "，图集 " + atlasName + " 无法建材质");
				return null;
			}
			Material material = new Material(template)
			{
				name = "Part_Atlas_" + atlasName,
				hideFlags = HideFlags.DontSave
			};
			material.mainTexture = texture;
			return material;
		}
	}
}
