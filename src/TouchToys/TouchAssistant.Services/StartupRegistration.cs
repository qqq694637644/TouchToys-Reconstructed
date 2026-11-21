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
	}

	private static StartupTask Task()
	{
		return StartupTask.GetAsync("TouchToysStartup").AsTask().GetAwaiter()
			.GetResult();
	}

	internal static async Task<string> DiagnoseAsync()
	{
		if (!Packaged)
		{
			return "Packaged=false; startup uses HKCU Run";
		}
		StartupTask startupTask = await StartupTask.GetAsync("TouchToysStartup");
		return $"Packaged=true; Family={Package.Current.Id.FamilyName}; TaskId={startupTask.TaskId}; State={startupTask.State}";
	}

	internal static async Task<string> EnableAndDiagnoseAsync()
	{
		if (!Packaged)
		{
			return "Packaged=false; enable probe not applicable";
		}
		StartupTask startupTask = await StartupTask.GetAsync("TouchToysStartup");
		StartupTaskState before = startupTask.State;
		if (before == StartupTaskState.Enabled || before == StartupTaskState.EnabledByPolicy)
		{
			return $"AlreadyEnabled; State={before}; no change made";
		}
		await SetEnabledAsync(value: true);
		StartupTaskState after = startupTask.State;
		if (after != StartupTaskState.Enabled && after != StartupTaskState.EnabledByPolicy)
		{
			throw new InvalidOperationException($"Windows did not persist startup enablement; state={after}");
		}
		return $"EnableRequest=PASS; Before={before}; Final={after}; Startup remains enabled";
	}

	private static void SetUnpackagedEnabled(bool value)
	{
		using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey(KeyPath);
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

	public static async Task SetEnabledAsync(bool value)
	{
		if (!Packaged)
		{
			SetUnpackagedEnabled(value);
			return;
		}
		StartupTask startupTask = await StartupTask.GetAsync("TouchToysStartup");
		if (!value)
		{
			startupTask.Disable();
			return;
		}
		if (startupTask.State == StartupTaskState.Enabled || startupTask.State == StartupTaskState.EnabledByPolicy)
		{
			return;
		}
		if (startupTask.State == StartupTaskState.DisabledByUser)
		{
			throw new InvalidOperationException("Windows 已在启动管理中禁用 TouchToys，请先在任务管理器的“启动应用”中重新启用。");
		}
		if (startupTask.State == StartupTaskState.DisabledByPolicy)
		{
			throw new InvalidOperationException("此设备的策略禁止 TouchToys 开机自启动。");
		}
		StartupTaskState startupTaskState = await startupTask.RequestEnableAsync();
		if (startupTaskState == StartupTaskState.Enabled || startupTaskState == StartupTaskState.EnabledByPolicy)
		{
			return;
		}
		throw new InvalidOperationException($"Windows 未启用 TouchToys 开机自启动（状态：{startupTaskState}）。请检查 Windows 设置 → 应用 → 启动。");
	}

	internal static async Task MigrateLegacyAsync()
	{
		bool legacyEnabled;
		using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(KeyPath))
		{
			legacyEnabled = registryKey?.GetValue("TouchAssistant") is string;
		}
		if (legacyEnabled)
		{
			await SetEnabledAsync(value: true);
			using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true);
			registryKey?.DeleteValue("TouchAssistant", throwOnMissingValue: false);
		}
	}
}
