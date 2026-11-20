using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace TouchAssistant.Native;

internal sealed class DwmThumbnail : IDisposable
{
	private struct Properties
	{
		public uint Flags;

		public Win32.RECT Destination;

		public Win32.RECT Source;

		public byte Opacity;

		[MarshalAs(UnmanagedType.Bool)]
		public bool Visible;

		[MarshalAs(UnmanagedType.Bool)]
		public bool ClientOnly;
	}

	private static readonly Win32.WndProc callback = (nint h, uint m, nuint w, nint l) => (m != 132) ? Win32.DefWindowProc(h, m, w, l) : (-1);

	private static bool registered;

	private nint window;

	private nint thumbnail;

	private string? last;

	internal string Geometry
	{
		get
		{
			Win32.GetWindowRect(window, out var r);
			return $"{r.Left},{r.Top},{r.Width},{r.Height}; DPI={Win32.GetDpiForWindow(window)}; Key={last}";
		}
	}

	internal Win32.RECT Bounds
	{
		get
		{
			Win32.GetWindowRect(window, out var r);
			return r;
		}
	}

	internal nint Handle => window;

	[DllImport("dwmapi")]
	private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);

	[DllImport("dwmapi")]
	private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref Properties properties);

	[DllImport("dwmapi")]
	private static extern int DwmUnregisterThumbnail(nint thumbnail);

	[DllImport("user32")]
	private static extern bool SetLayeredWindowAttributes(nint window, uint key, byte alpha, uint flags);

	[DllImport("user32")]
	private static extern int SetWindowRgn(nint window, nint region, bool redraw);

	[DllImport("gdi32")]
	private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

	[DllImport("gdi32")]
	private static extern nint CreateRectRgn(int left, int top, int right, int bottom);

	[DllImport("gdi32")]
	private static extern int CombineRgn(nint result, nint first, nint second, int mode);

	[DllImport("gdi32")]
	private static extern nint GetStockObject(int index);

	private DwmThumbnail()
	{
	}

	internal void PrepareOffscreen(int width, int height)
	{
		OverlayOrder.Remove(window);
		Properties properties = new Properties
		{
			Flags = 29u,
			Destination = new Win32.RECT
			{
				Right = width,
				Bottom = height
			},
			Opacity = byte.MaxValue,
			Visible = true,
			ClientOnly = false
		};
		Marshal.ThrowExceptionForHR(DwmUpdateThumbnailProperties(thumbnail, ref properties));
		Win32.SetWindowPos(window, 1, -30000, -30000, width, height, 80u);
	}

	[DllImport("dwmapi")]
	private static extern int DwmFlush();

	internal byte[] Snapshot()
	{
		DwmFlush();
		Win32.RECT bounds = Bounds;
		if (window == 0 || bounds.Width <= 0 || bounds.Height <= 0 || bounds.Width > 2048 || bounds.Height > 2048)
		{
			throw new InvalidOperationException("预览尺寸不可用。");
		}
		using Bitmap bitmap = new Bitmap(bounds.Width, bounds.Height);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, new Size(bounds.Width, bounds.Height));
		}
		double num = Math.Min(1.0, 480.0 / (double)Math.Max(bounds.Width, bounds.Height));
		using Bitmap bitmap2 = new Bitmap(bitmap, new Size(Math.Max(1, (int)((double)bounds.Width * num)), Math.Max(1, (int)((double)bounds.Height * num))));
		using MemoryStream memoryStream = new MemoryStream();
		bitmap2.Save(memoryStream, ImageFormat.Png);
		return memoryStream.ToArray();
	}

	public static DwmThumbnail? Create(nint source, nint owner = 0)
	{
		if (!registered)
		{
			Win32.WNDCLASS wc = new Win32.WNDCLASS
			{
				lpfnWndProc = callback,
				hInstance = Win32.GetModuleHandle(null),
				hbrBackground = GetStockObject(4),
				lpszClassName = "TouchAssistant.DwmPreview"
			};
			Win32.RegisterClass(ref wc);
			registered = true;
		}
		DwmThumbnail dwmThumbnail = new DwmThumbnail();
		dwmThumbnail.window = Win32.CreateWindowEx(134742184u, "TouchAssistant.DwmPreview", "", 2281701376u, 0, 0, 1, 1, owner, 0, Win32.GetModuleHandle(null), 0);
		if (dwmThumbnail.window == 0 || DwmRegisterThumbnail(dwmThumbnail.window, source, out dwmThumbnail.thumbnail) != 0)
		{
			dwmThumbnail.Dispose();
			return null;
		}
		SetLayeredWindowAttributes(dwmThumbnail.window, 0u, byte.MaxValue, 2u);
		OverlayOrder.Register(dwmThumbnail.window);
		return dwmThumbnail;
	}

	public void Update(int x, int y, int width, int fullHeight, int captionHeight, Win32.RECT viewport, double opacity, int radius)
	{
		if (window == 0)
		{
			return;
		}
		int num = fullHeight - captionHeight;
		if (width <= 0 || num <= 0 || x >= viewport.Right || y >= viewport.Bottom || x + width <= viewport.Left || y + num <= viewport.Top || opacity <= 0.001)
		{
			Hide();
			return;
		}
		byte b = (byte)(255.0 * Math.Clamp(opacity, 0.0, 1.0));
		string text = $"{x}:{y}:{width}:{num}:{viewport.Left}:{viewport.Top}:{viewport.Right}:{viewport.Bottom}:{b}:{radius}";
		if (!(last == text))
		{
			last = text;
			Properties properties = new Properties
			{
				Flags = 29u,
				Destination = new Win32.RECT
				{
					Right = width,
					Bottom = fullHeight
				},
				Opacity = byte.MaxValue,
				Visible = true,
				ClientOnly = false
			};
			DwmUpdateThumbnailProperties(thumbnail, ref properties);
			nint num2 = CreateRoundRectRgn(0, 0, width + 1, fullHeight + 1, radius * 2, radius * 2);
			nint num3 = CreateRectRgn(Math.Max(0, viewport.Left - x), Math.Max(0, viewport.Top - y), Math.Min(width, viewport.Right - x), Math.Min(num, viewport.Bottom - y));
			CombineRgn(num2, num2, num3, 1);
			Win32.DeleteObject(num3);
			if (SetWindowRgn(window, num2, redraw: false) == 0)
			{
				Win32.DeleteObject(num2);
			}
			SetLayeredWindowAttributes(window, 0u, b, 2u);
			Win32.SetWindowPos(window, -1, x, y, width, num, 80u);
		}
	}

	public void Hide()
	{
		last = null;
		if (window != 0)
		{
			Win32.ShowWindow(window, 0);
		}
	}

	public void Dispose()
	{
		if (thumbnail != 0)
		{
			DwmUnregisterThumbnail(thumbnail);
			thumbnail = 0;
		}
		if (window != 0)
		{
			OverlayOrder.Remove(window);
			Win32.DestroyWindow(window);
			window = 0;
		}
	}
}
