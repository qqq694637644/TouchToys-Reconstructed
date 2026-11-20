using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;

namespace TouchAssistant.Services;

internal static class Localization
{
	internal static readonly string[] Codes = new string[9] { "system", "zh-CN", "zh-TW", "en", "ja", "ko", "de", "fr", "es" };

	internal static readonly string[] Names = new string[9] { "跟随系统 / System", "简体中文", "繁體中文", "English", "日本語", "한국어", "Deutsch", "Français", "Español" };

	private static readonly Dictionary<string, string[]> catalog = Load();

	private static int column = -1;

	internal static string Language { get; private set; } = "zh-CN";

	internal static int Count => catalog.Count;

	internal static event Action? Changed;

	internal static bool Has(string key)
	{
		return catalog.ContainsKey(key);
	}

	internal static void Use(string? code)
	{
		string language = Language;
		if (string.IsNullOrEmpty(code) || code == "system")
		{
			code = CultureInfo.InstalledUICulture.Name;
		}
		code = code.Trim();
		if (code.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
		{
			Language = ((code.Contains("TW", StringComparison.OrdinalIgnoreCase) || code.Contains("HK", StringComparison.OrdinalIgnoreCase) || code.Contains("MO", StringComparison.OrdinalIgnoreCase) || code.Contains("Hant", StringComparison.OrdinalIgnoreCase)) ? "zh-TW" : "zh-CN");
		}
		else
		{
			Language = code.Split('-')[0].ToLowerInvariant();
		}
		column = Array.IndexOf(new string[7] { "zh-TW", "en", "ja", "ko", "de", "fr", "es" }, Language);
		if (column < 0)
		{
			Language = "zh-CN";
		}
		if (language != Language)
		{
			Localization.Changed?.Invoke();
		}
	}

	internal static string T(string text)
	{
		if (column < 0)
		{
			return text;
		}
		if (catalog.TryGetValue(text, out string[] value))
		{
			if (value.Length > column && !string.IsNullOrEmpty(value[column]))
			{
				return value[column];
			}
			if (value.Length > 1 && !string.IsNullOrEmpty(value[1]))
			{
				return value[1];
			}
		}
		return text;
	}

	internal static string T(string text, params object?[] args)
	{
		string text2 = T(text);
		if (args.Length == 0)
		{
			return text2;
		}
		try
		{
			return string.Format(CultureInfo.CurrentCulture, text2, args);
		}
		catch (FormatException)
		{
			return text2;
		}
	}

	private static Dictionary<string, string[]> Load()
	{
		Dictionary<string, string[]> dictionary = new Dictionary<string, string[]>(StringComparer.Ordinal);
		using Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TouchToys.Localization.tsv");
		if (stream == null)
		{
			return dictionary;
		}
		using StreamReader streamReader = new StreamReader(stream);
		while (true)
		{
			string text = streamReader.ReadLine();
			if (text == null)
			{
				break;
			}
			if (!string.IsNullOrWhiteSpace(text))
			{
				string[] array = text.Split('\t');
				if (array.Length == 8 && !string.IsNullOrWhiteSpace(array[0]) && !dictionary.TryAdd(array[0], array[1..]))
				{
					Diagnostics.Event("Localization", "Duplicated key ignored: " + array[0]);
				}
			}
		}
		return dictionary;
	}
}
