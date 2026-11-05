using System;
using System.Collections.Generic;
using Bpsim.Parts;
using Bpsim.Rendering;
using UnityEngine;

namespace Bpsim.Mods
{
	/// <summary>
	/// 给 Mod 用的部件扩展 API：把已有部件克隆一份、换索引、登记进 <c>PartCollection</c>，
	/// 于是零件选择器的「变体」列表里就多了一项。
	/// </summary>
	/// <remarks>
	/// <para>bpsim 的部件是 <c>(PartType, index)</c> 二元组，索引本身没有上限，所以「加部件」
	/// 不需要新 prefab —— 克隆基部件 + 换索引即可。这是这套体系能支撑 BPIR 那种
	/// 「变形框/三角框」扩展的关键。</para>
	/// <para>自定义索引从 <see cref="CustomIndexBase"/> 起，避开原版 0..~200 的占用区间，
	/// 也方便存档识别「这一件是 Mod 加的」。</para>
	/// </remarks>
	public static class ModParts
	{
		/// <summary>Mod 部件的索引起点。</summary>
		public const int CustomIndexBase = 9000;

		/// <summary>
		/// 注册一个部件变体。
		/// </summary>
		/// <param name="baseType">基部件类型（例如 <c>PartType.MetalFrame</c>）。</param>
		/// <param name="baseIndex">基部件索引（例如 12 = 彩色框第一件）。</param>
		/// <param name="newIndex">新索引，必须 ≥ <see cref="CustomIndexBase"/>。</param>
		/// <param name="displayName">显示名，同时作为 GameObject 名。</param>
		public static bool TryAddVariant(PartType baseType, int baseIndex, int newIndex, string displayName, out string error)
		{
			return TryAddVariant(baseType, baseIndex, newIndex, displayName, null, out error);
		}

		/// <summary>
		/// 注册一个部件变体，并在**建 ECS 预制体之前**先让 Mod 修饰克隆出来的模板。
		/// </summary>
		/// <param name="prepare">
		/// 在 <c>spawner.AddPrefab</c> 之前对克隆体执行的修饰（换贴图键名、加子物体等）。
		/// </param>
		/// <remarks>
		/// 修饰必须发生在建预制体之前：<c>PartEntityFactory.CreateSprite</c> 是在
		/// <c>AddPrefab</c> 时按当时的 <c>SpriteBase.SpriteName</c> 一次性把
		/// <see cref="PartAtlasIndex"/> 烤进 ECS 实体的。之后再改 <c>m_name</c>，
		/// 场上实体仍然指向旧图集（表现为「选择器里看得见新皮肤，放出来还是原版贴图」）。
		/// </remarks>
		public static bool TryAddVariant(PartType baseType, int baseIndex, int newIndex, string displayName, Action<ManagedPart> prepare, out string error)
		{
			error = null;
			if (newIndex < CustomIndexBase)
			{
				error = "自定义索引必须 ≥ " + CustomIndexBase;
				return false;
			}

			PartFactory factory = GetFactory(out error);
			if (factory == null)
			{
				return false;
			}

			PartCollection<ManagedPart> collection = factory.PartCollection;
			if (collection == null)
			{
				error = "PartCollection 为空";
				return false;
			}

			if (collection.TryFindPart(new PartTypeInfo(baseType, newIndex), out ManagedPart _))
			{
				error = "索引 " + newIndex + " 已被占用";
				return false;
			}

			ManagedPart basePart = collection.FindPart(new PartTypeInfo(baseType, baseIndex));
			if (basePart == null)
			{
				error = "找不到基部件 " + baseType + "#" + baseIndex;
				return false;
			}

			try
			{
				ManagedPart clone = UnityEngine.Object.Instantiate(basePart);
				clone.PartIndex = newIndex;
				clone.gameObject.name = displayName;
				clone.name = displayName;
				// 跟着基部件挂在同一个容器下，避免模板对象散落在场景根节点。
				if (basePart.transform.parent != null)
				{
					clone.transform.SetParent(basePart.transform.parent, false);
				}
				collection.AddPart(new PartTypeInfo(baseType, newIndex), clone);
				// 登记「变体 → 基部件」：原版的索引区间判断（彩色框 12..129 等）靠它
				// 把变体认成基部件那一类，否则变体实体缺 ColoredFrame、质量查不到。
				PartVariants.Register(clone.TypeInfo, basePart.TypeInfo, displayName);
				// 皮肤必须在这里改：AddPrefab 会立刻把 SpriteBase 的键名烤成
				// SpriteID + PartAtlasIndex 写进 ECS 实体，之后再改名字只影响模板对象。
				prepare?.Invoke(clone);
				// 关键：PartEntitySpawner 的预制体表是 PartManager.Start() 时一次性铺的，
				// 不在这里补登记的话 FindPrefab 对变体返回 Entity.Null，
				// 放置逻辑会直接跳过 —— 选择器里看得见，场上放不出来。
				PartEntitySpawner spawner = GetSpawner(out string spawnerError);
				if (spawner == null)
				{
					// 表还没建（PartManager 尚未 Start）：登记本身仍然有效，
					// 等 spawner 建表时会把 PartCollection 里已有的变体一起铺进去。
					Debug.LogWarning("[Mods] 部件变体 " + displayName + " 暂未建预制体：" + spawnerError);
				}
				else
				{
					spawner.AddPrefab(clone);
				}
				Debug.Log("[Mods] 已注册部件变体 " + displayName + " → " + baseType + "#" + newIndex);
				return true;
			}
			catch (Exception ex)
			{
				// 半途失败要把已写进 PartCollection / 登记表的痕迹清掉，否则这个索引
				// 以后再也登记不进来（AddPart 遇重复 key 会抛）。
				collection.RemovePart(new PartTypeInfo(baseType, newIndex));
				PartVariants.Unregister(new PartTypeInfo(baseType, newIndex));
				error = "克隆部件失败：" + ex.Message;
				return false;
			}
		}

