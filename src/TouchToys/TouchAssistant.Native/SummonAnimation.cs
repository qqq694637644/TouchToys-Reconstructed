using System;
using Microsoft.UI.Dispatching;
using TouchAssistant.Core;

namespace TouchAssistant.Native;

internal sealed class SummonAnimation : IDisposable
{
	private readonly DispatcherQueueTimer timer;

	private readonly Action draw;

	private long started;

	private double duration = 900.0;

	private bool reverse;

	private readonly bool compact;

	public double Progress { get; private set; } = 1.0;

	internal event Action? Finished;

	public SummonAnimation(Action render, bool compact = false)
	{
		this.compact = compact;
		draw = render;
		timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
		timer.Interval = TimeSpan.FromMilliseconds(16.0);
		timer.Tick += delegate
		{
			Tick();
		};
	}

	public void Start()
	{
		reverse = false;
		duration = (compact ? 300.0 : 900.0);
		started = Environment.TickCount64;
		Progress = 0.0;
		timer.Start();
		draw();
	}

	public void StartReverse(double seconds = 0.0)
	{
		reverse = true;
		duration = ((seconds > 0.0) ? seconds : (compact ? 220.0 : 450.0));
		started = Environment.TickCount64;
		Progress = 1.0;
		timer.Start();
		draw();
	}

	private void Tick()
	{
		double num = (double)(Environment.TickCount64 - started) / duration;
		Progress = (reverse ? Math.Max(0.0, 1.0 - num) : Math.Min(1.0, num));
		draw();
		if (reverse ? (Progress <= 0.0) : (Progress >= 1.0))
		{
			timer.Stop();
			if (reverse)
			{
				this.Finished?.Invoke();
			}
		}
	}

	public (double Inner, double Outer) Sample(double ratio)
	{
		if (!compact)
		{
			return SummonCurve.At(Progress, ratio);
		}
		return SummonCurve.CompactAt(Progress, ratio);
	}

	public void Dispose()
	{
		timer.Stop();
	}
}
