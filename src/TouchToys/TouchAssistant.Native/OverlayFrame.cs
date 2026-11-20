using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace TouchAssistant.Native;

internal static class OverlayFrame
{
	private delegate nint Subclass(nint hwnd, uint message, nuint wp, nint lp, nuint id, nuint data);

	private static readonly Subclass callback = Procedure;

	[DllImport("comctl32")]
	private static extern bool SetWindowSubclass(nint hwnd, Subclass callback, nuint id, nuint data);

	[DllImport("comctl32")]
	private static extern bool RemoveWindowSubclass(nint hwnd, Subclass callback, nuint id);

	[DllImport("comctl32")]
	private static extern nint DefSubclassProc(nint hwnd, uint message, nuint wp, nint lp);

	[DllImport("dwmapi")]
	private static extern int DwmSetWindowAttribute(nint h, uint attribute, ref uint value, uint size);

	[DllImport("gdi32")]
	private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);

	[DllImport("user32")]
	private static extern int SetWindowRgn(nint h, nint region, bool redraw);

	public static void Attach(Window window)
	{
		nint handle = WindowNative.GetWindowHandle(window);
		SetWindowSubclass(handle, callback, 403u, 0u);
		window.Closed += delegate
		{
			RemoveWindowSubclass(handle, callback, 403u);
		};
	}

	private static nint Procedure(nint hwnd, uint message, nuint wp, nint lp, nuint id, nuint data)
	{
		if (message == 131 || message == 133)
		{
			return 0;
		}
		nint result = DefSubclassProc(hwnd, message, wp, lp);
		if (message == 5 && Win32.GetWindowRect(hwnd, out var r) && r.Width > 0 && r.Height > 0)
		{
			int num = (int)((double)(16 * Win32.GetDpiForWindow(hwnd)) / 96.0);
			nint num2 = CreateRoundRectRgn(0, 1, r.Width + 1, r.Height + 1, num, num);
			if (SetWindowRgn(hwnd, num2, redraw: true) == 0)
			{
				Win32.DeleteObject(num2);
			}
		}
		if ((message == 134 || message == 794) ? true : false)
		{
			uint value = 4294967294u;
			DwmSetWindowAttribute(hwnd, 34u, ref value, 4u);
		}
		if (message == 132)
		{
			long num3 = ((IntPtr)result).ToInt64();
			if (num3 >= 10 && num3 <= 17)
			{
				return 1;
			}
		}
		return result;
	}
}
