using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;

namespace TouchAssistant.Native;

internal static class DesktopVisibility
{
	private delegate bool EnumProc(nint h, nint p);

	private static DispatcherQueue? queue;

	private static nint hook;

	private static nint desktopHook;

	private static nint foregroundHook;

	private static bool sessionLocked;

	private static DispatcherQueueTimer? desktopPoll;

	private static readonly Win32.WinEventProc desktopChanged = delegate
	{
		queue?.TryEnqueue(CheckDesktop);
	};

	private static readonly Win32.WinEventProc shown = Shown;

	private static readonly HashSet<nint> hidden = new HashSet<nint>();

	private static volatile bool locked;

	public static bool Locked => locked;

	public static nint AllowedWindow { get; set; }

	public static event Action? Changed;

	[DllImport("user32")]
	private static extern bool EnumWindows(EnumProc callback, nint parameter);

	[DllImport("user32")]
	private static extern bool IsWindowVisible(nint h);

	[DllImport("user32")]
	private static extern bool IsWindow(nint h);

	public static bool CanShow(nint h)
	{
		return !Locked;
	}

	internal static void ForgetWindow(nint h)
	{
		hidden.Remove(h);
	}

	public static void Start()
	{
		queue = DispatcherQueue.GetForCurrentThread();
		SystemEvents.SessionSwitch += Session;
		hook = Win32.SetWinEventHook(32770u, 32770u, 0, shown, (uint)Environment.ProcessId, 0u, 0u);
		desktopHook = Win32.SetWinEventHook(32u, 32u, 0, desktopChanged, 0u, 0u, 0u);
		foregroundHook = Win32.SetWinEventHook(3u, 3u, 0, desktopChanged, 0u, 0u, 0u);
		desktopPoll = queue.CreateTimer();
		desktopPoll.Interval = TimeSpan.FromMilliseconds(150.0);
		desktopPoll.Tick += delegate
		{
			CheckDesktop();
		};
		desktopPoll.Start();
		CheckDesktop();
	}

	public static void Stop()
	{
		SystemEvents.SessionSwitch -= Session;
		desktopPoll?.Stop();
		desktopPoll = null;
		if (hook != 0)
		{
			Win32.UnhookWinEvent(hook);
		}
		if (desktopHook != 0)
		{
			Win32.UnhookWinEvent(desktopHook);
		}
		if (foregroundHook != 0)
		{
			Win32.UnhookWinEvent(foregroundHook);
		}
		hook = (desktopHook = (foregroundHook = 0));
		hidden.Clear();
	}

	private static void Session(object sender, SessionSwitchEventArgs e)
	{
		SessionSwitchReason reason = e.Reason;
		if ((uint)(reason - 6) <= 1u)
		{
			queue?.TryEnqueue(delegate
			{
				sessionLocked = true;
				SetLocked(value: true);
			});
			return;
		}
		reason = e.Reason;
		if ((reason == SessionSwitchReason.SessionLogon || reason == SessionSwitchReason.SessionUnlock) ? true : false)
		{
			queue?.TryEnqueue(delegate
			{
				sessionLocked = false;
				SetLocked(value: false);
			});
		}
	}

	private static void Shown(nint h, uint e, nint w, int obj, int child, uint t, uint ms)
	{
		if (!Locked || obj != 0)
		{
			return;
		}
		queue?.TryEnqueue(delegate
		{
			if (!CanShow(w))
			{
				Win32.ShowWindow(w, 0);
			}
		});
	}

	public static void CheckDesktop()
	{
		SetLocked(sessionLocked || !InputDesktopState.CanInteract());
	}

	internal static void SetLocked(bool value)
	{
		if (locked == value)
		{
			return;
		}
		locked = value;
		if (value)
		{
			EnumWindows(delegate(nint h, nint _)
			{
				Win32.GetWindowThreadProcessId(h, out var process);
				if (process == Environment.ProcessId && IsWindowVisible(h))
				{
					hidden.Add(h);
					Win32.ShowWindow(h, 0);
				}
				return true;
			}, 0);
		}
		else
		{
			List<nint> list = new List<nint>(hidden);
			hidden.Clear();
			foreach (nint item in list)
			{
				if (IsWindow(item))
				{
					Win32.ShowWindow(item, 4);
				}
			}
		}
		DesktopVisibility.Changed?.Invoke();
	}
}
