using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using TouchAssistant.Models;

namespace TouchAssistant.Services;

internal static class SettingsTransfer
{
	private const long MaxFile = 16777216L;

	private const long MaxTotal = 67108864L;

	private static IEnumerable<GestureAction> Actions(Settings s)
	{
		foreach (DialAppProfile dialApp in s.DialApps)
		{
			foreach (DialTool tool in dialApp.Options.Tools)
			{
				GestureAction[] array = new GestureAction[3] { tool.Tap, tool.Clockwise, tool.CounterClockwise };
				for (int i = 0; i < array.Length; i++)
				{
					yield return array[i];
				}
			}
		}
		yield return s.MouseJoysticks.Pointer.Tap;
		yield return s.MouseJoysticks.RightDrag.Tap;
		foreach (GestureAppProfile gestureApp in s.GestureApps)
		{
			foreach (MultiTouchBinding binding in gestureApp.Options.Bindings)
			{
				yield return binding.Action;
			}
		}
		foreach (BottomAppProfile profile in s.BottomApps)
		{
			yield return profile.Bottom;
			foreach (GestureAction value in profile.Indicator.Values)
			{
				yield return value;
			}
		}
		yield return s.VirtualTouchpad.JoystickTap;
		yield return s.VirtualTouchpad.RightJoystickTap;
		foreach (GestureAction item in s.Gestures.Concat(s.EdgeGestures).Concat(s.IndicatorActions.Values).Concat(s.MultiTouch.Bindings.Select((MultiTouchBinding x) => x.Action)))
		{
			yield return item;
		}
		foreach (GamepadControl c in s.Gamepad.Layouts.SelectMany((GamepadLayout l) => l.Controls))
		{
			if (c.Appearance != null)
			{
				yield return c.Appearance;
			}
			yield return c.Action;
			yield return c.CenterTap;
			yield return c.CenterDoubleTap;
			foreach (GestureAction direction in c.Directions)
			{
				yield return direction;
			}
		}
		foreach (DialTool tool2 in s.Dial.Tools)
		{
			GestureAction[] array = new GestureAction[3] { tool2.Tap, tool2.Clockwise, tool2.CounterClockwise };
			for (int i = 0; i < array.Length; i++)
			{
				yield return array[i];
			}
		}
		foreach (ArkProfile p in s.ArkProfiles)
		{
			GestureAction[] array = new GestureAction[5] { p.Tap, p.DoubleTap, p.TripleTap, p.Drag, p.Hold };
			for (int i = 0; i < array.Length; i++)
			{
				yield return array[i];
			}
			foreach (ArkBubble button in p.Buttons)
			{
				yield return button.Action;
			}
		}
	}

	private static string Extension(string path)
	{
		string text = Path.GetExtension(path).ToLowerInvariant();
		bool flag;
		switch (text)
		{
		case ".png":
		case ".jpg":
		case ".jpeg":
		case ".bmp":
		case ".ico":
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag)
		{
			throw new InvalidDataException("设置中的图标不是支持的图片格式。");
		}
		return text;
	}

	public static void Export(Settings source, string destination)
	{
		Settings settings = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(source));
		settings.Normalize();
		string text = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			using (ZipArchive zipArchive = ZipFile.Open(text, ZipArchiveMode.Create))
			{
				HashSet<string> hashSet = new HashSet<string>(StringComparer.Ordinal);
				long num = 0L;
				foreach (GestureAction item in from x in Actions(settings)
					where !string.IsNullOrEmpty(x.IconPath)
					select x)
				{
					if (!File.Exists(item.IconPath))
					{
						throw new FileNotFoundException("图标文件缺失，请重新导入图标后再导出设置。", item.IconPath);
					}
					if (new FileInfo(item.IconPath).Length > 16777216)
					{
						throw new InvalidDataException("单个图标不能超过16MB。");
					}
					byte[] array = File.ReadAllBytes(item.IconPath);
					string text2 = "icons/" + Convert.ToHexString(SHA256.HashData(array)) + Extension(item.IconPath);
					if (hashSet.Add(text2))
					{
						num += array.Length;
						if (num > 67108864 || hashSet.Count > 512)
						{
							throw new InvalidDataException("图标总量超过64MB或512个。");
						}
						using Stream stream = zipArchive.CreateEntry(text2).Open();
						stream.Write(array);
					}
					item.IconPath = text2;
				}
				using StreamWriter streamWriter = new StreamWriter(zipArchive.CreateEntry("settings.json").Open());
				streamWriter.Write(JsonSerializer.Serialize(settings, new JsonSerializerOptions
				{
					WriteIndented = true
				}));
			}
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