		/// <summary>移除一个变体（Mod 卸载时用）。</summary>
		public static bool RemoveVariant(PartType baseType, int index)
		{
			PartFactory factory = GetFactory(out string _);
			PartCollection<ManagedPart> collection = factory != null ? factory.PartCollection : null;
			if (collection == null)
			{
				return false;
			}
			PartTypeInfo info = new PartTypeInfo(baseType, index);
			if (!collection.TryFindPart(info, out ManagedPart part))
			{
				return false;
			}
			// 只有 Mod 变体才归我们销毁：原版部件的 GameObject 是 PartGroups 里的模板，
			// 删掉会连带把整个模板目录搞坏。
			bool isVariant = PartVariants.IsVariant(info);
			PartEntitySpawner spawner = GetSpawner(out string _);
			if (spawner != null)
			{
				spawner.RemovePrefab(info);
			}
			PartVariants.Unregister(info);
			collection.RemovePart(info);
			if (isVariant && part != null)
			{
				UnityEngine.Object.Destroy(part.gameObject);
			}
			return true;
		}

		/// <summary>
		/// 注册一个带皮肤的部件变体：克隆基部件 → 换索引 → 换贴图/图标 → 登记。
		/// </summary>
		/// <remarks>
		/// <para>
		/// 这是 <see cref="TryAddVariant"/> 的皮肤版。区别在于克隆完成后会拿
		/// <paramref name="spriteKey"/> 覆盖变体上的 <c>MeshSprite.m_name</c>，
		/// 于是 <c>PartEntityFactory.CreateSprite</c> 会按新键名查到 <see cref="PartAtlasIndex"/>，
		/// 渲染时走另一张图集的材质。
		/// </para>
		/// <para>
		/// 皮肤必须先用 <see cref="ModSprites.RegisterAtlas"/> / <see cref="ModSprites.RegisterSprite"/>
		/// 注册好，否则这里只换名字、贴图仍是基部件那张。
		/// </para>
		/// </remarks>
		public static bool TryAddSkinVariant(PartType baseType, int baseIndex, int newIndex, string displayName, string spriteKey, string iconSpriteKey, out string error)
		{
			return TryAddSkinVariant(baseType, baseIndex, newIndex, displayName, spriteKey, null, iconSpriteKey, out error);
		}

