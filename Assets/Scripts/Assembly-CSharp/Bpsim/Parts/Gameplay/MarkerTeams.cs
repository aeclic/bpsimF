using System.Collections.Generic;
using Bpsim.Parts.Simulation;
using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts.Gameplay
{
	public struct PartMarkerTeam : IComponentData
	{
		public int Component;
		public int Teams;
		public bool Diagonal;
	}
	internal static class MarkerTeams
	{
		private static readonly Dictionary<int, PartMarkerTeam> Teams = new Dictionary<int, PartMarkerTeam>();
		internal static void Refresh()
		{
			Teams.Clear();
			var em = PartGameplayUtil.Em;
			var parts = PartGameplayUtil.Simulator.Parts;
			bool previouslyTagged = false;
			foreach (Entity part in parts)
			{
				if (!em.Exists(part)) continue;
				previouslyTagged |= em.HasComponent<PartMarkerTeam>(part);
				var info = PartGameplayUtil.TypeInfo(part);
				if (info.PartType != PartType.Kicker || info.PartIndex != 3) continue;
				int component = PartGameplayUtil.ConnectedComponent(part);
				PartGameplayUtil.TryGetGrid(part, out _, out _, out int rotation, out _);
				Teams.TryGetValue(component, out var team);
				team.Component = component;
				team.Teams |= 1 << (rotation % 4);
				team.Diagonal |= rotation >= 4;
				Teams[component] = team;
			}
			if (Teams.Count == 0 && !previouslyTagged) return;
			foreach (Entity part in parts)
			{
				if (!em.Exists(part)) continue;
				int component = PartGameplayUtil.ConnectedComponent(part);
				Teams.TryGetValue(component, out var team);
				team.Component = component;
				if (em.HasComponent<PartMarkerTeam>(part)) em.SetComponentData(part, team);
				else em.AddComponentData(part, team);
			}
			if (MegaStructure.Active && em.Exists(MegaStructure.Body) && parts.Length > 0)
			{
				Teams.TryGetValue(PartGameplayUtil.ConnectedComponent(parts[0]), out var team);
				if (em.HasComponent<PartMarkerTeam>(MegaStructure.Body)) em.SetComponentData(MegaStructure.Body, team);
				else em.AddComponentData(MegaStructure.Body, team);
			}
		}
		internal static bool SameTeam(Entity a, Entity b)
		{
			int ca = PartGameplayUtil.ConnectedComponent(a), cb = PartGameplayUtil.ConnectedComponent(b);
			if (ca >= 0 && ca == cb) return true;
			return Teams.TryGetValue(ca, out var ta) && Teams.TryGetValue(cb, out var tb) && (ta.Teams & tb.Teams) != 0;
		}
	}
}
