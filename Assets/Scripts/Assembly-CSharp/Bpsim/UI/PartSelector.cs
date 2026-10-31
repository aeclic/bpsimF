using System;
using System.Collections.Generic;
using Bpsim.Parts;
using Bpsim.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace Bpsim.UI
{
	internal class PartSelector : MonoBehaviour
	{
		[SerializeField]
		private GameObject m_itemTemplate;

		[SerializeField]
		private CustomPartSelector m_customPartSelectorTemplate;

		[SerializeField]
		private GameObject m_content;

		[SerializeField]
		private ToggleGroup m_toggleGroup;

		private PartType m_partType;

		private int[] m_indexMap;

		private int[] m_rotationMap;

		private bool[] m_flippedMap;

		private GameObject[] m_itemMap;

		public PartType PartType => m_partType;

		public int PartIndex => m_indexMap[(int)m_partType];

		public int Rotation => m_rotationMap[(int)m_partType];

		public bool Flipped => m_flippedMap[(int)m_partType];

		private void Start()
		{
			// 按 PartType.Max 分配（不是硬编码 49）：零件选择器会遍历整个
			// PartCollection（长度 = PartType.Max），数组短了就会在最后两个类型上越界。
			int capacity = (int)PartType.Max;
			m_indexMap = new int[capacity];
			m_rotationMap = new int[capacity];
			m_flippedMap = new bool[capacity];
			m_itemMap = new GameObject[capacity];
			PartCollection<ManagedPart> partCollection = PartManager.Instance.Factory.PartCollection;
			for (int i = 0; i < partCollection.Length; i++)
			{
				PartType partType = (PartType)i;
				if (partCollection.TryFindParts(partType, out var _))
				{
					m_itemMap[(int)partType] = CreateItem(partType);
				}
			}
		}

		private GameObject CreateItem(PartType partType)
		{
			GameObject obj = UnityEngine.Object.Instantiate(m_itemTemplate);
			obj.name = $"Item_{(int)partType}";
			obj.transform.SetParent(m_content.transform, worldPositionStays: false);
			obj.SetActive(value: true);
			obj.transform.Find("Name").GetComponent<Text>().text = partType.GetAliasName();
			Toggle component = obj.GetComponent<Toggle>();
			component.group = m_toggleGroup;
			component.onValueChanged.AddListener(delegate(bool value)
			{
				if (value)
				{
					m_partType = partType;
				}
			});
			obj.GetComponent<DoubleClickableButton>().onDoubleClick.AddListener(delegate
			{
				CreateCustomPartSelector(partType);
			});
			RectTransform rectTransform = (RectTransform)obj.transform.Find("PartIcon");
			SetRenderedIcon(rectTransform, $"Part_{partType}_0_Rendered", partType == PartType.SkinPart);
			PartPaletteDragSource dragSource = obj.AddComponent<PartPaletteDragSource>();
			dragSource.Initialize(this, partType);
			return obj;
		}

		/// <summary>
		/// Returns the variant currently represented by a palette row.  The drag source
		/// reads this at drag start so custom variant/rotation choices are preserved.
		/// </summary>
		internal PartTypeInfo GetDragPart(PartType partType, out int rotation, out bool flipped)
		{
			int index = m_indexMap[(int)partType];
			rotation = m_rotationMap[(int)partType];
			flipped = m_flippedMap[(int)partType];
			return new PartTypeInfo(partType, index);
		}

		private void UpdateItem(PartType partType, int partIndex, int rotation, bool flipped)
		{
			m_rotationMap[(int)partType] = rotation;
			m_flippedMap[(int)partType] = flipped;
			if (m_indexMap[(int)partType] != partIndex)
			{
				m_indexMap[(int)partType] = partIndex;
				RectTransform rectTransform = (RectTransform)m_itemMap[(int)partType].transform.Find("PartIcon");
				SetRenderedIcon(rectTransform, new PartTypeInfo(partType, partIndex), partType == PartType.SkinPart);
			}
		}

		private void CreateCustomPartSelector(PartType partType)
		{
			CustomPartSelector customPartSelector = UnityEngine.Object.Instantiate(m_customPartSelectorTemplate);
			customPartSelector.transform.SetParent(UserInterface.Instance.SubCanvas.transform, worldPositionStays: false);
			customPartSelector.Initialize(partType, m_indexMap[(int)partType], m_rotationMap[(int)partType], m_flippedMap[(int)partType]);
			customPartSelector.Completed += UpdateItem;
		}

		public static void NormalizeScale(RectTransform transform)
		{
			Vector2 sizeDelta = transform.sizeDelta;
			float num = Math.Max(sizeDelta.x, sizeDelta.y);
			transform.sizeDelta = Math.Min(75f / num, 0.625f) * sizeDelta;
		}

		/// <summary><c>Part_&lt;Type&gt;_&lt;Index&gt;_Rendered</c> 图集条目名。</summary>
		public static string RenderedName(PartTypeInfo info)
		{
			return "Part_" + info.PartType + "_" + info.PartIndex + "_Rendered";
		}

		/// <summary>
		/// 给选择器按钮设置 "渲染图" 图标，找不到图集条目时安静跳过。
		/// </summary>
		/// <remarks>
		/// <c>Part_&lt;Type&gt;_&lt;Index&gt;_Rendered</c> 是原版预烘焙的渲染图标，
		/// 只有原版就存在的部件才有。后来迁移进来的部件（FuelBox / FuelTube）没有这批图，
		/// 直接赋给 <c>SpriteBase.SpriteName</c> 会在 <c>Apply()</c> 里走
		/// <c>SpriteManager.GetID</c> 抛 <c>KeyNotFoundException</c>，把整个选择器构建打断。
		/// </remarks>
		public static void SetRenderedIcon(RectTransform icon, string spriteName)
		{
			SetRenderedIcon(icon, spriteName, skinFallback: false);
		}

		/// <summary>
		/// 按 <c>(类型, 索引)</c> 设置图标，变体找不到自己的预制图时回退到基部件的图标。
		/// </summary>
		/// <remarks>
		/// <para>
		/// Mod 变体（索引 ≥ <c>ModParts.CustomIndexBase</c>）是运行时克隆出来的，图集里
		/// 不可能有 <c>Part_MetalFrame_9001_Rendered</c> 这一格。以前这里直接 return，
		/// 而模板 <c>PartIcon</c> 的 <c>UISprite.m_name</c> 是空串、<c>Apply()</c> 遇到空名字
		/// 立刻返回，<c>uvRect</c> 于是停在 (0,0,1,1) —— 变体按钮上会铺开<b>整张图集</b>。
		/// </para>
		/// <para>
		/// 现在的回退顺序：自己的那格 → 基部件那格（<see cref="PartVariants.TryGetBase"/>）
		/// → 程序生成的占位图。任何一步都不会把 <c>uvRect</c> 留在整张图集上。
		/// </para>
		/// </remarks>
		public static void SetRenderedIcon(RectTransform icon, PartTypeInfo info, bool skinFallback)
		{
			if (icon == null)
			{
				return;
			}
			SpriteBase sprite = icon.GetComponent<SpriteBase>();
			if (sprite == null)
			{
				return;
			}
			SpriteManager manager = SpriteManager.Instance;
			if (manager == null)
			{
				return;
			}

			string exact = RenderedName(info);
			if (manager.HasID(exact))
			{
				ApplySprite(icon, sprite, exact);
				return;
			}

			// 变体没有自己的预制图：用基部件那一格，玩家一看就知道是「哪种框」。
			if (PartVariants.TryGetBase(info, out PartTypeInfo baseInfo))
			{
				string fallback = RenderedName(baseInfo);
				if (manager.HasID(fallback))
				{
					ApplySprite(icon, sprite, fallback);
					return;
				}
			}

			if (skinFallback)
			{
				ApplySkinIcon(icon);
				return;
			}
			ApplyFallbackIcon(icon, exact);
		}

		/// <param name="skinFallback">
		/// 图集里找不到条目时改用程序生成的蒙皮图标。模板 <c>PartIcon</c> 的 RawImage 贴的是
		/// 整张 <c>Part_Rendered_Texture_0</c>，而模板上的 <c>UISprite.m_name</c> 是空的，
		/// <c>Apply()</c> 直接 return、<c>uvRect</c> 停在 (0,0,1,1) —— 名字留空的部件
		/// （后加的蒙皮）于是把<b>整张图集</b>铺在按钮上。
		/// </param>
		public static void SetRenderedIcon(RectTransform icon, string spriteName, bool skinFallback)
		{
			if (icon == null)
			{
				return;
			}
			SpriteBase sprite = icon.GetComponent<SpriteBase>();
			if (sprite == null)
			{
				return;
			}
			SpriteManager manager = SpriteManager.Instance;
			if (manager == null)
			{
				return;
			}
			if (!manager.HasID(spriteName))
			{
				if (skinFallback)
				{
					ApplySkinIcon(icon);
				}
				else
				{
					ApplyFallbackIcon(icon, spriteName);
				}
				return;
			}
			ApplySprite(icon, sprite, spriteName);
		}

		private static void ApplySprite(RectTransform icon, SpriteBase sprite, string spriteName)
		{
			sprite.SpriteName = spriteName;
			if (sprite.IsDirty)
			{
				sprite.Apply();
				NormalizeScale(icon);
			}
		}

		/// <summary>把模板里那格 RawImage 换成程序生成的蒙皮图标。</summary>
		private static void ApplySkinIcon(RectTransform icon)
		{
			RawImage rawImage = icon.GetComponent<RawImage>();
			if (rawImage == null)
			{
				return;
			}
			rawImage.enabled = true;
			rawImage.texture = SkinPartIcon.Texture;
			rawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
			rawImage.rectTransform.sizeDelta = new Vector2(75f, 75f);
		}

		/// <summary>
		/// 图标彻底找不到时铺一张程序生成的占位图。
		/// </summary>
		/// <remarks>
		/// 关键是必须显式写 <c>uvRect</c>：模板 RawImage 贴的是整张图集，
		/// 只要 <c>uvRect</c> 停在 (0,0,1,1)，按钮上就是 4096×2048 的全图。
		/// 占位图是独立的小贴图，写 (0,0,1,1) 才是「整张占位图」。
		/// 警告按名字去重，否则每建一个按钮就刷一条。
		/// </remarks>
		private static void ApplyFallbackIcon(RectTransform icon, string spriteName)
		{
			RawImage rawImage = icon.GetComponent<RawImage>();
			if (rawImage == null)
			{
				return;
			}
			rawImage.enabled = true;
			rawImage.texture = PartFallbackIcon.Texture;
			rawImage.uvRect = new Rect(0f, 0f, 1f, 1f);
			rawImage.rectTransform.sizeDelta = new Vector2(75f, 75f);
			if (s_missingIcons.Add(spriteName))
			{
				Debug.Log("[PartSelector] no rendered icon '" + spriteName + "'，改用程序生成的占位图");
			}
		}

		/// <summary>已经报过的缺图名字，避免每次重建按钮都刷一条警告。</summary>
		private static readonly HashSet<string> s_missingIcons = new HashSet<string>();
	}
}
