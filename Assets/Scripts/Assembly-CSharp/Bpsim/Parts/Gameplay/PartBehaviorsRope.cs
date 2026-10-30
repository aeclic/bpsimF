using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// 绳索的玩家交互（BPSS <c>Rope.OnTouch</c>）：点一下就在最近的节点处把绳子砍断。
	/// </summary>
	/// <remarks>
	/// BPSS 会按点击位置找最近的节点（<c>FindInteractiveSegment</c>），限制在
	/// <c>[2, count-2]</c> 之间，然后把 <c>m_ropeCut</c> 置真、拆成两条 LineRenderer，
	/// 并调用 <c>Contraption.FindComponentsConnectedByRope()</c> 重算「哪些部件还挂在猪上」。
	/// bpsim 的绳索节点存在 <see cref="RopeNode" /> 缓冲区里，没有可点击的碰撞体，
	/// 所以这里退化成「从中间断」（BPSS 在 <c>hasPosition == false</c> 时也是这么做的）。
	/// </remarks>
	public sealed class RopeBehavior : PartBehavior
	{
		public override void Init()
		{
			Enabled = !Read().Cut;
		}

		public override void Trigger()
		{
			EntityManager entityManager = Em;
			if (!entityManager.HasComponent<Rope>(Part))
			{
				return;
			}
			Rope rope = entityManager.GetComponentData<Rope>(Part);
			if (rope.Cut)
			{
				return;
			}
			int count = (entityManager.HasBuffer<RopeNode>(Part) ? entityManager.GetBuffer<RopeNode>(Part).Length : Rope.SegmentCount);
			rope.Cut = true;
			rope.CutIndex = math.clamp(count / 2, 2, math.max(2, count - 2));
			entityManager.SetComponentData(Part, rope);
			Enabled = false;
			Manager.Log(Describe());
		}

		public override string Describe()
		{
			Rope rope = Read();
			return "rope#" + Info.PartIndex + " cut=" + rope.Cut + " at=" + rope.CutIndex;
		}

		private Rope Read()
		{
			EntityManager entityManager = Em;
			return (entityManager.HasComponent<Rope>(Part) ? entityManager.GetComponentData<Rope>(Part) : default(Rope));
		}
	}
}
