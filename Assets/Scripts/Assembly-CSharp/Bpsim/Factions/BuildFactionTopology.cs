using System.Collections.Generic;
using Bpsim.Parts;
using Bpsim.Parts.Circuits;
using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Factions
{
	/// <summary>Read-only preview of BuildJointsAndComponentsJob; never creates physics or edits the schematic.</summary>
	public sealed class BuildFactionTopology
	{
		private readonly Dictionary<Entity, int> m_indices = new Dictionary<Entity, int>();
		private readonly List<Entity> m_parts = new List<Entity>();
		private readonly List<int> m_parents = new List<int>();
		private readonly Dictionary<int, int> m_teams = new Dictionary<int, int>();
		public int MarkerCount { get; private set; }

		public int GetTeams(Entity part)
		{
			return m_indices.TryGetValue(part, out int index) && m_teams.TryGetValue(Find(index), out int teams) ? teams : 0;
		}

		public void Rebuild(PartScene scene)
		{
			Rebuild(scene.Unmanaged);
		}

		public void Rebuild(in PartSceneUnmanaged scene)
		{
			m_indices.Clear(); m_parts.Clear(); m_parents.Clear(); m_teams.Clear(); MarkerCount = 0;
			EntityManager em = scene.EntityManager;
			foreach (Entity part in scene.Parts)
			{
				if (!em.Exists(part) || !em.HasComponent<PartExtensionComponent>(part)) continue;
				m_indices[part] = m_parts.Count;
				m_parents.Add(m_parts.Count); m_parts.Add(part);
				var type = em.GetComponentData<PartTypeValue>(part);
				if (FactionRules.IsMarker(new PartTypeInfo(type.Type, type.Index))) MarkerCount++;
			}
			if (MarkerCount == 0) return;
			for (int i = 0; i < m_parts.Count; i++)
			{
				Entity part = m_parts[i];
				var source = em.GetAspect<PartAspect>(part);
				int layer = em.HasComponent<PartBuildLayer>(part) ? PartBuildLayer.Clamp(em.GetComponentData<PartBuildLayer>(part).Value) : 1;
				if (BasePart.IsContainer(source.TypeInfo)) Union(part, source.ContainedPart);
				for (int d = 0; d < 4; d++)
				{
					BitDirection direction = BitDirectionExtensions.FromIndex(d);
					var (dx, dy) = direction.ToVector();
					if (!scene.PartMap.TryGet(source.CoordX + dx, source.CoordY + dy, layer, out var cell)) continue;
					Connect(em, part, in source, cell.PartContainer, direction);
					Connect(em, part, in source, cell.Part, direction);
				}
				// Upper layers are welded to the common layer's same cell / cardinal neighbours.
				if (layer <= 1) continue;
				for (int dx = -1; dx <= 1; dx++)
				for (int dy = -1; dy <= 1; dy++)
				{
					if (math.abs(dx) + math.abs(dy) > 1 || !scene.PartMap.TryGet(source.CoordX + dx, source.CoordY + dy, 1, out var cell)) continue;
					Union(part, cell.PartContainer); Union(part, cell.Part);
				}
			}
			for (int i = 0; i < m_parts.Count; i++)
			{
				var type = em.GetComponentData<PartTypeValue>(m_parts[i]);
				if (!FactionRules.IsMarker(new PartTypeInfo(type.Type, type.Index))) continue;
				int root = Find(i);
				m_teams.TryGetValue(root, out int teams);
				m_teams[root] = teams | FactionRules.TeamMask(em.GetComponentData<PartTransform>(m_parts[i]).Rotation);
			}
		}

		private void Connect(EntityManager em, Entity part, in PartAspect source, Entity target, BitDirection direction)
		{
			if (!m_indices.ContainsKey(target)) return;
			var other = em.GetAspect<PartAspect>(target);
			bool electrical = source.PartType == PartType.ElectricalPart && other.PartType == PartType.ElectricalPart &&
				(CircuitPart.GetTerminals(source.PartIndex, source.Rotation, source.Flipped) & direction) != 0 &&
				(CircuitPart.GetTerminals(other.PartIndex, other.Rotation, other.Flipped) & direction.Reverse()) != 0;
			if (electrical || PartSimulator.CanConnectTo(part, in source, target, in other, direction)) Union(part, target);
		}

		private int Find(int index)
		{
			while (m_parents[index] != index) { m_parents[index] = m_parents[m_parents[index]]; index = m_parents[index]; }
			return index;
		}
		private void Union(Entity a, Entity b)
		{
			if (!m_indices.TryGetValue(a, out int ia) || !m_indices.TryGetValue(b, out int ib)) return;
			int ra = Find(ia), rb = Find(ib);
			if (ra != rb) m_parents[rb] = ra;
		}
	}
}
