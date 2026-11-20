using System;
using Microsoft.UI.Dispatching;
using TouchAssistant.Models;
using TouchAssistant.Services;
using TouchAssistant.UI;

namespace TouchAssistant.Native;

internal sealed class CenterHint : IDisposable
{
	private RectangleHintWindow? window;

	private string? passiveKey;

	private DispatcherQueueTimer? release;

	private bool disposed;

	internal bool HasWindow => window != null;

	public void Show(GestureAction action, Win32.POINT origin, double seconds = 0.0, bool custom = true, bool rectangle = false)
	{
		if (disposed)
		{
			return;
		}
		if (DesktopVisibility.Locked)
		{
			Hide();
			return;
		}
		release?.Stop();
		string text = $"{action.Label}:{action.Kind}:{action.Command}:{action.Icon}:{origin.X}:{origin.Y}:{custom}";
		if (seconds == 0.0 && passiveKey == text)
		{
			RectangleHintWindow rectangleHintWindow = window;
			if ((object)rectangleHintWindow != null && rectangleHintWindow.IsShown)
			{
				return;
			}
		}
		passiveKey = ((seconds == 0.0) ? text : null);
		if (window == null)
		{
			window = new RectangleHintWindow();
			window.Hidden += ScheduleRelease;
		}
		if (!custom)
		{
			action = new GestureAction
			{
				Label = ActionNames.Describe(action)
			};
		}
		window.Show(action, origin, seconds);
	}

	public void Hide()
	{
		passiveKey = null;
		window?.Hide();
	}

	private void ScheduleRelease()
	{
		passiveKey = null;
		if (disposed)
		{
			return;
		}
		if (release == null)
		{
			release = DispatcherQueue.GetForCurrentThread().CreateTimer();
			release.IsRepeating = false;
			release.Interval = TimeSpan.FromSeconds(30.0);
			release.Tick += delegate
			{
				ReleaseIdle();
			};
		}
		release.Stop();
		release.Start();
	}

	internal void ReleaseIdle()
	{
		RectangleHintWindow rectangleHintWindow = window;
		if ((object)rectangleHintWindow != null && !rectangleHintWindow.IsShown)
		{
			window = null;
			rectangleHintWindow.Hidden -= ScheduleRelease;
			rectangleHintWindow.Dispose();
			passiveKey = null;
		}
		release?.Stop();
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			release?.Stop();
			if (window != null)
			{
				window.Hidden -= ScheduleRelease;
				window.Dispose();
				window = null;
			}
			passiveKey = null;
		}
	}
}
