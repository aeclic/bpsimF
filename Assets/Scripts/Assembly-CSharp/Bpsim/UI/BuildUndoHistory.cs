using System;
using System.Collections.Generic;
using Bpsim.Parts;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;

namespace Bpsim.UI
{
	/// <summary>
	/// 建造撤销历史。bpsim 的场景本质上是一张 (X, Y, Type, Index, Rotation, Flipped) 的表，
	/// 所以这里用「整场快照」做撤销：每次会改动场景的操作开始前抓一张 <see cref="T:Bpsim.Parts.Schematics.Unit" />
	/// 数组，撤销时先清空场景再按快照重建。相比逐条反向操作，这种做法不会漏掉网格占用、
	/// 大件占格与相邻连接等隐式状态。
	/// </summary>
	internal sealed class BuildUndoHistory : IDisposable
	{
		public const int MaxDepth = 24;

		private readonly List<NativeArray<Schematics.Unit>> m_snapshots = new List<NativeArray<Schematics.Unit>>(MaxDepth);

		private readonly List<string> m_labels = new List<string>(MaxDepth);

		public bool CanUndo => m_snapshots.Count > 0;

		public int Count => m_snapshots.Count;

		public string PeekLabel => (m_snapshots.Count > 0) ? m_labels[m_snapshots.Count - 1] : string.Empty;

		/// <summary>在会改动场景的操作开始前调用，记录操作前的状态。</summary>
		public void Record(PartScene scene, string label)
		{
			if (scene == null)
			{
				return;
			}
			NativeArray<Schematics.Unit> snapshot = Capture(scene);
			m_snapshots.Add(snapshot);
			m_labels.Add(label ?? string.Empty);
			while (m_snapshots.Count > MaxDepth)
			{
				m_snapshots[0].Dispose();
				m_snapshots.RemoveAt(0);
				m_labels.RemoveAt(0);
			}
		}

		public bool Undo(PartScene scene)
		{
			if (scene == null || m_snapshots.Count == 0)
			{
				return false;
			}
			int index = m_snapshots.Count - 1;
			NativeArray<Schematics.Unit> snapshot = m_snapshots[index];
			m_snapshots.RemoveAt(index);
			m_labels.RemoveAt(index);
			Restore(scene, snapshot);
			snapshot.Dispose();
			return true;
		}

		public void Clear()
		{
			for (int i = 0; i < m_snapshots.Count; i++)
			{
				m_snapshots[i].Dispose();
			}
			m_snapshots.Clear();
			m_labels.Clear();
		}

		public void Dispose()
		{
			Clear();
		}

		private static NativeArray<Schematics.Unit> Capture(PartScene scene)
		{
			ref PartSceneUnmanaged unmanaged = ref scene.Unmanaged;
			unmanaged.EntityManager.CompleteAllTrackedJobs();
			int count = unmanaged.Parts.Length;
			NativeArray<Schematics.Unit> result = new NativeArray<Schematics.Unit>(count, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
			if (count > 0)
			{
				unmanaged.EntityManager.CompleteAllTrackedJobs();
				IJobExtensions.Run(PartSceneJobs.SaveParts(unmanaged.Parts, result));
			}
			return result;
		}

		private static void Restore(PartScene scene, NativeArray<Schematics.Unit> snapshot)
		{
			ref PartSceneUnmanaged unmanaged = ref scene.Unmanaged;
			unmanaged.EntityManager.CompleteAllTrackedJobs();
			unmanaged.ClearParts();
			if (snapshot.Length > 0)
			{
				NativeArray<Schematics.Unit> source = new NativeArray<Schematics.Unit>(snapshot.Length, Allocator.TempJob);
				source.CopyFrom(snapshot);
				IJobExtensions.Run(PartSceneJobs.PlaceParts(scene, false, source));
				source.Dispose();
			}
			unmanaged.EntityManager.CompleteAllTrackedJobs();
		}
	}
}
