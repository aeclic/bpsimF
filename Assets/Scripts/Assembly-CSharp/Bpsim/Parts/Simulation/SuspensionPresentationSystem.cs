using System.Collections.Generic;
using Bpsim.Parts.Gameplay;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;

namespace Bpsim.Parts.Simulation
{
	/// <summary>Hybrid line view driven by ECS anchors. No line object owns physics or an Update script.</summary>
	[UpdateInGroup(typeof(PresentationSystemGroup))]
	internal partial class SuspensionPresentationSystem : SystemBase
	{
		private EntityQuery m_query;
		private readonly Dictionary<Entity, LineRenderer> m_lines = new Dictionary<Entity, LineRenderer>();
		private readonly HashSet<Entity> m_visible = new HashSet<Entity>();
		private readonly List<Entity> m_removed = new List<Entity>();
		internal int LiveLineCount => m_lines.Count;
		protected override void OnCreate() => m_query = GetEntityQuery(ComponentType.ReadOnly<SuspensionRope>());
		protected override void OnUpdate()
		{
			if (PartManager.Instance == null || !PartManager.Instance.IsSimulating) { ClearRuntimeResources(); return; }
			if (Application.isBatchMode) return;
			Dependency.Complete(); m_visible.Clear();
			using var parts = m_query.ToEntityArray(Allocator.Temp);
			foreach (Entity part in parts)
			{
				var rope = EntityManager.GetComponentData<SuspensionRope>(part);
				if (!rope.Visible || !EntityManager.Exists(rope.Target)) continue;
				m_visible.Add(part);
				if (!m_lines.TryGetValue(part, out var line))
				{
					line = PartGameplayUtil.CreateLine("[Suspension rope] " + part.Index, Color.black, rope.Width);
					m_lines.Add(part, line);
				}
				var a = SuspensionRuntime.Point(EntityManager, part, rope.LocalAnchor);
				var b = SuspensionRuntime.Point(EntityManager, rope.Target, rope.ConnectedAnchor);
				PartGameplayUtil.SetLine(line, a, b, Color.black, a.z + .05f);
			}
			m_removed.Clear();
			foreach (var line in m_lines)
				if (!m_visible.Contains(line.Key)) { PartGameplayUtil.DestroyLine(line.Value); m_removed.Add(line.Key); }
			foreach (Entity part in m_removed) m_lines.Remove(part);
		}
		internal void ClearRuntimeResources()
		{
			foreach (var line in m_lines.Values) PartGameplayUtil.DestroyLine(line);
			m_lines.Clear(); m_visible.Clear(); m_removed.Clear();
		}
		protected override void OnDestroy() => ClearRuntimeResources();
	}
}
