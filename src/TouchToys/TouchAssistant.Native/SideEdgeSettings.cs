using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using TouchAssistant.Models;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class SideEdgeSettings : IDisposable
{
	private const string Key = "Software\\Microsoft\\Wisp\\Touch";

	private readonly SettingsStore store;

	private readonly string journal;

	private Dictionary<string, int?> saved = new Dictionary<string, int?>();

	private bool disposed;

	internal SideEdgeSettings(SettingsStore store)
	{
		this.store = store;
		journal = Path.Combine(store.StorageDirectory, "side-edge-restore.json");
		if (File.Exists(journal))
		{
			saved = JsonSerializer.Deserialize<Dictionary<string, int?>>(File.ReadAllText(journal)) ?? new Dictionary<string, int?>();
		}
		store.Changed += Update;
		Update();
	}

	private void Save()
	{
		Directory.CreateDirectory(store.StorageDirectory);
		string text = journal + ".tmp";
		File.WriteAllText(text, JsonSerializer.Serialize(saved));
		File.Move(text, journal, overwrite: true);
	}

	private void Update()
	{
		try
		{
			using RegistryKey registryKey = Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Wisp\\Touch");
			(int, string)[] array = new(int, string)[2]
			{
				(0, "Left_Edgy_Enabled"),
				(3, "Right_Edgy_Enabled")
			};
			for (int i = 0; i < array.Length; i++)
			{
				var (index, text) = array[i];
				if (!disposed && store.Value.EdgeGestures[index].Enabled && store.Value.EdgeGestures[index].Kind != ActionKind.None)
				{
					if (!saved.ContainsKey(text))
					{
						object value = registryKey.GetValue(text);
						if (value != null && !(value is int))
						{
							Diagnostics.Event("SideEdge", text + ": 非 DWORD 原值，跳过");
							continue;
						}
						saved[text] = value as int?;
						Save();
						registryKey.SetValue(text, 0, RegistryValueKind.DWord);
						Diagnostics.Event("SideEdge", text + ": 已禁用系统对应侧；可能需重新登录生效");
					}
				}
				else
				{
					if (!saved.TryGetValue(text, out var value2))
					{
						continue;
					}
					object value3 = registryKey.GetValue(text);
					if (value3 is int && (int)value3 == 0)
					{
						if (value2.HasValue)
						{
							registryKey.SetValue(text, value2.Value, RegistryValueKind.DWord);
						}
						else
						{
							registryKey.DeleteValue(text, throwOnMissingValue: false);
						}
					}
					saved.Remove(text);
					Save();
					Diagnostics.Event("SideEdge", text + ": 已释放并恢复原设置");
				}
			}
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "SideEdge");
		}
	}

	public void Dispose()
	{
		disposed = true;
		store.Changed -= Update;
		Update();
	}
}
