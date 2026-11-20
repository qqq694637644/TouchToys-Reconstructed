using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TouchAssistant.Native;

internal sealed class PanelShadow : IDisposable
{
	private const int BaseMargin = 14;

	private readonly nint handle;

	private readonly string cls = "TouchToys.PanelShadow." + Guid.NewGuid().ToString("N");

	private readonly Win32.WndProc callback;

	private Bitmap? bitmap;

	private nint hBitmap;

	private nint memDc;

	private nint screenDc;

	private int margin;

	private int width;

	private int height;

	private int x;

	private int y;

	private double cornerRadius = 16.0;

	private double opacity = 1.0;

	internal double Opacity => opacity;

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool UnregisterClass(string name, nint module);

	public PanelShadow()
	{
		callback = (nint h, uint m, nuint w, nint l) => (m != 132) ? Win32.DefWindowProc(h, m, w, l) : (-1);
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = callback,
			hInstance = Win32.GetModuleHandle(null),
			lpszClassName = cls
		};
		Win32.RegisterClass(ref wc);
		handle = Win32.CreateWindowEx(134742184u, cls, "", 2147483648u, 0, 0, 1, 1, 0, 0, wc.hInstance, 0);
		Win32.SuppressFeedback(handle);
		screenDc = Win32.GetDC(0);
	}

	public void Show(int panelX, int panelY, int panelWidth, int panelHeight, double scale, double radius = 16.0)
	{
		margin = (int)Math.Ceiling(14.0 * scale);
		width = panelWidth + margin * 2;
		height = panelHeight + margin * 2;
		x = panelX;
		y = panelY;
		cornerRadius = radius;
		opacity = 0.0;
		Build(panelWidth, panelHeight, scale);
		Win32.SetWindowPos(handle, -1, x - margin, y - margin, width, height, 80u);
		Win32.ShowWindow(handle, 4);
	}

	public void Move(int panelX, int panelY)
	{
		x = panelX;
		y = panelY;
		Win32.SetWindowPos(handle, 0, x - margin, y - margin, 0, 0, 21u);
	}

	public void SetOpacity(double value)
	{
		opacity = Math.Clamp(value, 0.0, 1.0);
		Present();
	}

	public void Hide()
	{
		Win32.ShowWindow(handle, 0);
	}

	private void Build(int panelWidth, int panelHeight, double scale)
	{
		ReleaseSurface();
		bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.Clear(Color.Transparent);
			int num = 6;
			float num2 = (float)(cornerRadius * scale);
			for (int num3 = num; num3 >= 1; num3--)
			{
				float num4 = (float)(margin * num3) / (float)num;
				RectangleF r = new RectangleF((float)margin - num4, (float)margin - num4, (float)panelWidth + num4 * 2f, (float)panelHeight + num4 * 2f);
				float radius = num2 + num4;
				using GraphicsPath path = Rounded(r, radius);
				using SolidBrush brush = new SolidBrush(Color.FromArgb(2, 0, 0, 0));
				graphics.FillPath(brush, path);
			}
			using GraphicsPath path2 = Rounded(new RectangleF(margin, margin, panelWidth, panelHeight), num2);
			graphics.CompositingMode = CompositingMode.SourceCopy;
			using SolidBrush brush2 = new SolidBrush(Color.Transparent);
			graphics.FillPath(brush2, path2);
		}
		hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
		memDc = Win32.CreateCompatibleDC(screenDc);
		Win32.SelectObject(memDc, hBitmap);
		Present();
	}

	private static GraphicsPath Rounded(RectangleF r, float radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(r.Left, r.Top, radius * 2f, radius * 2f, 180f, 90f);
		graphicsPath.AddArc(r.Right - radius * 2f, r.Top, radius * 2f, radius * 2f, 270f, 90f);
		graphicsPath.AddArc(r.Right - radius * 2f, r.Bottom - radius * 2f, radius * 2f, radius * 2f, 0f, 90f);
		graphicsPath.AddArc(r.Left, r.Bottom - radius * 2f, radius * 2f, radius * 2f, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	private void Present()
	{
		if (memDc != 0)
		{
			Win32.SIZE size = new Win32.SIZE(width, height);
			Win32.POINT dst = new Win32.POINT(x - margin, y - margin);
			Win32.POINT origin = default(Win32.POINT);
			Win32.BLENDFUNCTION blend = new Win32.BLENDFUNCTION
			{
				SourceConstantAlpha = (byte)Math.Round(opacity * 255.0),
				AlphaFormat = 1
			};
			Win32.UpdateLayeredWindow(handle, screenDc, ref dst, ref size, memDc, ref origin, 0u, ref blend, 2u);
		}
	}

	private void ReleaseSurface()
	{
		if (memDc != 0)
		{
			Win32.DeleteDC(memDc);
			memDc = 0;
		}
		if (hBitmap != 0)
		{
			Win32.DeleteObject(hBitmap);
			hBitmap = 0;
		}
		bitmap?.Dispose();
		bitmap = null;
	}

	public void Dispose()
	{
		ReleaseSurface();
		if (screenDc != 0)
		{
			Win32.ReleaseDC(0, screenDc);
			screenDc = 0;
		}
		Win32.DestroyWindow(handle);
		UnregisterClass(cls, Win32.GetModuleHandle(null));
	}
}
