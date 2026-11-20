using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal static class TaskGroupProbe
{
	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool UnregisterClass(string name, nint instance);

	internal static async Task CheckAsync()
	{
		Win32.WndProc callback = Win32.DefWindowProc;
		nint instance = Win32.GetModuleHandle(null);
		nint first = 0;
		nint second = 0;
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = callback,
			hInstance = instance,
			lpszClassName = "TouchAssistant.GroupRestoreProbe"
		};
		Win32.RegisterClass(ref wc);
		try
		{
			Win32.GetCursorPos(out var p);
			Win32.RECT rcWork = Win32.Monitor(p).rcWork;
			first = Win32.CreateWindowEx(134217864u, "TouchAssistant.GroupRestoreProbe", "Group test A", 2147483648u, rcWork.Left + 40, rcWork.Top + 80, 220, 160, 0, 0, instance, 0);
			second = Win32.CreateWindowEx(134217864u, "TouchAssistant.GroupRestoreProbe", "Group test B", 2147483648u, rcWork.Left + 260, rcWork.Top + 80, 220, 160, 0, 0, instance, 0);
			if (first == 0 || second == 0)
			{
				throw new InvalidOperationException("Cannot create group test windows.");
			}
			Win32.ShowWindow(first, 4);
			Win32.ShowWindow(second, 4);
			HashSet<string> targets = WindowCatalog.TargetsForWindow(first);
			if (!WindowCatalog.MatchesTarget(Environment.ProcessPath, targets) || WindowCatalog.MatchesTarget("Z:\\missing-app.exe", targets))
			{
				throw new InvalidOperationException("Window EXE identity matching failed.");
			}
			Win32.GetWindowRect(first, out var a);
			Win32.GetWindowRect(second, out var b);
			RunningWindow task = new RunningWindow(first, "Group test", "", new List<WindowTile>
			{
				new WindowTile(new RunningWindow(first, "A", ""), a, a),
				new WindowTile(new RunningWindow(second, "B", ""), b, b)
			});
			Win32.ShowWindow(first, 6);
			Win32.SetWindowPos(second, 0, b.Left + 40, b.Top + 40, 120, 100, 20u);
			WindowCatalog.ActivateTask(task, maximize: false);
			await Task.Delay(100);
			AssertBounds(first, a);
			AssertBounds(second, b);
			Win32.DestroyWindow(first);
			first = 0;
			Win32.ShowWindow(second, 6);
			WindowCatalog.ActivateTask(task, maximize: false);
			await Task.Delay(100);
			AssertBounds(second, b);
		}
		finally
		{
			if (first != 0)
			{
				Win32.DestroyWindow(first);
			}
			if (second != 0)
			{
				Win32.DestroyWindow(second);
			}
			UnregisterClass("TouchAssistant.GroupRestoreProbe", instance);
			GC.KeepAlive(callback);
		}
		static void AssertBounds(nint window, Win32.RECT expected)
		{
			Win32.GetWindowRect(window, out var r);
			if (WindowCatalog.Minimized(window) || r.Left != expected.Left || r.Top != expected.Top || r.Width != expected.Width || r.Height != expected.Height)
			{
				throw new InvalidOperationException("Grouped task did not restore saved outer bounds.");
			}
		}
	}
}
