using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace TouchAssistant.Native;

internal static class SystemPanelFrame
{
	private delegate nint Subclass(nint h, uint msg, nuint wp, nint lp, nuint id, nuint data);

	private delegate bool EnumCallback(nint h, nint p);

	private struct WindowPosition
	{
		public nint Window;

		public nint After;

		public int X;

		public int Y;

		public int Width;

		public int Height;

		public uint Flags;
	}

	private static readonly Subclass callback = Procedure;

	private static readonly Subclass contentCallback = ContentProcedure;

	[DllImport("comctl32")]
	private static extern bool SetWindowSubclass(nint h, Subclass proc, nuint id, nuint data);

	[DllImport("comctl32")]
	private static extern bool RemoveWindowSubclass(nint h, Subclass proc, nuint id);

	[DllImport("comctl32")]
	private static extern nint DefSubclassProc(nint h, uint msg, nuint wp, nint lp);

	[DllImport("dwmapi")]
	private static extern int DwmSetWindowAttribute(nint h, uint attribute, ref uint value, uint size);

	private static nint GetStyle(nint h, int index)
	{
		return WindowLong.Get(h, index);
	}

	private static nint SetStyle(nint h, int index, nint value)
	{
		return WindowLong.Set(h, index, value);
	}

	[DllImport("user32")]
	private static extern bool EnumChildWindows(nint h, EnumCallback callback, nint p);

	[DllImport("user32")]
	private static extern nint GetParent(nint h);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint h, StringBuilder name, int count);

	[DllImport("user32")]
	private static extern bool GetClientRect(nint h, out Win32.RECT r);

	internal static string Children(nint h)
	{
		StringBuilder text = new StringBuilder();
		Win32.GetWindowRect(h, out var r);
		EnumChildWindows(h, delegate(nint child, nint _)
		{
			StringBuilder stringBuilder = new StringBuilder(256);
			GetClassName(child, stringBuilder, 256);
			Win32.GetWindowRect(child, out var r2);
			StringBuilder stringBuilder2 = text;
			StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(14, 6, stringBuilder2);
			handler.AppendFormatted(stringBuilder);
			handler.AppendLiteral(": ");
			handler.AppendFormatted(r2.Left - r.Left);
			handler.AppendLiteral(",");
			handler.AppendFormatted(r2.Top - r.Top);
			handler.AppendLiteral(",");
			handler.AppendFormatted(r2.Width);
			handler.AppendLiteral(",");
			handler.AppendFormatted(r2.Height);
			handler.AppendLiteral("; parent=");
			handler.AppendFormatted(GetParent(child) == h);
			stringBuilder2.AppendLine(ref handler);
			return true;
		}, 0);
		return text.ToString();
	}

	public static void Attach(Window window)
	{
		nint handle = WindowNative.GetWindowHandle(window);
		SetStyle(handle, -16, (nint)(((IntPtr)GetStyle(handle, -16)).ToInt64() & -12845057));
		SetWindowSubclass(handle, callback, 404u, 0u);
		window.Closed += delegate
		{
			RemoveWindowSubclass(handle, callback, 404u);
		};
		Win32.SetWindowPos(handle, 0, 0, 0, 0, 0, 55u);
		FitContent(handle);
		window.DispatcherQueue.TryEnqueue(delegate
		{
			FitContent(handle);
		});
	}

	private static void FitContent(nint h)
	{
		if (!GetClientRect(h, out var client))
		{
			return;
		}
		EnumChildWindows(h, delegate(nint child, nint _)
		{
			if (GetParent(child) != h)
			{
				return true;
			}
			StringBuilder stringBuilder = new StringBuilder(128);
			GetClassName(child, stringBuilder, 128);
			if (stringBuilder.ToString() != "Microsoft.UI.Content.DesktopChildSiteBridge")
			{
				return true;
			}
			SetWindowSubclass(child, contentCallback, 405u, 0u);
			Win32.SetWindowPos(child, 0, 0, 0, client.Width, client.Height, 20u);
			return false;
		}, 0);
	}

	private static nint ContentProcedure(nint h, uint msg, nuint wp, nint lp, nuint id, nuint data)
	{
		if (msg == 70 && lp != 0 && GetClientRect(GetParent(h), out var r))
		{
			WindowPosition structure = Marshal.PtrToStructure<WindowPosition>(lp);
			if ((structure.Flags & 2) == 0)
			{
				structure.X = 0;
				structure.Y = 0;
			}
			if ((structure.Flags & 1) == 0)
			{
				structure.Width = r.Width;
				structure.Height = r.Height;
			}
			Marshal.StructureToPtr(structure, lp, fDeleteOld: false);
		}
		return DefSubclassProc(h, msg, wp, lp);
	}

	internal static string Diagnostics(nint h)
	{
		return $"Style=0x{((IntPtr)GetStyle(h, -16)).ToInt64():X}; Ex=0x{((IntPtr)GetStyle(h, -20)).ToInt64():X}";
	}

	private static nint Procedure(nint h, uint msg, nuint wp, nint lp, nuint id, nuint data)
	{
		if ((msg == 131 || msg == 133) ? true : false)
		{
			return 0;
		}
		nint result = DefSubclassProc(h, msg, wp, lp);
		if (msg == 5)
		{
			FitContent(h);
		}
		if ((msg == 134 || msg == 794) ? true : false)
		{
			uint value = 4294967294u;
			DwmSetWindowAttribute(h, 34u, ref value, 4u);
		}
		if (msg == 132)
		{
			long num = ((IntPtr)result).ToInt64();
			if (num >= 10 && num <= 17)
			{
				return 1;
			}
		}
		return result;
	}
}
