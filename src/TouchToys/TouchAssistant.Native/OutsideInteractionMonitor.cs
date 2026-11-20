using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace TouchAssistant.Native;

internal sealed class OutsideInteractionMonitor : IDisposable
{
	private struct MSG
	{
		public nint hwnd;

		public uint message;

		public nuint wp;

		public nint lp;

		public uint time;

		public Win32.POINT pt;

		public uint extra;
	}

	private readonly nint panel;

	private readonly Action<bool> outside;

	private readonly Thread thread;

	private readonly Win32.HookProc mouse;

	private readonly Win32.WinEventProc foreground;

	private volatile bool stopped;

	private volatile bool suspended;

	private uint threadId;

	private nint mouseHook;

	private nint foregroundHook;

	private OutsideTouchShield? shield;

	public bool Suspended
	{
		set
		{
			suspended = value;
			shield?.SetSuspended(value);
		}
	}

	[DllImport("user32")]
	private static extern int GetMessage(out MSG message, nint hwnd, uint min, uint max);

	[DllImport("user32")]
	private static extern bool PeekMessage(out MSG message, nint hwnd, uint min, uint max, uint remove);

	[DllImport("user32")]
	private static extern bool PostThreadMessage(uint thread, uint message, nuint wp, nint lp);

	[DllImport("user32")]
	private static extern bool TranslateMessage(ref MSG message);

	[DllImport("user32")]
	private static extern nint DispatchMessage(ref MSG message);

	[DllImport("kernel32")]
	private static extern uint GetCurrentThreadId();

	public void RefreshBounds()
	{
		shield?.Refresh();
	}

	public OutsideInteractionMonitor(nint panel, Action<bool> outside)
	{
		this.panel = panel;
		this.outside = outside;
		mouse = Mouse;
		foreground = Foreground;
		thread = new Thread(Run)
		{
			IsBackground = true,
			Name = "TouchAssistant.OutsideInput"
		};
		thread.Start();
	}

	private void Run()
	{
		threadId = GetCurrentThreadId();
		PeekMessage(out var _, 0, 0u, 0u, 0u);
		if (stopped)
		{
			return;
		}
		mouseHook = Win32.SetWindowsHookEx(14, mouse, Win32.GetModuleHandle(null), 0u);
		foregroundHook = Win32.SetWinEventHook(3u, 3u, 0, foreground, 0u, 0u, 0u);
		shield = new OutsideTouchShield(panel, delegate
		{
			if (!stopped && !suspended)
			{
				outside(obj: false);
			}
		});
		shield.SetSuspended(suspended);
		try
		{
			MSG message2;
			while (!stopped && GetMessage(out message2, 0, 0u, 0u) > 0)
			{
				TranslateMessage(ref message2);
				DispatchMessage(ref message2);
			}
		}
		finally
		{
			shield?.Dispose();
			shield = null;
			if (mouseHook != 0)
			{
				Win32.UnhookWindowsHookEx(mouseHook);
			}
			if (foregroundHook != 0)
			{
				Win32.UnhookWinEvent(foregroundHook);
			}
		}
	}

	private nint Mouse(int code, nuint wp, nint lp)
	{
		bool flag = code >= 0 && !stopped && !suspended;
		if (flag)
		{
			bool flag2;
			switch (wp)
			{
			case (nuint)513:
			case (nuint)516:
			case (nuint)519:
			case (nuint)523:
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		if (flag)
		{
			Win32.POINT pOINT = Marshal.PtrToStructure<Win32.POINT>(lp);
			if (Win32.GetWindowRect(panel, out var r) && (pOINT.X < r.Left || pOINT.X >= r.Right || pOINT.Y < r.Top || pOINT.Y >= r.Bottom))
			{
				outside(obj: false);
			}
		}
		return Win32.CallNextHookEx(mouseHook, code, wp, lp);
	}

	private void Foreground(nint hook, uint evt, nint hwnd, int obj, int child, uint tid, uint time)
	{
		if (!stopped && !suspended && hwnd != 0 && hwnd != panel && Win32.GetAncestor(hwnd, 3u) != panel)
		{
			Win32.GetWindowThreadProcessId(hwnd, out var process);
			if (process != Environment.ProcessId)
			{
				outside(obj: true);
			}
		}
	}

	public void Dispose()
	{
		stopped = true;
		if (threadId != 0)
		{
			PostThreadMessage(threadId, 18u, 0u, 0);
		}
	}
}
