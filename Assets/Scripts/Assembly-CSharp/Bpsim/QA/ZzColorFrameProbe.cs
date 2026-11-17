using System.Text;
using Bpsim.Parts;
using Bpsim.Rendering;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;

namespace Bpsim.QA
{
	/// <summary>
	/// 只读探针：按部件 dump 每个渲染实体的 <c>_BlendFactor</c>（BlendFactor）与 <c>_Color</c>
	/// （MaterialColor），用来判断彩色框为什么渲染成白块。
	/// 用 <c>invoke_static type=Bpsim.QA.ZzColorFrameProbe method=DumpBlend</c> 调用。
	/// </summary>
	public static class ZzColorFrameProbe
	{
		public static string DumpBlend()
		{
			World world = World.DefaultGameObjectInjectionWorld;
			if (world == null)
			{
				return "no world";
			}
			EntityManager em = world.EntityManager;
			EntityQuery query = em.CreateEntityQuery(
				ComponentType.ReadOnly<PartTag>(),
				ComponentType.ReadOnly<LinkedEntityGroup>());
			using NativeArray<Entity> roots = query.ToEntityArray(Allocator.Temp);
			StringBuilder sb = new StringBuilder();
			sb.Append("roots=").Append(roots.Length).Append('\n');
			int shown = 0;
			for (int r = 0; r < roots.Length && shown < 40; r++)
			{
				Entity root = roots[r];
				PartTypeValue pv = em.GetComponentData<PartTypeValue>(root);
				bool isFrame = em.HasComponent<ColoredFrame>(root);
				// 只关心彩色框和被包住的部件，别把整个场景刷屏。
				Entity container = em.HasComponent<PartContainerValue>(root)
					? em.GetComponentData<PartContainerValue>(root).Value : Entity.Null;
				bool containerIsFrame = container != Entity.Null && em.HasComponent<ColoredFrame>(container);
				if (!isFrame && !containerIsFrame)
				{
					continue;
				}
				shown++;
				sb.Append("root ").Append(root.Index)
					.Append(' ').Append(pv.Type).Append('#').Append(pv.Index)
					.Append(isFrame ? " CF" : " cf-sub")
					.Append(" container=").Append(container == Entity.Null ? "-" : container.Index.ToString());
				if (isFrame)
				{
					ColoredFrame cf = em.GetComponentData<ColoredFrame>(root);
					sb.Append(" color=").Append(cf.Color).Append(" transparent=").Append(cf.IsTransparent)
						.Append(" changed=").Append(cf.HasChanged);
				}
				DynamicBuffer<LinkedEntityGroup> group = em.GetBuffer<LinkedEntityGroup>(root, isReadOnly: true);
				for (int i = 0; i < group.Length; i++)
				{
					Entity child = group[i].Value;
					sb.Append("\n    [").Append(i).Append("] ").Append(NameOf(em, child)).Append(" id=").Append(child.Index);
					if (em.HasComponent<BlendFactor>(child))
					{
						sb.Append(" blend=").Append(em.GetComponentData<BlendFactor>(child).Value);
					}
					else
					{
						sb.Append(" blend=<none>");
					}
					if (em.HasComponent<MaterialColor>(child))
					{
						sb.Append(" mat=").Append(em.GetComponentData<MaterialColor>(child).Value);
					}
					else
					{
						sb.Append(" mat=<none>");
					}
					if (em.HasComponent<SpriteID>(child))
					{
						sb.Append(" sprite=").Append(em.GetComponentData<SpriteID>(child).Value);
					}
				}
				sb.Append('\n');
			}
			return sb.ToString();
		}

		private static string NameOf(EntityManager em, Entity entity)
		{
			try
			{
				return em.GetName(entity);
			}
			catch
			{
				return "?";
			}
		}
	}
}
