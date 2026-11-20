using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Win32;
using TouchAssistant.Services;
using TouchAssistant.UI;
using Windows.Graphics;
using Windows.Networking.Connectivity;
using Windows.System.Power;
using Windows.UI;

namespace TouchAssistant.Native;

internal sealed class SystemStatusBadge : Window, IDisposable
{
	private struct FrameMargins
	{
		public int Left;

		public int Right;

		public int Top;

		public int Bottom;
	}

	private const int BaseWidth = 74;

	private const int BaseHeight = 26;

	private const int Margin = 12;

	private readonly SystemControl system;

	private readonly DispatcherQueueTimer timer;

	private readonly Border pill;

	private readonly FontIcon wifiBase;

	private readonly FontIcon wifiIcon;

	private readonly FontIcon batteryIcon;

	private readonly TextBlock percentText;

	private PanelShadow? shadow;

	private bool shown;

	private bool disposed;

	private int width;

	private int height;

	private double scale = 1.0;

	private string stamp = "";

	public bool IsVisible => shown;

	internal string RenderText => percentText.Text;

	private static bool Dark
	{
		get
		{
			try
			{
				using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
				return Convert.ToInt32(registryKey?.GetValue("SystemUsesLightTheme", 1)) == 0;
			}
			catch
			{
				return false;
			}
		}
	}

