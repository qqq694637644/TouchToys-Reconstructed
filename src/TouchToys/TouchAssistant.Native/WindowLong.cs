using System;
using System.Runtime.InteropServices;

namespace TouchAssistant.Native;

internal static class WindowLong
{
	internal static nint Get(nint window, int index)
	{
		if (IntPtr.Size != 8)
		{
			return Get32(window, index);
		}
		return Get64(window, index);
	}

	internal static nint Set(nint window, int index, nint value)
	{
		if (IntPtr.Size != 8)
		{
			return Set32(window, index, (int)value);
		}
		return Set64(window, index, value);
	}

	internal static nint Class(nint window, int index)
	{
		if (IntPtr.Size != 8)
		{
			return (nint)Class32(window, index);
		}
		return Class64(window, index);
	}

	[DllImport("user32", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
	private static extern nint Get64(nint window, int index);

	[DllImport("user32", EntryPoint = "GetWindowLongW", SetLastError = true)]
	private static extern int Get32(nint window, int index);

	[DllImport("user32", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
	private static extern nint Set64(nint window, int index, nint value);

	[DllImport("user32", EntryPoint = "SetWindowLongW", SetLastError = true)]
	private static extern int Set32(nint window, int index, int value);

	[DllImport("user32", EntryPoint = "GetClassLongPtrW", SetLastError = true)]
	private static extern nint Class64(nint window, int index);

	[DllImport("user32", EntryPoint = "GetClassLongW", SetLastError = true)]
	private static extern uint Class32(nint window, int index);
}
