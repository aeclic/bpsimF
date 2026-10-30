using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;

namespace Bpsim.Parts
{
	/// <summary>
	/// 机械闸门（<see cref="PartType.MechanicalPart"/> 索引 0-4）的 ECS 数据。
	///
	/// BPSS 侧是 <c>MechanicalGate : MechanicalPart</c>：一个可以点击伸出/收回的滑动门，
	/// 门体由一个 <c>BoxCollider</c> 子物体（<c>GateCollider</c>）和一个 <c>MeshRenderer</c>
	/// 子物体（<c>GateRenderer</c>）构成，两者都按同一个「当前长度」缩放并平移。
	/// 注意它 **不是电路元件** —— 原版的 <c>MechanicalPart</c> 只是空壳，闸门行为完全独立，
	/// 所以这里也不进 <see cref="CircuitPart"/> 那条求解链。
	/// </summary>
	public struct MechanicalGate : IComponentData, IQueryTypeParameter
	{
		/// <summary>目标状态：true = 伸出到 <see cref="TargetLength"/>，false = 收回到 0。</summary>
		public bool Enabled;

		/// <summary>完全伸出时的长度（格）。索引 0-3 固定 1/2/4/8，索引 4 可调（0.1-8）。</summary>
		public float TargetLength;

		/// <summary>当前长度（格）。碰撞体与渲染体都按它缩放。</summary>
		public float Value;

		/// <summary>缓动进度 0-1（BPSS <c>CurveController.m_progress</c>）。</summary>
		public float Progress;

		/// <summary>本次缓动的起点长度。</summary>
		public float Start;

		/// <summary>本次缓动的终点长度。</summary>
		public float End;

		/// <summary>每帧进度增量（BPSS <c>delta = min(|speed / (end - start)|, 1)</c>）。</summary>
		public float Delta;

		/// <summary>伸出速度（长度/秒）。BPSS 默认 <c>StarSeaSettings.GateExtensionSpeed = 0.2</c>。</summary>
		public float Speed;

		/// <summary>门宽系数（BPSS <c>GateWidthCoefficient</c>）。</summary>
		public float Width;

		/// <summary>门厚系数（BPSS <c>GateLengthCoefficients[type]</c>，作用在本地 Y）。</summary>
		public float Thickness;

		/// <summary>门体不透明度（BPSS <c>GateOpacity</c>）。</summary>
		public float Opacity;

		/// <summary>上次重建复合碰撞体时的长度（量化后）。负值 = 还没建过。</summary>
		public float BuiltLength;

		/// <summary>当前生效的复合碰撞体（本系统创建，负责释放）。</summary>
		public BlobAssetReference<Collider> OwnedCompound;

		/// <summary>上一次的复合碰撞体，下次重建时释放（避开物理系统换缓存的那一帧）。</summary>
		public BlobAssetReference<Collider> PendingDispose;

		/// <summary>按 BPSS <c>MechanicalGate.GetTargetLength()</c> 的默认表建组件。</summary>
		public static MechanicalGate Create(PartTypeInfo info)
		{
			MechanicalGate gate = default(MechanicalGate);
			gate.Enabled = false;
			gate.TargetLength = GetDefaultLength(info.PartIndex);
			gate.Value = 0f;
			gate.Progress = 0f;
			gate.Start = 0f;
			gate.End = 0f;
			gate.Delta = 0f;
			gate.Speed = 0.2f;
			gate.Width = 1f;
			gate.Thickness = 1f;
			gate.Opacity = 0.8f;
			gate.BuiltLength = -1f;
			gate.OwnedCompound = default(BlobAssetReference<Collider>);
			gate.PendingDispose = default(BlobAssetReference<Collider>);
			return gate;
		}

		/// <summary>BPSS <c>GetTargetLength()</c>：0→1、1→2、2→4、3→8、4→1（4 号走滑块可调）。</summary>
		public static float GetDefaultLength(int index)
		{
			return index switch
			{
				0 => 1f,
				1 => 2f,
				2 => 4f,
				3 => 8f,
				_ => 1f,
			};
		}

		/// <summary>
		/// 重新设定缓动区间（BPSS <c>SetController(start, end)</c>）。
		/// <c>delta = min(|speed / (end - start)|, 1)</c>，所以「走得越远每帧增量越小」，整段耗时恒定。
		/// </summary>
		public void SetController(float start, float end)
		{
			Start = start;
			End = end;
			Progress = 0f;
			Value = start;
			float span = math.abs(end - start);
			Delta = (span <= 1E-05f) ? 1f : math.min(math.abs(Speed / (end - start)), 1f);
		}

		/// <summary>
		/// 推进缓动（BPSS <c>CurveController.Update()</c>）：
		/// <c>progress += delta</c> 夹到 0-1，<c>eased = 0.5(1 - cos(π·progress))</c>，值按 eased 线性插值。
		/// </summary>
		public bool Advance(float deltaTime)
		{
			float next = math.clamp(Progress + Delta, 0f, 1f);
			if (math.abs(next - Progress) <= 1E-06f)
			{
				return false;
			}
			Progress = next;
			float eased = 0.5f * (1f - math.cos(math.PI * next));
			Value = (1f - eased) * Start + eased * End;
			return true;
		}
	}

	/// <summary>
	/// 闸门两个受控子物体的标记（按 GameObject 名在 <see cref="PartEntityFactory"/> 里打上）。
	/// <c>GateCollider</c> 带 <c>BoxCollider</c>，<c>GateRenderer</c> 带 <c>MeshRenderer</c>，
	/// 两者都要跟着 <see cref="MechanicalGate.Value"/> 平移+缩放。
	/// </summary>
	public struct GateVisual : IComponentData
	{
		/// <summary>true = GateCollider，false = GateRenderer。</summary>
		public bool Collider;
	}
}
