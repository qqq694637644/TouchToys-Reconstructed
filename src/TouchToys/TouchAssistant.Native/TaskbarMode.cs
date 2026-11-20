using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using TouchAssistant.Models;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class TaskbarMode : IDisposable
{
	private struct AppBarData
	{
		public uint Size;

		public nint Window;

		public uint Callback;

		public uint Edge;

		public Win32.RECT Rect;

		public nint Parameter;
	}

	private readonly SettingsStore store;

	private readonly string recovery = Path.Combine(SettingsStore.DirectoryPath, "taskbar-restore.txt");

	private uint? original;

	private bool active;

	private readonly string revealRecovery = Path.Combine(SettingsStore.DirectoryPath, "taskbar-reveal-restore.txt");

	private readonly DispatcherQueueTimer revealTimer;

	public static bool AutoHide
	{
		get
		{
			try
			{
				return (State() & 1) != 0;
			}
			catch
			{
				return false;
			}
		}
	}

	[DllImport("shell32")]
	private static extern nuint SHAppBarMessage(uint message, ref AppBarData data);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern nint FindWindow(string cls, string? title);

	public TaskbarMode(SettingsStore settings)
	{
		store = settings;
		Recover();
		RestoreReveal();
		revealTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
		revealTimer.Interval = TimeSpan.FromSeconds(4.0);
		revealTimer.IsRepeating = false;
		revealTimer.Tick += delegate
		{
			RestoreReveal();
		};
		store.Changed += Update;
		Update();
	}

	public void Reveal()
	{
		try
		{
			uint num = State();
			if ((num & 1) != 0)
			{
				Directory.CreateDirectory(SettingsStore.DirectoryPath);
				File.WriteAllText(revealRecovery, num.ToString());
				Set(num & 0xFFFFFFFEu);
			}
			nint num2 = FindWindow("Shell_TrayWnd", null);
			if (num2 != 0)
			{
				Win32.ShowWindow(num2, 4);
			}
			revealTimer.Stop();
			revealTimer.Start();
			Diagnostics.Event("Taskbar", "边缘手势显示任务栏");
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Taskbar.Reveal");
		}
	}

	private void RestoreReveal()
	{
		try
		{
			if (File.Exists(revealRecovery))
			{
				if (uint.TryParse(File.ReadAllText(revealRecovery), out var result) && State() == (result & 0xFFFFFFFEu))
				{
					Set(result);
				}
				File.Delete(revealRecovery);
			}
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Taskbar.Restore");
		}
	}

	private static uint State()
	{
		AppBarData data = new AppBarData
		{
			Size = (uint)Marshal.SizeOf<AppBarData>()
		};
		return (uint)SHAppBarMessage(4u, ref data);
	}

	private static void Set(uint state)
	{
		AppBarData data = new AppBarData
		{
			Size = (uint)Marshal.SizeOf<AppBarData>(),
			Window = FindWindow("Shell_TrayWnd", null),
			Parameter = (nint)state
		};
		if (data.Window != 0)
		{
			SHAppBarMessage(10u, ref data);
		}
	}

	private void Recover()
	{
		try
		{
			if (File.Exists(recovery) && uint.TryParse(File.ReadAllText(recovery), out var result))
			{
				if (State() == (result | 1))
				{
					Set(result);
				}
				File.Delete(recovery);
			}
		}
		catch (Exception error)
		{
			App.Log(error);
		}
	}

	private void Update()
	{
		bool flag = false;
		if (active == flag)
		{
			return;
		}
		active = flag;
		if (!active)
		{
			Restore();
			return;
		}
		uint num = State();
		if ((num & 1) != 0)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(SettingsStore.DirectoryPath);
			File.WriteAllText(recovery, num.ToString());
			original = num;
			Set(num | 1);
		}
		catch (Exception error)
		{
			App.Log(error);
		}
	}

	private void Restore()
	{
		try
		{
			uint? num = original;
			if (num.HasValue)
			{
				uint valueOrDefault = num.GetValueOrDefault();
				if (State() == (valueOrDefault | 1))
				{
					Set(valueOrDefault);
				}
				original = null;
				if (File.Exists(recovery))
				{
					File.Delete(recovery);
				}
			}
		}
		catch (Exception error)
		{
			App.Log(error);
		}
	}

	public void Dispose()
	{
		revealTimer.Stop();
		RestoreReveal();
		store.Changed -= Update;
		Restore();
	}
}
