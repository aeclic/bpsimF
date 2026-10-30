using System;

namespace Bpsim
{
	public static class BitDirectionExtensions
	{
		public static BitDirection Rotate(this BitDirection direction, int count)
		{
			count = (count % 4 + 4) % 4;
			int num = (int)direction << count;
			int num2 = num & 0xF;
			int num3 = num & 0xF0;
			return (BitDirection)(num2 | (num3 >> 4));
		}

		public static BitDirection Reverse(this BitDirection direction)
		{
			return (BitDirection)(((int)(direction & (BitDirection)3) << 2) | ((int)(direction & (BitDirection)12) >> 2));
		}

		public static int BitCount(this BitDirection direction)
		{
			int num = (int)direction;
			int num2 = 0;
			while (num != 0)
			{
				num2++;
				num &= num - 1;
			}
			return num2;
		}

		public static BitDirection FromIndex(int index)
		{
			if (index < 0 || index >= 4)
			{
				throw new ArgumentOutOfRangeException("index");
			}
			return (BitDirection)(1 << index);
		}

		public static int ToIndex(this BitDirection direction)
		{
			return direction switch
			{
				BitDirection.Right => 0, 
				BitDirection.Up => 1, 
				BitDirection.Left => 2, 
				BitDirection.Down => 3, 
				_ => throw new InvalidCastException(), 
			};
		}

		public static (int, int) ToVector(this BitDirection direction)
		{
			return direction switch
			{
				BitDirection.Right => (1, 0), 
				BitDirection.Up => (0, 1), 
				BitDirection.Left => (-1, 0), 
				BitDirection.Down => (0, -1), 
				_ => throw new InvalidCastException(), 
			};
		}
	}
}
