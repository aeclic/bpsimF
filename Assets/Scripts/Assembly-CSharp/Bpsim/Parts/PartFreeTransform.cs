using Unity.Entities;
using Unity.Mathematics;

namespace Bpsim.Parts
{
	/// <summary>
	/// 无网格/精细变换层：与 <see cref="PartTransform" /> 的整数格 + 离散朝向叠加使用。
	/// 单独成组件的原因：网格占用表、连接、包容与 90° 整车旋转都只认 <see cref="PartTransform" />，
	/// 自由偏移/连续角/缩放不影响任何建造判定，只在表现层合成 <see cref="Unity.Transforms.LocalTransform" />。
	/// </summary>
	public struct PartFreeTransform : IComponentData
	{
		/// <summary>叠加在格坐标（世界单位）上的平移，量纲与格间距一致。</summary>
		public float2 Offset;

		/// <summary>叠加在离散朝向之上的连续旋转角（度，逆时针为正）。</summary>
		public float RotationDegrees;

		/// <summary>根实体附加缩放；默认 (1,1)。</summary>
		public float2 Scale;

		/// <summary>BPRC <c>BasePart.ValidScale</c> 的同一区间：单轴 0.05 ~ 20。</summary>
		public const float MinScale = 0.05f;

		public const float MaxScale = 20f;

		public static PartFreeTransform Identity => new PartFreeTransform
		{
			Offset = float2.zero,
			RotationDegrees = 0f,
			Scale = new float2(1f, 1f)
		};

		public bool IsIdentity => Offset.x == 0f && Offset.y == 0f && RotationDegrees == 0f && Scale.x == 1f && Scale.y == 1f;

		/// <summary>
		/// 把任何来源（旧存档缺字段、外部写入、组件默认值）的数值整理到合法区间：
		/// NaN/Inf 归零，缩放 &lt;= 0 归 1 再钳制到 [0.05, 20]。
		/// </summary>
		public static PartFreeTransform Sanitize(in PartFreeTransform value)
		{
			float2 offset = value.Offset;
			if (!math.all(math.isfinite(offset)))
			{
				offset = float2.zero;
			}
			float rotation = value.RotationDegrees;
			if (!math.isfinite(rotation))
			{
				rotation = 0f;
			}
			float2 scale = value.Scale;
			if (!math.all(math.isfinite(scale)))
			{
				scale = new float2(1f, 1f);
			}
			if (scale.x <= 0f)
			{
				scale.x = 1f;
			}
			if (scale.y <= 0f)
			{
				scale.y = 1f;
			}
			scale = math.clamp(scale, MinScale, MaxScale);
			return new PartFreeTransform
			{
				Offset = offset,
				RotationDegrees = rotation,
				Scale = scale
			};
		}
	}
}
