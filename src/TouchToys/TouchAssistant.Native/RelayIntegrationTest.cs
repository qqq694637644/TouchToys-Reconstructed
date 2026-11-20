using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using TouchAssistant.Models;

namespace TouchAssistant.Native;

internal static class RelayIntegrationTest
{
	internal static async Task<string> Run(SettingsStore store, GlobalTouchGestures relay)
	{
		int downs = 0;
		int ups = 0;
		int updates = 0;
		ConcurrentQueue<string> trace = new ConcurrentQueue<string>();
		relay.RelayDiagnostic = trace.Enqueue;
		Win32.WndProc callback = delegate(nint h, uint m, nuint w, nint l)
		{
			switch (m)
			{
			case 582u:
				downs++;
				break;
			case 583u:
				ups++;
				break;
			case 581u:
				updates++;
				break;
			}
			if (m - 581 <= 2)
			{
				return 0;
			}
			return (m == 33) ? 3 : Win32.DefWindowProc(h, m, w, l);
		};
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = callback,
			hInstance = Win32.GetModuleHandle(null),
			hCursor = Win32.ArrowCursor,
			lpszClassName = "TouchAssistant.RelayTest",
			hbrBackground = 6
		};
		Win32.RegisterClass(ref wc);
		Win32.RECT rcWork = Win32.Monitor(default(Win32.POINT)).rcWork;
		int x = rcWork.Left + rcWork.Width / 2 - 220;
		int y = rcWork.Top + rcWork.Height / 2 - 140;
		nint window = Win32.CreateWindowEx(134217864u, wc.lpszClassName, "Touch relay test", 2147483648u, x, y, 500, 360, 0, 0, wc.hInstance, 0);
		Process child = null;
		string result;
		try
		{
			Win32.ShowWindow(window, 4);
			store.Value.MultiTouch.Enabled = true;
			store.Save();
			for (int i = 0; i < 30; i++)
			{
				if (relay.IsActive)
				{
					break;
				}
				if (!store.Value.MultiTouch.Enabled)
				{
					break;
				}
				await Task.Delay(100);
			}
			if (!relay.IsActive)
			{
				throw new InvalidOperationException(relay.Status);
			}
			ProcessStartInfo processStartInfo = new ProcessStartInfo(Environment.ProcessPath)
			{
				UseShellExecute = false,
				CreateNoWindow = true
			};
			processStartInfo.ArgumentList.Add("--inject-relay-fixture");
			processStartInfo.ArgumentList.Add((x + 100).ToString());
			processStartInfo.ArgumentList.Add((y + 100).ToString());
			child = Process.Start(processStartInfo) ?? throw new InvalidOperationException("无法启动触摸注入子进程。");
			await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20.0));
			await Task.Delay(300);
			if (child.ExitCode != 0 || !relay.IsActive || downs != 9 || ups != 9 || updates < 20)
			{
				throw new InvalidOperationException($"Relay integration: child={child.ExitCode}, down={downs}, up={ups}, update={updates}; {relay.Status}");
			}
			result = $"PASS: external-process 2/3/4-finger slide/hold; down={downs}, up={ups}, updates={updates}; forwarding remained active.";
		}
		finally
		{
			relay.RelayDiagnostic = null;
			File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "relay-trace.txt"), trace);
			if (child != null)
			{
				if (!child.HasExited)
				{
					child.Kill();
				}
				child.Dispose();
			}
			store.Value.MultiTouch.Enabled = false;
			store.Save();
			await Task.Delay(100);
			Win32.DestroyWindow(window);
			GC.KeepAlive(callback);
		}
		return result;
	}
}
