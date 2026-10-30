using System;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

namespace Bpsim.Parts
{
	public struct PartSceneUnmanaged : IDisposable
	{
		private bool m_dirty;

		private int m_id;

		private NativeList<Entity> m_parts;

		private NativeParallelHashSet<Entity> m_deletedParts;

		private PartGridMap<PartGridInfo> m_partMap;

		private EntityManager m_entityManager;

		private int m_activeLayer;

		private int m_layerCount;

		public int SceneID => m_id;

		public EntityManager EntityManager => m_entityManager;

		public NativeList<Entity> Parts => m_parts;

		public PartGridMap<PartGridInfo> PartMap => m_partMap;

		/// <summary>当前活动建造层；0 = 全部（查看），只读。</summary>
		public int ActiveLayer => m_activeLayer;

		/// <summary>当前装配的层数（1..16）；层号与 <see cref="PartBuildLayer" /> 一致。</summary>
		public int LayerCount => m_layerCount;

		/// <summary>活动层为 0 时禁止建造编辑。</summary>
		public bool CanEdit => m_activeLayer >= PartBuildLayer.MinLayer;

		public void Initialize(int id, WorldUnmanaged world)
		{
			m_id = id;
			m_activeLayer = 1;
			m_layerCount = PartBuildLayer.DefaultLayerCount;
			m_parts = new NativeList<Entity>(256, Allocator.Persistent);
			m_deletedParts = new NativeParallelHashSet<Entity>(256, Allocator.Persistent);
			m_partMap = new PartGridMap<PartGridInfo>(256, Allocator.Persistent);
			m_entityManager = world.EntityManager;
		}

		/// <summary>切换活动建造层（0 = 全部查看）；层数不足时自动扩展到该层。</summary>
		public void SetActiveLayer(int layer)
		{
			int count = math.clamp(layer, 0, m_layerCount);
			m_activeLayer = count;
		}

		/// <summary>设置层数；不会删除已有零件（由调用方保证只收缩空层）。</summary>
		public void SetLayerCount(int count)
		{
			m_layerCount = math.clamp(count, PartBuildLayer.MinLayer, PartBuildLayer.MaxLayer);
			if (m_activeLayer > m_layerCount)
			{
				m_activeLayer = m_layerCount;
			}
		}

		/// <summary>在栈顶新加一层并切换过去；层数已满返回当前层号。</summary>
		public int AddLayer()
		{
			if (m_layerCount < PartBuildLayer.MaxLayer)
			{
				m_layerCount++;
			}
			m_activeLayer = m_layerCount;
			return m_activeLayer;
		}

		/// <summary>删除栈顶空层；顶层仍有零件时返回 false。</summary>
		public bool RemoveTopLayer()
		{
			if (m_layerCount <= PartBuildLayer.MinLayer || CountPartsOnLayer(m_layerCount) > 0)
			{
				return false;
			}
			m_layerCount--;
			if (m_activeLayer > m_layerCount)
			{
				m_activeLayer = m_layerCount;
			}
			return true;
		}

		public int GetPartLayer(Entity part)
		{
			return PartBuildLayerUtility.GetLayer(EntityManager, part);
		}

		public int CountPartsOnLayer(int layer)
		{
			int count = 0;
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity entity = m_parts[i];
				if (EntityManager.Exists(entity) && GetPartLayer(entity) == layer)
				{
					count++;
				}
			}
			return count;
		}

		/// <summary>
		/// 把单个部件移动到另一个建造层：先在目标层检查占位（容器/被包容件分别处理），
		/// 再从旧层表项移除、写入新层表项，最后两侧刷新附件与连接。
		/// </summary>
		public bool TrySetPartLayer(Entity part, int layer)
		{
			if (part == Entity.Null || !EntityManager.Exists(part) || !EntityManager.HasComponent<PartTransform>(part))
			{
				return false;
			}
			int target = PartBuildLayer.Clamp(layer);
			int oldLayer = GetPartLayer(part);
			if (target > m_layerCount)
			{
				SetLayerCount(target);
			}
			if (oldLayer == target)
			{
				return true;
			}
			PartTransform transform = EntityManager.GetComponentData<PartTransform>(part);
			PartTypeInfo typeInfo = EntityManager.GetComponentData<PartTypeValue>(part).Value;
			// 旧层：把该件从所在格的表项里摘掉。
			PartGridInfo oldGrid = default(PartGridInfo);
			if (m_partMap.TryGet(transform.X, transform.Y, oldLayer, out PartGridInfo existing))
			{
				oldGrid = existing;
				if (oldGrid.PartContainer == part)
				{
					oldGrid.PartContainer = Entity.Null;
				}
				if (oldGrid.Part == part)
				{
					oldGrid.Part = Entity.Null;
				}
				SetPartGridAt(transform.X, transform.Y, oldGrid, oldLayer);
			}
			// 新层：同格已有该类的件时不覆盖，回滚并失败。
			m_partMap.TryGet(transform.X, transform.Y, target, out PartGridInfo targetGrid);
			bool isContainer = BasePart.IsContainer(typeInfo);
			if (isContainer ? targetGrid.HasPartContainer : targetGrid.HasPart)
			{
				SetPartGridAt(transform.X, transform.Y, oldGrid, oldLayer);
				return false;
			}
			if (BasePart.IsLarge(typeInfo))
			{
				SetLargePartGridAt(transform.X, transform.Y, -1, part, oldLayer);
			}
			PartBuildLayerUtility.SetLayer(EntityManager, part, target);
			if (isContainer)
			{
				targetGrid.PartContainer = part;
			}
			else
			{
				targetGrid.Part = part;
			}
			SetPartGridAt(transform.X, transform.Y, targetGrid, target);
			if (BasePart.IsLarge(typeInfo))
			{
				SetLargePartGridAt(transform.X, transform.Y, 1, part, target);
			}
			UpdateNeighbours(transform.X, transform.Y, oldLayer);
			UpdateNeighbours(transform.X, transform.Y, target);
			SetDirty();
			return true;
		}

