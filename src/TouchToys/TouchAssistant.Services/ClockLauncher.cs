using System;
using System.Threading.Tasks;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Core;
using Windows.Management.Deployment;

namespace TouchAssistant.Services;

internal static class ClockLauncher
{
	internal static async Task OpenAsync()
	{
		_ = 1;
		try
		{
			PackageManager packageManager = new PackageManager();
			foreach (Package item in packageManager.FindPackagesForUser(""))
			{
				if (!string.Equals(item.Id.Name, "Microsoft.WindowsAlarms", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				foreach (AppListEntry item2 in await item.GetAppListEntriesAsync())
				{
					if (await item2.LaunchAsync())
					{
						Diagnostics.Event("Clock", "已启动已安装的时钟应用");
						return;
					}
				}
			}
			App.CurrentApp.Report("未能打开 Windows 时钟，请确认已安装并可从开始菜单打开。");
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Clock.Activation");
			App.CurrentApp.Report("未能打开 Windows 时钟，请确认已安装并可从开始菜单打开。");
		}
	}
}
