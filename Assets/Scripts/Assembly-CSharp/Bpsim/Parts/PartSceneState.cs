using UnityEngine;

namespace Bpsim.Parts
{
	public class PartSceneState
	{
		public float LastSelectedTime { get; set; }

		public Vector2 CameraPosition { get; set; }

		public float CameraSize { get; set; }

		/// <summary>
		/// 空 / 超平坦场景标记：由 <see cref="PartManager.LoadEmptyScene"/> 置 true。
		/// 宿主据此显示 taffy 式「超平坦」地面（视觉 + 运动学物理地面）；
		/// 关卡场景与从存档读入的场景保持 false，使用它们自己的地形。
		/// </summary>
		public bool UseFlatGround { get; set; }

		public bool HasSelection
		{
			get
			{
				if (Selection.width > 0)
				{
					return Selection.height > 0;
				}
				return false;
			}
		}

		public RectInt Selection { get; set; }
	}
}
