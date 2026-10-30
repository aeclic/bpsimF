using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Properties;

namespace Bpsim.Parts
{
	public readonly partial struct PartAspect : IAspect, IQueryTypeParameter, IAspectCreate<PartAspect>
	{
		public readonly Entity Entity;
		// --- Generated members were removed here (ctor / AddComponentRequirementsTo / Lookup / ResolvedChunk /
		// --- TypeHandle / Enumerator / Query / CreateAspect / CompleteDependencyBeforeRO|RW): Entities 1.0.0-pre.44
		// --- emits them into PartAspect__Aspect_*.g.cs. Keeping the baked copies caused CS0111 duplicates.
		private readonly RefRO<PartTypeValue> m_partType;

		private readonly RefRW<PartTransform> m_partTransform;

		private readonly RefRW<ContainedPart> m_containedPart;

		private readonly RefRW<PartContainerValue> m_partContainer;

		private readonly RefRW<PartConnectedComponent> m_connectedComponent;

		private readonly RefRO<PartExtensionComponent> m_extensionComponent;

		public PartType PartType => m_partType.ValueRO.Type;

		public int PartIndex => m_partType.ValueRO.Index;

		public PartTypeInfo TypeInfo => new PartTypeInfo(m_partType.ValueRO.Type, m_partType.ValueRO.Index);

		public ref readonly PartExtensionData ExtensionData => ref m_extensionComponent.ValueRO.Value;

		[CreateProperty]
		public int CoordX
		{
			get
			{
				return m_partTransform.ValueRO.X;
			}
			set
			{
				m_partTransform.ValueRW.X = value;
			}
		}

		[CreateProperty]
		public int CoordY
		{
			get
			{
				return m_partTransform.ValueRO.Y;
			}
			set
			{
				m_partTransform.ValueRW.Y = value;
			}
		}

		[CreateProperty]
		public int Rotation
		{
			get
			{
				return m_partTransform.ValueRO.Rotation;
			}
			set
			{
				m_partTransform.ValueRW.Rotation = value;
			}
		}

		[CreateProperty]
		public bool Flipped
		{
			get
			{
				return m_partTransform.ValueRO.Flipped;
			}
			set
			{
				m_partTransform.ValueRW.Flipped = value;
			}
		}

		[CreateProperty]
		public Entity ContainedPart
		{
			get
			{
				return m_containedPart.ValueRO.Value;
			}
			set
			{
				m_containedPart.ValueRW.Value = value;
			}
		}

		[CreateProperty]
		public Entity PartContainer
		{
			get
			{
				return m_partContainer.ValueRO.Value;
			}
			set
			{
				m_partContainer.ValueRW.Value = value;
			}
		}

		[CreateProperty]
		public int ConnectedComponent
		{
			get
			{
				return m_connectedComponent.ValueRO.Index;
			}
			set
			{
				m_connectedComponent.ValueRW.Index = value;
			}
		}

		// pre.44 AspectGenerator emits IAspect/Query/CompleteDependencyBefore* but not IAspectCreate.CreateAspect,
		// so the interface member stays hand-written here.
		public PartAspect CreateAspect(Entity entity, ref SystemState systemState, bool isReadOnly)
		{
			return new Lookup(ref systemState, isReadOnly)[entity];
		}
	}
}
