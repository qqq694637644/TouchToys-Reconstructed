using System;
using System.Drawing;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace TouchAssistant.Native;

internal sealed class WheelBackdrop : IDisposable
{
	private struct Paint
	{
		public nint Dc;

		public int Erase;

		public Win32.RECT Rect;

		public int Restore;

		public int Update;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
		public byte[] Reserved;
	}

	private readonly string windowClass = "TouchToys.WheelBackdrop." + Guid.NewGuid().ToString("N");

	private bool disposed;

	private readonly nint handle;

	private readonly Win32.WndProc callback;

	private readonly DispatcherQueueTimer clock;

	private Win32.RECT bounds;

	private double scale;

	private bool showClock;

	private bool visible;

	private string time = "";

	internal bool IsPresented
	{
		get
		{
			if (visible)
			{
				return Win32.IsWindowVisible(handle);
			}
			return false;
		}
	}

	[DllImport("user32")]
	private static extern nint BeginPaint(nint h, out Paint p);

	[DllImport("user32")]
	private static extern bool EndPaint(nint h, ref Paint p);

	[DllImport("user32")]
	private static extern bool InvalidateRect(nint h, nint rect, bool erase);

	[DllImport("user32")]
	private static extern bool SetLayeredWindowAttributes(nint h, uint color, byte alpha, uint flags);

	[DllImport("gdi32")]
	private static extern nint GetStockObject(int index);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool UnregisterClass(string name, nint module);

	public WheelBackdrop()
	{
		callback = Procedure;
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = callback,
			hInstance = Win32.GetModuleHandle(null),
			lpszClassName = windowClass,
			hbrBackground = GetStockObject(4)
		};
		Win32.RegisterClass(ref wc);
		handle = Win32.CreateWindowEx(134742184u, wc.lpszClassName, "", 2147483648u, 0, 0, 1, 1, 0, 0, wc.hInstance, 0);
		clock = DispatcherQueue.GetForCurrentThread().CreateTimer();
		clock.Interval = TimeSpan.FromSeconds(1.0);
		clock.Tick += delegate
		{
			string text = DateTime.Now.ToString("HH:mm");
			if (text != time)
			{
				time = text;
				InvalidateRect(handle, 0, erase: false);
			}
		};
	}

	public void Update(nint panel, Win32.POINT origin, double progress, bool clockEnabled)
	{
		if (progress <= 0.0)
		{
			if (visible)
			{
				Win32.ShowWindow(handle, 0);
			}
			visible = false;
			clock.Stop();
			return;
		}
		Win32.RECT rcMonitor = Win32.Monitor(origin).rcMonitor;
		bool num = !visible || showClock != clockEnabled || rcMonitor.Left != bounds.Left || rcMonitor.Top != bounds.Top || rcMonitor.Width != bounds.Width || rcMonitor.Height != bounds.Height;
		bounds = rcMonitor;
		scale = Win32.Scale(origin);
		showClock = clockEnabled;
		visible = true;
		SetLayeredWindowAttributes(handle, 0u, (byte)(Math.Clamp(progress, 0.0, 1.0) * 192.0), 2u);
		if (num)
		{
			Win32.SetWindowPos(handle, panel, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 80u);
		}
		if (num)
		{
			InvalidateRect(handle, 0, erase: true);
		}
		if (showClock)
		{
			clock.Start();
		}
		else
		{
			clock.Stop();
		}
	}

	private nint Procedure(nint h, uint msg, nuint w, nint l)
	{
		switch (msg)
		{
		case 132u:
			return -1;
		case 33u:
			return 3;
		case 15u:
		{
			Paint p;
			nint hdc = BeginPaint(h, out p);
			try
			{
				using Graphics graphics = Graphics.FromHdc(hdc);
				graphics.Clear(Color.Black);
				if (showClock)
				{
					graphics.ScaleTransform((float)scale, (float)scale);
					float width = (float)((double)bounds.Width / scale);
					float num = (float)((double)bounds.Height / scale * 0.1);
					using Font font = new Font("Segoe UI", 54f, GraphicsUnit.Pixel);
					using Font font2 = new Font("Microsoft YaHei UI", 18f, GraphicsUnit.Pixel);
					using StringFormat format = new StringFormat
					{
						Alignment = StringAlignment.Center
					};
					graphics.DrawString(DateTime.Now.ToString("HH:mm"), font, Brushes.White, new RectangleF(0f, num, width, 78f), format);
					graphics.DrawString(DateTime.Now.ToString("yyyy年 M月 d日 dddd"), font2, Brushes.White, new RectangleF(0f, num + 82f, width, 36f), format);
				}
			}
			finally
			{
				EndPaint(h, ref p);
			}
			return 0;
		}
		default:
			return Win32.DefWindowProc(h, msg, w, l);
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			clock.Stop();
			Win32.DestroyWindow(handle);
			UnregisterClass(windowClass, Win32.GetModuleHandle(null));
		}
	}
}
