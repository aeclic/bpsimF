using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Bpsim.Parts
{
	/// <summary>
	/// 部件 → 预制体实体的映射表。
	/// </summary>
	/// <remarks>
	/// <para>这张表由 <see cref="PartManager.Start"/> 一次性铺好，而 Mod 的部件变体要等到
	/// <c>ModBootstrap</c>（AfterSceneLoad）之后才登记进 <c>PartCollection</c> ——
	/// 于是变体永远进不了这张表，<see cref="FindPrefab"/> 对它返回 <c>Entity.Null</c>，
	/// 放置逻辑（<c>PartDrawer</c> 的 <c>if (entity != Entity.Null)</c>、
	/// <c>PartSceneJobs.PlacePartsJob</c> 的 <c>PrefabCollection.TryGetValue</c>）直接跳过，
	/// 表现就是「选择器里能看到变体，拖到场上没反应」。</para>
	/// <para>所以这里必须支持<b>增量登记</b>：<see cref="AddPrefab"/> / <see cref="RemovePrefab"/>
	/// 让运行期新加的部件也能拿到预制体实体。</para>
	/// </remarks>
	internal class PartEntitySpawner
	{
		private NativeParallelHashMap<PartTypeInfo, Entity> m_prefabCollection;

		private PartEntityFactory.Context m_context;

		public NativeParallelHashMap<PartTypeInfo, Entity> Collection => m_prefabCollection;

		public PartEntitySpawner(World world)
		{
			Mesh builtinResource = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
			Material material = CoreManager.Instance.Resources.LoadAsset<Material>("Part_Texture_0");
			m_context = new PartEntityFactory.Context(world.EntityManager, material, builtinResource);
			m_prefabCollection = CreateEntityCollection(m_context);
		}

		private static NativeParallelHashMap<PartTypeInfo, Entity> CreateEntityCollection(PartEntityFactory.Context context)
		{
			PartCollection<ManagedPart> partCollection = PartManager.Instance.Factory.PartCollection;
			NativeParallelHashMap<PartTypeInfo, Entity> result = new NativeParallelHashMap<PartTypeInfo, Entity>(CountParts(partCollection) + 64, Allocator.Persistent);
			for (int i = 0; i < partCollection.Length; i++)
			{
				PartType partType = (PartType)i;
				if (!partCollection.TryFindParts(partType, out var entry))
				{
					continue;
				}
				foreach (KeyValuePair<int, ManagedPart> datum in entry.Data)
				{
					Entity item = PartEntityFactory.Create(context, datum.Value.gameObject, prefab: true);
					result.Add(new PartTypeInfo(partType, datum.Key), item);
				}
			}
			return result;
		}

		private static int CountParts(PartCollection<ManagedPart> partCollection)
		{
			int num = 0;
			for (int i = 0; i < partCollection.Length; i++)
			{
				if (partCollection.TryFindParts((PartType)i, out var entry))
				{
					num += entry.Data.Count;
				}
			}
			return num;
		}

		/// <summary>
		/// 给一个新登记的部件建预制体实体并写进表里。已存在则直接返回。
		/// </summary>
		/// <remarks>
		/// 调用方负责保证 <paramref name="part"/> 已经进了 <c>PartCollection</c>
		/// （<c>ModParts.TryAddVariant</c> 里是先 AddPart 再调这里）。
		/// </remarks>
		public bool AddPrefab(ManagedPart part)
		{
			if (part == null || m_context == null)
			{
				return false;
			}
			PartTypeInfo info = part.TypeInfo;
			if (m_prefabCollection.ContainsKey(info))
			{
				return true;
			}
			// 变体是运行期才冒出来的，容量可能在原版部件铺满后已经用尽。
			EnsureCapacity(m_prefabCollection.Count() + 1);
			Entity entity = PartEntityFactory.Create(m_context, part.gameObject, prefab: true);
			m_prefabCollection.Add(info, entity);
			return true;
		}

		/// <summary>移除一个部件的预制体实体（Mod 卸载时用）。</summary>
		public bool RemovePrefab(PartTypeInfo info)
		{
			if (m_context == null || !m_prefabCollection.TryGetValue(info, out Entity entity))
			{
				return false;
			}
			m_prefabCollection.Remove(info);
			// 预制体实体是一整棵子树，DestroyEntity 会连带 LinkedEntityGroup 里的子实体。
			EntityManager entityManager = m_context.EntityManager;
			if (entityManager.World != null && entityManager.World.IsCreated && entityManager.Exists(entity))
			{
				entityManager.DestroyEntity(entity);
			}
			return true;
		}

		private void EnsureCapacity(int required)
		{
			if (m_prefabCollection.Capacity >= required)
			{
				return;
			}
			int capacity = m_prefabCollection.Capacity;
			while (capacity < required)
			{
				capacity = Mathf.Max(64, capacity * 2);
			}
			// Capacity 的 setter 内部会 ReallocateHashMap，原地扩容即可，不必换表。
			m_prefabCollection.Capacity = capacity;
		}

		public Entity FindPrefab(PartTypeInfo info)
		{
			m_prefabCollection.TryGetValue(info, out var item);
			return item;
		}
	}
}
