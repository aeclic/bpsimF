using System;
using System.Globalization;
using System.Text;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	[InternalBufferCapacity(4)]
	internal struct PartRemoteConnection : IBufferElementData
	{
		public Entity Target;

		internal static FixedList64Bytes<int3> Parse(string raw)
		{
			FixedList64Bytes<int3> cells = default;
			if (string.IsNullOrWhiteSpace(raw)) return cells;
			foreach (string token in raw.Split('|'))
			{
				string[] fields = token.Split(new[] { ':', ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
				if (fields.Length < 2 || fields.Length > 3 || !int.TryParse(fields[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ||
					!int.TryParse(fields[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)) continue;
				int layer = PartBuildLayer.MinLayer;
				if (fields.Length == 3 && !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out layer)) continue;
				int3 cell = new int3(x, y, PartBuildLayer.Clamp(layer));
				if (cells.IndexOf(cell) < 0) cells.Add(cell);
				if (cells.Length == 4) break;
			}
			return cells;
		}

		internal static string Format(in FixedList64Bytes<int3> cells)
		{
			if (cells.Length == 0) return string.Empty;
			StringBuilder result = new StringBuilder();
			for (int index = 0; index < cells.Length; index++)
			{
				if (index != 0) result.Append('|');
				int3 cell = cells[index];
				result.AppendFormat(CultureInfo.InvariantCulture, "{0}:{1}:{2}", cell.x, cell.y, cell.z);
			}
			return result.ToString();
		}

		internal static FixedList64Bytes<int3> Capture(DynamicBuffer<PartRemoteConnection> links, ComponentLookup<PartTransform> transforms, ComponentLookup<PartBuildLayer> layers, NativeParallelHashSet<Entity> allowedTargets = default)
		{
			FixedList64Bytes<int3> cells = default;
			foreach (PartRemoteConnection link in links)
			{
				if (link.Target == Entity.Null || (allowedTargets.IsCreated && !allowedTargets.Contains(link.Target)) ||
					!transforms.TryGetComponent(link.Target, out PartTransform transform)) continue;
				int layer = layers.TryGetComponent(link.Target, out PartBuildLayer buildLayer) ? PartBuildLayer.Clamp(buildLayer.Value) : PartBuildLayer.MinLayer;
				cells.Add(new int3(transform.X, transform.Y, layer));
			}
			return cells;
		}

		internal static bool TrySet(in PartSceneUnmanaged scene, Entity source, bool enabled, in FixedList64Bytes<int3> cells, bool requireTargets = false)
		{
			EntityManager manager = scene.EntityManager;
			if (!manager.Exists(source) || !manager.HasComponent<PartExtensionComponent>(source)) return false;
			if (!enabled)
			{
				manager.RemoveComponent<PartRemoteConnection>(source);
				return true;
			}

			PartExtensionData extension = manager.GetComponentData<PartExtensionComponent>(source).Value;
			int limit = (extension.IsConnectionSource || extension.HasCustomConnection) ? math.countbits((uint)extension.ConnectionDirection & 15u) : 0;
			if (requireTargets && cells.Length > limit) return false;
			FixedList64Bytes<Entity> targets = default;
			for (int index = 0; index < cells.Length && targets.Length < limit; index++)
			{
				int3 cell = cells[index];
				if (!scene.FindPartAt(cell.x, cell.y, cell.z, out Entity target) || target == source)
				{
					if (requireTargets) return false;
					continue;
				}
				if (targets.IndexOf(target) < 0) targets.Add(target);
			}
			DynamicBuffer<PartRemoteConnection> buffer = manager.HasBuffer<PartRemoteConnection>(source)
				? manager.GetBuffer<PartRemoteConnection>(source) : manager.AddBuffer<PartRemoteConnection>(source);
			buffer.Clear();
			foreach (Entity target in targets) buffer.Add(new PartRemoteConnection { Target = target });
			return true;
		}
	}
}
