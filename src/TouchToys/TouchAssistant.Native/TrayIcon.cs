using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class TrayIcon : IDisposable
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct IconData
	{
		public uint Size;

		public nint Window;

		public uint Id;

		public uint Flags;

		public uint Callback;

		public nint Icon;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string Tip;

		public uint State;

		public uint StateMask;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string Info;

		public uint Version;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
		public string Title;

		public uint InfoFlags;

		public Guid Guid;

		public nint BalloonIcon;
	}

	private readonly Win32.WndProc procedure;

	private readonly uint taskbarCreated;

	private IconData data;

	public event Action? Invoked;

	public event Action? ContextRequested;

	[DllImport("shell32", CharSet = CharSet.Unicode)]
	private static extern bool Shell_NotifyIcon(uint command, ref IconData data);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern uint RegisterWindowMessage(string name);

	[DllImport("user32")]
	private static extern bool DestroyIcon(nint icon);

	[DllImport("user32")]
	private static extern nint CopyIcon(nint icon);

	[DllImport("user32")]
	private static extern uint GetDpiForSystem();

	[DllImport("user32")]
	private static extern int GetSystemMetricsForDpi(int index, uint dpi);

	public TrayIcon()
	{
		procedure = WindowProc;
		taskbarCreated = RegisterWindowMessage("TaskbarCreated");
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = procedure,
			hCursor = Win32.ArrowCursor,
			hInstance = Win32.GetModuleHandle(null),
			lpszClassName = "TouchAssistant.Tray"
		};
		Win32.RegisterClass(ref wc);
		nint window = Win32.CreateWindowEx(0u, wc.lpszClassName, "TouchToys Tray", 0u, 0, 0, 0, 0, 0, 0, wc.hInstance, 0);
		data = new IconData
		{
			Size = (uint)Marshal.SizeOf<IconData>(),
			Window = window,
			Id = 1u,
			Flags = 135u,
			Callback = 32769u,
			Icon = LoadTrayIcon(),
			Tip = Localization.T("TouchToys · 触控辅助"),
			Info = "",
			Title = "",
			Version = 4u
		};
		Add();
		Localization.Changed += RefreshLanguage;
	}

	private void RefreshLanguage()
	{
		data.Tip = Localization.T("TouchToys · 触控辅助");
		Shell_NotifyIcon(1u, ref data);
	}

	private void Add()
	{
		Shell_NotifyIcon(0u, ref data);
		data.Version = 4u;
		Shell_NotifyIcon(4u, ref data);
	}

	private static nint LoadTrayIcon()
	{
		int num = Math.Max(16, GetSystemMetricsForDpi(49, GetDpiForSystem()));
		using Icon icon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "TouchToys.ico"), num, num);
		return CopyIcon(icon.Handle);
	}

	private void RefreshIcon()
	{
		nint num = LoadTrayIcon();
		if (num != 0)
		{
			nint icon = data.Icon;
			data.Icon = num;
			Shell_NotifyIcon(1u, ref data);
			if (icon != 0)
			{
				DestroyIcon(icon);
			}
		}
	}

	private nint WindowProc(nint h, uint m, nuint wp, nint lp)
	{
		if (m == taskbarCreated && data.Window != 0)
		{
			RefreshIcon();
			Add();
		}
		if ((m == 26 || m == 736) ? true : false)
		{
			RefreshIcon();
		}
		if (m == 32769)
		{
			uint num = (uint)((int)lp & 0xFFFF);
			if (num - 1024 <= 1)
			{
				this.Invoked?.Invoke();
			}
			else if (num == 123)
			{
				this.ContextRequested?.Invoke();
			}
			return 0;
		}
		return Win32.DefWindowProc(h, m, wp, lp);
	}

	public void Notify(string message)
	{
		data.Flags = 16u;
		data.Info = ((message.Length > 255) ? message.Substring(0, 255) : message);
		data.Title = "TouchToys";
		data.InfoFlags = 1u;
		Shell_NotifyIcon(1u, ref data);
		data.Flags = 135u;
	}

	public void Dispose()
	{
		Localization.Changed -= RefreshLanguage;
		Shell_NotifyIcon(2u, ref data);
		DestroyIcon(data.Icon);
		Win32.DestroyWindow(data.Window);
		data.Window = 0;
	}
}
