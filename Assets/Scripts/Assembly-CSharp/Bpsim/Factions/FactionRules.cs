using Bpsim.Parts;

namespace Bpsim.Factions
{
	/// <summary>三星分离器：四个方向代表四队，斜放保留队伍并允许同队穿透。</summary>
	public static class FactionRules
	{
		public static bool IsMarker(PartTypeInfo info) => BasePart.Resolve(info).Equals(BasePart.Marker);
		public static int TeamMask(int rotation) => 1 << (((rotation % 4) + 4) % 4);
		public static string TeamName(int rotation) => TeamMask(rotation) switch { 1 => "红阵营", 2 => "蓝阵营", 4 => "绿阵营", _ => "黄阵营" };
		public static bool SameTeam(int componentA, int teamsA, int componentB, int teamsB) =>
			(componentA >= 0 && componentA == componentB) || (teamsA & teamsB) != 0;
	}
}
