using System;

namespace TouchAssistant.Core;

internal sealed class IndicatorTaps
{
	private long deadline;

	private double x;

	private double y;

	public bool Pending { get; private set; }

	public void Reset()
	{
		Pending = false;
	}

	public bool CanCombine(long now, double px, double py)
	{
		if (Pending && now <= deadline && Math.Abs(px - x) <= 24.0)
		{
			return Math.Abs(py - y) <= 24.0;
		}
		return false;
	}

	public string? Tap(long now, double px, double py, int interval)
	{
		if (CanCombine(now, px, py))
		{
			Reset();
			return "DoubleTap";
		}
		Pending = true;
		deadline = now + interval;
		x = px;
		y = py;
		return null;
	}

	public string? Flush(long now)
	{
		if (!Pending || now < deadline)
		{
			return null;
		}
		Reset();
		return "Tap";
	}
}
