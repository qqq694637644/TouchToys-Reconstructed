using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using TouchAssistant.Core;
using TouchAssistant.Native;

namespace TouchAssistant.Services;

internal static class WindowCatalog
{
	private delegate bool EnumProc(nint hwnd, nint data);

	private struct Placement
	{
		public uint Length;

		public uint Flags;

		public uint Show;

		public Win32.POINT Min;

		public Win32.POINT Max;

		public Win32.RECT Normal;
	}

	internal static nint SettingsHandle { get; set; }

	[DllImport("user32")]
	private static extern bool EnumWindows(EnumProc callback, nint data);

	[DllImport("user32")]
	private static extern bool IsWindowVisible(nint hwnd);

	[DllImport("user32")]
	public static extern bool IsWindow(nint hwnd);

	[DllImport("user32")]
	private static extern nint GetWindow(nint hwnd, uint command);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);

	private static nint GetWindowLong(nint hwnd, int index)
	{
		return WindowLong.Get(hwnd, index);
	}

	[DllImport("dwmapi")]
	private static extern int DwmGetWindowAttribute(nint hwnd, uint attribute, out int value, int size);

	[DllImport("user32")]
	private static extern bool IsIconic(nint hwnd);

	[DllImport("kernel32")]
	private static extern nint OpenProcess(uint access, bool inherit, uint pid);

	[DllImport("kernel32")]
	private static extern bool CloseHandle(nint handle);

	[DllImport("kernel32", CharSet = CharSet.Unicode)]
	private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref uint length);

	[DllImport("kernel32", CharSet = CharSet.Unicode)]
	private static extern int GetApplicationUserModelId(nint process, ref uint length, StringBuilder? id);

	[DllImport("user32")]
	private static extern bool EnumChildWindows(nint parent, EnumProc callback, nint data);

	[DllImport("user32", SetLastError = true)]
	private static extern bool PostMessage(nint hwnd, uint message, nuint w, nint l);

	public static bool Minimized(nint hwnd)
	{
		return IsIconic(hwnd);
	}

	[DllImport("user32")]
	private static extern bool GetWindowPlacement(nint hwnd, ref Placement placement);

	public static double Aspect(nint hwnd)
	{
		if (IsIconic(hwnd))
		{
			Placement placement = new Placement
			{
				Length = (uint)Marshal.SizeOf<Placement>()
			};
			if (GetWindowPlacement(hwnd, ref placement) && placement.Normal.Height > 0)
			{
				return (double)placement.Normal.Width / (double)placement.Normal.Height;
			}
		}
		if (!Win32.GetWindowRect(hwnd, out var r) || r.Height <= 0)
		{
			return 1.7777777777777777;
		}
		return (double)r.Width / (double)r.Height;
	}

	public static string TargetForWindow(nint hwnd)
	{
		Win32.GetWindowThreadProcessId(hwnd, out var pid);
		if (pid == Environment.ProcessId)
		{
			return "";
		}
		string target = ProcessTarget(pid);
		if (target.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
		{
			EnumChildWindows(hwnd, delegate(nint child, nint _)
			{
				Win32.GetWindowThreadProcessId(child, out var process);
				if (process == pid)
				{
					return true;
				}
				string text = ProcessTarget(process);
				if (!text.Contains('!'))
				{
					return true;
				}
				target = text;
				return false;
			}, 0);
		}
		return target;
	}

	internal static string NormalizeTarget(string target)
	{
		target = target.Trim().Trim('"');
		if (target.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
		{
			string text = target;
			int length = "shell:AppsFolder\\".Length;
			target = text.Substring(length, text.Length - length);
		}
		return target.TrimEnd('\\');
	}

	public static bool MatchesTarget(string configured, nint window)
	{
		return MatchesTarget(configured, TargetsForWindow(window));
	}

	internal static bool MatchesTarget(string configured, HashSet<string> targets)
	{
		configured = NormalizeTarget(InstalledApps.ResolveMatchTarget(configured));
		if (configured.Length > 0)
		{
			return targets.Contains(configured);
		}
		return false;
	}

	internal static HashSet<string> TargetsForWindow(nint window)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (window == 0 || !IsWindow(window))
		{
			return hashSet;
		}
		string text = NormalizeTarget(TargetForWindow(window));
		if (text.Length > 0)
		{
			hashSet.Add(text);
		}
		Win32.GetWindowThreadProcessId(window, out var process);
		nint num = OpenProcess(4096u, inherit: false, process);
		if (num == 0)
		{
			return hashSet;
		}
		try
		{
			StringBuilder stringBuilder = new StringBuilder(32768);
			uint length = (uint)stringBuilder.Capacity;
			if (QueryFullProcessImageName(num, 0u, stringBuilder, ref length))
			{
				hashSet.Add(NormalizeTarget(stringBuilder.ToString()));
			}
		}
		finally
		{
			CloseHandle(num);
		}
		return hashSet;
	}

	public static bool RequestClose(nint hwnd)
	{
		if (IsWindow(hwnd))
		{
			return PostMessage(hwnd, 16u, 0u, 0);
		}
		return false;
	}

	private static string ProcessTarget(uint pid)
	{
		nint num = OpenProcess(4096u, inherit: false, pid);
		if (num == 0)
		{
			return "";
		}
		try
		{
			uint length = 0u;
			if (GetApplicationUserModelId(num, ref length, null) == 122 && length > 1 && length < 4096)
			{
				StringBuilder stringBuilder = new StringBuilder((int)length);
				if (GetApplicationUserModelId(num, ref length, stringBuilder) == 0)
				{
					return stringBuilder.ToString();
				}
			}
			StringBuilder stringBuilder2 = new StringBuilder(32768);
			length = (uint)stringBuilder2.Capacity;
			return QueryFullProcessImageName(num, 0u, stringBuilder2, ref length) ? stringBuilder2.ToString() : "";
		}
		finally
		{
			CloseHandle(num);
		}
	}

	public static List<RunningWindow> Read()
	{
		List<RunningWindow> list = new List<RunningWindow>();
		EnumWindows(delegate(nint h, nint _)
		{
			Win32.GetWindowThreadProcessId(h, out var pid);
			if ((pid == Environment.ProcessId && h != SettingsHandle) || !IsWindowVisible(h) || GetWindow(h, 4u) != 0 || ((ulong)GetWindowLong(h, -20) & 0x80uL) != 0L)
			{
				return true;
			}
			if (DwmGetWindowAttribute(h, 14u, out var value, 4) == 0 && value != 0)
			{
				return true;
			}
			StringBuilder stringBuilder = new StringBuilder(512);
			GetWindowText(h, stringBuilder, stringBuilder.Capacity);
			if (stringBuilder.Length == 0 || stringBuilder.ToString() == "Program Manager")
			{
				return true;
			}
			string target = "";
			try
			{
				target = ProcessTarget(pid);
				if (target.EndsWith("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
				{
					EnumChildWindows(h, delegate(nint child, nint num)
					{
						Win32.GetWindowThreadProcessId(child, out var process);
						if (process == pid)
						{
							return true;
						}
						string text = ProcessTarget(process);
						if (!text.Contains('!'))
						{
							return true;
						}
						target = text;
						return false;
					}, 0);
				}
			}
			catch
			{
			}
			list.Add(new RunningWindow(h, stringBuilder.ToString(), target));
			return true;
		}, 0);
		return list;
	}

	[DllImport("dwmapi", EntryPoint = "DwmGetWindowAttribute")]
	private static extern int FrameBounds(nint hwnd, uint attribute, out Win32.RECT bounds, int size);

	public static List<RunningWindow> ReadStageTasks()
	{
		List<RunningWindow> list = Read();
		List<WindowTile> list2 = new List<WindowTile>();
		foreach (RunningWindow item in list)
		{
			if (!IsIconic(item.Handle) && (FrameBounds(item.Handle, 9u, out var bounds, Marshal.SizeOf<Win32.RECT>()) == 0 || Win32.GetWindowRect(item.Handle, out bounds)))
			{
				Win32.GetWindowRect(item.Handle, out var r);
				list2.Add(new WindowTile(item, bounds, (r.Width > 0) ? r : bounds));
			}
		}
		Dictionary<nint, RunningWindow> grouped = new Dictionary<nint, RunningWindow>();
		HashSet<nint> hidden = new HashSet<nint>();
		foreach (IGrouping<(int, int, int, int), WindowTile> item2 in list2.GroupBy(delegate(WindowTile t)
		{
			Win32.RECT bounds2 = t.Bounds;
			Win32.RECT rcWork = Win32.Monitor(new Win32.POINT((bounds2.Left + bounds2.Right) / 2, (bounds2.Top + bounds2.Bottom) / 2)).rcWork;
			return (Left: rcWork.Left, Top: rcWork.Top, Right: rcWork.Right, Bottom: rcWork.Bottom);
		}))
		{
			List<WindowTile> members = item2.ToList();
			(int, int, int, int) key = item2.Key;
			List<int> list3 = TileGrouping.Find(work: new TileRect(key.Item1, key.Item2, key.Item3 - key.Item1, key.Item4 - key.Item2), windows: members.Select((WindowTile t) => new TileRect(t.Bounds.Left, t.Bounds.Top, t.Bounds.Width, t.Bounds.Height)).ToArray(), tolerance: 16.0 * Win32.Scale(new Win32.POINT(key.Item1, key.Item2)));
			if (list3.Count < 2)
			{
				continue;
			}
			List<WindowTile> list4 = list3.Select((int i) => members[i]).ToList();
			RunningWindow window = list4[0].Window;
			grouped[window.Handle] = new RunningWindow(window.Handle, $"并排任务 · {list4.Count} 个窗口", window.Target, list4);
			foreach (WindowTile item3 in list4.Skip(1))
			{
				hidden.Add(item3.Window.Handle);
			}
		}
		return (from w in list
			where !hidden.Contains(w.Handle)
			select grouped.GetValueOrDefault(w.Handle, w)).ToList();
	}

	public static void ActivateTask(RunningWindow task, bool maximize)
	{
		List<WindowTile> tiles = task.Tiles;
		if (tiles == null || tiles.Count <= 1)
		{
			Activate(task.Handle, maximize);
			return;
		}
		List<WindowTile> list = task.Tiles.Where((WindowTile tile) => IsWindow(tile.Window.Handle)).ToList();
		foreach (WindowTile item in Enumerable.Reverse(list))
		{
			Win32.ShowWindow(item.Window.Handle, 9);
			Win32.RECT restoreBounds = item.RestoreBounds;
			Win32.SetWindowPos(item.Window.Handle, 0, restoreBounds.Left, restoreBounds.Top, restoreBounds.Width, restoreBounds.Height, 20u);
		}
		if (list.Count > 0)
		{
			Activate(list[0].Window.Handle);
		}
	}

	public static void Activate(nint hwnd, bool maximize = false)
	{
		if (IsWindow(hwnd))
		{
			if (maximize)
			{
				Win32.ShowWindow(hwnd, 3);
			}
			else if (IsIconic(hwnd))
			{
				Win32.ShowWindow(hwnd, 9);
			}
			Win32.SetForegroundWindow(hwnd);
		}
	}
}
