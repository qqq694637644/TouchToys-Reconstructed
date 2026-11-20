using System;

namespace TouchAssistant.Core;

internal sealed class ScrollMomentum
{
	private double lastPosition;

	private double velocity;

	private long lastSample;

	private long lastTick;

	public bool Running { get; private set; }

	public void Reset(double position, long now)
	{
		Running = false;
		lastPosition = position;
		lastSample = (lastTick = now);
		velocity = 0.0;
	}

	public void Sample(double position, long now)
	{
		long num = now - lastSample;
		if (num > 0)
		{
			double num2 = (position - lastPosition) / (double)num;
			velocity = ((num > 100) ? num2 : (velocity * 0.35 + num2 * 0.65));
			lastSample = now;
			lastPosition = position;
		}
	}

	public void Release(long now)
	{
		Running = now - lastSample < 100 && Math.Abs(velocity) > 0.04;
		lastTick = now;
	}

	public double Step(long now)
	{
		if (!Running)
		{
			return 0.0;
		}
		double num = Math.Clamp(now - lastTick, 0L, 50L);
		lastTick = now;
		double num2 = Math.Exp((0.0 - num) / 240.0);
		double result = velocity * 240.0 * (1.0 - num2);
		velocity *= num2;
		if (Math.Abs(velocity) < 0.015)
		{
			Running = false;
		}
		return result;
	}

	public void Stop()
	{
		Running = false;
		velocity = 0.0;
	}
}
