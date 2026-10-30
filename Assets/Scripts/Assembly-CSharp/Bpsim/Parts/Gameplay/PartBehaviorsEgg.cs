using Unity.Entities;

namespace Bpsim.Parts.Gameplay
{
	/// <summary>
	/// 鸟蛋的玩家交互（BPSS <c>Egg.OnTouch</c> / <c>AlienEgg.OnTouch</c>）：点一下翻转效果开关。
	/// </summary>
	/// <remarks>
	/// <para>
	/// 原版把所有效果直接写在 <c>SetEnabled</c> 里；这里只翻转 <see cref="Egg.Enabled" />，
	/// 由 <c>EggSystem</c> 每物理帧把 <see cref="Egg.Applied" /> 对齐过去 ——
	/// 于是「点一下就生效」的时机变成下一物理帧，和原版 FixedUpdate 一致。
	/// </para>
	/// <para>
	/// 普通蛋（索引 0）没有任何效果，不建行为。
	/// </para>
	/// </remarks>
	public sealed class EggBehavior : PartBehavior
	{
		public override void Init()
		{
			// 行为在 StartSimulation 时创建，早于 EggSystem 的第一次 Bind，所以这里不能
			// 只读 Egg.Enabled（那是 Create 时按 rotation 0 算的默认值）。虚空蛋的初值
			// 取决于实际朝向（BPSS DefaultVoidOn() 只认正交），所以按 PartTransform 重算。
			EntityManager entityManager = Em;
			if (!entityManager.HasComponent<Egg>(Part))
			{
				Enabled = false;
				return;
			}
			Egg egg = entityManager.GetComponentData<Egg>(Part);
			if (!egg.Bound)
			{
				int rotation = (entityManager.HasComponent<PartTransform>(Part) ? entityManager.GetComponentData<PartTransform>(Part).Rotation : 0);
				egg.Enabled = Egg.GetDefaultEnabled(egg.Kind, rotation);
				entityManager.SetComponentData(Part, egg);
			}
			Enabled = egg.Enabled;
		}

		public override void Trigger()
		{
			EntityManager entityManager = Em;
			if (!entityManager.HasComponent<Egg>(Part))
			{
				return;
			}
			Egg egg = entityManager.GetComponentData<Egg>(Part);
			egg.Enabled = !egg.Enabled;
			entityManager.SetComponentData(Part, egg);
			Enabled = egg.Enabled;
			Manager.Log(Describe());
		}

		public override string Describe()
		{
			Egg egg = Read();
			return "egg#" + Info.PartIndex + " kind=" + egg.Kind + " enabled=" + egg.Enabled + " applied=" + egg.Applied;
		}

		private Egg Read()
		{
			EntityManager entityManager = Em;
			return (entityManager.HasComponent<Egg>(Part) ? entityManager.GetComponentData<Egg>(Part) : default(Egg));
		}
	}
}
