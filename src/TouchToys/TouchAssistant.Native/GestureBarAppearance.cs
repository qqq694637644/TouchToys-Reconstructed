using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace TouchAssistant.Native;

internal static class GestureBarAppearance
{
	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern nint FindWindowEx(nint parent, nint after, string cls, string? title);

	[DllImport("user32")]
	private static extern bool IsWindowVisible(nint h);

	[DllImport("gdi32")]
	private static extern uint GetPixel(nint dc, int x, int y);

	public static int Bottom(Win32.RECT screen)
	{
		int num = screen.Bottom;
		string[] array = new string[2] { "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };
		foreach (string cls in array)
		{
			nint num2 = 0;
			while ((num2 = FindWindowEx(0, num2, cls, null)) != 0)
			{
				if (IsWindowVisible(num2) && Win32.GetWindowRect(num2, out var r) && r.Right > screen.Left && r.Left < screen.Right && r.Bottom >= screen.Bottom - 2 && r.Top > screen.Top + screen.Height / 2 && r.Top < screen.Bottom - 4)
				{
					num = Math.Min(num, r.Top);
				}
			}
		}
		return num;
	}

	public static bool Light(int x, int y, int width, bool? previous = null)
	{
		nint dC = Win32.GetDC(0);
		try
		{
			double num = 0.0;
			int num2 = 0;
			for (int i = 1; i <= 5; i++)
			{
				uint pixel = GetPixel(dC, x + width * i / 6, y - 3);
				if (pixel != uint.MaxValue)
				{
					num += 0.2126 * (double)(pixel & 0xFF) + 0.7152 * (double)((pixel >> 8) & 0xFF) + 0.0722 * (double)((pixel >> 16) & 0xFF);
					num2++;
				}
			}
			return num2 == 0 || num / (double)num2 < (double)((previous == true) ? 155 : ((previous == false) ? 125 : 140));
		}
		finally
		{
			Win32.ReleaseDC(0, dC);
		}
	}

	public static void Draw(nint window, int x, int y, int width, int height, bool light)
	{
		using Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using GraphicsPath graphicsPath = new GraphicsPath();
			float num = height - 1;
			graphicsPath.AddArc(0f, 0f, num, num, 90f, 180f);
			graphicsPath.AddArc(width - height, 0f, num, num, 270f, 180f);
			graphicsPath.CloseFigure();
			using SolidBrush brush = new SolidBrush(light ? Color.FromArgb(235, 245, 245, 245) : Color.FromArgb(235, 25, 25, 25));
			graphics.FillPath(brush, graphicsPath);
		}
		nint dC = Win32.GetDC(0);
		nint num2 = Win32.CreateCompatibleDC(dC);
		nint hbitmap = bitmap.GetHbitmap(Color.FromArgb(0));
		nint o = Win32.SelectObject(num2, hbitmap);
		try
		{
			Win32.POINT dst = new Win32.POINT(x, y);
			Win32.SIZE size = new Win32.SIZE(width, height);
			Win32.POINT origin = default(Win32.POINT);
			Win32.BLENDFUNCTION blend = new Win32.BLENDFUNCTION
			{
				SourceConstantAlpha = byte.MaxValue,
				AlphaFormat = 1
			};
			Win32.UpdateLayeredWindow(window, dC, ref dst, ref size, num2, ref origin, 0u, ref blend, 2u);
		}
		finally
		{
			Win32.SelectObject(num2, o);
			Win32.DeleteObject(hbitmap);
			Win32.DeleteDC(num2);
			Win32.ReleaseDC(0, dC);
		}
	}
}