		public void SetDirty()
		{
			m_dirty = true;
		}

		public void Update()
		{
			if (!m_dirty)
			{
				return;
			}
			m_dirty = false;
			int length = m_parts.Length;
			int i = 0;
			int length2 = 0;
			while (i < length)
			{
				for (; i < length && m_deletedParts.Contains(m_parts[i]); i++)
				{
				}
				if (i < length)
				{
					m_parts[length2++] = m_parts[i++];
				}
			}
			m_parts.Resize(length2, NativeArrayOptions.UninitializedMemory);
			foreach (Entity deletedPart in m_deletedParts)
			{
				EntityManager.DestroyEntity(deletedPart);
			}
			m_deletedParts.Clear();
		}

		public void OnDestroy()
		{
			foreach (Entity part in m_parts)
			{
				EntityManager.DestroyEntity(part);
			}
		}

		public void Dispose()
		{
			m_parts.Dispose();
			m_partMap.Dispose();
			m_deletedParts.Dispose();
		}

		public Entity FindFirstPart(PartType partType, int partIndex = -1)
		{
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity entity = m_parts[i];
				PartTypeValue componentData = EntityManager.GetComponentData<PartTypeValue>(entity);
				if (componentData.Type == partType && (partIndex == -1 || componentData.Index == partIndex))
				{
					return entity;
				}
			}
			return Entity.Null;
		}

		public Entity FindLastPart(PartType partType, int partIndex = -1)
		{
			for (int num = m_parts.Length - 1; num >= 0; num--)
			{
				Entity entity = m_parts[num];
				PartTypeValue componentData = EntityManager.GetComponentData<PartTypeValue>(entity);
				if (componentData.Type == partType && (partIndex == -1 || componentData.Index == partIndex))
				{
					return entity;
				}
			}
			return Entity.Null;
		}

		public bool FindPartAt(int x, int y, out Entity part)
		{
			return FindPartAt(x, y, m_activeLayer, out part);
		}

		public bool FindPartAt(int x, int y, int layer, out Entity part)
		{
			if (layer >= PartBuildLayer.MinLayer && m_partMap.TryGet(x, y, layer, out var part2))
			{
				if (part2.HasPartContainer)
				{
					part = part2.PartContainer;
					return true;
				}
				if (part2.HasPart)
				{
					part = part2.Part;
					return true;
				}
			}
			part = Entity.Null;
			return false;
		}

		public bool FindPartGridAt(int x, int y, out PartGridInfo grid)
		{
			return FindPartGridAt(x, y, m_activeLayer, out grid);
		}

		public bool FindPartGridAt(int x, int y, int layer, out PartGridInfo grid)
		{
			if (layer >= PartBuildLayer.MinLayer)
			{
				return m_partMap.TryGet(x, y, layer, out grid);
			}
			grid = default(PartGridInfo);
			return false;
		}

		/// <summary>
		/// 按视觉位置拾取部件：命中形状是格坐标叠加 <see cref="PartFreeTransform" /> 后的最终位姿
		/// （偏移 + 连续旋转 + 非均匀缩放），而不是只认整数格。建造工具的点选、拖动、双击都走这里，
		/// 这样无网格平移过的部件点它「看起来所在的位置」就能选中。
		/// 同一点命中多个部件时，先取视觉中心最近的；被包容件（箱子里的东西）再加一点惩罚，避免外框抢走。
		/// 不回落网格查询：旧格位置必须真的点不到，否则用户仍会被拉回原来的格子。
		/// </summary>
		public bool TryFindPartAtWorld(float2 world, out Entity part)
		{
			return TryFindPartAtWorld(world, m_activeLayer, out part);
		}

		/// <summary>按指定层做视觉拾取。</summary>
		public bool TryFindPartAtWorld(float2 world, int layer, out Entity part)
		{
			part = Entity.Null;
			if (layer < PartBuildLayer.MinLayer)
			{
				return false;
			}
			Update();
			float bestScore = float.MaxValue;
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity candidate = m_parts[i];
				if (!EntityManager.Exists(candidate) || GetPartLayer(candidate) != layer || !EntityManager.HasComponent<PartTransform>(candidate))
				{
					continue;
				}
				PartTransform transform = EntityManager.GetComponentData<PartTransform>(candidate);
				PartTypeInfo typeInfo = EntityManager.GetComponentData<PartTypeValue>(candidate).Value;
				PartFreeTransform freeTransform = EntityManager.HasComponent<PostTransformScale>(candidate) ? GetPartFreeTransform(candidate) : PartFreeTransform.Identity;
				if (!PartPicking.Contains(in transform, in freeTransform, typeInfo, world))
				{
					continue;
				}
				float2 center = PartPicking.GetVisualCenter(in transform, in freeTransform);
				float score = math.lengthsq(world - center);
				if (BasePart.IsContainer(typeInfo))
				{
					// 框和里面的件视觉上同格：点里面的件时不能选中外框。
					score += 1f;
				}
				if (score < bestScore)
				{
					bestScore = score;
					part = candidate;
				}
			}
			return part != Entity.Null;
		}

		public bool PlacePartAt(in PartCreationRequest request, bool overlay)
		{
			if (!CanEdit)
			{
				return false;
			}
			int layer = PartBuildLayer.Clamp(request.Layer);
			if (layer > m_layerCount)
			{
				SetLayerCount(layer);
			}
			int coordX = request.CoordX;
			int coordY = request.CoordY;
			m_partMap.TryGet(coordX, coordY, layer, out var part);
			PartTypeInfo typeInfo = request.TypeInfo;
			if (!CanPlacePartAt(coordX, coordY, typeInfo, part.Occupied, layer))
			{
				return false;
			}
			bool flag = false;
			Entity part2 = Entity.Null;
			if (BasePart.IsContainer(typeInfo))
			{
				bool flag2 = !part.HasPart || BasePart.CanBeContained(EntityManager.GetComponentData<PartTypeValue>(part.Part).Value);
				if ((!part.HasPartContainer && flag2) || overlay)
				{
					if (part.HasPartContainer)
					{
						RemovePart(part.PartContainer);
					}
					if (part.HasPart && !flag2)
					{
						SetLargePartGridAt(coordX, coordY, -1, part.Part, layer);
						RemovePart(part.Part);
						part.Part = Entity.Null;
					}
					flag = true;
					part2 = (part.PartContainer = request.Submit(in this));
				}
			}
			else if (BasePart.CanBeContained(typeInfo))
			{
				if (!part.HasPart || overlay)
				{
					if (part.HasPart)
					{
						SetLargePartGridAt(coordX, coordY, -1, part.Part, layer);
						RemovePart(part.Part);
					}
					flag = true;
					part2 = (part.Part = request.Submit(in this));
				}
			}
			else if ((!part.HasPart && !part.HasPartContainer) || overlay)
			{
				if (part.HasPartContainer)
				{
					RemovePart(part.PartContainer);
					part.PartContainer = Entity.Null;
				}
				if (part.HasPart)
				{
					SetLargePartGridAt(coordX, coordY, -1, part.Part, layer);
					RemovePart(part.Part);
				}
				flag = true;
				part2 = (part.Part = request.Submit(in this));
			}
			if (flag)
			{
				AddPart(part2);
				UpdateContainer(part);
				SetPartGridAt(coordX, coordY, part, layer);
				SetLargePartGridAt(coordX, coordY, 1, part2, layer);
				UpdateNeighbours(coordX, coordY, layer);
				if (request.AutoAlign)
				{
					TryAutoAlignPart(part2, coordX, coordY, layer);
				}
			}
			return flag;
		}

		/// <summary>
		/// 按部件当前的格位与图层重算自动对齐朝向。
		/// </summary>
		/// <remarks>
		/// 拖动落位后走这里，等价于 BPSS <c>ConstructionUI.SetPartAt(..., autoalign: true)</c>：
		/// 玩家把部件从一个位置挪到另一个位置后，朝向要重新贴合新的邻居，而不是保持拎起来之前的朝向。
		/// </remarks>
		public bool TryAutoAlignPart(Entity part)
		{
			if (part == Entity.Null || !EntityManager.Exists(part) || !EntityManager.HasComponent<PartTransform>(part))
			{
				return false;
			}
			PartTransform transform = EntityManager.GetComponentData<PartTransform>(part);
			return TryAutoAlignPart(part, transform.X, transform.Y, GetPartLayer(part));
		}

		/// <summary>
		/// 落位后按邻居自动对齐朝向（BPSS <c>Contraption.AutoAlign</c>）。
		/// </summary>
		/// <remarks>
		/// 探测顺序与 BPSS 一致：左 → 右 → 上 → 下，第一个「朝我开口的连接源」决定朝向。
		/// 与 BPSS 的差别在于判定用的是 bpsim 自己的连接真值
		/// （<see cref="Bpsim.Parts.Simulation.PartSimulator.CanConnectTo" /> 的 target 侧条件：邻居是连接源，且它的端子掩码
		/// 旋转后包含「指向我」的方向），而不是 <c>JointConnectionType</c> 枚举。
		/// 绳子在 bpsim 里没有连接端子（扩展表 <c>dir=None src=False</c>），所以 BPSS 的绳子特例
		/// 在这里自然退化为「找不到邻居、保持原朝向」。
		/// </remarks>
		private bool TryAutoAlignPart(Entity part, int coordX, int coordY, int layer)
		{
			if (part == Entity.Null || !EntityManager.Exists(part))
			{
				return false;
			}
			PartTypeInfo typeInfo = EntityManager.GetComponentData<PartTypeValue>(part).Value;
			PartAutoAlignMode mode = PartAutoAlign.GetMode(typeInfo);
			if (mode == PartAutoAlignMode.None)
			{
				return false;
			}
			if (mode == PartAutoAlignMode.FlipVertically)
			{
				// 机翼/尾翼只关心「左右哪边是机架」：右边是机架就不镜像，左边是机架才镜像。
				if (IsChassisAt(coordX + 1, coordY, layer))
				{
					if (SetPartFlipped(part, flipped: false))
					{
						UpdateNeighbours(coordX, coordY, layer);
						return true;
					}
					return false;
				}
				if (IsChassisAt(coordX - 1, coordY, layer))
				{
					if (SetPartFlipped(part, flipped: true))
					{
						UpdateNeighbours(coordX, coordY, layer);
						return true;
					}
					return false;
				}
				return false;
			}
			if (!TryFindConnectionTarget(coordX, coordY, layer, out var target))
			{
				return false;
			}
			PartExtensionData extension = EntityManager.GetComponentData<PartExtensionComponent>(part).Value;
			PartTransform transform = EntityManager.GetComponentData<PartTransform>(part);
			if (!PartAutoAlign.TrySolveRotation(extension.ConnectionDirection, transform.Rotation, target, BasePart.IsEightWay(typeInfo), out var rotation))
			{
				return false;
			}
			if (rotation == transform.Rotation)
			{
				return false;
			}
			transform.Rotation = rotation;
			EntityManager.SetComponentData(part, transform);
			UpdateNeighbours(coordX, coordY, layer);
			return true;
		}

		/// <summary>按 BPSS 的顺序（左、右、上、下）找出第一个可以作为连接源朝我开口的邻居方向。</summary>
		private bool TryFindConnectionTarget(int coordX, int coordY, int layer, out BitDirection target)
		{
			// 方向 = 从我指向邻居；邻居的端子要能指向「反方向」才接得上。
			target = BitDirection.None;
			if (IsConnectionSourceFacing(coordX - 1, coordY, layer, BitDirection.Right))
			{
				target = BitDirection.Left;
				return true;
			}
			if (IsConnectionSourceFacing(coordX + 1, coordY, layer, BitDirection.Left))
			{
				target = BitDirection.Right;
				return true;
			}
			if (IsConnectionSourceFacing(coordX, coordY + 1, layer, BitDirection.Down))
			{
				target = BitDirection.Up;
				return true;
			}
			if (IsConnectionSourceFacing(coordX, coordY - 1, layer, BitDirection.Up))
			{
				target = BitDirection.Down;
				return true;
			}
			return false;
		}

		/// <summary>该格是否有连接源，且它的端子（按自身朝向旋转后）指向 <paramref name="facing" />。</summary>
		private bool IsConnectionSourceFacing(int x, int y, int layer, BitDirection facing)
		{
			if (!m_partMap.TryGet(x, y, layer, out var grid))
			{
				return false;
			}
			// 与 BuildJointsAndComponentsJob 一致：容器件优先，其次才是格内小件。
			if (grid.HasPartContainer && IsConnectionSourceFacing(grid.PartContainer, facing))
			{
				return true;
			}
			return grid.HasPart && IsConnectionSourceFacing(grid.Part, facing);
		}

		private bool IsConnectionSourceFacing(Entity part, BitDirection facing)
		{
			if (part == Entity.Null || !EntityManager.Exists(part) || !EntityManager.HasComponent<PartExtensionComponent>(part))
			{
				return false;
			}
			PartExtensionData extension = EntityManager.GetComponentData<PartExtensionComponent>(part).Value;
			if (!extension.IsConnectionSource)
			{
				return false;
			}
			int rotation = EntityManager.GetComponentData<PartTransform>(part).Rotation;
			return (extension.ConnectionDirection.Rotate(rotation) & facing) != 0;
		}

		private bool IsChassisAt(int x, int y, int layer)
		{
			if (!m_partMap.TryGet(x, y, layer, out var grid))
			{
				return false;
			}
			if (grid.HasPartContainer && PartAutoAlign.IsChassis(EntityManager.GetComponentData<PartTypeValue>(grid.PartContainer).Value))
			{
				return true;
			}
			return grid.HasPart && PartAutoAlign.IsChassis(EntityManager.GetComponentData<PartTypeValue>(grid.Part).Value);
		}

		/// <summary>设置镜像；返回是否真的发生了变化。</summary>
		private bool SetPartFlipped(Entity part, bool flipped)
		{
			if (!EntityManager.Exists(part) || !EntityManager.HasComponent<PartTransform>(part))
			{
				return false;
			}
			PartTransform transform = EntityManager.GetComponentData<PartTransform>(part);
			if (transform.Flipped == flipped)
			{
				return false;
			}
			transform.Flipped = flipped;
			EntityManager.SetComponentData(part, transform);
			return true;
		}

		public bool RemovePartAt(int x, int y, PartType partType = PartType.All, int partIndex = -1)
		{
			if (!CanEdit)
			{
				return false;
			}
			int layer = m_activeLayer;
			if (!m_partMap.TryGet(x, y, layer, out var part))
			{
				return false;
			}
			bool flag = false;
			if (part.HasPartContainer && Match(in this, part.PartContainer, partType, partIndex))
			{
				flag = true;
				RemovePart(part.PartContainer);
				part.PartContainer = Entity.Null;
			}
			if (part.HasPart && Match(in this, part.Part, partType, partIndex))
			{
				flag = true;
				SetLargePartGridAt(x, y, -1, part.Part, layer);
				RemovePart(part.Part);
				part.Part = Entity.Null;
			}
			if (flag)
			{
				UpdateContainer(part);
				SetPartGridAt(x, y, part, layer);
				UpdateNeighbours(x, y, layer);
			}
			return flag;
			static bool Match(in PartSceneUnmanaged scene, Entity entity, PartType partType2, int num)
			{
				PartTypeInfo value = scene.EntityManager.GetComponentData<PartTypeValue>(entity).Value;
				if (partType2 == PartType.All || partType2 == value.PartType)
				{
					if (num != -1)
					{
						return num == value.PartIndex;
					}
					return true;
				}
				return false;
			}
		}

		private void AddPart(Entity part)
		{
			m_parts.Add(in part);
		}

		private void RemovePart(Entity part)
		{
			SetDirty();
			m_deletedParts.Add(part);
		}

		private bool CanPlacePartAt(int x, int y, PartTypeInfo typeInfo, int occupied, int layer)
		{
			if (occupied > 0 && !BasePart.IsStructural(typeInfo))
			{
				return false;
			}
			if (BasePart.IsLarge(typeInfo))
			{
				RectInt gridRect = BasePart.GetGridRect(typeInfo);
				for (int i = gridRect.xMin; i < gridRect.xMax; i++)
				{
					for (int j = gridRect.yMin; j < gridRect.yMax; j++)
					{
						if ((i != 0 || j != 0) && m_partMap.TryGet(x + i, y + j, layer, out var part) && part.HasPart && !BasePart.IsStructural(EntityManager.GetComponentData<PartTypeValue>(part.Part).Value))
						{
							return false;
						}
					}
				}
			}
			return true;
		}

		private void SetPartGridAt(int x, int y, PartGridInfo grid, int layer)
		{
			if (grid.IsEmpty)
			{
				m_partMap.Remove(x, y, layer);
			}
			else
			{
				m_partMap.Set(x, y, layer, grid);
			}
		}

		private void SetLargePartGridAt(int x, int y, int delta, Entity part, int layer)
		{
			PartTypeInfo value = EntityManager.GetComponentData<PartTypeValue>(part).Value;
			if (!BasePart.IsLarge(value))
			{
				return;
			}
			RectInt gridRect = BasePart.GetGridRect(value);
			for (int i = gridRect.xMin; i < gridRect.xMax; i++)
			{
				for (int j = gridRect.yMin; j < gridRect.yMax; j++)
				{
					if (i != 0 || j != 0)
					{
						m_partMap.TryGet(x + i, y + j, layer, out var part2);
						part2.Occupied += delta;
						m_partMap.Set(x + i, y + j, layer, part2);
					}
				}
			}
		}

		private void UpdateContainer(PartGridInfo grid)
		{
			if (grid.HasPartContainer)
			{
				EntityManager.SetComponentData(grid.PartContainer, new ContainedPart
				{
					Value = grid.Part
				});
			}
			if (grid.HasPart)
			{
				EntityManager.SetComponentData(grid.Part, new PartContainerValue
				{
					Value = grid.PartContainer
				});
			}
		}

		public void UpdateNeighbours(in PartAspect partAspect)
		{
			UpdateNeighbours(partAspect.CoordX, partAspect.CoordY, m_activeLayer);
		}

		private void UpdateNeighbours(int x, int y, int layer)
		{
			UpdateNeighbour(x, y, layer);
			UpdateNeighbour(x + 1, y, layer);
			UpdateNeighbour(x, y + 1, layer);
			UpdateNeighbour(x - 1, y, layer);
			UpdateNeighbour(x, y - 1, layer);
		}

		private void UpdateNeighbour(int x, int y, int layer)
		{
			m_partMap.TryGet(x, y, layer, out var part);
			if (!part.HasPart)
			{
				return;
			}
			int attachmentCount = BasePart.GetAttachmentCount(EntityManager.GetComponentData<PartTypeValue>(part.Part).Value);
			if (attachmentCount == 0)
			{
				return;
			}
			NativeArray<Entity> attachments = new NativeArray<Entity>(attachmentCount, Allocator.Temp);
			foreach (Entity item in EntityManager.GetBuffer<LinkedEntityGroup>(part.Part).Reinterpret<Entity>())
			{
				EntityManager.GetName(item, out var name);
				int num = (int)BasePart.FindAttachment(in name);
				if (num != -1)
				{
					attachments[num] = item;
				}
			}
			AlignAttachments(part.Part, in attachments);
		}

		private void AlignAttachments(Entity part, in NativeArray<Entity> attachments)
		{
			PartTransform componentData = EntityManager.GetComponentData<PartTransform>(part);
			PartContainerValue componentData2 = EntityManager.GetComponentData<PartContainerValue>(part);
			int x = componentData.X;
			int y = componentData.Y;
			int num = componentData.Rotation % 8;
			int layer = GetPartLayer(part);
			bool flag = componentData2.Value != Entity.Null;
			Entity part2;
			bool flag2 = FindPartAt(x + 1, y, layer, out part2);
			Entity part3;
			bool flag3 = FindPartAt(x, y + 1, layer, out part3);
			Entity part4;
			bool flag4 = FindPartAt(x - 1, y, layer, out part4);
			Entity part5;
			bool flag5 = FindPartAt(x, y - 1, layer, out part5);
			for (int i = 0; i < attachments.Length; i++)
			{
				if (attachments[i] == Entity.Null)
				{
					continue;
				}
				bool flag6 = false;
				if (!flag)
				{
					if (i >= 4 == num >= 4)
					{
						int num2 = (i + num) % 4;
						flag6 = num2 switch
						{
							0 => flag2, 
							1 => flag3, 
							2 => flag4, 
							3 => flag5, 
							_ => throw new SwitchExpressionException(num2), 
						};
					}
					if (i == 3 && !flag2 && !flag3 && !flag4 && !flag5)
					{
						flag6 = true;
					}
				}
				if (EntityManager.HasComponent<DisableRendering>(attachments[i]) == flag6)
				{
					if (flag6)
					{
						EntityManager.RemoveComponent<DisableRendering>(attachments[i]);
					}
					else
					{
						EntityManager.AddComponent<DisableRendering>(attachments[i]);
					}
				}
			}
		}

		/// <summary>移除场景内所有部件并清空网格占用表，用于撤销后按快照重建。</summary>
		/// <summary>
		/// 清空整场：所有部件实体进入待销毁列表，网格表清空。
		/// </summary>
		/// <remarks>
		/// 早期实现是「逐个部件按 <c>PartTransform</c> 的坐标调 <c>RemovePartAt</c>」，遇到两个部件
		/// 共用同一格（叠加放置、或者某次搬运后表项与实体对不上）时，第二个部件找不到自己的表项，
		/// 于是留在 <c>m_parts</c> 里没被销毁 —— 撤销回放（先 <c>ClearParts</c> 再重新摆放）会因此
		/// 留下幽灵部件。这里改成直接按实体销毁，不再依赖网格表。
		/// </remarks>
		public void ClearParts()
		{
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity entity = m_parts[i];
				if (EntityManager.Exists(entity))
				{
					m_deletedParts.Add(entity);
				}
			}
			m_parts.Clear();
			m_partMap.Clear();
			SetDirty();
			Update();
		}

		/// <summary>
		/// 把 (srcX, srcY) 格上的内容（容器件 + 被包容件）整体移动到同层目标格。
		/// 只搬运网格表项与 PartTransform，不重建实体，所以包容关系、附件与连接状态都保持原样。
		/// </summary>
		public bool TryMovePartContent(int srcX, int srcY, int dstX, int dstY)
		{
			if (!CanEdit)
			{
				return false;
			}
			int layer = m_activeLayer;
			if (srcX == dstX && srcY == dstY)
			{
				return true;
			}
			if (!m_partMap.TryGet(srcX, srcY, layer, out var source))
			{
				return false;
			}
			if (!source.HasPartContainer && !source.HasPart)
			{
				return false;
			}
			m_partMap.TryGet(dstX, dstY, layer, out var target);
			if (target.HasPartContainer || target.HasPart)
			{
				return false;
			}
			// 取「代表实体」时优先容器件，与 FindPartAt 的优先级一致：
			// 一个格子里可以同时有铁框（结构性）和框内的小件，此时结构判定必须看铁框，
			// 否则铁框永远无法移到已被占用的格子上。
			Entity entity = (source.HasPartContainer ? source.PartContainer : source.Part);
			PartTypeInfo typeInfo = EntityManager.GetComponentData<PartTypeValue>(entity).Value;
			if (target.Occupied > 0 && !BasePart.IsStructural(typeInfo))
			{
				return false;
			}
			bool flag = BasePart.IsLarge(typeInfo);
			if (flag)
			{
				RectInt gridRect = BasePart.GetGridRect(typeInfo);
				for (int i = gridRect.xMin; i < gridRect.xMax; i++)
				{
					for (int j = gridRect.yMin; j < gridRect.yMax; j++)
					{
						if ((i != 0 || j != 0) && m_partMap.TryGet(dstX + i, dstY + j, layer, out var occupied) && occupied.Occupied > 0)
						{
							return false;
						}
					}
				}
			}
			Entity partContainer = source.PartContainer;
			Entity part = source.Part;
			source.PartContainer = Entity.Null;
			source.Part = Entity.Null;
			if (flag)
			{
				SetLargePartGridAt(srcX, srcY, -1, entity, layer);
			}
			SetPartGridAt(srcX, srcY, source, layer);
			if (partContainer != Entity.Null)
			{
				SetPartPosition(partContainer, dstX, dstY);
			}
			if (part != Entity.Null)
			{
				SetPartPosition(part, dstX, dstY);
			}
			target.PartContainer = partContainer;
			target.Part = part;
			m_partMap.Set(dstX, dstY, layer, target);
			if (flag)
			{
				SetLargePartGridAt(dstX, dstY, 1, entity, layer);
			}
			UpdateNeighbours(srcX, srcY, layer);
			UpdateNeighbours(dstX, dstY, layer);
			return true;
		}

		/// <summary>按部件自身步长步进旋转（4 向部件 90°，8 向部件 45°）。</summary>
		public bool TryRotatePart(Entity part, int delta)
		{
			if (delta == 0 || !EntityManager.Exists(part))
			{
				return false;
			}
			PartTransform transform = EntityManager.GetComponentData<PartTransform>(part);
			transform.Rotation += delta;
			EntityManager.SetComponentData(part, transform);
			UpdateNeighbours(transform.X, transform.Y, GetPartLayer(part));
			return true;
		}

		/// <summary>
		/// 点按旋转：按部件自身的步长前进一个「朝向单位」（普通件 90°，八向件 45°）。
		/// </summary>
		/// <remarks>
		/// BPSS 的短按旋转走 <c>Contraption.Flip</c>：可翻转件（机翼/尾翼）在「不镜像」与「镜像」之间切换，
		/// 其余件顺时针转一格，并最多重试 5 次直到部件报告「连得上」。
		/// bpsim 的朝向权威是 <see cref="PartTransform.Rotation" />（连接判定
		/// <see cref="Bpsim.Parts.Simulation.PartSimulator.CanConnectTo" /> 直接读它），所以这里只步进
		/// 离散朝向，不动 <see cref="PartFreeTransform.RotationDegrees" />（那是玩家叠加的精细角，
		/// 由 <see cref="PartFreeTransformSystem" /> 合成到视觉上）。
		/// </remarks>
		public bool TryStepPartRotation(Entity part)
		{
			if (part == Entity.Null || !EntityManager.Exists(part) || !EntityManager.HasComponent<PartTransform>(part))
			{
				return false;
			}
			PartTypeInfo typeInfo = EntityManager.GetComponentData<PartTypeValue>(part).Value;
			if (BasePart.CanFlip(typeInfo))
			{
				// 可翻转件按 BPSS 的 FlipVertically 语义翻转，而不是转 90°。
				PartTransform transform = EntityManager.GetComponentData<PartTransform>(part);
				transform.Flipped = !transform.Flipped;
				EntityManager.SetComponentData(part, transform);
				UpdateNeighbours(transform.X, transform.Y, GetPartLayer(part));
				return true;
			}
			// 八向件（抓手/探照灯）的 Rotation 每步 45°，普通件每步 90°，都只前进 1。
			return TryRotatePart(part, 1);
		}

		/// <summary>整场按格平移（整车平移）。移动后重建网格占用表。</summary>
		public bool TryMoveAllParts(int deltaX, int deltaY)
		{
			if ((deltaX == 0 && deltaY == 0) || m_parts.Length == 0)
			{
				return false;
			}
			Update();
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity entity = m_parts[i];
				if (EntityManager.Exists(entity))
				{
					PartTransform transform = EntityManager.GetComponentData<PartTransform>(entity);
					transform.X += deltaX;
					transform.Y += deltaY;
					EntityManager.SetComponentData(entity, transform);
				}
			}
			RebuildGrid();
			SetDirty();
			return true;
		}

		/// <summary>
		/// 整场绕包围盒中心旋转 90° 的整数倍（整车旋转）。位置按 <c>delta</c> 个 90° 逆时针转动，
		/// 每个部件自身的朝向同步累加，所以部件之间的相对布局保持不变。旋转后重建网格占用表。
		/// </summary>
		public bool TryRotateAllParts(int delta)
		{
			int quarter = ((delta % 4) + 4) % 4;
			if (quarter == 0 || m_parts.Length == 0)
			{
				return false;
			}
			Update();
			int minX = int.MaxValue;
			int minY = int.MaxValue;
			int maxX = int.MinValue;
			int maxY = int.MinValue;
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity entity = m_parts[i];
				if (!EntityManager.Exists(entity))
				{
					continue;
				}
				PartTransform transform = EntityManager.GetComponentData<PartTransform>(entity);
				minX = Math.Min(minX, transform.X);
				minY = Math.Min(minY, transform.Y);
				maxX = Math.Max(maxX, transform.X);
				maxY = Math.Max(maxY, transform.Y);
			}
			if (minX > maxX)
			{
				return false;
			}
			int centerX = (minX + maxX) / 2;
			int centerY = (minY + maxY) / 2;
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity entity = m_parts[i];
				if (!EntityManager.Exists(entity))
				{
					continue;
				}
				PartTransform transform = EntityManager.GetComponentData<PartTransform>(entity);
				int dx = transform.X - centerX;
				int dy = transform.Y - centerY;
				int x;
				int y;
				switch (quarter)
				{
				case 1:
					x = -dy;
					y = dx;
					break;
				case 2:
					x = -dx;
					y = -dy;
					break;
				default:
					x = dy;
					y = -dx;
					break;
				}
				transform.X = centerX + x;
				transform.Y = centerY + y;
				transform.Rotation += delta;
				EntityManager.SetComponentData(entity, transform);
			}
			RebuildGrid();
			SetDirty();
			return true;
		}

		private void SetPartPosition(Entity part, int x, int y)
		{
			PartTransform transform = EntityManager.GetComponentData<PartTransform>(part);
			transform.X = x;
			transform.Y = y;
			EntityManager.SetComponentData(part, transform);
		}

		// ------------------------------------------------------------- 自由变换（无网格层）
		// 下面这些方法只读写 PartFreeTransform：格子坐标（PartTransform）和网格占用表保持不变，
		// 所以自由偏移/连续角/缩放不会影响连接、包容、90° 整车旋转等建造判定。
		// 视觉结果由 PartFreeTransformSystem 合成到 LocalTransform / PostTransformScale。

		/// <summary>读取自由变换；实体不存在或没有该组件时返回默认值（偏移 0、角度 0、缩放 1）。</summary>
		public PartFreeTransform GetPartFreeTransform(Entity part)
		{
			if (EntityManager.Exists(part) && EntityManager.HasComponent<PartFreeTransform>(part))
			{
				return PartFreeTransform.Sanitize(EntityManager.GetComponentData<PartFreeTransform>(part));
			}
			return PartFreeTransform.Identity;
		}

		public bool TryGetPartFree(Entity part, out PartFreeTransform freeTransform)
		{
			if (EntityManager.Exists(part) && EntityManager.HasComponent<PartFreeTransform>(part))
			{
				freeTransform = PartFreeTransform.Sanitize(EntityManager.GetComponentData<PartFreeTransform>(part));
				return true;
			}
			freeTransform = PartFreeTransform.Identity;
			return false;
		}

		/// <summary>整份写入自由变换（缺组件时补上），返回实体是否存在。</summary>
		public bool TrySetPartFree(Entity part, in PartFreeTransform freeTransform)
		{
			if (!EntityManager.Exists(part))
			{
				return false;
			}
			PartFreeTransform value = PartFreeTransform.Sanitize(freeTransform);
			if (EntityManager.HasComponent<PartFreeTransform>(part))
			{
				EntityManager.SetComponentData(part, value);
			}
			else
			{
				EntityManager.AddComponentData(part, value);
			}
			// 自由缩放由 PostTransformScale 承载；不是所有创建路径（例如玩法脚本直接 Instantiate）
			// 都会经过 PartCreationRequest，所以这里兜底补上，否则 PartFreeTransformSystem 的查询会漏掉该实体。
			if (!EntityManager.HasComponent<PostTransformScale>(part))
			{
				EntityManager.AddComponentData(part, new PostTransformScale
				{
					Value = float3x3.Scale(value.Scale.x, value.Scale.y, 1f)
				});
			}
			// 同上的兜底：根的非等比缩放只有走 LocalToWorld 矩阵路径才能传给子渲染实体，
			// 否则建造界面里部件外观不跟着整车缩放变化（模拟期由物理插值补上，所以只在建造期可见）。
			if (!EntityManager.HasComponent<PropagateLocalToWorld>(part))
			{
				EntityManager.AddComponent<PropagateLocalToWorld>(part);
			}
			PartBuildLayerUtility.EnsureLayer(EntityManager, part);
			return true;
		}

		/// <summary>设置相对格坐标的平移偏移（世界单位）。不动网格表。</summary>
		public bool TryMovePartFree(Entity part, float2 offset)
		{
			if (!EntityManager.Exists(part))
			{
				return false;
			}
			PartFreeTransform freeTransform = GetPartFreeTransform(part);
			freeTransform.Offset = offset;
			return TrySetPartFree(part, in freeTransform);
		}

		/// <summary>给部件叠加连续旋转角（度）；离散朝向 Rotation 不变。</summary>
		public bool TryRotatePartFree(Entity part, float rotationDegrees)
		{
			if (!EntityManager.Exists(part))
			{
				return false;
			}
			PartFreeTransform freeTransform = GetPartFreeTransform(part);
			freeTransform.RotationDegrees = rotationDegrees;
			return TrySetPartFree(part, in freeTransform);
		}

		/// <summary>设置部件根节点的附加缩放；单轴钳制到 BPRC 的 0.05 ~ 20。</summary>
		public bool TryScalePartFree(Entity part, float2 scale)
		{
			if (!EntityManager.Exists(part))
			{
				return false;
			}
			PartFreeTransform freeTransform = GetPartFreeTransform(part);
			freeTransform.Scale = scale;
			return TrySetPartFree(part, in freeTransform);
		}

		/// <summary>整场自由平移：所有部件的 Offset 累加，格子坐标不动。</summary>
		public bool TryTranslateAllPartsFree(float2 delta)
		{
			if ((delta.x == 0f && delta.y == 0f) || m_parts.Length == 0)
			{
				return false;
			}
			Update();
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity entity = m_parts[i];
				if (!EntityManager.Exists(entity))
				{
					continue;
				}
				PartFreeTransform freeTransform = GetPartFreeTransform(entity);
				freeTransform.Offset += delta;
				TrySetPartFree(entity, in freeTransform);
			}
			return true;
		}

		/// <summary>整场绕世界枢轴自由旋转：视觉位置绕 <paramref name="pivot" /> 转动，每个部件同时累加连续角。</summary>
		public bool TryRotateAllPartsFree(float degrees, float2 pivot)
		{
			if (degrees == 0f || m_parts.Length == 0)
			{
				return false;
			}
			Update();
			float radians = math.radians(degrees);
			float cos = math.cos(radians);
			float sin = math.sin(radians);
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity entity = m_parts[i];
				if (!EntityManager.Exists(entity))
				{
					continue;
				}
				PartTransform transform = EntityManager.GetComponentData<PartTransform>(entity);
				PartFreeTransform freeTransform = GetPartFreeTransform(entity);
				float2 world = new float2(transform.X, transform.Y) + freeTransform.Offset;
				float2 local = world - pivot;
				float2 rotated = pivot + new float2(local.x * cos - local.y * sin, local.x * sin + local.y * cos);
				freeTransform.Offset = rotated - new float2(transform.X, transform.Y);
				freeTransform.RotationDegrees += degrees;
				TrySetPartFree(entity, in freeTransform);
			}
			return true;
		}

		/// <summary>整场绕世界枢轴等比缩放：视觉位置随枢轴伸缩，每个部件同时乘上缩放倍数。</summary>
		public bool TryScaleAllPartsFree(float factor, float2 pivot)
		{
			if (factor <= 0f || math.abs(factor - 1f) < 0.00001f || m_parts.Length == 0)
			{
				return false;
			}
			Update();
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity entity = m_parts[i];
				if (!EntityManager.Exists(entity))
				{
					continue;
				}
				PartTransform transform = EntityManager.GetComponentData<PartTransform>(entity);
				PartFreeTransform freeTransform = GetPartFreeTransform(entity);
				float2 cell = new float2(transform.X, transform.Y);
				float2 world = cell + freeTransform.Offset;
				freeTransform.Offset = pivot + (world - pivot) * factor - cell;
				freeTransform.Scale *= factor;
				TrySetPartFree(entity, in freeTransform);
			}
			return true;
		}

		/// <summary>整场视觉包围盒（含自由偏移与缩放），用于整车 gizmo 的枢轴与尺寸。</summary>
		public bool TryGetPartsBounds(out float2 min, out float2 max)
		{
			min = float2.zero;
			max = float2.zero;
			Update();
			bool hasValue = false;
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity entity = m_parts[i];
				if (!EntityManager.Exists(entity))
				{
					continue;
				}
				PartTransform transform = EntityManager.GetComponentData<PartTransform>(entity);
				PartFreeTransform freeTransform = GetPartFreeTransform(entity);
				float2 world = new float2(transform.X, transform.Y) + freeTransform.Offset;
				float2 half = 0.5f * freeTransform.Scale;
				float2 partMin = world - half;
				float2 partMax = world + half;
				if (!hasValue)
				{
					min = partMin;
					max = partMax;
					hasValue = true;
				}
				else
				{
					min = math.min(min, partMin);
					max = math.max(max, partMax);
				}
			}
			return hasValue;
		}

		/// <summary>整场视觉包围盒中心；场景为空时返回原点。</summary>
		public float2 GetPartsCenter()
		{
			return TryGetPartsBounds(out var min, out var max) ? (min + max) * 0.5f : float2.zero;
		}

		private void RebuildGrid()
		{
			m_partMap.Clear();
			for (int i = 0; i < m_parts.Length; i++)
			{
				Entity entity = m_parts[i];
				if (!EntityManager.Exists(entity))
				{
					continue;
				}
				int layer = GetPartLayer(entity);
				PartTransform transform = EntityManager.GetComponentData<PartTransform>(entity);
				PartTypeInfo typeInfo = EntityManager.GetComponentData<PartTypeValue>(entity).Value;
				m_partMap.TryGet(transform.X, transform.Y, layer, out var grid);
				if (BasePart.IsContainer(typeInfo))
				{
					grid.PartContainer = entity;
				}
				else
				{
					grid.Part = entity;
				}
				m_partMap.Set(transform.X, transform.Y, layer, grid);
				if (BasePart.IsLarge(typeInfo))
				{
					RectInt gridRect = BasePart.GetGridRect(typeInfo);
					for (int j = gridRect.xMin; j < gridRect.xMax; j++)
					{
						for (int k = gridRect.yMin; k < gridRect.yMax; k++)
						{
							if (j != 0 || k != 0)
							{
								m_partMap.TryGet(transform.X + j, transform.Y + k, layer, out var occupied);
								occupied.Occupied++;
								m_partMap.Set(transform.X + j, transform.Y + k, layer, occupied);
							}
						}
					}
				}
			}
		}
	}
}
