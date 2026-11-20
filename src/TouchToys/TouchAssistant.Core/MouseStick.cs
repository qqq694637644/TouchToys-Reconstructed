using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchAssistant.Core;

internal sealed class MouseStick
{
	public double X = 270.0;

	public double Y = 110.0;

	public double Size = 80.0;

	public double DX;

	public double DY;

	private readonly Dictionary<uint, (double X, double Y)> points = new Dictionary<uint, (double, double)>();

	private bool editing;

	private bool moved;

	private bool centerTap;

	private long began;

	private double lastCX;

	private double lastCY;

	private double lastDistance;

	public bool Active => points.Count > 0;

	public bool Moving
	{
		get
		{
			if (!editing && points.Count == 1)
			{
				if (DX == 0.0)
				{
					return DY != 0.0;
				}
				return true;
			}
			return false;
		}
	}

	public event Action? Changed;

	public event Action? Tap;

	public event Action? Saved;

	private static double Length(double x, double y)
	{
		return Math.Sqrt(x * x + y * y);
	}

	public bool Contains(uint id)
	{
		return points.ContainsKey(id);
	}

	public bool Hit(double x, double y)
	{
		return Length(x - X, y - Y) <= Size / 2.0;
	}

	public bool Feed(uint message, uint id, double x, double y, long now)
	{
		if (message == 582)
		{
			if (points.Count == 0)
			{
				if (Length(x - X, y - Y) > Size / 2.0)
				{
					return false;
				}
				began = now;
				editing = (moved = false);
				centerTap = Length(x - X, y - Y) < Size * 0.25;
			}
			points[id] = (x, y);
			if (points.Count >= 2)
			{
				editing = true;
				DX = (DY = 0.0);
				Baseline();
			}
			this.Changed?.Invoke();
			return true;
		}
		if (!points.ContainsKey(id))
		{
			return false;
		}
		points[id] = (x, y);
		if (message == 581)
		{
			if (editing && points.Count == 2)
			{
				(double, double)[] array = points.Values.ToArray();
				double num = (array[0].Item1 + array[1].Item1) / 2.0;
				double num2 = (array[0].Item2 + array[1].Item2) / 2.0;
				double num3 = Length(array[0].Item1 - array[1].Item1, array[0].Item2 - array[1].Item2);
				X += num - lastCX;
				Y += num2 - lastCY;
				if (lastDistance > 10.0)
				{
					Size = Math.Clamp(Size * num3 / lastDistance, 32.0, 160.0);
				}
				Baseline();
			}
			else if (!editing)
			{
				double num4 = x - X;
				double num5 = y - Y;
				double num6 = Length(num4, num5);
				if (num6 > 6.0)
				{
					moved = true;
				}
				double val = Size / 2.0;
				DX = ((num6 < 6.0) ? 0.0 : (num4 / Math.Max(val, num6)));
				DY = ((num6 < 6.0) ? 0.0 : (num5 / Math.Max(val, num6)));
			}
			this.Changed?.Invoke();
		}
		if (message == 583)
		{
			points.Remove(id);
			DX = (DY = 0.0);
			if (points.Count == 0)
			{
				bool num7 = !editing && !moved && centerTap && now - began <= 600;
				bool num8 = editing;
				editing = false;
				this.Changed?.Invoke();
				if (num8)
				{
					this.Saved?.Invoke();
				}
				if (num7)
				{
					this.Tap?.Invoke();
				}
			}
			else
			{
				this.Changed?.Invoke();
			}
		}
		return true;
	}

	private void Baseline()
	{
		(double, double)[] array = points.Values.Take(2).ToArray();
		lastCX = (array[0].Item1 + array[1].Item1) / 2.0;
		lastCY = (array[0].Item2 + array[1].Item2) / 2.0;
		lastDistance = Length(array[0].Item1 - array[1].Item1, array[0].Item2 - array[1].Item2);
	}

	public void Reset()
	{
		points.Clear();
		DX = (DY = 0.0);
		editing = false;
		this.Changed?.Invoke();
	}
}
