using Bpsim.Parts;
using UnityEngine;

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

		public static Color OutlineColor(int teams)
		{
			// Mixed-marker vehicles belong to every indicated team; use a stable mixed colour.
			Color color = Color.clear;
			int count = 0;
			if ((teams & 1) != 0) { color += new Color(1f, .22f, .25f, 1f); count++; }
			if ((teams & 2) != 0) { color += new Color(.2f, .6f, 1f, 1f); count++; }
			if ((teams & 4) != 0) { color += new Color(.25f, 1f, .4f, 1f); count++; }
			if ((teams & 8) != 0) { color += new Color(1f, .8f, .15f, 1f); count++; }
			return count == 0 ? Color.clear : color / count;
		}
	}
}
