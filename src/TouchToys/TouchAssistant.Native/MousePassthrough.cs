using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace TouchAssistant.Native;

internal sealed class MousePassthrough : IDisposable
{
	private struct Mouse
	{
		public Win32.POINT Point;

		public uint Data;

		public uint Flags;

		public uint Time;

		public nuint Extra;
	}

	private readonly nint window;

	private readonly Func<bool> enabled;

	private readonly Func<bool>? suppressTouchMouse;

	private readonly bool suppressFirstTouch;

	private static Win32.POINT lastMouse;

	private static bool hasMouse;

	private static bool hasRealMouse;

	private static readonly Win32.HookProc callback = OnMouse;

	private static readonly List<MousePassthrough> registrations = new List<MousePassthrough>();

	private readonly DispatcherQueueTimer restore;

	private static nint hook;

	private bool transparent;

	public static Win32.POINT LastMousePosition
	{
		get
		{
			if (!hasMouse)
			{
				Win32.GetCursorPos(out lastMouse);
			}
			return lastMouse;
		}
	}

	private static nint GetStyle(nint h, int index)
	{
		return WindowLong.Get(h, index);
	}

	private static nint SetStyle(nint h, int index, nint style)
	{
		return WindowLong.Set(h, index, style);
	}

	[DllImport("user32")]
	private static extern bool SetCursorPos(int x, int y);

	public static void RestoreMousePosition()
	{
		if (hasRealMouse && !DesktopVisibility.Locked && InputDesktopState.CanInteract())
		{
			SetCursorPos(lastMouse.X, lastMouse.Y);
		}
	}

	[DllImport("user32")]
	private static extern bool IsWindowVisible(nint window);

	public MousePassthrough(nint window, Func<bool> enabled, Func<bool>? suppressTouchMouse = null, bool suppressFirstTouch = false)
	{
		this.suppressFirstTouch = suppressFirstTouch;
		this.window = window;
		this.enabled = enabled;
		this.suppressTouchMouse = suppressTouchMouse;
		if (!hasMouse)
		{
			Win32.GetCursorPos(out lastMouse);
			hasMouse = true;
		}
		restore = DispatcherQueue.GetForCurrentThread().CreateTimer();
		restore.IsRepeating = false;
		restore.Interval = TimeSpan.FromMilliseconds(30.0);
		restore.Tick += delegate
		{
			Reset();
		};
		if (registrations.Count == 0)
		{
			DesktopVisibility.Changed += DesktopChanged;
		}
		registrations.Add(this);
		DesktopChanged();
	}

	private static void DesktopChanged()
	{
		if (DesktopVisibility.Locked)
		{
			if (hook != 0)
			{
				Win32.UnhookWindowsHookEx(hook);
				hook = 0;
			}
			{
				foreach (MousePassthrough registration in registrations)
				{
					registration.restore.Stop();
					registration.Reset();
				}
				return;
			}
		}
		if (registrations.Count > 0 && hook == 0)
		{
			hook = Win32.SetWindowsHookEx(14, callback, Win32.GetModuleHandle(null), 0u);
		}
	}

	private static nint OnMouse(int code, nuint message, nint data)
	{
		if (DesktopVisibility.Locked || !InputDesktopState.CanInteract())
		{
			return Win32.CallNextHookEx(hook, code, message, data);
		}
		if (code >= 0)
		{
			Mouse mouse = Marshal.PtrToStructure<Mouse>(data);
			bool flag = (mouse.Extra & 0xFFFFFF00u) == 4283520768u;
			if (flag)
			{
				foreach (MousePassthrough registration in registrations)
				{
					if (IsWindowVisible(registration.window))
					{
						Func<bool>? func = registration.suppressTouchMouse;
						if ((func != null && func()) || (registration.suppressFirstTouch && registration.enabled() && Win32.GetWindowRect(registration.window, out var r) && mouse.Point.X >= r.Left && mouse.Point.X < r.Right && mouse.Point.Y >= r.Top && mouse.Point.Y < r.Bottom))
						{
							return 1;
						}
					}
				}
			}
			else
			{
				lastMouse = mouse.Point;
				hasMouse = (hasRealMouse = true);
			}
			if (!flag)
			{
				foreach (MousePassthrough registration2 in registrations)
				{
					if (registration2.enabled() && IsWindowVisible(registration2.window) && Win32.GetWindowRect(registration2.window, out var r2) && mouse.Point.X >= r2.Left && mouse.Point.X < r2.Right && mouse.Point.Y >= r2.Top && mouse.Point.Y < r2.Bottom)
					{
						if (!registration2.transparent)
						{
							SetStyle(registration2.window, -20, (nint)(((IntPtr)GetStyle(registration2.window, -20)).ToInt64() | 0x20));
							registration2.transparent = true;
						}
						registration2.restore.Stop();
						registration2.restore.Start();
					}
				}
			}
		}
		return Win32.CallNextHookEx(hook, code, message, data);
	}

	private void Reset()
	{
		if (transparent)
		{
			SetStyle(window, -20, (nint)(((IntPtr)GetStyle(window, -20)).ToInt64() & -33));
			transparent = false;
		}
	}

	public void Dispose()
	{
		registrations.Remove(this);
		if (registrations.Count == 0)
		{
			DesktopVisibility.Changed -= DesktopChanged;
			if (hook != 0)
			{
				Win32.UnhookWindowsHookEx(hook);
				hook = 0;
			}
		}
		restore.Stop();
		Reset();
	}
}
