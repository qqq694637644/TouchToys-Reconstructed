using System;
using System.Collections.Generic;
using System.Linq;
using TouchAssistant.Models;

namespace TouchAssistant.Core;

internal static class ShapeGesture
{
	public static double Length(IReadOnlyList<GesturePoint> path)
	{
		return Enumerable.Range(1, Math.Max(0, path.Count - 1)).Sum((int i) => Distance(path[i - 1], path[i]));
	}

	private static double Distance(GesturePoint a, GesturePoint b)
	{
		return Math.Sqrt(Math.Pow(a.X - b.X, 2.0) + Math.Pow(a.Y - b.Y, 2.0));
	}

	public static List<GesturePoint> Normalize(IReadOnlyList<GesturePoint> path)
	{
		List<GesturePoint> list = new List<GesturePoint>();
		if (path.Count < 2 || path.Any((GesturePoint p) => !double.IsFinite(p.X) || !double.IsFinite(p.Y)))
		{
			return list;
		}
		double num = Length(path);
		if (num < 1.0)
		{
			return list;
		}
		double num2 = 0.0;
		double num3 = 0.0;
		int num4 = 1;
		for (int num5 = 0; num5 < 64; num5++)
		{
			for (num2 = num * (double)num5 / 63.0; num4 < path.Count - 1 && num3 + Distance(path[num4 - 1], path[num4]) < num2; num4++)
			{
				num3 += Distance(path[num4 - 1], path[num4]);
			}
			GesturePoint gesturePoint = path[num4 - 1];
			GesturePoint gesturePoint2 = path[num4];
			double num6 = Distance(gesturePoint, gesturePoint2);
			double num7 = ((num6 > 0.0) ? Math.Clamp((num2 - num3) / num6, 0.0, 1.0) : 0.0);
			list.Add(new GesturePoint(gesturePoint.X + (gesturePoint2.X - gesturePoint.X) * num7, gesturePoint.Y + (gesturePoint2.Y - gesturePoint.Y) * num7));
		}
		double num8 = list.Min((GesturePoint p) => p.X);
		double num9 = list.Min((GesturePoint p) => p.Y);
		double size = Math.Max(list.Max((GesturePoint p) => p.X) - num8, list.Max((GesturePoint p) => p.Y) - num9);
		if (size < 1.0)
		{
			return new List<GesturePoint>();
		}
		double cx = list.Average((GesturePoint p) => p.X);
		double cy = list.Average((GesturePoint p) => p.Y);
		return list.Select((GesturePoint p) => new GesturePoint((p.X - cx) / size, (p.Y - cy) / size)).ToList();
	}

	public static MultiTouchBinding? Match(IReadOnlyList<GesturePoint> path, int fingers, IEnumerable<MultiTouchBinding> bindings, double strictness = 60.0)
	{
		if (Length(path) < 60.0)
		{
			return null;
		}
		List<GesturePoint> list = Normalize(path);
		if (list.Count != 64)
		{
			return null;
		}
		MultiTouchBinding result = null;
		double num = double.MaxValue;
		double num2 = double.MaxValue;
		foreach (MultiTouchBinding item in bindings.Where((MultiTouchBinding b) => b.Action.Enabled && b.Fingers == fingers && b.Gesture == "Custom" && b.Paths.Count == 0 && b.Points.Count >= 2))
		{
			List<GesturePoint> template = Normalize(item.Points);
			if (template.Count == 64)
			{
				double num3 = Math.Sqrt(list.Select((GesturePoint p, int i) => Math.Pow(Distance(p, template[i]), 2.0)).Average());
				if (num3 < num)
				{
					num2 = num;
					num = num3;
					result = item;
				}
				else
				{
					num2 = Math.Min(num2, num3);
				}
			}
		}
		if (!(num <= 0.32 - 0.24 * Math.Clamp((strictness - 30.0) / 60.0, 0.0, 1.0)) || !(num2 - num >= 0.035))
		{
			return null;
		}
		return result;
	}

	public static MultiTouchBinding? MatchPaths(IReadOnlyList<List<GesturePoint>> paths, IEnumerable<MultiTouchBinding> bindings, double strictness = 60.0)
	{
		if (paths.Count == 0 || paths.Count > 5 || !paths.Any((List<GesturePoint> p) => Length(p) >= 60.0))
		{
			return null;
		}
		MultiTouchBinding result = null;
		double num = double.MaxValue;
		double num2 = double.MaxValue;
		MultiTouchBinding binding;
		double score;
		bool[] used;
		foreach (MultiTouchBinding item in bindings.Where((MultiTouchBinding b) => b.Action.Enabled && b.Gesture == "Custom" && b.Fingers == paths.Count && b.Paths.Count == paths.Count))
		{
			binding = item;
			score = 10.0;
			used = new bool[paths.Count];
			Assign(0, 0.0);
			if (score < num)
			{
				num2 = num;
				num = score;
				result = binding;
			}
			else
			{
				num2 = Math.Min(num2, score);
			}
		}
		if (!(num <= 0.35 - 0.24 * Math.Clamp((strictness - 30.0) / 60.0, 0.0, 1.0)) || !(num2 - num >= 0.035))
		{
			return null;
		}
		return result;
		void Assign(int i, double worst)
		{
			if (!(worst >= score))
			{
				if (i == paths.Count)
				{
					score = worst;
				}
				else
				{
					for (int j = 0; j < paths.Count; j++)
					{
						if (!used[j])
						{
							used[j] = true;
							Assign(i + 1, Math.Max(worst, StrokeScore(paths[i], binding.Paths[j])));
							used[j] = false;
						}
					}
				}
			}
		}
		static double StrokeScore(IReadOnlyList<GesturePoint> a, IReadOnlyList<GesturePoint> b)
		{
			bool flag = Length(a) < 20.0;
			bool flag2 = Length(b) < 20.0;
			if (flag != flag2)
			{
				return 10.0;
			}
			if (flag)
			{
				return 0.0;
			}
			List<GesturePoint> list = Normalize(a);
			List<GesturePoint> y = Normalize(b);
			if (list.Count != 64 || y.Count != 64)
			{
				return 10.0;
			}
			return Math.Sqrt(list.Select((GesturePoint p, int i) => Math.Pow(Distance(p, y[i]), 2.0)).Average());
		}
	}
}
