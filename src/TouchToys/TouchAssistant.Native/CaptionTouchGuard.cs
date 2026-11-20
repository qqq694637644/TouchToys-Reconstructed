using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.UI.Dispatching;
using TouchAssistant.Models;

namespace TouchAssistant.Native;

internal sealed class CaptionTouchGuard : IDisposable
{
	private struct InputSource
	{
		public int Device;

		public int Origin;
	}

	private static nint inputWindow;

	private readonly SettingsStore store;

	private readonly Win32.WndProc callback;

	private readonly DispatcherQueue queue;

	private readonly DispatcherQueueTimer timer;

	private readonly Win32.WinEventProc changed;

	private nint foregroundHook;

	private nint locationHook;

	private readonly nint handle;

	private nint target;

	private nint pressedTarget;

	private Win32.RECT closeBounds;

	private bool disposed;

	private bool updateQueued;

	public event Action<uint, nuint, nint>? PointerInput;

	internal static bool IsInputWindow(nint window)
	{
		if (window != 0)
		{
			return window == Volatile.Read(in inputWindow);
		}
		return false;
	}

	[DllImport("user32")]
	private static extern bool GetCurrentInputMessageSource(out InputSource source);

	[DllImport("user32")]
	private static extern nint GetMessageExtraInfo();

	[DllImport("user32")]
	private static extern bool IsZoomed(nint h);

	[DllImport("user32")]
	private static extern bool IsWindow(nint h);

	[DllImport("user32")]
	private static extern bool SetLayeredWindowAttributes(nint h, uint key, byte alpha, uint flags);

	[DllImport("gdi32")]
	private static extern nint GetStockObject(int index);

	[DllImport("user32")]
	private static extern nint MonitorFromWindow(nint window, uint flags);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool PostMessage(nint h, uint message, nuint w, nint l);

	[DllImport("user32")]
	private static extern nint GetCapture();

	[DllImport("user32")]
	private static extern bool UnhookWinEvent(nint hook);

