using System;
using System.Runtime.InteropServices;

namespace TouchAssistant.Native;

internal static class Win32
{
	public delegate nint WndProc(nint hwnd, uint msg, nuint wp, nint lp);

	public delegate nint HookProc(int code, nuint wp, nint lp);

	public delegate void WinEventProc(nint hook, uint evt, nint hwnd, int objectId, int childId, uint thread, uint time);

	public struct POINT
	{
		public int X;

		public int Y;

		public POINT(int x, int y)
		{
			X = x;
			Y = y;
		}
	}

	public struct SIZE
	{
		public int X;

		public int Y;

		public SIZE(int x, int y)
		{
			X = x;
			Y = y;
		}
	}

	public struct RECT
	{
		public int Left;

		public int Top;

		public int Right;

		public int Bottom;

		public int Width => Right - Left;

		public int Height => Bottom - Top;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	public struct WNDCLASS
	{
		public uint style;

		public WndProc lpfnWndProc;

		public int cbClsExtra;

		public int cbWndExtra;

		public nint hInstance;

		public nint hIcon;

		public nint hCursor;

		public nint hbrBackground;

		public string? lpszMenuName;

		public string lpszClassName;
	}

	public struct MONITORINFO
	{
		public int cbSize;

		public RECT rcMonitor;

		public RECT rcWork;

		public uint dwFlags;
	}

	[StructLayout(LayoutKind.Sequential, Pack = 1)]
	public struct BLENDFUNCTION
	{
		public byte BlendOp;

		public byte BlendFlags;

		public byte SourceConstantAlpha;

		public byte AlphaFormat;
	}

	public struct POINTER_INFO
	{
		public uint pointerType;

		public uint pointerId;

		public uint frameId;

		public uint pointerFlags;

		public nint sourceDevice;

		public nint hwndTarget;

		public POINT ptPixelLocation;

		public POINT ptHimetricLocation;

		public POINT ptPixelLocationRaw;

		public POINT ptHimetricLocationRaw;

		public uint dwTime;

		public uint historyCount;

		public int InputData;

		public uint dwKeyStates;

		public ulong PerformanceCount;

		public uint ButtonChangeType;
	}

	public struct INPUT
	{
		public uint type;

		public INPUTUNION data;
	}

	[StructLayout(LayoutKind.Explicit)]
	public struct INPUTUNION
	{
		[FieldOffset(0)]
		public KEYBDINPUT ki;

		[FieldOffset(0)]
		public MOUSEINPUT mi;
	}

	public struct KEYBDINPUT
	{
		public ushort wVk;

		public ushort wScan;

		public uint dwFlags;

		public uint time;

		public nuint dwExtraInfo;
	}

	public struct MOUSEINPUT
	{
		public int dx;

		public int dy;

		public uint mouseData;

		public uint dwFlags;

		public uint time;

		public nuint dwExtraInfo;
	}

	public static nint ArrowCursor => LoadCursor(0, 32512);

	[DllImport("user32")]
	public static extern nint SetWinEventHook(uint min, uint max, nint module, WinEventProc callback, uint process, uint thread, uint flags);

	[DllImport("user32")]
	public static extern bool UnhookWinEvent(nint hook);

	[DllImport("user32")]
	public static extern nint GetAncestor(nint hwnd, uint flags);

	[DllImport("user32")]
	public static extern uint GetWindowThreadProcessId(nint hwnd, out uint process);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	public static extern ushort RegisterClass(ref WNDCLASS wc);

	[DllImport("kernel32", CharSet = CharSet.Unicode)]
	public static extern nint GetModuleHandle(string? name);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	public static extern nint CreateWindowEx(uint ex, string cls, string title, uint style, int x, int y, int w, int h, nint parent, nint menu, nint instance, nint param);

	[DllImport("user32")]
	public static extern nint DefWindowProc(nint h, uint m, nuint w, nint l);

	[DllImport("user32")]
	public static extern bool DestroyWindow(nint h);

	[DllImport("user32")]
	public static extern bool IsWindowVisible(nint h);

	[DllImport("user32", EntryPoint = "ShowWindow")]
	private static extern bool NativeShowWindow(nint h, int cmd);

	public static bool ShowWindow(nint h, int cmd)
	{
		if (cmd != 0 && DesktopVisibility.Locked)
		{
			GetWindowThreadProcessId(h, out var process);
			if (process == Environment.ProcessId && !DesktopVisibility.CanShow(h))
			{
				return NativeShowWindow(h, 0);
			}
		}
		return NativeShowWindow(h, cmd);
	}

