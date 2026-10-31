using System;
using System.Collections.Generic;
using Bpsim.Collections;
using Bpsim.Serialization;
using Bpsim.Templates;
using Unity.Collections;
using Unity.Rendering.Authoring;
using Unity.VisualScripting;
using UnityEngine;

namespace Bpsim.Parts
{
	public class PartFactory : MonoBehaviour
	{
		[SerializeField]
		private PartResources m_resources;

		[SerializeField]
		private GameObject m_runtimePartList;

		private PartCollection<GameObjectTemplate> m_templateCollection;

		private PartCollection<ManagedPart> m_partCollection;

		private Dictionary<PartTypeInfo, PartExtensionData> m_partExtensionMap;

		public PartCollection<GameObjectTemplate> TemplateCollection => m_templateCollection;

		public PartCollection<ManagedPart> PartCollection => m_partCollection;

		public Dictionary<PartTypeInfo, PartExtensionData> PartExtensionMap => m_partExtensionMap;

		/// <summary>
		/// 取部件的物理参数（质量/动力/连接强度）。
		/// </summary>
		/// <remarks>
		/// <see cref="m_partExtensionMap"/> 是从 Resources 里的 JSON 一次性铺出来的，
		/// 只覆盖原版索引区间；Mod 变体的索引（9000+）不在里面，直接用
		/// <c>PartExtensionMap[info]</c> 会抛 <c>KeyNotFoundException</c>
		/// （<c>PartEntityFactory.cs:443</c> 与 <c>PartSimulator.cs:183</c> 都是这样）。
		/// 这里按 <see cref="PartVariants"/> 回退到基部件的参数：变体本来就只是「同一件部件的
		/// 另一种外观/形状」，物理属性应当与基部件一致。
		/// </remarks>
		public PartExtensionData GetExtension(PartTypeInfo info)
		{
			if (m_partExtensionMap == null)
			{
				return DefaultExtension;
			}
			if (m_partExtensionMap.TryGetValue(info, out PartExtensionData value))
			{
				return value;
			}
			PartTypeInfo baseInfo = PartVariants.Resolve(info);
			if (m_partExtensionMap.TryGetValue(baseInfo, out value))
			{
				return value;
			}
			return DefaultExtension;
		}

		/// <summary>查不到参数时的兜底：按「普通 1 质量的结构件」处理，不至于让模拟起不来。</summary>
		public static PartExtensionData DefaultExtension => new PartExtensionData(1f, 0f, 0f, false, false, BitDirection.None, 600f);

		/// <summary>
		/// 给模拟用的原生参数表：在 <see cref="m_partExtensionMap"/> 基础上，把每个 Mod 变体
		/// 按基部件的参数补进去。
		/// </summary>
		/// <remarks>
		/// 模拟里的 <c>ApplyPartsJob</c>（<c>PartSimulator.cs:183</c>）是 Burst job，
		/// 没法调用 <see cref="GetExtension"/> 那种走托管字典的回退，只能预先铺好。
		/// 不补的话变体在模拟一开始就会因为 <c>ExtensionMap[value]</c> 抛
		/// <c>KeyNotFoundException</c> 把整次模拟打断。
		/// </remarks>
		public NativeParallelHashMap<PartTypeInfo, PartExtensionData> ToNativeExtensionMap(Allocator allocator)
		{
			NativeParallelHashMap<PartTypeInfo, PartExtensionData> map = (m_partExtensionMap ?? new Dictionary<PartTypeInfo, PartExtensionData>()).ToNative(allocator);
			foreach (KeyValuePair<PartTypeInfo, PartTypeInfo> pair in PartVariants.All)
			{
				if (map.ContainsKey(pair.Key))
				{
					continue;
				}
				PartExtensionData value = m_partExtensionMap != null && m_partExtensionMap.TryGetValue(pair.Value, out PartExtensionData baseValue)
					? baseValue
					: DefaultExtension;
				map.TryAdd(pair.Key, value);
			}
			return map;
		}

		private void Awake()
		{
			BuildTemplates();
			BuildParts(OnPartCreated);
			BuildExtensionMap();
		}

		private void BuildTemplates()
		{
			PartCollection<GameObjectTemplate> partCollection = new PartCollection<GameObjectTemplate>();
			foreach (TextAsset templateGroup in m_resources.TemplateGroups)
			{
				foreach (GameObjectTemplate item in Json.Deserialize<List<GameObjectTemplate>>(templateGroup.text))
				{
					PartTemplate component = item.GetComponent<PartTemplate>();
					partCollection.AddPart(new PartTypeInfo(component.PartType, component.PartIndex), item);
				}
			}
			m_templateCollection = partCollection;
		}

		private void BuildParts(Action<ManagedPart> callback)
		{
			// 部件目录要重建了：旧变体连同它们的登记一起作废（Mod 会在重建后重新登记）。
			PartVariants.Clear();
			PartCollection<ManagedPart> partCollection = new PartCollection<ManagedPart>();
			for (int i = 0; i < m_resources.PartGroups.Count; i++)
			{
				GameObject gameObject = UnityEngine.Object.Instantiate(m_resources.PartGroups[i]);
				gameObject.transform.parent = m_runtimePartList.transform;
				for (int j = 0; j < gameObject.transform.childCount; j++)
				{
					ManagedPart component = gameObject.transform.GetChild(j).GetComponent<ManagedPart>();
					callback?.Invoke(component);
					partCollection.AddPart(component.TypeInfo, component);
				}
			}
			ModifyParts(partCollection);
			m_partCollection = partCollection;
			// 部件目录建好后通知已加载的 Mod（早于 Mod 加载时是空操作，Mod 加载后会补发一次）。
			Bpsim.Mods.ModHooks.NotifyPartsReady();
		}

		private void ModifyParts(PartCollection<ManagedPart> collection)
		{
			foreach (ManagedPart value in collection.FindParts(PartType.MetalFrame).Data.Values)
			{
				if (BasePart.BelongsTo(value.TypeInfo, BasePart.ColoredFrames) || BasePart.BelongsTo(value.TypeInfo, BasePart.TransparentFrames))
				{
					value.AddComponent<MaterialColor>().color = new Color(1f, 1f, 1f, 1f);
					value.transform.Find("Background").AddComponent<MaterialColor>().color = new Color(1f, 1f, 1f, 0.6f);
				}
			}
		}

		private void BuildExtensionMap()
		{
			List<PartExtensionPair> list = Json.Deserialize<List<PartExtensionPair>>(m_resources.ExtensionMap.text);
			Dictionary<PartTypeInfo, PartExtensionData> dictionary = new Dictionary<PartTypeInfo, PartExtensionData>();
			foreach (PartExtensionPair item in list)
			{
				for (int i = item.Key.PartStartIndex; i <= item.Key.PartEndIndex; i++)
				{
					dictionary.Add(new PartTypeInfo(item.Key.PartType, i), item.Value);
				}
			}
			m_partExtensionMap = dictionary;
		}

		private void OnPartCreated(ManagedPart part)
		{
			part.transform.position = new Vector3(2 * (int)part.PartType, -2 * part.PartIndex, 0f);
		}
	}
}
