using System.Collections.Generic;
using Bpsim.Parts;
using Bpsim.Rendering;
using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary>
	/// 给 Mod 用的图集/精灵扩展 API：注册一张自备图集，再往里塞精灵条目。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版所有部件共用一张 <c>Part_Texture_0</c> 图集和一张材质。变体想换皮肤，就得让
	/// 「这个变体的 sprite」落到另一张图集上——这条链路的三段都在这里接起来：
	/// </para>
	/// <list type="number">
	/// <item><see cref="RegisterAtlas"/>：把 Mod 自带的 PNG 变成一张贴图 + 一份独立材质，
	/// 并在 <see cref="SpriteManager"/> 里开一个同名图集。</item>
	/// <item><see cref="RegisterSprite"/>：往该图集里登记一个精灵条目（像素矩形 + 轴心 + 缩放），
	/// 返回的键名直接赋给 <c>SpriteBase.SpriteName</c> 就能显示。</item>
	/// <item><see cref="SetVariantSkin"/> / <see cref="SetVariantIcon"/>：把变体上那几个
	/// <c>SpriteBase</c> 换成新键名，并（若克隆时没带图集组件）补上 <see cref="PartAtlasIndex"/>。</item>
	/// </list>
	/// <para>
	/// 图标走 UGUI，<c>PartSelector</c> 只要能在 <see cref="SpriteManager"/> 里查到
	/// <c>Part_&lt;Type&gt;_&lt;Index&gt;_Rendered</c> 就会用；所以 <see cref="RegisterIcon"/>
	/// 登记的名字必须按这个格式。
	/// </para>
	/// </remarks>
	public static class ModSprites
	{
		/// <summary>
		/// 注册一张 Mod 图集。
		/// </summary>
		/// <param name="atlasName">图集名，必须与精灵表里的图集名一致（例如 <c>"BPREBOOT_Skins"</c>）。</param>
		/// <param name="texture">整张图集贴图。</param>
		/// <param name="width">图集宽（像素）。</param>
		/// <param name="height">图集高（像素）。</param>
		/// <param name="scale">全局缩放，与 <c>Part_Texture_0</c> 的 0.01302083 同单位。</param>
		public static bool RegisterAtlas(string atlasName, Texture2D texture, int width, int height, float scale, out string error)
		{
			error = null;
			if (string.IsNullOrEmpty(atlasName))
			{
				error = "图集名为空";
				return false;
			}
			if (texture == null)
			{
				error = "图集贴图为空";
				return false;
			}
			if (width <= 0 || height <= 0)
			{
				error = "图集尺寸无效";
				return false;
			}
			SpriteManager manager = SpriteManager.Instance;
			if (manager == null)
			{
				error = "SpriteManager 尚未初始化";
				return false;
			}
			if (!PartAtlasRegistry.Register(atlasName, texture, out int _))
			{
				error = "注册图集材质失败";
				return false;
			}
			if (manager.HasAtlas(atlasName))
			{
				return true;
			}
			if (!manager.TryAddAtlas(atlasName, width, height, scale, out error))
			{
				return false;
			}
			return true;
		}

		/// <summary>
		/// 往已注册的图集里加一个精灵。
		/// </summary>
		/// <param name="key">精灵键名（全局唯一）。</param>
		/// <param name="atlasName">所属图集。</param>
		/// <param name="entry">像素矩形 + 轴心 + 缩放，语义同 <c>SpriteMap.MapEntry</c>。</param>
		/// <param name="error">失败原因。</param>
		public static bool RegisterSprite(string key, string atlasName, SpriteMap.MapEntry entry, out string error)
		{
			SpriteManager manager = SpriteManager.Instance;
			if (manager == null)
			{
				error = "SpriteManager 尚未初始化";
				return false;
			}
			return manager.TryAddSprite(key, atlasName, entry, out int _, out error);
		}

		/// <summary>
		/// 登记一张建造列表图标。名字必须是 <c>Part_&lt;Type&gt;_&lt;Index&gt;_Rendered</c>，
		/// 否则 <c>PartSelector</c> 查不到。
		/// </summary>
		public static bool RegisterIcon(PartTypeInfo info, string atlasName, SpriteMap.MapEntry entry, out string error)
		{
			return RegisterSprite("Part_" + info.PartType + "_" + info.PartIndex + "_Rendered", atlasName, entry, out error);
		}

		/// <summary>
		/// 把变体的皮肤换成某个精灵；返回换过的 <c>SpriteBase</c> 数量。
		/// </summary>
		/// <param name="part">变体 GameObject（<see cref="ModParts.TryAddVariant"/> 返回后的实例，可用
		/// <c>PartManager.Instance.Factory.PartCollection.FindPart(info)</c> 取回）。</param>
		/// <param name="spriteKey">目标精灵键名。</param>
		/// <remarks>
		/// 变体是整份克隆出来的，通常只该有一个「主贴图」子物体（或变体自己身上），
		/// 所以这里默认只换第一个命中的 <c>SpriteBase</c>；要换多段（吸盘轮的轮/辐条/支架）
		/// 用 <see cref="SetSkins"/> 按子物体名精确指定。
		/// </remarks>
		public static int SetVariantSkin(GameObject part, string spriteKey)
		{
			// 部件上的精灵组件是 MaterialSprite（写 _Rect/_UVRect property block 那种），
			// 不是 MeshSprite；只找 MeshSprite 会一个都命中不了，皮肤于是静默不生效。
			SpriteBase[] sprites = part.GetComponentsInChildren<SpriteBase>(includeInactive: true);
			if (sprites.Length == 0)
			{
				return 0;
			}
			sprites[0].SpriteName = spriteKey;
			sprites[0].Apply();
			return 1;
		}

		/// <summary>
		/// 按「子物体名 → 精灵键」批量换皮肤，用于多段皮肤（轮/辐条/支架等）。
		/// </summary>
		/// <param name="part">变体 GameObject。</param>
		/// <param name="map">子物体名（或 <c>SpriteBase</c> 名）到精灵键的映射。</param>
		/// <returns>换成功的数量。</returns>
		public static int SetSkins(GameObject part, IDictionary<string, string> map)
		{
			if (map == null || map.Count == 0)
			{
				return 0;
			}
			int count = 0;
			SpriteBase[] sprites = part.GetComponentsInChildren<SpriteBase>(includeInactive: true);
			foreach (SpriteBase sprite in sprites)
			{
				string objectName = sprite.gameObject.name;
				if (map.TryGetValue(objectName, out string key) || map.TryGetValue(sprite.SpriteName, out key))
				{
					sprite.SpriteName = key;
					sprite.Apply();
					count++;
				}
			}
			return count;
		}

		/// <summary>把变体的图标换成已登记的 <c>Part_&lt;Type&gt;_&lt;Index&gt;_Rendered</c>。</summary>
		public static bool SetVariantIcon(PartTypeInfo info, string atlasName, SpriteMap.MapEntry entry, out string error)
		{
			return RegisterIcon(info, atlasName, entry, out error);
		}

		/// <summary>清空 Mod 注册的图集（Mod 卸载 / 回主菜单时用）。</summary>
		public static void ClearAtlasMaterials()
		{
			PartAtlasRegistry.Clear();
		}
	}
}
