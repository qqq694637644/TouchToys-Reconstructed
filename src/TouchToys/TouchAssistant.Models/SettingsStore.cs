using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TouchAssistant.Native;
using TouchAssistant.Services;

namespace TouchAssistant.Models;

public sealed class SettingsStore
{
	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private readonly string directory;

	public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TouchAssistant");

	public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

	public Settings Value { get; private set; }

	private string DataFile => Path.Combine(directory, "settings.json");

	public string StorageDirectory => directory;

	public event Action? Changed;

	public event Action? Replaced;

	public SettingsStore(string? storageDirectory = null)
	{
		directory = storageDirectory ?? DirectoryPath;
		if (storageDirectory == null && !File.Exists(Path.Combine(directory, "settings.json")))
		{
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TouchAssistant");
			if (File.Exists(Path.Combine(text, "settings.json")))
			{
				try
				{
					SystemInputControl.RestoreSaved(text);
				}
				catch (Exception error)
				{
					Diagnostics.Error(error, "Migration.NativeInputRestore");
				}
				Directory.CreateDirectory(directory);
				string path = Path.Combine(text, "Icons");
				string text2 = Path.Combine(directory, "Icons");
				if (Directory.Exists(path))
				{
					Directory.CreateDirectory(text2);
					foreach (string item in Directory.EnumerateFiles(path))
					{
						string text3 = Path.Combine(text2, Path.GetFileName(item));
						if (!File.Exists(text3))
						{
							File.Copy(item, text3, overwrite: false);
						}
					}
				}
				string text4 = File.ReadAllText(Path.Combine(text, "settings.json"));
				text4 = text4.Replace(JsonSerializer.Serialize(text + Path.DirectorySeparatorChar).Trim('"'), JsonSerializer.Serialize(directory + Path.DirectorySeparatorChar).Trim('"'), StringComparison.OrdinalIgnoreCase);
				File.WriteAllText(Path.Combine(directory, "settings.migration.tmp"), text4);
				File.Move(Path.Combine(directory, "settings.migration.tmp"), Path.Combine(directory, "settings.json"));
			}
		}
		try
		{
			Value = JsonSerializer.Deserialize<Settings>(File.ReadAllText(DataFile)) ?? new Settings();
		}
		catch (Exception ex) when (((ex is IOException || ex is JsonException || ex is UnauthorizedAccessException) ? 1 : 0) != 0)
		{
			Value = new Settings();
			if (!(ex is FileNotFoundException) && !(ex is DirectoryNotFoundException))
			{
				Diagnostics.Error(ex, "Settings.Load");
			}
		}
		Value.Normalize();
	}

	public void Save()
	{
		Value.Normalize();
		Directory.CreateDirectory(directory);
		string text = DataFile + ".tmp";
		File.WriteAllText(text, JsonSerializer.Serialize(Value, JsonOptions));
		File.Move(text, DataFile, overwrite: true);
		this.Changed?.Invoke();
	}

	public void RestoreDefaults(bool preserveLaunchers = false)
	{
		List<LauncherEntry> launchers = (preserveLaunchers ? Value.Launchers : new List<LauncherEntry>());
		Settings settings = new Settings
		{
			Launchers = launchers
		};
		settings.Normalize();
		Value = settings;
		this.Replaced?.Invoke();
		Save();
	}

	public void Import(Settings settings)
	{
		settings.Normalize();
		Directory.CreateDirectory(directory);
		if (File.Exists(DataFile))
		{
			File.Copy(DataFile, Path.Combine(directory, "settings.before-import-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".json"), overwrite: false);
		}
		string text = DataFile + ".tmp";
		File.WriteAllText(text, JsonSerializer.Serialize(settings, JsonOptions));
		File.Move(text, DataFile, overwrite: true);
		Value = settings;
		this.Replaced?.Invoke();
		this.Changed?.Invoke();
	}
}
