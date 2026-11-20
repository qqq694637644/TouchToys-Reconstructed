using System;
using System.Threading.Tasks;
using Microsoft.Win32;
using Windows.ApplicationModel;

namespace TouchAssistant.Services;

internal static class StartupRegistration
{
	private const string KeyPath = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private static bool Packaged
	{
		get
		{
			try
			{
				_ = Package.Current.Id;
				return true;
			}
			catch
			{
				return false;
			}
		}
	}

	public static bool Enabled
	{
		get
		{
			if (Packaged)
			{
				try
				{
					StartupTaskState state = Task().State;
					return (state == StartupTaskState.Enabled || state == StartupTaskState.EnabledByPolicy) ? true : false;
				}
				catch
				{
					return false;
				}
			}
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			return registryKey?.GetValue("TouchToys") is string || registryKey?.GetValue("TouchAssistant") is string;
		}
		set
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
			registryKey.DeleteValue("TouchAssistant", throwOnMissingValue: false);
			if (value)
			{
				registryKey.SetValue("TouchToys", "\"" + Environment.ProcessPath + "\"", RegistryValueKind.String);
			}
			else
			{
				registryKey.DeleteValue("TouchToys", throwOnMissingValue: false);
			}
		}
	}

	private static StartupTask Task()
	{
		return StartupTask.GetAsync("TouchToysStartup").AsTask().GetAwaiter()
			.GetResult();
	}

	public static async Task SetEnabledAsync(bool value)
	{
		if (!Packaged)
		{
			Enabled = value;
			return;
		}
		StartupTask startupTask = await StartupTask.GetAsync("TouchToysStartup");
		if (!value)
		{
			startupTask.Disable();
			return;
		}
		StartupTaskState startupTaskState = await startupTask.RequestEnableAsync();
		if (startupTaskState == StartupTaskState.Enabled || startupTaskState == StartupTaskState.EnabledByPolicy)
		{
			return;
		}
		throw new InvalidOperationException("自启动未启用，请在 Windows 设置 → 应用 → 启动中允许 TouchToys。");
	}

	internal static void MigrateLegacy()
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
		if (registryKey?.GetValue("TouchAssistant") is string)
		{
			Enabled = true;
		}
	}
}
