using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Unity.Mathematics;
using UnityEngine;

namespace Bpsim.Rendering
{
	public class GpuRenderer
	{
		public readonly struct RenderInfo
		{
			public readonly Mesh Mesh;

			public readonly ImmutableArray<ComputeBuffer> Buffers;

			public readonly RenderParams RenderParams;

			public readonly int Count;

			public RenderInfo(Mesh mesh, Material material, ImmutableArray<ComputeBuffer> buffers, Bounds bounds, int count)
			{
				Mesh = mesh;
				Buffers = buffers;
				RenderParams = new RenderParams(material);
				RenderParams.worldBounds = bounds;
				Count = count;
			}

			public RenderInfo(Mesh mesh, ImmutableArray<ComputeBuffer> buffers, in RenderParams renderParams, int count)
			{
				Mesh = mesh;
				Buffers = buffers;
				RenderParams = renderParams;
				Count = count;
			}
		}

		public struct Properties : IComparable<Properties>
		{
			public float4 Position;

			public float2x2 Rotation;

			public float4 UV;

			public float4 Color;

			public const int Size = 64;

			public int CompareTo(Properties other)
			{
				return -Position.z.CompareTo(other.Position.z);
			}
		}

		private List<RenderInfo> m_renderInfo;

		public int Count => m_renderInfo.Count;

		public GpuRenderer()
		{
			m_renderInfo = new List<RenderInfo>();
		}

		public void SubmitBatch(Mesh mesh, Material material, ComputeBuffer buffer, Bounds bounds, int count)
		{
			m_renderInfo.Add(new RenderInfo(mesh, material, ImmutableArray.Create(buffer), bounds, count));
		}

		public void SubmitBatch(Mesh mesh, Material material, IEnumerable<ComputeBuffer> buffers, Bounds bounds, int count)
		{
			m_renderInfo.Add(new RenderInfo(mesh, material, buffers.ToImmutableArray(), bounds, count));
		}

		public void Render()
		{
			foreach (RenderInfo item in m_renderInfo)
			{
				RenderInfo current = item;
				Graphics.RenderMeshPrimitives(in current.RenderParams, current.Mesh, 0, current.Count);
			}
		}

		public void Release()
		{
			foreach (RenderInfo item in m_renderInfo)
			{
				ImmutableArray<ComputeBuffer>.Enumerator enumerator2 = item.Buffers.GetEnumerator();
				while (enumerator2.MoveNext())
				{
					enumerator2.Current.Release();
				}
			}
			m_renderInfo.Clear();
		}
	}
}
