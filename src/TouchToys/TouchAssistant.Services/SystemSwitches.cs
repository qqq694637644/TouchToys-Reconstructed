using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Automation;
using TouchAssistant.Native;

namespace TouchAssistant.Services;

internal static class SystemSwitches
{
	[DllImport("user32", EntryPoint = "#2507")]
	private static extern bool SetAutoRotationOrdinal([MarshalAs(UnmanagedType.Bool)] bool enabled);

	public static async Task<bool> SetRotationLockAsync(bool locked)
	{
		try
		{
			if (SetAutoRotationOrdinal(!locked))
			{
				await Task.Delay(200);
				if (ShellPreferences.Rotation.Locked == locked)
				{
					return true;
				}
			}
		}
		catch (EntryPointNotFoundException)
		{
		}
		return await SetSettingsToggleAsync("ms-settings:display", locked, new string[3] { "旋转锁定", "Rotation lock", "旋轉鎖定" });
	}

	public static Task<bool> SetEnergySaverAsync(bool enabled)
	{
		return SetSettingsToggleAsync("ms-settings:batterysaver", enabled, new string[4] { "始终使用节能模式", "始终使用节电模式", "Always use energy saver", "一律使用省電模式" });
	}

	private static async Task<bool> SetSettingsToggleAsync(string uri, bool desired, string[] labels)
	{
		SystemControl.Launch(uri);
		return await Task.Run(async delegate
		{
			for (int attempt = 0; attempt < 24; attempt++)
			{
				await Task.Delay(250);
				try
				{
					Process[] processesByName = Process.GetProcessesByName("SystemSettings");
					foreach (Process process in processesByName)
					{
						using (process)
						{
							AutomationElement automationElement = AutomationElement.RootElement.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, process.Id));
							if (!(automationElement == null))
							{
								List<AutomationElement> list = (from AutomationElement element in automationElement.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.IsTogglePatternAvailableProperty, true))
									where labels.Any((string label) => string.Equals(element.Current.Name.Trim(), label, StringComparison.OrdinalIgnoreCase))
									select element).ToList();
								if (list.Count == 1 && list[0].Current.IsEnabled)
								{
									TogglePattern toggle = (TogglePattern)list[0].GetCurrentPattern(TogglePattern.Pattern);
									ToggleState wanted = (desired ? ToggleState.On : ToggleState.Off);
									if (toggle.Current.ToggleState != wanted)
									{
										toggle.Toggle();
									}
									await Task.Delay(150);
									return toggle.Current.ToggleState == wanted;
								}
							}
						}
					}
				}
				catch (ElementNotAvailableException)
				{
				}
				catch (InvalidOperationException)
				{
				}
				catch (COMException)
				{
				}
			}
			return false;
		});
	}
}