		/// <summary>
		/// 注册一个带皮肤的部件变体；<paramref name="skins"/> 非空时按「子物体名 → 精灵键」
		/// 精确换多段皮肤（吸盘轮的轮/辐条、拳套箱的本体/弹簧、发动机的可视化子物体）。
		/// </summary>
		public static bool TryAddSkinVariant(PartType baseType, int baseIndex, int newIndex, string displayName, string spriteKey, IDictionary<string, string> skins, string iconSpriteKey, out string error)
		{
			// 换皮肤必须在建 ECS 预制体之前完成，否则 PartAtlasIndex 会烤成基部件那张图集。
			if (!TryAddVariant(baseType, baseIndex, newIndex, displayName, delegate (ManagedPart variant)
			{
				if (skins != null && skins.Count > 0)
				{
					ModSprites.SetSkins(variant.gameObject, skins);
				}
				else if (!string.IsNullOrEmpty(spriteKey))
				{
					ModSprites.SetVariantSkin(variant.gameObject, spriteKey);
				}
			}, out error))
			{
				return false;
			}
			if (!string.IsNullOrEmpty(iconSpriteKey))
			{
				RegisterVariantIcon(new PartTypeInfo(baseType, newIndex), iconSpriteKey);
			}
			return true;
		}

		/// <summary>把一个 GameObject 下所有 <c>SpriteBase</c> 的贴图键名换成 <paramref name="spriteKey"/>。</summary>
		public static int ApplySkin(GameObject part, string spriteKey)
		{
			if (part == null || string.IsNullOrEmpty(spriteKey))
			{
				return 0;
			}
			SpriteBase[] sprites = part.GetComponentsInChildren<SpriteBase>(includeInactive: true);
			int count = 0;
			foreach (SpriteBase sprite in sprites)
			{
				sprite.SpriteName = spriteKey;
				sprite.Apply();
				count++;
			}
			return count;
		}

		/// <summary>
		/// 给变体登记建造列表图标：把已注册的精灵条目重命名成
		/// <c>Part_&lt;Type&gt;_&lt;Index&gt;_Rendered</c>，<c>PartSelector</c> 就能直接查到。
		/// </summary>
		public static void RegisterVariantIcon(PartTypeInfo info, string spriteKey)
		{
			SpriteManager manager = SpriteManager.Instance;
			if (manager == null || !manager.TryGetID(spriteKey, out int id))
			{
				return;
			}
			string rendered = "Part_" + info.PartType + "_" + info.PartIndex + "_Rendered";
			if (manager.HasID(rendered))
			{
				return;
			}
			SpriteRect rect = manager.GetSprite(id);
			string atlas = manager.GetSpriteAtlas(id);
			SpriteMap.MapEntry entry = ToEntry(rect, atlas, manager);
			manager.TryAddSprite(rendered, atlas, in entry, out int _, out string _);
		}