	[DllImport("dwmapi")]
	private static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref uint value, uint size);

	[DllImport("dwmapi")]
	private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref FrameMargins margins);

	private static string BatteryGlyph(int percent, bool charging, bool saver)
	{
		int num = Math.Clamp(percent / 10, 0, 10);
		return char.ConvertFromUtf32((!charging) ? ((!saver) ? ((num == 10) ? 59455 : (59472 + num)) : (num switch
		{
			9 => 60052, 
			10 => 60053, 
			_ => 59491 + num, 
		})) : (num switch
		{
			9 => 59454, 
			10 => 60051, 
			_ => 59482 + num, 
		}));
	}

	public SystemStatusBadge(SystemControl source)
	{
		system = source;
		base.Title = "TouchToys status";
		base.ExtendsContentIntoTitleBar = false;
		base.SystemBackdrop = new PanelBackdrop();
		wifiBase = Ui.Icon("\ue701", 13.0);
		wifiIcon = Ui.Icon("\ue701", 13.0);
		batteryIcon = Ui.Icon("\ue83f", 13.0);
		percentText = Ui.Text("--", 8.0);
		percentText.MinWidth = 12.0;
		percentText.FontWeight = FontWeights.SemiBold;
		percentText.VerticalAlignment = VerticalAlignment.Center;
		wifiBase.HorizontalAlignment = (wifiIcon.HorizontalAlignment = HorizontalAlignment.Center);
		wifiBase.VerticalAlignment = (wifiIcon.VerticalAlignment = VerticalAlignment.Center);
		batteryIcon.VerticalAlignment = VerticalAlignment.Center;
		Grid grid = new Grid
		{
			Width = 18.0,
			Height = 18.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.Children.Add(wifiBase);
		grid.Children.Add(wifiIcon);
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 2.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		stackPanel.Children.Add(grid);
		stackPanel.Children.Add(batteryIcon);
		stackPanel.Children.Add(percentText);
		pill = new Border
		{
			Child = stackPanel,
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(5.0, 0.0, 5.0, 0.0),
			BorderThickness = new Thickness(0.0),
			BorderBrush = null,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			VerticalAlignment = VerticalAlignment.Stretch
		};
		Grid grid2 = new Grid
		{
			Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0))
		};
		grid2.Children.Add(pill);
		base.Content = grid2;
		ConfigureWindow();
		timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
		timer.Interval = TimeSpan.FromSeconds(2.0);
		timer.Tick += delegate
		{
			Refresh();
		};
		base.Closed += delegate
		{
			timer.Stop();
			disposed = true;
		};
	}

	private void ConfigureWindow()
	{
		nint num = Ui.Handle(this);
		long num2 = ((IntPtr)WindowLong.Get(num, -16)).ToInt64() & -12845057;
		WindowLong.Set(num, -16, (nint)num2);
		long num3 = ((IntPtr)WindowLong.Get(num, -20)).ToInt64() | 0x80000A0;
		WindowLong.Set(num, -20, (nint)num3);
		if (base.AppWindow.Presenter is OverlappedPresenter overlappedPresenter)
		{
			overlappedPresenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
			overlappedPresenter.IsResizable = false;
			overlappedPresenter.IsMaximizable = false;
			overlappedPresenter.IsMinimizable = false;
			overlappedPresenter.IsAlwaysOnTop = true;
		}
		base.AppWindow.IsShownInSwitchers = false;
		base.AppWindow.TitleBar.BackgroundColor = Colors.Transparent;
		base.AppWindow.TitleBar.InactiveBackgroundColor = Colors.Transparent;
		Win32.SuppressFeedback(num);
		Win32.SetWindowPos(num, 0, 0, 0, 0, 0, 55u);
		RemoveBorder(num);
		FrameMargins margins = new FrameMargins
		{
			Left = -1,
			Right = -1,
			Top = -1,
			Bottom = -1
		};
		DwmExtendFrameIntoClientArea(num, ref margins);
	}

	private static void RemoveBorder(nint handle)
	{
		uint value = 4294967294u;
		DwmSetWindowAttribute(handle, 34u, ref value, 4u);
		uint value2 = 2u;
		DwmSetWindowAttribute(handle, 33u, ref value2, 4u);
	}

	private static void ClearBorderColor(nint handle)
	{
		uint value = 4294967294u;
		DwmSetWindowAttribute(handle, 34u, ref value, 4u);
	}

	public void Show(Win32.POINT origin)
	{
		if (!disposed)
		{
			Win32.MONITORINFO mONITORINFO = Win32.Monitor(origin);
			scale = Win32.Scale(origin);
			width = (int)Math.Round(74.0 * scale);
			height = (int)Math.Round(26.0 * scale);
			int num = mONITORINFO.rcMonitor.Right - width - (int)Math.Round(12.0 * scale);
			int num2 = mONITORINFO.rcMonitor.Bottom - height - (int)Math.Round(12.0 * scale);
			base.AppWindow.MoveAndResize(new RectInt32(num, num2, width, height));
			if (shadow == null)
			{
				shadow = new PanelShadow();
			}
			shadow.Show(num, num2, width, height, scale, 8.0);
			shadow.SetOpacity(0.28);
			shown = true;
			Refresh();
			base.AppWindow.Show(activateWindow: false);
			(base.SystemBackdrop as PanelBackdrop)?.PrepareForShow();
			RemoveBorder(Ui.Handle(this));
			ClearBorderColor(Ui.Handle(this));
			Win32.SetWindowPos(Ui.Handle(this), -1, 0, 0, 0, 0, 19u);
			timer.Start();
		}
	}

	public void Hide()
	{
		if (!disposed)
		{
			shown = false;
			timer.Stop();
			shadow?.Hide();
			base.AppWindow.Hide();
		}
	}

	public void Raise()
	{
		if (shown && !disposed)
		{
			Win32.SetWindowPos(Ui.Handle(this), -1, 0, 0, 0, 0, 19u);
		}
	}

	private void Refresh()
	{
		if (!shown || disposed)
		{
			return;
		}
		bool flag = false;
		int num = 0;
		bool flag2 = false;
		try
		{
			BatteryStatus batteryStatus = PowerManager.BatteryStatus;
			flag = batteryStatus != BatteryStatus.NotPresent;
			num = PowerManager.RemainingChargePercent;
			flag2 = batteryStatus == BatteryStatus.Charging || PowerManager.PowerSupplyStatus == PowerSupplyStatus.Adequate;
		}
		catch
		{
		}
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		int num2 = Array.IndexOf(commandLineArgs, "--battery-preview");
		if (int.TryParse(Environment.GetEnvironmentVariable("TOUCHTOYS_BATTERY_PERCENT"), out var result) || (num2 >= 0 && num2 + 1 < commandLineArgs.Length && int.TryParse(commandLineArgs[num2 + 1], out result)))
		{
			flag = true;
			num = Math.Clamp(result, 0, 100);
		}
		if (Environment.GetEnvironmentVariable("TOUCHTOYS_BATTERY_CHARGING") == "1" || Array.IndexOf(commandLineArgs, "--battery-charging") >= 0)
		{
			flag2 = true;
		}
		bool flag3 = system.HasWifi && system.Wifi;
		bool dark = Dark;
		int num3 = 0;
		if (flag3)
		{
			try
			{
				ConnectionProfile internetConnectionProfile = NetworkInformation.GetInternetConnectionProfile();
				if (internetConnectionProfile != null)
				{
					MethodInfo method = internetConnectionProfile.GetType().GetMethod("GetSignalStrength");
					if (method != null)
					{
						num3 = Convert.ToInt32(method.Invoke(internetConnectionProfile, null));
					}
				}
			}
			catch
			{
				num3 = 0;
			}
		}
		int num4 = (flag3 ? Math.Clamp((int)Math.Ceiling((double)(num3 * 3) / 5.0), 1, 3) : 0);
		string text = $"{flag3}:{num4}:{flag}:{num}:{flag2}:{dark}:{scale}";
		if (!(text == stamp))
		{
			stamp = text;
			Color color = (dark ? Color.FromArgb(byte.MaxValue, 246, 248, 251) : Color.FromArgb(byte.MaxValue, 26, 30, 35));
			FrameworkElement frameworkElement = base.Content as FrameworkElement;
			if (frameworkElement != null)
			{
				frameworkElement.RequestedTheme = ((!dark) ? ElementTheme.Light : ElementTheme.Dark);
			}
			ClearBorderColor(Ui.Handle(this));
			pill.Background = new SolidColorBrush(dark ? Color.FromArgb(220, 28, 28, 32) : Color.FromArgb(225, 224, 224, 228));
			(base.SystemBackdrop as PanelBackdrop)?.Refresh();
			wifiBase.Foreground = new SolidColorBrush(color);
			wifiBase.Opacity = (flag3 ? 0.5 : 0.0);
			Ui.SetIconGlyph(wifiIcon, (!flag3) ? "\ue701" : (num4 switch
			{
				2 => "\ue874", 
				1 => "\ue873", 
				_ => "\ue701", 
			}));
			wifiIcon.Foreground = new SolidColorBrush(color);
			wifiIcon.Opacity = 1.0;
			batteryIcon.Foreground = new SolidColorBrush(color);
			percentText.Foreground = new SolidColorBrush(color);
			Ui.SetIconGlyph(batteryIcon, flag ? BatteryGlyph(num, flag2, PowerManager.EnergySaverStatus == EnergySaverStatus.On) : "\ue83f");
			percentText.Text = (flag ? (num + "%") : "--");
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			timer.Stop();
			shadow?.Hide();
			shadow?.Dispose();
			shadow = null;
			base.AppWindow.Hide();
			Close();
		}
	}
}