	public static Settings Read(string path, string storageDirectory)
	{
		if (new FileInfo(path).Length > 67108864)
		{
			throw new InvalidDataException("设置文件不能超过64MB。");
		}
		using ZipArchive zipArchive = ZipFile.OpenRead(path);
		if (zipArchive.Entries.Count > 513 || zipArchive.Entries.Sum((ZipArchiveEntry x) => x.Length) > 67108864)
		{
			throw new InvalidDataException("设置包解压后超出限制。");
		}
		if (zipArchive.Entries.GroupBy<ZipArchiveEntry, string>((ZipArchiveEntry x) => x.FullName, StringComparer.Ordinal).ToArray().Any((IGrouping<string, ZipArchiveEntry> x) => x.Count() != 1))
		{
			throw new InvalidDataException("设置包存在重复文件。");
		}
		ZipArchiveEntry? obj = zipArchive.GetEntry("settings.json") ?? throw new InvalidDataException("设置包缺少settings.json。");
		if (obj.Length > 4194304)
		{
			throw new InvalidDataException("设置内容过大。");
		}
		Settings settings;
		using (Stream utf8Json = obj.Open())
		{
			using JsonDocument jsonDocument = JsonDocument.Parse(utf8Json);
			if (jsonDocument.RootElement.ValueKind != JsonValueKind.Object)
			{
				goto IL_017c;
			}
			JsonElement value = jsonDocument.RootElement;
			if (!value.TryGetProperty("SchemaVersion", out var value2) || !value2.TryGetInt32(out var value3) || value3 > 12 || !jsonDocument.RootElement.TryGetProperty("Dial", out value))
			{
				goto IL_017c;
			}
			settings = jsonDocument.RootElement.Deserialize<Settings>() ?? throw new InvalidDataException("设置内容为空。");
			settings.Normalize();
			goto end_IL_0126;
			IL_017c:
			throw new InvalidDataException("不是支持的应用设置包，或来自更新版本。");
			end_IL_0126:;
		}
		Dictionary<string, byte[]> dictionary = new Dictionary<string, byte[]>();
		foreach (GestureAction item in from x in Actions(settings)
			where !string.IsNullOrEmpty(x.IconPath)
			select x)
		{
			string iconPath = item.IconPath;
			if (iconPath.StartsWith("icons/", StringComparison.Ordinal) && !iconPath.Contains('\\') && !iconPath.Contains(".."))
			{
				string text = iconPath;
				if (!text.Substring(6, text.Length - 6).Contains('/'))
				{
					Extension(iconPath);
					ZipArchiveEntry zipArchiveEntry = zipArchive.GetEntry(iconPath) ?? throw new InvalidDataException("设置包缺少图标。");
					if (zipArchiveEntry.Length > 16777216)
					{
						throw new InvalidDataException("图标超过16MB。");
					}
					if (dictionary.ContainsKey(iconPath))
					{
						continue;
					}
					using (Stream stream = zipArchiveEntry.Open())
					{
						using MemoryStream memoryStream = new MemoryStream();
						stream.CopyTo(memoryStream);
						dictionary[iconPath] = memoryStream.ToArray();
					}
					continue;
				}
			}
			throw new InvalidDataException("图标路径无效。");
		}
		string text2 = Path.Combine(storageDirectory, "Icons");
		if (dictionary.Count > 0)
		{
			Directory.CreateDirectory(text2);
		}
		foreach (GestureAction item2 in from x in Actions(settings)
			where !string.IsNullOrEmpty(x.IconPath)
			select x)
		{
			byte[] array = dictionary[item2.IconPath];
			string text3 = Path.Combine(text2, Convert.ToHexString(SHA256.HashData(array)) + Extension(item2.IconPath));
			if (!File.Exists(text3))
			{
				File.WriteAllBytes(text3, array);
			}
			item2.IconPath = text3;
		}
		return settings;
	}
}
