using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchAssistant.Core;

internal sealed class TwoFingerDrag
{
	private readonly Dictionary<uint, (int X, int Y)> points = new Dictionary<uint, (int, int)>();

	private bool active;

	private (double X, double Y) Center => (X: points.Values.Average(((int X, int Y) p) => p.X), Y: points.Values.Average(((int X, int Y) p) => p.Y));

	public bool Feed(uint message, uint id, int x, int y, Action begin, Action<int, int> move, Action end)
	{
		if (message == 582)
		{
			points[id] = (x, y);
			if (!active && points.Count == 2)
			{
				active = true;
				begin();
			}
			return active;
		}
		if (!points.ContainsKey(id))
		{
			return active;
		}
		bool result = active;
		(double, double) center = Center;
		points[id] = (x, y);
		if (active && points.Count >= 2)
		{
			(double, double) center2 = Center;
			move((int)Math.Round(center2.Item1 - center.Item1), (int)Math.Round(center2.Item2 - center.Item2));
		}
		if (message == 583)
		{
			points.Remove(id);
			if (points.Count == 0 && active)
			{
				active = false;
				end();
			}
		}
		return result;
	}

	public void Reset()
	{
		points.Clear();
		active = false;
	}
}
