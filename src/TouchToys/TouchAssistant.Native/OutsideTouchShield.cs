using System;
using System.Runtime.InteropServices;

namespace TouchAssistant.Native;

internal sealed class OutsideTouchShield : IDisposable
{
	private struct InputSource
	{
		public int Device;

		public int Origin;
	}

	private readonly Win32.WndProc procedure;

	private readonly Action dismiss;

	private readonly nint panel;

	private readonly nint[] handles = new nint[4];

	private readonly string className;

	private bool suspended;

	private bool disposed;

	[DllImport("user32")]
	private static extern int GetSystemMetrics(int index);

	[DllImport("user32")]
	private static extern bool SetLayeredWindowAttributes(nint h, uint color, byte alpha, uint flags);

	[DllImport("gdi32")]
	private static extern nint GetStockObject(int index);

	[DllImport("user32")]
	private static extern bool GetCurrentInputMessageSource(out InputSource source);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool UnregisterClass(string name, nint module);

	[DllImport("user32")]
	private static extern nuint SetTimer(nint h, nuint id, uint interval, nint callback);

	[DllImport("user32")]
	private static extern bool KillTimer(nint h, nuint id);

	private void HideAll()
	{
		nint[] array = handles;
		foreach (nint num in array)
		{
			if (num != 0)
			{
				Win32.ShowWindow(num, 0);
			}
		}
	}

	public OutsideTouchShield(nint panel, Action dismiss)
	{
		this.panel = panel;
		this.dismiss = dismiss;
		procedure = Procedure;
		className = "TouchAssistant.Dismiss." + Guid.NewGuid().ToString("N");
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = procedure,
			hCursor = Win32.ArrowCursor,
			hInstance = Win32.GetModuleHandle(null),
			lpszClassName = className,
			hbrBackground = GetStockObject(4)
		};
		Win32.RegisterClass(ref wc);
		for (int i = 0; i < handles.Length; i++)
		{
			handles[i] = Win32.CreateWindowEx(134742152u, className, "TouchToys Dismiss", 2147483648u, 0, 0, 1, 1, 0, 0, wc.hInstance, 0);
			SetLayeredWindowAttributes(handles[i], 0u, 1, 2u);
			Win32.SuppressFeedback(handles[i]);
		}
		SetTimer(handles[0], 1u, 250u, 0);
		Refresh();
		OverlayOrder.Raise(panel);
	}

	public void SetSuspended(bool value)
	{
		suspended = value;
		Refresh();
	}

	public void Refresh()
	{
		if (disposed)
		{
			return;
		}
		if (suspended || DesktopVisibility.Locked || !Win32.IsWindowVisible(panel) || !Win32.GetWindowRect(panel, out var r) || r.Width <= 0 || r.Height <= 0)
		{
			HideAll();
			return;
		}
		int systemMetrics = GetSystemMetrics(76);
		int systemMetrics2 = GetSystemMetrics(77);
		int num = systemMetrics + GetSystemMetrics(78);
		int num2 = systemMetrics2 + GetSystemMetrics(79);
		int num3 = Math.Clamp(r.Left, systemMetrics, num);
		int num4 = Math.Clamp(r.Right, systemMetrics, num);
		int num5 = Math.Clamp(r.Top, systemMetrics2, num2);
		int num6 = Math.Clamp(r.Bottom, systemMetrics2, num2);
		if (num3 >= num4 || num5 >= num6)
		{
			HideAll();
			return;
		}
		Place(0, systemMetrics, systemMetrics2, num - systemMetrics, num5 - systemMetrics2);
		Place(1, systemMetrics, num6, num - systemMetrics, num2 - num6);
		Place(2, systemMetrics, num5, num3 - systemMetrics, num6 - num5);
		Place(3, num4, num5, num - num4, num6 - num5);
	}

	private void Place(int index, int x, int y, int width, int height)
	{
		nint num = handles[index];
		if (num != 0)
		{
			if (suspended || width <= 0 || height <= 0)
			{
				Win32.ShowWindow(num, 0);
			}
			else
			{
				Win32.SetWindowPos(num, 0, x, y, width, height, 84u);
			}
		}
	}

	private nint Procedure(nint h, uint msg, nuint wp, nint lp)
	{
		if (msg == 275 && wp == 1)
		{
			Refresh();
			return 0;
		}
		switch (msg)
		{
		case 33u:
			return 3;
		case 32u:
			Win32.SetCursor(Win32.ArrowCursor);
			return 1;
		case 132u:
		{
			if (GetCurrentInputMessageSource(out var source) && source.Device == 2)
			{
				return -1;
			}
			break;
		}
		}
		Win32.POINTER_INFO info = default(Win32.POINTER_INFO);
		bool flag = msg == 582 && Win32.GetPointerInfo((uint)((int)wp & 0xFFFF), out info);
		if (flag)
		{
			uint pointerType = info.pointerType;
			bool flag2 = pointerType - 2 <= 1;
			flag = flag2;
		}
		if (flag)
		{
			suspended = true;
			HideAll();
			dismiss();
			return 0;
		}
		if ((msg == 513 || msg == 516) ? true : false)
		{
			suspended = true;
			HideAll();
			dismiss();
			return 0;
		}
		return Win32.DefWindowProc(h, msg, wp, lp);
	}

	public void Dispose()
	{
		disposed = true;
		KillTimer(handles[0], 1u);
		for (int i = 0; i < handles.Length; i++)
		{
			if (handles[i] != 0)
			{
				Win32.DestroyWindow(handles[i]);
			}
			handles[i] = 0;
		}
		UnregisterClass(className, Win32.GetModuleHandle(null));
	}
}
