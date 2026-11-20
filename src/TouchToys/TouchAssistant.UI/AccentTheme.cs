using System;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TouchAssistant.Models;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TouchAssistant.UI;

internal sealed class AccentTheme : IDisposable
{
	private readonly SettingsStore store;

	private readonly UISettings system = new UISettings();

	private readonly DispatcherQueue queue;

	public static SolidColorBrush Brush { get; } = new SolidColorBrush(Colors.DodgerBlue);

	public static Color Color => Brush.Color;

	public static event Action? Changed;

	public static Color Parse(string value)
	{
		value = Settings.NormalizeColor(value, "#FFFFFF");
		return Color.FromArgb(byte.MaxValue, Convert.ToByte(value.Substring(1, 2), 16), Convert.ToByte(value.Substring(3, 2), 16), Convert.ToByte(value.Substring(5, 2), 16));
	}

	public AccentTheme(SettingsStore settings)
	{
		store = settings;
		queue = DispatcherQueue.GetForCurrentThread();
		store.Changed += Update;
		system.ColorValuesChanged += SystemChanged;
		Update();
	}

	private void SystemChanged(UISettings sender, object args)
	{
		queue.TryEnqueue(Update);
	}

	private void Update()
	{
		Color color = (store.Value.UseSystemAccent ? system.GetColorValue(UIColorType.Accent) : Parse(store.Value.AccentColor));
		bool flag = Brush.Color != color;
		Brush.Color = color;
		ResourceDictionary resources = Application.Current.Resources;
		resources["AppAccentBrush"] = Brush;
		string[] array = new string[7] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3" };
		foreach (string key in array)
		{
			resources[key] = color;
		}
		array = new string[5] { "AccentFillColorDefaultBrush", "AccentFillColorSecondaryBrush", "AccentFillColorTertiaryBrush", "SliderTrackValueFill", "ToggleSwitchFillOn" };
		foreach (string key2 in array)
		{
			resources[key2] = Brush;
		}
		if (flag)
		{
			AccentTheme.Changed?.Invoke();
		}
	}

	public void Dispose()
	{
		store.Changed -= Update;
		system.ColorValuesChanged -= SystemChanged;
	}
}
