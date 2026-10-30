using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Bpsim.Parts
{
	internal class PartEntitySpawner
	{
		private NativeParallelHashMap<PartTypeInfo, Entity> m_prefabCollection;

		public NativeParallelHashMap<PartTypeInfo, Entity> Collection => m_prefabCollection;

		public PartEntitySpawner(World world)
		{
			Mesh builtinResource = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
			Material material = CoreManager.Instance.Resources.LoadAsset<Material>("Part_Texture_0");
			PartEntityFactory.Context context = new PartEntityFactory.Context(world.EntityManager, material, builtinResource);
			m_prefabCollection = CreateEntityCollection(context);
		}

		private static NativeParallelHashMap<PartTypeInfo, Entity> CreateEntityCollection(PartEntityFactory.Context context)
		{
			PartCollection<ManagedPart> partCollection = PartManager.Instance.Factory.PartCollection;
			NativeParallelHashMap<PartTypeInfo, Entity> result = new NativeParallelHashMap<PartTypeInfo, Entity>(256, Allocator.Persistent);
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

		public Entity FindPrefab(PartTypeInfo info)
		{
			m_prefabCollection.TryGetValue(info, out var item);
			return item;
		}
	}
}
