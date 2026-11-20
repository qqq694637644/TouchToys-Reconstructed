using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;

namespace TouchAssistant.Native;

internal sealed class TouchpadCursor : IDisposable
{
	private struct CursorInfo
	{
		public int Size;

		public uint Flags;

		public nint Cursor;

		public Win32.POINT Position;
	}

	private readonly nint handle;

	private readonly string cls = "TouchToys.PadCursor." + Guid.NewGuid().ToString("N");

	private readonly Win32.WndProc callback;

	private readonly DispatcherQueueTimer timer;

	private readonly DispatcherQueue queue = DispatcherQueue.GetForCurrentThread();

	private bool visible;

	private bool disposed;

	private Win32.POINT last;

	private int generation;

	[DllImport("user32")]
	private static extern bool GetCursorInfo(ref CursorInfo info);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool UnregisterClass(string name, nint module);

	public TouchpadCursor()
	{
		callback = (nint h, uint m, nuint w, nint l) => (m != 132) ? Win32.DefWindowProc(h, m, w, l) : (-1);
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = callback,
			hInstance = Win32.GetModuleHandle(null),
			lpszClassName = cls
		};
		Win32.RegisterClass(ref wc);
		handle = Win32.CreateWindowEx(134742184u, cls, "TT cursor", 2147483648u, 0, 0, 32, 40, 0, 0, wc.hInstance, 0);
		timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
		timer.Interval = TimeSpan.FromMilliseconds(16.0);
		timer.Tick += delegate
		{
			Refresh();
		};
	}

	public void SetActive(bool active)
	{
		if (!disposed)
		{
			if (active)
			{
				generation++;
				Refresh();
				timer.Start();
			}
			else
			{
				generation++;
				timer.Stop();
				Win32.ShowWindow(handle, 0);
				visible = false;
			}
		}
	}

	public void Linger(int milliseconds)
	{
		SetActive(active: true);
		int token = ++generation;
		Task.Delay(milliseconds).ContinueWith((Task _) => queue.TryEnqueue(delegate
		{
			if (!disposed && token == generation)
			{
				SetActive(active: false);
			}
		}), TaskScheduler.Default);
	}

	private void Refresh()
	{
		try
		{
			RefreshCore();
		}
		catch
		{
			Win32.ShowWindow(handle, 0);
			visible = false;
		}
	}

	private void RefreshCore()
	{
		CursorInfo info = new CursorInfo
		{
			Size = Marshal.SizeOf<CursorInfo>()
		};
		if (!GetCursorInfo(ref info) || (info.Flags & 1) != 0)
		{
			if (visible)
			{
				Win32.ShowWindow(handle, 0);
			}
			visible = false;
		}
		else if (!visible || info.Position.X != last.X || info.Position.Y != last.Y)
		{
			last = info.Position;
			Draw(last);
			Win32.ShowWindow(handle, 4);
			visible = true;
		}
	}

	private void Draw(Win32.POINT p)
	{
		using Icon icon = Icon.FromHandle(Win32.ArrowCursor);
		using Bitmap bitmap = icon.ToBitmap();
		using Bitmap bitmap2 = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppPArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap2))
		{
			graphics.DrawImageUnscaled(bitmap, 0, 0);
		}
		nint dC = Win32.GetDC(0);
		nint num = Win32.CreateCompatibleDC(dC);
		nint hbitmap = bitmap2.GetHbitmap(Color.FromArgb(0));
		nint o = Win32.SelectObject(num, hbitmap);
		try
		{
			Win32.SIZE size = new Win32.SIZE(bitmap.Width, bitmap.Height);
			Win32.POINT origin = default(Win32.POINT);
			Win32.BLENDFUNCTION blend = new Win32.BLENDFUNCTION
			{
				SourceConstantAlpha = byte.MaxValue,
				AlphaFormat = 1
			};
			Win32.UpdateLayeredWindow(handle, dC, ref p, ref size, num, ref origin, 0u, ref blend, 2u);
		}
		finally
		{
			Win32.SelectObject(num, o);
			Win32.DeleteObject(hbitmap);
			Win32.DeleteDC(num);
			Win32.ReleaseDC(0, dC);
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			timer.Stop();
			Win32.DestroyWindow(handle);
			UnregisterClass(cls, Win32.GetModuleHandle(null));
		}
	}
}
