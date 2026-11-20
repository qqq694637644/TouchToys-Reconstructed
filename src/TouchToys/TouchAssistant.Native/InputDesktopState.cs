using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TouchAssistant.Native;

internal static class InputDesktopState
{
	[ThreadStatic]
	private static StringBuilder? desktopName;

	[ThreadStatic]
	private static nint lastForeground;

	[ThreadStatic]
	private static bool consentForeground;

	[DllImport("user32")]
	private static extern nint OpenInputDesktop(uint flags, bool inherit, uint access);

	[DllImport("user32")]
	private static extern bool CloseDesktop(nint desktop);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool GetUserObjectInformation(nint h, int index, StringBuilder value, int bytes, out int needed);

	internal static bool CanInteract()
	{
		nint num = OpenInputDesktop(0u, inherit: false, 1u);
		if (num == 0)
		{
			return false;
		}
		try
		{
			StringBuilder stringBuilder = desktopName ?? (desktopName = new StringBuilder(256));
			stringBuilder.Clear();
			if (!GetUserObjectInformation(num, 2, stringBuilder, 512, out var _) || !string.Equals(stringBuilder.ToString(), "Default", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}
		}
		finally
		{
			CloseDesktop(num);
		}
		nint foregroundWindow = Win32.GetForegroundWindow();
		if (foregroundWindow != lastForeground)
		{
			lastForeground = foregroundWindow;
			consentForeground = false;
			if (foregroundWindow != 0)
			{
				Win32.GetWindowThreadProcessId(foregroundWindow, out var process);
				try
				{
					using Process process2 = Process.GetProcessById((int)process);
					consentForeground = string.Equals(process2.ProcessName, "consent", StringComparison.OrdinalIgnoreCase);
				}
				catch (ArgumentException)
				{
				}
				catch (InvalidOperationException)
				{
				}
				catch (Win32Exception)
				{
				}
			}
		}
		return !consentForeground;
	}
}
