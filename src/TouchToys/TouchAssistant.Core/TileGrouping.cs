using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchAssistant.Core;

internal static class TileGrouping
{
	public static List<int> Find(IReadOnlyList<TileRect> windows, TileRect work, double tolerance)
	{
		int[] array = Enumerable.Range(0, windows.Count).Where(delegate(int index)
		{
			TileRect tileRect = windows[index];
			return tileRect.Area >= work.Area * 0.15 && tileRect.Area <= work.Area * 0.85 && tileRect.X >= work.X - tolerance && tileRect.Y >= work.Y - tolerance && tileRect.Right <= work.Right + tolerance && tileRect.Bottom <= work.Bottom + tolerance;
		}).Take(12)
			.ToArray();
		int[] array2 = array;
		foreach (int num2 in array2)
		{
			List<int> list = new List<int> { num2 };
			int[] array3 = array;
			foreach (int i in array3)
			{
				if (i == num2 || list.Any((int j) => windows[i].Overlap(windows[j]) > Math.Min(windows[i].Area, windows[j].Area) * 0.015))
				{
					continue;
				}
				list.Add(i);
				if (list.Count >= 2)
				{
					double num4 = list.Sum((int j) => windows[j].Area);
					double num5 = list.Min((int j) => windows[j].X);
					double num6 = list.Min((int j) => windows[j].Y);
					double num7 = list.Max((int j) => windows[j].Right);
					double num8 = list.Max((int j) => windows[j].Bottom);
					if (num4 >= work.Area * 0.9 && num4 <= work.Area * 1.02 && Math.Abs(num5 - work.X) <= tolerance && Math.Abs(num6 - work.Y) <= tolerance && Math.Abs(num7 - work.Right) <= tolerance && Math.Abs(num8 - work.Bottom) <= tolerance)
					{
						return list;
					}
				}
			}
		}
		return new List<int>();
	}
}
