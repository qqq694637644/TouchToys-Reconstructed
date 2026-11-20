using System;
using System.IO;
using System.Security;
using Microsoft.Win32;
using TouchAssistant.Models;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class SystemEdgePolicy : IDisposable
{
	private readonly SideEdgeSettings sideSettings;

	private const string Key = "Software\\Policies\\Microsoft\\Windows\\EdgeUI";

	public static string Status { get; private set; } = "未修改系统边缘手势策略";

	public SystemEdgePolicy(SettingsStore settings)
	{
		sideSettings = new SideEdgeSettings(settings);
		string path = Path.Combine(SettingsStore.DirectoryPath, "edge-policy-restore.txt");
		if (!File.Exists(path))
		{
			return;
		}
		try
		{
			string text = File.ReadAllText(path);
			using (RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Policies\\Microsoft\\Windows\\EdgeUI"))
			{
				object obj = registryKey?.GetValue("AllowEdgeSwipe");
				if (!(obj is int) || (int)obj != 0)
				{
					File.Delete(path);
					return;
				}
			}
			if (text != "absent" && !int.TryParse(text, out var _))
			{
				throw new InvalidDataException("旧边缘策略恢复记录无效，已保留记录。");
			}
			using RegistryKey registryKey2 = Registry.CurrentUser.OpenSubKey("Software\\Policies\\Microsoft\\Windows\\EdgeUI", writable: true) ?? throw new InvalidOperationException("旧边缘策略路径不可写。");
			if (text == "absent")
			{
				registryKey2.DeleteValue("AllowEdgeSwipe", throwOnMissingValue: false);
			}
			else
			{
				registryKey2.SetValue("AllowEdgeSwipe", int.Parse(text), RegistryValueKind.DWord);
			}
			File.Delete(path);
			Status = "已恢复旧版本修改的边缘策略，系统可能需要重新登录。";
			Diagnostics.Event("EdgePolicy", Status);
		}
		catch (Exception ex) when (((ex is UnauthorizedAccessException || ex is SecurityException) ? 1 : 0) != 0)
		{
			Status = "旧版本边缘策略因系统权限限制尚未恢复；记录已保留，可在有权限时恢复。当前边缘手势不依赖该策略。";
			Diagnostics.State("EdgePolicy", Status);
		}
		catch (Exception ex2)
		{
			Status = "旧版本的边缘策略尚未恢复，恢复记录已保留：" + ex2.Message;
			Diagnostics.Error(ex2, "EdgePolicy.Migration");
		}
	}

	public void Dispose()
	{
		sideSettings.Dispose();
	}
}
