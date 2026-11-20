using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchAssistant.Core;

internal sealed class MultiTouchRecognizer
{
	private sealed class Finger
	{
		public double X;

		public double Y;

		public double LastX;

		public double LastY;

		public double MaxDistance;

		public bool Active = true;
	}

	public readonly record struct Match(int Fingers, string Gesture, double X, double Y);

	public readonly record struct Drag(int Fingers, string Gesture, double X, double Y, double Distance);

	private readonly Dictionary<uint, Finger> fingers = new Dictionary<uint, Finger>();

	private long started;

	private bool invalid;

	private bool releasing;

	private int peak;

	public bool Invalid => invalid;

	public Drag? CurrentDrag(double strictness = 60.0)
	{
		if (invalid || releasing || peak < 2 || fingers.Count != peak)
		{
			return null;
		}
		double num = fingers.Values.Average((Finger f) => f.LastX - f.X);
		double num2 = fingers.Values.Average((Finger f) => f.LastY - f.Y);
		bool flag = Math.Abs(num) > Math.Abs(num2);
		double value = (flag ? num : num2);
		if (Math.Abs(value) < 12.0 || Math.Abs(value) < (1.15 + 0.7 * Math.Clamp((strictness - 30.0) / 60.0, 0.0, 1.0)) * Math.Abs(flag ? num2 : num))
		{
			return null;
		}
		return new Drag(peak, (!flag) ? ((num2 > 0.0) ? "Down" : "Up") : ((num > 0.0) ? "Right" : "Left"), fingers.Values.Average((Finger f) => f.X), fingers.Values.Average((Finger f) => f.Y), Math.Abs(value));
	}

	public double DragDistance(string direction)
	{
		if (fingers.Count != 0)
		{
			return Math.Max(0.0, fingers.Values.Average((Finger f) => direction switch
			{
				"Up" => f.Y - f.LastY, 
				"Down" => f.LastY - f.Y, 
				"Left" => f.X - f.LastX, 
				_ => f.LastX - f.X, 
			}));
		}
		return 0.0;
	}

	private static double Distance(double x, double y)
	{
		return Math.Sqrt(x * x + y * y);
	}

	public void Reset()
	{
		fingers.Clear();
		invalid = (releasing = false);
		peak = 0;
	}

	public Match? Feed(uint id, double x, double y, bool down, bool up, bool cancelled, long now, double maxSpacing, double holdMilliseconds, double strictness = 60.0)
	{
		double accuracy = Math.Clamp((strictness - 30.0) / 60.0, 0.0, 1.0);
		if (down)
		{
			if (fingers.Count == 0)
			{
				started = now;
			}
			if (releasing || now - started > 220 || fingers.ContainsKey(id) || fingers.Count >= 5)
			{
				invalid = true;
			}
			if (!fingers.ContainsKey(id))
			{
				fingers[id] = new Finger
				{
					X = x,
					Y = y,
					LastX = x,
					LastY = y
				};
			}
			peak = Math.Max(peak, fingers.Values.Count((Finger f) => f.Active));
			foreach (Finger value3 in fingers.Values)
			{
				if (Distance(x - value3.X, y - value3.Y) > maxSpacing)
				{
					invalid = true;
				}
			}
		}
		if (cancelled)
		{
			invalid = true;
		}
		if (fingers.TryGetValue(id, out Finger value))
		{
			value.LastX = x;
			value.LastY = y;
			value.MaxDistance = Math.Max(value.MaxDistance, Distance(x - value.X, y - value.Y));
			if (up || cancelled)
			{
				value.Active = false;
				releasing = true;
			}
		}
		if (fingers.Count == 0 || fingers.Values.Any((Finger f) => f.Active))
		{
			return null;
		}
		Match? result = null;
		if (!invalid && peak == fingers.Count)
		{
			int num = peak;
			if (num >= 1 && num <= 5)
			{
				double num2 = now - started;
				double num3 = fingers.Values.Average((Finger f) => f.LastX - f.X);
				double num4 = fingers.Values.Average((Finger f) => f.LastY - f.Y);
				string text = null;
				if (fingers.Values.All((Finger f) => f.MaxDistance <= 24.0 - 20.0 * accuracy))
				{
					text = ((num2 >= holdMilliseconds && num2 <= 10000.0) ? "Hold" : ((num2 <= 500.0) ? "Tap" : null));
				}
				else if (num2 <= 1800.0 && Math.Max(Math.Abs(num3), Math.Abs(num4)) >= 48.0 + 32.0 * accuracy && Math.Max(Math.Abs(num3), Math.Abs(num4)) >= (1.15 + 0.7 * accuracy) * Math.Min(Math.Abs(num3), Math.Abs(num4)))
				{
					bool horizontal = Math.Abs(num3) > Math.Abs(num4);
					double major = (horizontal ? num3 : num4);
					if (fingers.Values.All(delegate(Finger f)
					{
						double num5 = (horizontal ? (f.LastX - f.X) : (f.LastY - f.Y));
						double value2 = (horizontal ? (f.LastY - f.Y) : (f.LastX - f.X));
						return num5 * (double)Math.Sign(major) >= 36.0 + 24.0 * accuracy && Math.Abs(num5) >= (1.05 + 0.5 * accuracy) * Math.Abs(value2);
					}))
					{
						text = ((!horizontal) ? ((num4 > 0.0) ? "Down" : "Up") : ((num3 > 0.0) ? "Right" : "Left"));
					}
				}
				if (peak == 1 && text != "Hold")
				{
					text = null;
				}
				if (text != null)
				{
					result = new Match(peak, text, fingers.Values.Average((Finger f) => f.LastX), fingers.Values.Average((Finger f) => f.LastY));
				}
			}
		}
		Reset();
		return result;
	}
}
