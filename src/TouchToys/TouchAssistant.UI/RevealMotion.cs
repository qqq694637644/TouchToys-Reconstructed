using System;
using Microsoft.UI.Dispatching;

namespace TouchAssistant.UI;

internal sealed class RevealMotion
{
	private readonly DispatcherQueueTimer timer;

	private readonly Action<double> apply;

	private readonly Action hidden;

	private double from;

	private double target;

	private long started;

	public double Progress { get; private set; }

	public RevealMotion(DispatcherQueue queue, Action<double> apply, Action hidden)
	{
		RevealMotion revealMotion = this;
		this.apply = apply;
		this.hidden = hidden;
		timer = queue.CreateTimer();
		timer.Interval = TimeSpan.FromMilliseconds(16.0);
		timer.Tick += delegate
		{
			double num = Math.Clamp((double)(Environment.TickCount64 - revealMotion.started) / 180.0, 0.0, 1.0);
			revealMotion.Progress = revealMotion.from + (revealMotion.target - revealMotion.from) * (1.0 - Math.Pow(1.0 - num, 3.0));
			apply(revealMotion.Progress);
			if (num == 1.0)
			{
				revealMotion.timer.Stop();
				if (revealMotion.target == 0.0)
				{
					hidden();
				}
			}
		};
	}

	public void Set(double progress)
	{
		timer.Stop();
		Progress = Math.Clamp(progress, 0.0, 1.0);
		apply(Progress);
	}

	public void To(double value)
	{
		timer.Stop();
		from = Progress;
		target = value;
		started = Environment.TickCount64;
		timer.Start();
	}

	public void Stop()
	{
		timer.Stop();
	}
}
