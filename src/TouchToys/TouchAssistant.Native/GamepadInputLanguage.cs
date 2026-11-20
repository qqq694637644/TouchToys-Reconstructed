using System;
using System.Runtime.InteropServices;

namespace TouchAssistant.Native;

internal static class GamepadInputLanguage
{
	private struct GUITHREADINFO
	{
		public int cbSize;

		public int flags;

		public nint hwndActive;

		public nint hwndFocus;

		public nint hwndCapture;

		public nint hwndMenuOwner;

		public nint hwndMoveSize;

		public nint hwndCaret;

		public int left;

		public int top;

		public int right;

		public int bottom;
	}

	private const uint SMTO_ABORTIFHUNG = 2u;

	private const uint SMTO_BLOCK = 1u;

	private const uint IME_CMODE_NATIVE = 1u;

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern nint LoadKeyboardLayout(string name, uint flags);

	[DllImport("user32")]
	private static extern nint GetKeyboardLayout(uint thread);

	[DllImport("user32")]
	private static extern uint GetWindowThreadProcessId(nint window, out uint pid);

	[DllImport("user32")]
	private static extern bool GetGUIThreadInfo(uint thread, ref GUITHREADINFO info);

	[DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint SendMessageTimeout(nint window, uint message, nuint wParam, nint lParam, uint flags, uint timeout, out nuint result);

	[DllImport("imm32")]
	private static extern nint ImmGetContext(nint window);

	[DllImport("imm32")]
	private static extern bool ImmReleaseContext(nint window, nint context);

	[DllImport("imm32")]
	private static extern bool ImmGetConversionStatus(nint context, out uint conversion, out uint sentence);

	internal static void EnsureEnglish()
	{
		nint foregroundWindow = Win32.GetForegroundWindow();
		if (foregroundWindow == 0)
		{
			return;
		}
		uint pid;
		uint windowThreadProcessId = GetWindowThreadProcessId(foregroundWindow, out pid);
		if (windowThreadProcessId != 0 && pid != Environment.ProcessId && !IsEnglish(windowThreadProcessId, foregroundWindow))
		{
			nint num = LoadKeyboardLayout("00000409", 128u);
			if (num != 0)
			{
				SendMessageTimeout(foregroundWindow, 80u, 0u, num, 3u, 30u, out var _);
			}
		}
	}

	private static bool IsEnglish(uint thread, nint window)
	{
		if (((int)((long)GetKeyboardLayout(thread) & 0xFFFFL) & 0xFF) == 9)
		{
			return true;
		}
		nint window2 = window;
		GUITHREADINFO info = new GUITHREADINFO
		{
			cbSize = Marshal.SizeOf<GUITHREADINFO>()
		};
		if (GetGUIThreadInfo(thread, ref info) && info.hwndFocus != 0)
		{
			window2 = info.hwndFocus;
		}
		nint num = ImmGetContext(window2);
		if (num == 0)
		{
			return false;
		}
		try
		{
			uint conversion;
			uint sentence;
			return ImmGetConversionStatus(num, out conversion, out sentence) && (conversion & 1) == 0;
		}
		finally
		{
			ImmReleaseContext(window2, num);
		}
	}
}
