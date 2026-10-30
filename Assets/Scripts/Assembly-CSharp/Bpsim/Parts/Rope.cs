using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	/// <summary>
	/// 绳索（BPSS <c>Rope</c>）：把左右两端的部件用一条 8 节的链子连起来，可以拉长、可以砍断。
	/// </summary>
	/// <remarks>
	/// BPSS 的做法是实例化 8 个 <c>RopeSegment</c> 刚体（每个 mass 0.05、SphereCollider 0.09），
	/// 相邻两节之间挂 <c>HingeJoint</c>（axis = forward），两端的节再铰接到左右部件上；
	/// 每帧用 <c>FixedUpdate</c> 手写弹簧力（拉伸 70、压缩 50、端点 750）把链子维持成「绳」，
	/// 再用 <c>LineRenderer</c> + Catmull-Rom 画 20 个采样点。
	///
	/// bpsim 侧不复制这 8 个刚体（它们只是绳子的内部自由度，玩家看不到也碰不到 ——
	/// <c>CollidableRope</c> 默认 false）：节点位置放进 <see cref="RopeNode" /> 缓冲区，
	/// 由 <c>RopeSystem</c> 统一做 Verlet 积分 + 距离约束；只有两端的约束会把反作用力
	/// 施加到部件刚体上（等价于 BPSS 端点节的 <c>AddForceAtPosition</c>）。
	/// </remarks>
	public struct Rope : IComponentData, IQueryTypeParameter
	{
		/// <summary>BPSS <c>INFeature.RopeSegmentCount</c>（INDeclarationSettingsExp.json = 8）。</summary>
		public const int SegmentCount = 8;

		/// <summary>相邻节点的静止距离：BPSS 每节长 0.5（<c>position += 0.5f * (rotation * Vector3.right)</c>）。</summary>
		public const float RestLength = 0.5f;

		/// <summary>BPSS 的初始折角：每节交替绕 z 轴 ±74.4°，让 8 节绳在 1 格里折成锯齿。</summary>
		public const float ZigZagDegrees = 74.4f;

		/// <summary>中间节点断绳阈值（BPSS <c>PullEndpoints</c>：<c>num2 &gt; 1.25f</c>）。</summary>
		public const float CutDistanceInner = 1.25f;

		/// <summary>端点节点断绳阈值（BPSS：<c>num2 &gt; 1.75f</c>）。</summary>
		public const float CutDistanceEnd = 1.75f;

		/// <summary>拉伸时的弹簧系数（BPSS：<c>70f * vector</c>）。</summary>
		public const float StretchForce = 70f;

		/// <summary>压缩时的弹簧系数（BPSS：<c>50f * (magnitude - 0.4f)</c>）。</summary>
		public const float CompressForce = 50f;

		/// <summary>端点拉力系数与上限（BPSS：<c>Mathf.Clamp(750f * num4, 0f, 300f)</c>）。</summary>
		public const float EndpointForce = 750f;

		public const float MaxEndpointForce = 300f;

		/// <summary>拉伸系数上限（BPSS：<c>num5 = 0.4f</c>）。</summary>
		public const float StretchLimit = 0.4f;

		/// <summary>绳长超过「理论长度 × 1.18」才开始拉端点（BPSS 的 <c>1.18f * num3</c>）。</summary>
		public const float SlackFactor = 1.18f;

		/// <summary>第一次更新时构建节点链（BPSS 在 <c>Create()</c> 里一次性建好）。</summary>
		public bool Built;

		/// <summary>是否已被砍断（BPSS <c>m_ropeCut</c>）。</summary>
		public bool Cut;

		/// <summary>断口所在的节点序号（BPSS <c>m_cutIndex</c>），未断时为 -1。</summary>
		public int CutIndex;

		/// <summary>上一帧的拉伸系数，用来判断绳子正在「回弹」（BPSS <c>m_previousStretchFactor</c>）。</summary>
		public float PreviousStretch;

		/// <summary>左端部件（BPSS <c>m_leftPart</c>），<see cref="Entity.Null" /> 表示悬空。</summary>
		public Entity LeftPart;

		/// <summary>右端部件（BPSS <c>m_rightPart</c>）。</summary>
		public Entity RightPart;

		/// <summary>
		/// 首节点相对 <see cref="LeftPart" /> 的局部偏移（BPSS <c>HingeJoint.anchor</c>）。
		/// 关节铰在节点自己的位置上，所以部件移动时节点跟着平移同样的量，而不是被拽到部件中心。
		/// </summary>
		public float3 LeftAnchor;

		/// <summary>末节点相对 <see cref="RightPart" /> 的局部偏移。</summary>
		public float3 RightAnchor;

		/// <summary>只有索引 0-3 是绳索；4-7 是铰接板（BPSS <c>HingePlate</c>），另算。</summary>
		public static bool IsRope(PartTypeInfo info)
		{
			return info.PartType == PartType.Rope && info.PartIndex <= 3;
		}

		public static Rope Create(PartTypeInfo info)
		{
			return new Rope
			{
				Built = false,
				Cut = false,
				CutIndex = -1,
				PreviousStretch = 0f,
				LeftPart = Entity.Null,
				RightPart = Entity.Null,
				LeftAnchor = float3.zero,
				RightAnchor = float3.zero
			};
		}
	}

	/// <summary>绳索的一个节点。位置与上一帧位置足够做 Verlet 积分，速度可由两者差分得到。</summary>
	public struct RopeNode : IBufferElementData
	{
		public float3 Position;

		public float3 Previous;
	}
}
