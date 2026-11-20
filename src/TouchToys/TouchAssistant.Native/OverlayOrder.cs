using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace TouchAssistant.Native;

internal static class OverlayOrder
{
	private static readonly Dictionary<nint, int> windows = new Dictionary<nint, int>();

	private static KeyValuePair<nint, int>[]? orderedWindows;

	private static readonly Win32.WinEventProc callback = Changed;

	private static nint hook;

	private static nint pendingForeground;

	private static nint lastForeground;

	private static bool foregroundQueued;

	private static DispatcherQueue? queue;

	private static DispatcherQueueTimer? watchdog;

	private static KeyValuePair<nint, int>[] OrderedWindows => orderedWindows ?? (orderedWindows = windows.OrderBy((KeyValuePair<nint, int> x) => x.Value).ToArray());

	public static event Action<nint>? ForegroundChanged;

	[DllImport("user32")]
	private static extern bool IsWindowVisible(nint window);

	public static void Register(nint hwnd, int priority = 0)
	{
		windows[hwnd] = priority;
		orderedWindows = null;
		if ((object)queue == null)
		{
			queue = DispatcherQueue.GetForCurrentThread();
		}
		if (hook == 0)
		{
			hook = Win32.SetWinEventHook(3u, 3u, 0, callback, 0u, 0u, 0u);
		}
		if (watchdog == null)
		{
			watchdog = queue.CreateTimer();
			watchdog.Interval = TimeSpan.FromMilliseconds(750.0);
			watchdog.Tick += delegate
			{
				DesktopVisibility.CheckDesktop();
				RaiseAll();
			};
			watchdog.Start();
		}
		Raise(hwnd);
	}

	public static void Remove(nint hwnd)
	{
		windows.Remove(hwnd);
		orderedWindows = null;
		if (windows.Count == 0)
		{
			if (hook != 0)
			{
				Win32.UnhookWinEvent(hook);
				hook = 0;
			}
			watchdog?.Stop();
			watchdog = null;
		}
	}

	private static void Changed(nint h, uint e, nint w, int o, int c, uint t, uint ms)
	{
		pendingForeground = w;
		if (foregroundQueued)
		{
			return;
		}
		foregroundQueued = true;
		queue?.TryEnqueue(delegate
		{
			foregroundQueued = false;
			nint num = pendingForeground;
			if (num != lastForeground)
			{
				lastForeground = num;
				OverlayOrder.ForegroundChanged?.Invoke(num);
				RaiseAll();
			}
		});
	}

	public static void RaiseAll()
	{
		KeyValuePair<nint, int>[] array = OrderedWindows;
		foreach (KeyValuePair<nint, int> keyValuePair in array)
		{
			RaiseNative(keyValuePair.Key);
		}
	}

	public static void Raise(nint hwnd)
	{
		RaiseNative(hwnd);
		if (!windows.TryGetValue(hwnd, out var value) || value >= 0)
		{
			return;
		}
		KeyValuePair<nint, int>[] array = OrderedWindows;
		for (int i = 0; i < array.Length; i++)
		{
			KeyValuePair<nint, int> keyValuePair = array[i];
			if (keyValuePair.Value > value)
			{
				RaiseNative(keyValuePair.Key);
			}
		}
	}

	private static void RaiseNative(nint hwnd)
	{
		if (!DesktopVisibility.CanShow(hwnd))
		{
			Win32.ShowWindow(hwnd, 0);
		}
		else if (IsWindowVisible(hwnd))
		{
			Win32.SetWindowPos(hwnd, -1, 0, 0, 0, 0, 19u);
		}
	}
}