	[DllImport("user32", EntryPoint = "SetWindowPos")]
	private static extern bool NativeSetWindowPos(nint h, nint after, int x, int y, int w, int height, uint flags);

	public static bool SetWindowPos(nint h, nint after, int x, int y, int w, int height, uint flags)
	{
		if ((flags & 0x40) != 0 && DesktopVisibility.Locked)
		{
			GetWindowThreadProcessId(h, out var process);
			if (process == Environment.ProcessId && !DesktopVisibility.CanShow(h))
			{
				NativeShowWindow(h, 0);
				flags &= 0xFFFFFFBFu;
			}
		}
		return NativeSetWindowPos(h, after, x, y, w, height, flags);
	}

	[DllImport("user32")]
	public static extern nint GetDC(nint h);

	[DllImport("user32")]
	public static extern int ReleaseDC(nint h, nint dc);

	[DllImport("gdi32")]
	public static extern nint CreateCompatibleDC(nint dc);

	[DllImport("gdi32")]
	public static extern nint SelectObject(nint dc, nint o);

	[DllImport("gdi32")]
	public static extern bool DeleteObject(nint o);

	[DllImport("gdi32")]
	public static extern bool DeleteDC(nint dc);

	[DllImport("user32")]
	public static extern bool UpdateLayeredWindow(nint h, nint dc, ref POINT dst, ref SIZE size, nint src, ref POINT origin, uint key, ref BLENDFUNCTION blend, uint flags);

	[DllImport("user32", SetLastError = true)]
	public static extern bool GetPointerInfo(uint id, out POINTER_INFO info);

	[DllImport("user32")]
	public static extern bool GetCursorPos(out POINT p);

	[DllImport("user32")]
	public static extern nint SetCapture(nint h);

	[DllImport("user32")]
	public static extern bool ReleaseCapture();

	[DllImport("user32", CharSet = CharSet.Unicode)]
	public static extern nint LoadCursor(nint instance, nint name);

	[DllImport("user32")]
	public static extern nint SetCursor(nint cursor);

	[DllImport("user32")]
	public static extern nuint GetMessageExtraInfo();

	[DllImport("user32")]
	public static extern uint GetDoubleClickTime();

	[DllImport("user32")]
	public static extern bool SetWindowFeedbackSetting(nint h, int feedback, uint flags, uint size, ref int value);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	public static extern bool SetProp(nint h, string key, nint data);

	[DllImport("user32")]
	public static extern nint MonitorFromPoint(POINT p, uint flags);

	[DllImport("user32")]
	public static extern bool GetMonitorInfo(nint monitor, ref MONITORINFO info);

	[DllImport("shcore")]
	public static extern int GetDpiForMonitor(nint monitor, int type, out uint x, out uint y);

	[DllImport("user32")]
	public static extern uint GetDpiForWindow(nint h);

	[DllImport("user32")]
	public static extern bool GetWindowRect(nint h, out RECT r);

	[DllImport("user32")]
	public static extern nint SetWindowsHookEx(int id, HookProc proc, nint module, uint thread);

	[DllImport("user32")]
	public static extern bool UnhookWindowsHookEx(nint hook);

	[DllImport("user32")]
	public static extern nint CallNextHookEx(nint hook, int code, nuint wp, nint lp);

	[DllImport("user32")]
	public static extern uint SendInput(uint count, INPUT[] input, int size);

	[DllImport("user32")]
	public static extern bool LockWorkStation();

	[DllImport("user32")]
	public static extern nint GetForegroundWindow();

	[DllImport("user32")]
	public static extern bool SetForegroundWindow(nint h);

	[DllImport("user32")]
	public static extern short GetAsyncKeyState(int key);

	public static MONITORINFO Monitor(POINT p)
	{
		MONITORINFO info = new MONITORINFO
		{
			cbSize = Marshal.SizeOf<MONITORINFO>()
		};
		GetMonitorInfo(MonitorFromPoint(p, 2u), ref info);
		return info;
	}

	public static double Scale(POINT p)
	{
		GetDpiForMonitor(MonitorFromPoint(p, 2u), 0, out var x, out var _);
		if (x == 0)
		{
			return 1.0;
		}
		return (double)x / 96.0;
	}

	public static void SuppressFeedback(nint hwnd)
	{
		int value = 0;
		int[] array = new int[11]
		{
			1, 2, 3, 4, 5, 6, 7, 8, 9, 10,
			11
		};
		foreach (int feedback in array)
		{
			SetWindowFeedbackSetting(hwnd, feedback, 0u, 4u, ref value);
		}
		SetProp(hwnd, "MicrosoftTabletPenServiceProperty", 1114137);
	}
}
