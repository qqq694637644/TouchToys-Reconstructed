using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using TouchAssistant.Models;

namespace TouchAssistant.Services;

internal static class GamepadLayoutTransfer
{
	private sealed class Bundle
	{
		public int Version { get; set; } = 1;

		public List<GamepadLayout> Layouts { get; set; } = new List<GamepadLayout>();
	}

	private const int CurrentVersion = 1;

	private const long MaxBytes = 8388608L;

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true,
		PropertyNameCaseInsensitive = true
	};

	public static void Export(IReadOnlyList<GamepadLayout> source, string destination)
	{
		Settings settings = new Settings();
		settings.Gamepad.Layouts = new List<GamepadLayout>();
		foreach (GamepadLayout item in source)
		{
			settings.Gamepad.Layouts.Add(JsonSerializer.Deserialize<GamepadLayout>(JsonSerializer.Serialize(item, Json), Json));
		}
		settings.Gamepad.ActiveLayout = 0;
		settings.Gamepad.Controls = settings.Gamepad.Layouts[0].Controls;
		settings.Normalize();
		Bundle value = new Bundle
		{
			Layouts = settings.Gamepad.Layouts
		};
		string text = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			File.WriteAllText(text, JsonSerializer.Serialize(value, Json));
			File.Move(text, destination, overwrite: true);
		}
		finally
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
		}
	}

	public static List<GamepadLayout> Read(string path)
	{
		if (new FileInfo(path).Length > 8388608)
		{
			throw new InvalidDataException(Localization.T("布局文件过大。"));
		}
		Bundle bundle;
		using (FileStream utf8Json = File.OpenRead(path))
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(utf8Json);
			JsonElement rootElement = jsonDocument.RootElement;
			if (rootElement.ValueKind != JsonValueKind.Object || !rootElement.TryGetProperty("Version", out var value) || !value.TryGetInt32(out var value2) || value2 > 1 || !rootElement.TryGetProperty("Layouts", out var _))
			{
				throw new InvalidDataException(Localization.T("布局文件无效或版本过新。"));
			}
			bundle = rootElement.Deserialize<Bundle>(Json);
		}
		if (bundle?.Layouts == null || bundle.Layouts.Count == 0)
		{
			throw new InvalidDataException(Localization.T("没有可导入的布局。"));
		}
		if (bundle.Layouts.Count > 64)
		{
			throw new InvalidDataException(Localization.T("最多保留 64 个布局。"));
		}
		Settings settings = new Settings();
		settings.Gamepad.Layouts = bundle.Layouts;
		settings.Gamepad.ActiveLayout = 0;
		settings.Gamepad.Controls = settings.Gamepad.Layouts[0].Controls;
		settings.Normalize();
		return settings.Gamepad.Layouts;
	}
}
