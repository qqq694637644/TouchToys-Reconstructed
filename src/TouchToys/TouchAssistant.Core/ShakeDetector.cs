using System;

namespace TouchAssistant.Core;

internal sealed class ShakeDetector
{
	private double anchorX;

	private double anchorY;

	private double lastX;

	private double lastY;

	private int axis = -1;

	private int direction;

	private int reversals;

	private long started;

	private long lastTurn;

	public void Reset(double x, double y, long now)
	{
		anchorX = (lastX = x);
		anchorY = (lastY = y);
		axis = -1;
		direction = (reversals = 0);
		started = (lastTurn = now);
	}

	public bool Add(double x, double y, long now)
	{
		if (now - started > 1000 || now - lastTurn > 450)
		{
			Reset(x, y, now);
			return false;
		}
		double num = x - anchorX;
		double num2 = y - anchorY;
		if (axis < 0)
		{
			if (Math.Max(Math.Abs(num), Math.Abs(num2)) < 24.0)
			{
				return false;
			}
			axis = ((!(Math.Abs(num) > Math.Abs(num2))) ? 1 : 0);
			direction = Math.Sign((axis == 0) ? num : num2);
			lastX = x;
			lastY = y;
			lastTurn = now;
			return false;
		}
		double value = ((axis == 0) ? (x - lastX) : (y - lastY));
		if (Math.Sign(value) == direction)
		{
			lastX = x;
			lastY = y;
			return false;
		}
		if (Math.Abs(value) < 24.0)
		{
			return false;
		}
		direction = -direction;
		lastX = x;
		lastY = y;
		lastTurn = now;
		reversals++;
		if (reversals < 3)
		{
			return false;
		}
		Reset(x, y, now);
		return true;
	}
}