	public CaptionTouchGuard(SettingsStore settings)
	{
		store = settings;
		queue = DispatcherQueue.GetForCurrentThread();
		callback = Procedure;
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			hInstance = Win32.GetModuleHandle(null),
			lpfnWndProc = callback,
			hCursor = Win32.ArrowCursor,
			lpszClassName = "TouchAssistant.CaptionGuard",
			hbrBackground = GetStockObject(0)
		};
		Win32.RegisterClass(ref wc);
		handle = Win32.CreateWindowEx(134742152u, wc.lpszClassName, "TouchToys Close Button Touch Guard", 2147483648u, 0, 0, 1, 1, 0, 0, wc.hInstance, 0);
		Volatile.Write(ref inputWindow, handle);
		SetLayeredWindowAttributes(handle, 0u, 1, 2u);
		Win32.SuppressFeedback(handle);
		timer = queue.CreateTimer();
		timer.Interval = TimeSpan.FromMilliseconds(250.0);
		timer.Tick += delegate
		{
			Update();
		};
		changed = delegate(nint _, uint _, nint h, int _, int _, uint _, uint _)
		{
			if (!disposed && h != handle && store.Value.CloseGuardEnabled && !updateQueued && (h == target || h == Win32.GetForegroundWindow()))
			{
				updateQueued = true;
				queue.TryEnqueue(delegate
				{
					updateQueued = false;
					if (!disposed)
					{
						Update();
					}
				});
			}
		};
		foregroundHook = Win32.SetWinEventHook(3u, 3u, 0, changed, 0u, 0u, 0u);
		locationHook = Win32.SetWinEventHook(32779u, 32779u, 0, changed, 0u, 0u, 0u);
		store.Changed += Update;
		DesktopVisibility.Changed += Update;
		Update();
	}

	private static nint Position(Win32.POINT point)
	{
		return (nint)(uint)((ushort)point.X | ((ushort)point.Y << 16));
	}

	private static bool Contains(Win32.RECT r, Win32.POINT p)
	{
		if (p.X >= r.Left && p.X < r.Right && p.Y >= r.Top)
		{
			return p.Y < r.Bottom;
		}
		return false;
	}

	internal static bool TryCloseBounds(nint window, out Win32.RECT rect)
	{
		rect = default(Win32.RECT);
		if (window == 0 || !IsWindow(window) || !IsZoomed(window))
		{
			return false;
		}
		nint num = MonitorFromWindow(window, 2u);
		Win32.MONITORINFO info = new Win32.MONITORINFO
		{
			cbSize = Marshal.SizeOf<Win32.MONITORINFO>()
		};
		if (num == 0 || !Win32.GetMonitorInfo(num, ref info))
		{
			return false;
		}
		Win32.GetDpiForMonitor(num, 0, out var x, out var _);
		rect = FixedBounds(info.rcWork, x);
		if (rect.Width > 0)
		{
			return rect.Height > 0;
		}
		return false;
	}

	internal static Win32.RECT FixedBounds(Win32.RECT work, uint dpi)
	{
		if (work.Width <= 0 || work.Height <= 0)
		{
			return default(Win32.RECT);
		}
		double num = (double)((dpi == 0) ? 96u : dpi) / 96.0;
		int num2 = (int)Math.Round(46.0 * num, MidpointRounding.AwayFromZero);
		int num3 = (int)Math.Round(32.0 * num, MidpointRounding.AwayFromZero);
		return new Win32.RECT
		{
			Left = Math.Max(work.Left, work.Right - num2),
			Top = work.Top,
			Right = work.Right,
			Bottom = Math.Min(work.Bottom, work.Top + num3)
		};
	}

	private void CancelMouse()
	{
		pressedTarget = 0;
		if (GetCapture() == handle)
		{
			Win32.ReleaseCapture();
		}
	}

	private void Hide()
	{
		CancelMouse();
		target = 0;
		Win32.ShowWindow(handle, 0);
	}

	private void Update()
	{
		if (disposed)
		{
			return;
		}
		if (!store.Value.CloseGuardEnabled || DesktopVisibility.Locked)
		{
			timer.Stop();
			Hide();
			return;
		}
		if (!timer.IsRunning)
		{
			timer.Start();
		}
		nint foregroundWindow = Win32.GetForegroundWindow();
		if (!TryCloseBounds(foregroundWindow, out var rect))
		{
			Hide();
			return;
		}
		if (target != foregroundWindow || rect.Left != closeBounds.Left || rect.Top != closeBounds.Top || rect.Right != closeBounds.Right || rect.Bottom != closeBounds.Bottom)
		{
			CancelMouse();
		}
		target = foregroundWindow;
		closeBounds = rect;
		Win32.SetWindowPos(handle, -1, rect.Left, rect.Top, rect.Width, rect.Height, 80u);
	}

	private static bool RealMouse()
	{
		if ((((IntPtr)GetMessageExtraInfo()).ToInt64() & 0xFFFFFF00u) == 4283520768u)
		{
			return false;
		}
		InputSource source;
		bool flag = !GetCurrentInputMessageSource(out source);
		if (!flag)
		{
			int device = source.Device;
			bool flag2 = ((device == 0 || device == 2) ? true : false);
			flag = flag2;
		}
		return flag;
	}

	private nint Procedure(nint h, uint message, nuint w, nint l)
	{
		bool flag;
		switch (message)
		{
		case 33u:
			return 3;
		case 32u:
			Win32.SetCursor(Win32.ArrowCursor);
			return 1;
		case 581u:
		case 582u:
		case 583u:
		case 588u:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			if (message == 588)
			{
				this.PointerInput?.Invoke(message, w, l);
				return 0;
			}
			flag = Win32.GetPointerInfo((uint)((int)w & 0xFFFF), out var info);
			if (flag)
			{
				uint pointerType = info.pointerType;
				bool flag2 = pointerType - 2 <= 1;
				flag = flag2;
			}
			if (flag)
			{
				CancelMouse();
				this.PointerInput?.Invoke(message, w, l);
				return 0;
			}
		}
		switch (message)
		{
		case 576u:
			TouchMessages.Read(w, l, delegate
			{
			});
			return 0;
		case 513u:
		case 515u:
		{
			if (RealMouse() && target != 0 && Win32.GetForegroundWindow() == target && Win32.GetCursorPos(out var p3) && Contains(closeBounds, p3))
			{
				pressedTarget = target;
				Win32.SetCapture(handle);
			}
			return 0;
		}
		case 514u:
		{
			nint num = pressedTarget;
			Win32.POINT p2 = default(Win32.POINT);
			bool num2 = RealMouse() && num != 0 && num == target && Win32.GetForegroundWindow() == num && Win32.GetCursorPos(out p2) && Contains(closeBounds, p2);
			CancelMouse();
			if (num2 && TryCloseBounds(num, out var rect) && Contains(rect, p2))
			{
				PostMessage(num, 274u, 61536u, 0);
			}
			return 0;
		}
		case 517u:
		{
			if (RealMouse() && target != 0 && Win32.GetCursorPos(out var p) && Contains(closeBounds, p))
			{
				PostMessage(target, 274u, 61584u, Position(p));
			}
			return 0;
		}
		case 31u:
		case 533u:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			CancelMouse();
		}
		return Win32.DefWindowProc(h, message, w, l);
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			if (IsInputWindow(handle))
			{
				Volatile.Write(ref inputWindow, 0);
			}
			store.Changed -= Update;
			DesktopVisibility.Changed -= Update;
			timer.Stop();
			CancelMouse();
			if (foregroundHook != 0)
			{
				UnhookWinEvent(foregroundHook);
			}
			if (locationHook != 0)
			{
				UnhookWinEvent(locationHook);
			}
			Win32.DestroyWindow(handle);
		}
	}
}
