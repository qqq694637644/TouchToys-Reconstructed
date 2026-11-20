using System;
using Microsoft.UI.Dispatching;

namespace TouchAssistant.Native;

internal sealed class IdleOpacity : IDisposable
{
	private readonly DispatcherQueueTimer timer;

	private readonly Func<double> normal;

	private readonly Func<double> idle;

	private readonly Action changed;

	private bool fading;

	private bool disposed;

	private long started;

	internal double Value { get; private set; }

	internal IdleOpacity(Func<double> normal, Func<double> idle, Action changed)
	{
		this.normal = normal;
		this.idle = idle;
		this.changed = changed;
		Value = normal();
		timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
		timer.IsRepeating = false;
		timer.Tick += Tick;
	}

	internal void Active(bool held)
	{
		if (!disposed)
		{
			timer.Stop();
			fading = false;
			double num = normal();
			bool num2 = Math.Abs(Value - num) > 0.001;
			Value = num;
			if (num2)
			{
				changed();
			}
			if (!held)
			{
				timer.Interval = TimeSpan.FromSeconds(3.0);
				timer.Start();
			}
		}
	}

	internal void Pause()
	{
		timer.Stop();
	}

	private void Tick(DispatcherQueueTimer sender, object args)
	{
		if (!fading)
		{
			fading = true;
			started = Environment.TickCount64;
		}
		double num = Math.Clamp((double)(Environment.TickCount64 - started) / 300.0, 0.0, 1.0);
		double num2 = num * num * (3.0 - 2.0 * num);
		Value = normal() + (idle() - normal()) * num2;
		changed();
		if (num < 1.0)
		{
			timer.Interval = TimeSpan.FromMilliseconds(30.0);
			timer.Start();
		}
	}

	public void Dispose()
	{
		disposed = true;
		timer.Stop();
		timer.Tick -= Tick;
	}
}
