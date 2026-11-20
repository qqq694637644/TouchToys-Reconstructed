using System;

namespace TouchAssistant.Core;

internal sealed class TouchpadMiddleGesture
{
	private readonly Func<string, bool> bound;

	private readonly Action<string, double> emit;

	private readonly Func<double> holdMilliseconds;

	private double startX;

	private double startY;

	private double lastY;

	private double maxTravel;

	private long began;

	private long deadline;

	private int taps;

	private bool active;

	private bool scrolled;

	private bool held;

	public bool NeedsTick
	{
		get
		{
			if (!active)
			{
				return taps > 0;
			}
			return true;
		}
	}

	public TouchpadMiddleGesture(Func<string, bool> bound, Action<string, double> emit, Func<double> holdMilliseconds)
	{
		this.bound = bound;
		this.emit = emit;
		this.holdMilliseconds = holdMilliseconds;
	}

	public void Down(double x, double y, long now)
	{
		Tick(now);
		active = true;
		scrolled = (held = false);
		maxTravel = 0.0;
		startX = x;
		startY = (lastY = y);
		began = now;
	}

	public void Move(double x, double y)
	{
		if (!active)
		{
			return;
		}
		maxTravel = Math.Max(maxTravel, Math.Sqrt((x - startX) * (x - startX) + (y - startY) * (y - startY)));
		double num = y - lastY;
		lastY = y;
		if (held)
		{
			return;
		}
		if (Math.Abs(y - startY) > 6.0)
		{
			scrolled = true;
		}
		if (scrolled)
		{
			taps = 0;
			if (num != 0.0)
			{
				emit((num < 0.0) ? "Up" : "Down", Math.Abs(num));
			}
		}
	}

	public void Up(double x, double y, long now)
	{
		if (!active)
		{
			return;
		}
		Move(x, y);
		Tick(now);
		active = false;
		if (!scrolled && !held && !(maxTravel > 6.0))
		{
			taps++;
			deadline = now + 350;
			int num = (bound("TripleTap") ? 3 : ((!bound("DoubleTap")) ? 1 : 2));
			if (taps >= num)
			{
				Flush();
			}
		}
	}

	public void Tick(long now)
	{
		if (active && !scrolled && !held && maxTravel <= 6.0 && (double)(now - began) >= holdMilliseconds() && bound("Hold"))
		{
			held = true;
			taps = 0;
			emit("Hold", 0.0);
		}
		if (!active && taps > 0 && now >= deadline)
		{
			Flush();
		}
	}

	private void Flush()
	{
		int num = taps;
		taps = 0;
		if (num > 0)
		{
			emit((num >= 3) ? "TripleTap" : ((num == 2) ? "DoubleTap" : "Tap"), 0.0);
		}
	}

	public void Reset()
	{
		active = (scrolled = (held = false));
		taps = 0;
		maxTravel = 0.0;
	}
}
