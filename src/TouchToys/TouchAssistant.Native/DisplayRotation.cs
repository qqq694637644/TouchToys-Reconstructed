using System;
using System.Runtime.InteropServices;

namespace TouchAssistant.Native;

internal static class DisplayRotation
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DeviceMode
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string device;

		public ushort spec;

		public ushort driver;

		public ushort size;

		public ushort extra;

		public uint fields;

		public int x;

		public int y;

		public uint orientation;

		public uint fixedOutput;

		public short color;

		public short duplex;

		public short resolution;

		public short tt;

		public short collate;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string form;

		public ushort logPixels;

		public uint bits;

		public uint width;

		public uint height;

		public uint flags;

		public uint frequency;

		public uint icmMethod;

		public uint icmIntent;

		public uint media;

		public uint dither;

		public uint reserved1;

		public uint reserved2;

		public uint panWidth;

		public uint panHeight;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct MonitorInfoEx
	{
		public int size;

		public Win32.RECT monitor;

		public Win32.RECT work;

		public uint flags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string device;
	}

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool EnumDisplaySettings(string device, int mode, ref DeviceMode value);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern int ChangeDisplaySettingsEx(string device, ref DeviceMode value, nint hwnd, uint flags, nint param);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfoEx info);

	public static void Rotate(nint hwnd, Action<string> notice)
	{
		Win32.GetWindowRect(hwnd, out var r);
		nint monitor = Win32.MonitorFromPoint(new Win32.POINT(r.Left + r.Width / 2, r.Top + r.Height / 2), 2u);
		MonitorInfoEx info = new MonitorInfoEx
		{
			size = Marshal.SizeOf<MonitorInfoEx>(),
			device = ""
		};
		if (!GetMonitorInfo(monitor, ref info))
		{
			return;
		}
		DeviceMode value = new DeviceMode
		{
			size = (ushort)Marshal.SizeOf<DeviceMode>(),
			device = "",
			form = ""
		};
		if (EnumDisplaySettings(info.device, -1, ref value))
		{
			ref uint width = ref value.width;
			ref uint height = ref value.height;
			uint height2 = value.height;
			uint width2 = value.width;
			width = height2;
			height = width2;
			value.orientation = (value.orientation + 1) % 4;
			value.fields = 1572992u;
			if (ChangeDisplaySettingsEx(info.device, ref value, 0, 2u, 0) != 0 || ChangeDisplaySettingsEx(info.device, ref value, 0, 0u, 0) != 0)
			{
				notice("显示器不支持此旋转方向。");
			}
		}
	}
}
