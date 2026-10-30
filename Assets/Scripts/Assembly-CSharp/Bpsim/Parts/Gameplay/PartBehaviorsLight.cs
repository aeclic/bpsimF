using Unity.Mathematics;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// BPSS <c>PointLight</c> 及它的三个只是「换图 + 开关」的型号，
	/// 对应 <c>Part_PointLight_01/_04</c>（基础灯）、<c>_02</c>（Mushroom）、
	/// <c>_03</c>（Lantern）、<c>_05</c>（AlienPointLight）。
	/// </summary>
	/// <remarks>
	/// <para>原版这三段代码几乎一样，<c>OnTouch</c> 都是：</para>
	/// <code>
	/// activated = !activated;
	/// activeSprite.SetActive(activated);
	/// inactiveSprite.SetActive(!activated);
	/// if (lightSource) lightSource.isEnabled = activated;
	/// </code>
	/// <para>
	/// bpsim 没有 <c>PointLightSource</c>（这个仓库里根本没有光源渲染层），所以能复刻的
	/// 只有图的那一半：通过 <see cref="Bpsim.Parts.PointLightVisual"/> 标记 + <c>DisableRendering</c>
	/// 在 active/inactive 两个子物体之间切换。
	/// </para>
	/// <para>各型号与 BPSS 的差异（bpsim 没有对应机制，只保留可按的部分）：</para>
	/// <list type="bullet">
	/// <item><b>Mushroom</b>：原版 <c>LightSystem</c> 关闭时强制常亮且不可切换；bpsim 没有该设置，
	/// 按「开」处理——默认熄灭、可点击点亮，和基础灯一致。</item>
	/// <item><b>Lantern</b>：原版点亮后按 <c>sin(t·mainSinSpeed)·sin(t·subSinSpeed)·flicker</c>
	/// 抖动 <c>lightTransform.localScale</c> 与灯罩 <c>SphereCollider.radius</c>，纯光效；
	/// bpsim 没有光锥/光源，这里不做抖动。</item>
	/// <item><b>AlienPointLight</b>：原版 <c>OnTouch</c> 还会翻转 <c>LightManager.NightVisionOn</c>
	/// （全屏夜视）；bpsim 没有夜视渲染，翻转后只在日志里说明，不影响物理。</item>
	/// </list>
	/// </remarks>
	public sealed class PointLightBehavior : PartBehavior
	{
		private bool m_activated;

		private int m_swaps;

		/// <summary>bpsim 侧没有光源组件可开，这里按 BPSS 的型号名给出诊断用的名字。</summary>
		private string Variant
		{
			get
			{
				switch (Info.PartIndex)
				{
				case 1:
					return "mushroom";
				case 2:
					return "lantern";
				case 4:
					return "alienlight";
				default:
					return "pointlight";
				}
			}
		}

		public override void Init()
		{
			// prefab 的默认状态就是熄灭（Inactive 图可见、Active 图带 DisableRendering），
			// 所以这里只把可见性按 m_activated 对齐一次，保证从任何初始状态都能收敛。
			Enabled = m_activated;
			Apply();
		}

		public override void Trigger()
		{
			m_activated = !m_activated;
			Enabled = m_activated;
			int changed = Apply();
			Manager.Log(Variant + "#" + Info.PartIndex + ": " + (m_activated ? "on" : "off") + " spritesSwapped=" + changed);
			if (Info.PartIndex == 4)
			{
				// BPSS AlienPointLight.OnTouch 顺带翻转全局夜视。bpsim 没有夜视渲染层，
				// 这里保留日志以便对照行为顺序，不改变任何物理状态。
				Manager.Log("alienlight: nightvision toggle skipped (no light manager in bpsim)");
			}
		}

		private int Apply()
		{
			int changed = PartGameplayUtil.SetPointLightVisual(Part, m_activated, false);
			m_swaps += changed;
			return changed;
		}

		public override string Describe()
		{
			return Variant + "#" + Info.PartIndex + " enabled=" + m_activated + " swaps=" + m_swaps;
		}
	}

	/// <summary>
	/// BPSS <c>SpotLight</c>（聚光灯，<c>Part_SpotLight_01..04</c>）：点击开关，
	/// 开启时光锥容器 <c>m_lightConeContainer.localScale</c> 从 0.001 渐变到 (3, 1, 1)，
	/// 关闭时反向收回。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版 <c>SetEnabled</c> 只改两个状态位：<c>lightSource.isEnabled</c> 与
	/// <c>m_lightConeTargetScale = activated ? 3f : 0.001f</c>；真正的插值在
	/// <c>FixedUpdate</c>：
	/// </para>
	/// <code>
	/// m_lightConeContainer.localScale =
	///     Vector3.Lerp(m_lightConeContainer.localScale,
	///                  new Vector3(target, 1, 1), Time.deltaTime * 30f);
	/// </code>
	/// <para>
	/// 八个 <c>*Attachment</c> 的显隐是 <c>ChangeVisualConnections</c> 的职责
	/// （按相邻格子能否连接决定），bpsim 已由 <c>PartSceneUnmanaged</c> 的附件逻辑负责，
	/// 这里不重复处理。
	/// </para>
	/// </remarks>
	public sealed class SpotLightBehavior : PartBehavior
	{
		/// <summary>BPSS <c>m_lightConeTargetScale</c> 的两个端点。</summary>
		private const float ConeOffScale = 0.001f;

		private const float ConeOnScale = 3f;

		private bool m_activated;

		/// <summary>当前写入的容器缩放；<c>Awake</c> 里原版把它初始化成 (target, target, target) = 0。</summary>
		private float3 m_current;

		private int m_coneWrites;

		/// <summary>最近一次向容器写缩放时是否命中（没有命中说明 prefab 结构变了）。</summary>
		private bool m_coneFound;

		public override void Init()
		{
			m_current = float3.zero;
			m_coneFound = PartGameplayUtil.SetSpotLightCone(Part, m_current) > 0;
			Enabled = m_activated;
		}

		public override void Trigger()
		{
			m_activated = !m_activated;
			Enabled = m_activated;
			Manager.Log("spotlight: " + (m_activated ? "on" : "off"));
		}

		public override void Tick(float deltaTime)
		{
			float3 target = new float3(m_activated ? ConeOnScale : ConeOffScale, 1f, 1f);
			// 收敛后就别再每帧写组件了（原版 Vector3.Lerp 也是这个语义，只是它一直在算）。
			if (math.lengthsq(m_current - target) < 1E-08f)
			{
				return;
			}
			m_current = math.lerp(m_current, target, math.saturate(deltaTime * 30f));
			int written = PartGameplayUtil.SetSpotLightCone(Part, m_current);
			m_coneFound = written > 0;
			m_coneWrites += written;
		}

		public override string Describe()
		{
			return "spotlight enabled=" + m_activated + " cone=(" + m_current.x.ToString("F3") + ","
				+ m_current.y.ToString("F3") + ") writes=" + m_coneWrites + " found=" + m_coneFound;
		}
	}
}
