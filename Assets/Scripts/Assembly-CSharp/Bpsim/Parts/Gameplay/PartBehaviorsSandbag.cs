namespace Bpsim.Parts.Gameplay
{
	/// <summary>Input/diagnostic adapter only. SandbagSystem owns joints, impulses and lifecycle.</summary>
	public sealed class SandbagBehavior : PartBehavior
	{
		public override void Init() => Enabled = !Em.HasComponent<Sandbag>(Part) || !Em.GetComponentData<Sandbag>(Part).Dropped;
		public override void Trigger()
		{
			if (!Em.Exists(Part) || !Em.HasComponent<Sandbag>(Part)) return;
			var bag = Em.GetComponentData<Sandbag>(Part);
			if (bag.Dropped) return;
			bag.DropRequest = true; Em.SetComponentData(Part, bag); Enabled = false;
		}
		public override void Tick(float deltaTime) => Enabled = Em.Exists(Part) && Em.HasComponent<Sandbag>(Part)
			&& !Em.GetComponentData<Sandbag>(Part).Dropped && !Em.GetComponentData<Sandbag>(Part).DropRequest;
		public override string Describe()
		{
			if (!Em.Exists(Part)) return "sandbag removed";
			var bag = Em.GetComponentData<Sandbag>(Part);
			return "sandbag balloons=" + bag.Balloons + " dropped=" + bag.Dropped + " anchor=" + bag.Anchor
				+ " joint=" + bag.Joint + " max=" + bag.MaxDistance + " mass=" + bag.Mass;
		}
	}
}