		/// <summary>
		/// 由已有的 <see cref="SpriteRect"/> 反推一份 <see cref="SpriteMap.MapEntry"/>，
		/// 用于「同一个像素格子换个名字」。
		/// </summary>
		private static SpriteMap.MapEntry ToEntry(in SpriteRect rect, string atlasName, SpriteManager manager)
		{
			// SpriteRect 已经把像素换算成局部尺寸与归一化 UV，反推回去会丢精度；
			// 这里直接把 UV 转成像素，轴心取 (0,0)、缩放取 1，得到同一块像素。
			if (!manager.TryGetAtlasHeader(atlasName, out SpriteMap.MapHeader header))
			{
				return default(SpriteMap.MapEntry);
			}
			int x = Mathf.RoundToInt(rect.U * header.Width);
			int y = Mathf.RoundToInt((1f - rect.V - rect.H) * header.Height);
			int w = Mathf.Max(1, Mathf.RoundToInt(rect.W * header.Width));
			int h = Mathf.Max(1, Mathf.RoundToInt(rect.H * header.Height));
			// 顶点尺寸 = 2 * (int)(像素 * 缩放) * header.Scale，反解出 ScaleX/ScaleY。
			float scaleFactor = header.Scale;
			float sx = 1f;
			float sy = 1f;
			if (scaleFactor > 0f && w > 0 && h > 0)
			{
				int pxW = Mathf.Max(1, Mathf.RoundToInt(rect.VertexW / (2f * scaleFactor)));
				int pxH = Mathf.Max(1, Mathf.RoundToInt(rect.VertexH / (2f * scaleFactor)));
				sx = (float)pxW / (float)w;
				sy = (float)pxH / (float)h;
			}
			int pivotX = 0;
			int pivotY = 0;
			if (scaleFactor > 0f && sx != 0f)
			{
				pivotX = Mathf.RoundToInt(-rect.VertexX / (2f * scaleFactor * sx));
			}
			if (scaleFactor > 0f && sy != 0f)
			{
				pivotY = Mathf.RoundToInt(-rect.VertexY / (2f * scaleFactor * sy));
			}
			return new SpriteMap.MapEntry(x, y, w, h, pivotX, pivotY, sx, sy);
		}

		/// <summary>这个索引是不是 Mod 加的部件。</summary>
		public static bool IsCustomIndex(int index)
		{
			return index >= CustomIndexBase;
		}

		/// <summary>
		/// 「部件形状」特性是否开启（<see cref="FrameShapeSystem.FeatureEnabled"/>）。
		/// </summary>
		/// <remarks>
		/// 这个特性默认是关的：开着会让所有框类部件的碰撞体按形状重建，
		/// 原版流程用不到，所以由 <c>FrameShapeSystem.SetEnabled</c> 显式控制。
		/// Mod 想用形状就得自己打开 —— 之前框架没有对外暴露这个开关，
		/// 导致变体注册成功了却永远套不上形状（<see cref="FrameShapeSystem.SetShape"/>
		/// 第一行就因为 <c>!FeatureEnabled</c> 直接返回）。
		/// </remarks>
		public static bool FrameShapesEnabled => FrameShapeSystem.FeatureEnabled;

		/// <summary>
		/// 打开/关闭「部件形状」特性。开启后 <see cref="FrameShapeSystem.SetShape"/>
		/// 才会真正写组件并重建凸包，属性面板的「形状」按钮也会出现。
		/// </summary>
		/// <remarks>
		/// 模拟中调用会先把模拟停掉（改碰撞体拓扑必须重算质量与关节），
		/// 所以别在每帧里反复调用。
		/// </remarks>
		public static void SetFrameShapesEnabled(bool enabled)
		{
			FrameShapeSystem.SetEnabled(enabled);
		}

		private static PartFactory GetFactory(out string error)
		{
			error = null;
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				error = "PartManager 尚未初始化";
				return null;
			}
			PartFactory factory = manager.Factory;
			if (factory == null)
			{
				error = "PartFactory 尚未初始化";
				return null;
			}
			return factory;
		}

		private static PartEntitySpawner GetSpawner(out string error)
		{
			error = null;
			PartManager manager = PartManager.Instance;
			if (manager == null)
			{
				error = "PartManager 尚未初始化";
				return null;
			}
			PartEntitySpawner spawner = manager.EntitySpawner;
			if (spawner == null)
			{
				error = "PartEntitySpawner 尚未初始化（PartManager.Start 还没跑）";
				return null;
			}
			return spawner;
		}
	}
}
