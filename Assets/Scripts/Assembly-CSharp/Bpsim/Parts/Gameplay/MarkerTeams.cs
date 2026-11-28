using System.Collections.Generic;
using Bpsim.Factions;
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
		private static int s_lastPartCount = -1;
		private static int s_lastJointCount = -1;
		private static bool s_dirty = true;

		internal static void MarkDirty()
		{
			s_dirty = true;
		}

		internal static void Reset()
		{
			Teams.Clear();
			s_lastPartCount = -1;
			s_lastJointCount = -1;
			s_dirty = true;
		}

		internal static void Refresh()
		{
			var simulator = PartGameplayUtil.Simulator;
			var parts = simulator.Parts;
			int partCount = parts.Length;
			int jointCount = simulator.Joints.Length;
			if (!s_dirty && partCount == s_lastPartCount && jointCount == s_lastJointCount) return;

			Teams.Clear();
			var em = PartGameplayUtil.Em;
			bool previouslyTagged = false;
			foreach (Entity part in parts)
			{
				if (!em.Exists(part)) continue;
				previouslyTagged |= em.HasComponent<PartMarkerTeam>(part);
				var info = PartGameplayUtil.TypeInfo(part);
				if (!FactionRules.IsMarker(info)) continue;
				int component = PartGameplayUtil.ConnectedComponent(part);
				PartGameplayUtil.TryGetGrid(part, out _, out _, out int rotation, out _);
				Teams.TryGetValue(component, out var team);
				team.Component = component;
				team.Teams |= FactionRules.TeamMask(rotation);
				team.Diagonal |= rotation >= 4;
				Teams[component] = team;
			}
			if (Teams.Count == 0 && !previouslyTagged)
			{
				s_lastPartCount = partCount;
				s_lastJointCount = jointCount;
				s_dirty = false;
				return;
			}
			foreach (Entity part in parts)
			{
				if (!em.Exists(part)) continue;
				int component = PartGameplayUtil.ConnectedComponent(part);
				Teams.TryGetValue(component, out var team);
				team.Component = component;
				if (em.HasComponent<PartMarkerTeam>(part))
				{
					PartMarkerTeam current = em.GetComponentData<PartMarkerTeam>(part);
					if (current.Component != team.Component || current.Teams != team.Teams || current.Diagonal != team.Diagonal)
						em.SetComponentData(part, team);
				}
				else em.AddComponentData(part, team);
			}
			if (MegaStructure.Active && em.Exists(MegaStructure.Body) && parts.Length > 0)
			{
				Teams.TryGetValue(PartGameplayUtil.ConnectedComponent(parts[0]), out var team);
				if (em.HasComponent<PartMarkerTeam>(MegaStructure.Body))
				{
					PartMarkerTeam current = em.GetComponentData<PartMarkerTeam>(MegaStructure.Body);
					if (current.Component != team.Component || current.Teams != team.Teams || current.Diagonal != team.Diagonal)
						em.SetComponentData(MegaStructure.Body, team);
				}
				else em.AddComponentData(MegaStructure.Body, team);
			}
			s_lastPartCount = partCount;
			s_lastJointCount = jointCount;
			s_dirty = false;
		}
		internal static bool SameTeam(Entity a, Entity b)
		{
			int ca = PartGameplayUtil.ConnectedComponent(a), cb = PartGameplayUtil.ConnectedComponent(b);
			Teams.TryGetValue(ca, out var ta); Teams.TryGetValue(cb, out var tb);
			return FactionRules.SameTeam(ca, ta.Teams, cb, tb.Teams);
		}
	}
}
