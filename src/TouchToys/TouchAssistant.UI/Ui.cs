using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using TouchAssistant.Models;
using TouchAssistant.Native;
using TouchAssistant.Services;
using WinRT.Interop;
using Windows.UI;

namespace TouchAssistant.UI;

internal static class Ui
{
	private struct FrameMargins
	{
		public int Left;

		public int Right;

		public int Top;

		public int Bottom;
	}

	private sealed class DialogOwner(nint handle) : IWin32Window
	{
		public nint Handle => handle;
	}

	private delegate bool TestEnumWindow(nint handle, nint data);

	private static readonly Dictionary<Window, DispatcherQueueTimer> temporaryRaises = new Dictionary<Window, DispatcherQueueTimer>();

	internal static readonly Dictionary<UIElement, nint> OverlayHandles = new Dictionary<UIElement, nint>();

	private static Icon? appIcon;

	public static void PresentTemporarilyTopmost(Window window)
	{
		if (!DesktopVisibility.CanShow(Handle(window)))
		{
			return;
		}
		Present(window);
		window.Activate();
		nint handle = Handle(window);
		Win32.SetWindowPos(handle, -1, 0, 0, 0, 0, 67u);
		Win32.SetForegroundWindow(handle);
		if (!temporaryRaises.TryGetValue(window, out DispatcherQueueTimer timer))
		{
			timer = window.DispatcherQueue.CreateTimer();
			timer.IsRepeating = false;
			timer.Interval = TimeSpan.FromMilliseconds(800.0);
			timer.Tick += delegate
			{
				Win32.SetWindowPos(handle, -2, 0, 0, 0, 0, 19u);
			};
			temporaryRaises.Add(window, timer);
			window.Closed += delegate
			{
				timer.Stop();
				temporaryRaises.Remove(window);
			};
		}
		timer.Stop();
		timer.Start();
	}

	public static SolidColorBrush Brush(byte r, byte g, byte b, byte a = byte.MaxValue)
	{
		return new SolidColorBrush(Windows.UI.Color.FromArgb(a, r, g, b));
	}

	public static TextBlock Text(string value, double size = 14.0)
	{
		return new TextBlock
		{
			Text = Localization.T(value),
			FontSize = size,
			TextWrapping = TextWrapping.Wrap
		};
	}

	public static TextBlock Description(string value, double size = 13.0)
	{
		TextBlock text = Text(value, size);
		text.Loaded += delegate
		{
			Theme();
		};
		text.ActualThemeChanged += delegate
		{
			Theme();
		};
		return text;
		void Theme()
		{
			text.Foreground = (Microsoft.UI.Xaml.Media.Brush)Microsoft.UI.Xaml.Application.Current.Resources["TextFillColorSecondaryBrush"];
		}
	}

	public static FontIcon Icon(string glyph, double size = 20.0)
	{
		return new FontIcon
		{
			Glyph = glyph,
			FontFamily = IconFontResolver.CreateXamlFontFamily(glyph),
			FontSize = size
		};
	}

	public static void SetIconGlyph(FontIcon icon, string glyph, string? preferredFamily = null)
	{
		icon.Glyph = glyph;
		icon.FontFamily = IconFontResolver.CreateXamlFontFamily(glyph, preferredFamily);
	}

	public static Microsoft.UI.Xaml.Controls.Button Button(string text, Action action)
	{
		Microsoft.UI.Xaml.Controls.Button button = new Microsoft.UI.Xaml.Controls.Button();
		button.Content = Localization.T(text);
		button.MinHeight = 48.0;
		button.Padding = new Thickness(18.0, 10.0, 18.0, 10.0);
		button.CornerRadius = new CornerRadius(12.0);
		button.Click += delegate
		{
			action();
		};
		return button;
	}

	public static Microsoft.UI.Xaml.Controls.Button IconButton(string glyph, string name, Action action, double size = 48.0, Action? longPress = null)
	{
		Microsoft.UI.Xaml.Controls.Button button = new Microsoft.UI.Xaml.Controls.Button
		{
			Content = Icon(glyph),
			Width = size,
			Height = size,
			CornerRadius = new CornerRadius(12.0),
			Padding = new Thickness(0.0),
			BorderThickness = new Thickness(0.0),
			Background = Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue, 230),
			Foreground = Brush(30, 36, 44)
		};
		ToolTipService.SetToolTip(button, Localization.T(name));
		AutomationProperties.SetName(button, Localization.T(name));
		bool held = false;
		button.AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)delegate
		{
			held = false;
		}, handledEventsToo: true);
		button.Click += delegate
		{
			if (!held)
			{
				action();
			}
			held = false;
		};
		if (longPress != null)
		{
			button.IsHoldingEnabled = true;
			button.AddHandler(UIElement.HoldingEvent, (HoldingEventHandler)delegate(object _, HoldingRoutedEventArgs e)
			{
				if (e.HoldingState == HoldingState.Started)
				{
					held = true;
					longPress();
				}
				e.Handled = true;
			}, handledEventsToo: true);
			button.RightTapped += delegate(object _, RightTappedRoutedEventArgs e)
			{
				held = true;
				longPress();
				e.Handled = true;
			};
		}
		return button;
	}

	public static Border Card(UIElement child)
	{
		return new Border
		{
			Child = child,
			Style = (Style)Microsoft.UI.Xaml.Application.Current.Resources["SettingsCardStyle"],
			CornerRadius = new CornerRadius(18.0),
			Padding = new Thickness(20.0),
			BorderThickness = new Thickness(1.0)
		};
	}

	public static nint Handle(Window window)
	{
		return WindowNative.GetWindowHandle(window);
	}

	[DllImport("user32")]
	private static extern bool RedrawWindow(nint hwnd, nint rect, nint region, uint flags);

	public static void Present(Window window)
	{
		if (DesktopVisibility.CanShow(Handle(window)))
		{
			window.AppWindow.Show(activateWindow: false);
			if (window.Content is FrameworkElement frameworkElement)
			{
				frameworkElement.InvalidateMeasure();
				frameworkElement.InvalidateArrange();
				frameworkElement.UpdateLayout();
			}
			RedrawWindow(Handle(window), 0, 0, 385u);
		}
	}

	public static void ConfigureOverlay(Window window, bool systemFrame = false, bool clipFrame = true)
	{
		UIElement content = window.Content;
		if ((object)content != null)
		{
			OverlayHandles[content] = Handle(window);
			window.Closed += delegate
			{
				OverlayHandles.Remove(content);
			};
		}
		SetAppIcon(window);
		window.ExtendsContentIntoTitleBar = true;
		if (window.AppWindow.Presenter is OverlappedPresenter overlappedPresenter)
		{
			overlappedPresenter.SetBorderAndTitleBar(hasBorder: false, hasTitleBar: false);
			overlappedPresenter.IsResizable = false;
			overlappedPresenter.IsMaximizable = false;
			overlappedPresenter.IsMinimizable = false;
			overlappedPresenter.IsAlwaysOnTop = true;
		}
		window.AppWindow.IsShownInSwitchers = false;
		Win32.SuppressFeedback(Handle(window));
		if (systemFrame)
		{
			SystemPanelFrame.Attach(window);
			uint value = 2u;
			uint value2 = 2u;
			uint value3 = 4294967294u;
			DwmSetWindowAttribute(Handle(window), 2u, ref value, 4u);
			DwmSetWindowAttribute(Handle(window), 33u, ref value2, 4u);
			DwmSetWindowAttribute(Handle(window), 34u, ref value3, 4u);
			FrameMargins margins = new FrameMargins
			{
				Left = -1,
				Right = -1,
				Top = -1,
				Bottom = -1
			};
			DwmExtendFrameIntoClientArea(Handle(window), ref margins);
		}
		else
		{
			if (clipFrame)
			{
				OverlayFrame.Attach(window);
			}
			RemoveOverlayBorder(window);
		}
	}

	private static void RemoveOverlayBorder(Window window)
	{
		nint num = Handle(window);
		long num2 = ((IntPtr)GetWindowLongPtr(num, -16)).ToInt64();
		long num3 = num2 & -12845057;
		if (num2 != num3)
		{
			SetWindowLongPtr(num, -16, (nint)num3);
			Win32.SetWindowPos(num, 0, 0, 0, 0, 0, 55u);
		}
		long num4 = ((IntPtr)GetWindowLongPtr(num, -20)).ToInt64();
		if ((num4 & 0x200) != 0L)
		{
			SetWindowLongPtr(num, -20, (nint)(num4 & -513));
		}
		uint value = 4294967294u;
		DwmSetWindowAttribute(num, 34u, ref value, 4u);
		uint value2 = 1u;
		DwmSetWindowAttribute(num, 2u, ref value2, 4u);
		uint value3 = 1u;
		DwmSetWindowAttribute(num, 17u, ref value3, 4u);
		FrameMargins margins = new FrameMargins
		{
			Left = -1,
			Right = -1,
			Top = -1,
			Bottom = -1
		};
		DwmExtendFrameIntoClientArea(num, ref margins);
	}

	[DllImport("dwmapi")]
	private static extern int DwmExtendFrameIntoClientArea(nint hwnd, ref FrameMargins margins);

	private static nint GetWindowLongPtr(nint hwnd, int index)
	{
		return WindowLong.Get(hwnd, index);
	}

	private static nint SetWindowLongPtr(nint hwnd, int index, nint value)
	{
		return WindowLong.Set(hwnd, index, value);
	}

	[DllImport("dwmapi")]
	private static extern int DwmSetWindowAttribute(nint hwnd, uint attribute, ref uint value, uint size);

	[DllImport("dwmapi")]
	internal static extern int DwmFlush();

	public static void SetAppIcon(Window window)
	{
		if (appIcon == null)
		{
			appIcon = new Icon(Path.Combine(AppContext.BaseDirectory, "Assets", "TouchToys.ico"), 256, 256);
		}
		window.AppWindow.SetIcon(Win32Interop.GetIconIdFromIcon(appIcon.Handle));
	}

	internal static void ReleaseAppIcon()
	{
		appIcon?.Dispose();
		appIcon = null;
	}

	internal static void ReleaseTitleBar(Window window)
	{
		window.AppWindow.Hide();
		if (window.ExtendsContentIntoTitleBar)
		{
			window.SetTitleBar(null);
			window.ExtendsContentIntoTitleBar = false;
			window.AppWindow.TitleBar.ResetToDefault();
		}
	}

	public static string? SettingsFile(Window owner, bool save)
	{
		using FileDialog fileDialog = (save ? ((FileDialog)new SaveFileDialog
		{
			AutoUpgradeEnabled = false,
			FileName = "TouchToys-settings-" + DateTime.Now.ToString("yyyyMMdd") + ".zip",
			OverwritePrompt = true
		}) : ((FileDialog)new OpenFileDialog
		{
			AutoUpgradeEnabled = false
		}));
		fileDialog.Filter = "TouchToys 设置包 (*.zip)|*.zip";
		fileDialog.RestoreDirectory = true;
		fileDialog.DefaultExt = "zip";
		return (fileDialog.ShowDialog(new DialogOwner(Handle(owner))) == DialogResult.OK) ? fileDialog.FileName : null;
	}

	public static string? BrowseImage(Window owner, string? title = null, string? filter = null)
	{
		using OpenFileDialog openFileDialog = new OpenFileDialog
		{
			Title = Localization.T(title ?? "选择图标"),
			Filter = (filter ?? (Localization.T("图片") + "|*.png;*.jpg;*.jpeg;*.bmp;*.ico")),
			CheckFileExists = true,
			AutoUpgradeEnabled = false,
			RestoreDirectory = true
		};
		return (openFileDialog.ShowDialog(new DialogOwner(Handle(owner))) == DialogResult.OK) ? openFileDialog.FileName : null;
	}

	public static string? LayoutFile(Window owner, bool save)
	{
		using FileDialog fileDialog = (save ? ((FileDialog)new SaveFileDialog
		{
			AutoUpgradeEnabled = false,
			FileName = "TouchToys-layouts-" + DateTime.Now.ToString("yyyyMMdd") + ".json",
			OverwritePrompt = true
		}) : ((FileDialog)new OpenFileDialog
		{
			AutoUpgradeEnabled = false,
			CheckFileExists = true
		}));
		fileDialog.Filter = Localization.T("TouchToys 布局 (*.json)|*.json");
		fileDialog.DefaultExt = "json";
		fileDialog.RestoreDirectory = true;
		return (fileDialog.ShowDialog(new DialogOwner(Handle(owner))) == DialogResult.OK) ? fileDialog.FileName : null;
	}

	public static string? BrowseExe(Window owner)
	{
		try
		{
			using OpenFileDialog openFileDialog = new OpenFileDialog
			{
				Title = Localization.T("选择应用"),
				Filter = "应用程序 (*.exe)|*.exe",
				CheckFileExists = true,
				Multiselect = false,
				AutoUpgradeEnabled = false
			};
			return (openFileDialog.ShowDialog(new DialogOwner(Handle(owner))) == DialogResult.OK) ? openFileDialog.FileName : null;
		}
		catch (Exception ex)
		{
			Diagnostics.Error(ex, "ApplicationPicker.BrowseExe");
			App.CurrentApp.Report(Localization.T("无法浏览 EXE：") + ex.Message);
			return null;
		}
	}

	[DllImport("user32")]
	private static extern bool EnumWindows(TestEnumWindow callback, nint data);

	[DllImport("user32")]
	private static extern nint GetDlgItem(nint handle, int id);

	[DllImport("user32")]
	private static extern nint GetWindow(nint handle, uint command);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern nint SendMessage(nint handle, uint message, nint wp, string text);

	[DllImport("user32")]
	private static extern bool PostMessage(nint handle, uint message, nint wp, nint lp);

	internal static async Task CheckExePickerAsync(Window owner)
	{
		string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "notepad.exe");
		await CheckFilePickerAsync(owner, expected, () => BrowseExe(owner));
	}

	internal static async Task CheckFilePickerAsync(Window owner, string expected, Func<string?> pick)
	{
		nint ownerHandle = Handle(owner);
		Task task = Task.Run(async delegate
		{
			for (int attempt = 0; attempt < 60; attempt++)
			{
				await Task.Delay(100);
				nint dialog = 0;
				EnumWindows(delegate(nint handle, nint _)
				{
					Win32.GetWindowThreadProcessId(handle, out var process);
					if (process == Environment.ProcessId && GetWindow(handle, 4u) == ownerHandle && GetDlgItem(handle, 1148) != 0 && GetDlgItem(handle, 1) != 0)
					{
						dialog = handle;
						return false;
					}
					return true;
				}, 0);
				if (dialog != 0)
				{
					await Task.Delay(300);
					SendMessage(GetDlgItem(dialog, 1148), 12u, 0, expected);
					PostMessage(dialog, 273u, 1, 0);
					return;
				}
			}
			throw new InvalidOperationException("EXE 文件选择器未出现或无法选择文件。");
		});
		string selected = pick();
		await task;
		if (!string.Equals(expected, selected, StringComparison.OrdinalIgnoreCase))
		{
			throw new InvalidOperationException("EXE 文件选择器没有返回所选路径：" + selected);
		}
	}

	public static async Task<LauncherEntry?> PickLauncherAsync(XamlRoot root, Window owner)
	{
		return (await PickLaunchersAsync(root, owner, multiple: false)).FirstOrDefault();
	}

	public static async Task<List<LauncherEntry>> PickLaunchersAsync(XamlRoot root, Window owner, bool multiple = true)
	{
		Microsoft.UI.Xaml.Controls.TextBox name = new Microsoft.UI.Xaml.Controls.TextBox
		{
			Header = Localization.T("显示名称"),
			PlaceholderText = Localization.T("例如：记事本"),
			MinWidth = 320.0
		};
		Microsoft.UI.Xaml.Controls.TextBox target = new Microsoft.UI.Xaml.Controls.TextBox
		{
			Header = Localization.T("EXE 路径、应用 AUMID 或 URI"),
			PlaceholderText = "C:\\Apps\\app.exe 或 Package!App",
			TextWrapping = TextWrapping.Wrap
		};
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 16.0
		};
		Microsoft.UI.Xaml.Controls.TextBox search = new Microsoft.UI.Xaml.Controls.TextBox
		{
			PlaceholderText = Localization.T("搜索已安装应用"),
			MinHeight = 40.0
		};
		Microsoft.UI.Xaml.Controls.ListView list = new Microsoft.UI.Xaml.Controls.ListView
		{
			Height = 200.0,
			SelectionMode = ((!multiple) ? ListViewSelectionMode.Single : ListViewSelectionMode.Multiple)
		};
		HashSet<LauncherItem> selected = new HashSet<LauncherItem>();
		bool filtering = false;
		ContentDialog dialog = null;
		list.ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Height='44' ColumnSpacing='12'><Grid.ColumnDefinitions><ColumnDefinition Width='36'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Image Source='{Binding Icon}' Width='30' Height='30'/><TextBlock Grid.Column='1' Text='{Binding Name}' VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/></Grid></DataTemplate>");
		TextBlock loadStatus = Text("正在读取已安装应用…", 12.0);
		stackPanel.Children.Add(search);
		stackPanel.Children.Add(list);
		stackPanel.Children.Add(loadStatus);
		List<LauncherItem> installed = new List<LauncherItem>();
		search.TextChanged += delegate
		{
			Filter();
		};
		list.ContainerContentChanging += delegate(ListViewBase sender, ContainerContentChangingEventArgs e)
		{
			if (!e.InRecycleQueue && e.Item is LauncherItem launcherItem)
			{
				launcherItem.LoadIconAsync();
			}
		};
		list.SelectionChanged += delegate(object _, SelectionChangedEventArgs e)
		{
			if (!filtering)
			{
				if (multiple)
				{
					foreach (LauncherItem removedItem in e.RemovedItems)
					{
						selected.Remove(removedItem);
					}
					foreach (LauncherItem addedItem in e.AddedItems)
					{
						selected.Add(addedItem);
					}
					loadStatus.Text = Localization.T("已选 {0} 个应用 · 可继续搜索选择", selected.Count);
					UpdateAdd();
				}
				else if (list.SelectedItem is LauncherItem launcherItem)
				{
					name.Text = launcherItem.Name;
					target.Text = launcherItem.Target;
				}
			}
		};
		stackPanel.Children.Add(name);
		stackPanel.Children.Add(target);
		stackPanel.Children.Add(Button("浏览 EXE…", async delegate
		{
			string text = await TouchFilePicker.Pick(owner, "选择应用程序", ".exe");
			if (text != null)
			{
				target.Text = text;
				if (string.IsNullOrWhiteSpace(name.Text))
				{
					name.Text = Path.GetFileNameWithoutExtension(text);
				}
			}
		}));
		stackPanel.Children.Add(Text("UWP 应用可填 shell:AppsFolder\\PackageFamilyName!App，或直接填写 AUMID。", 12.0));
		dialog = new ContentDialog
		{
			XamlRoot = root,
			Title = Localization.T(multiple ? "选择应用（可多选）" : "添加快捷启动"),
			Content = stackPanel,
			PrimaryButtonText = Localization.T("添加"),
			CloseButtonText = Localization.T("取消"),
			DefaultButton = ContentDialogButton.Primary,
			IsPrimaryButtonEnabled = false
		};
		target.TextChanged += delegate
		{
			UpdateAdd();
		};
		LoadInstalled();
		if (await dialog.ShowAsync() != ContentDialogResult.Primary)
		{
			return new List<LauncherEntry>();
		}
		List<LauncherEntry> list2 = (from x in installed.Where(selected.Contains)
			select x.Entry).ToList();
		if (!string.IsNullOrWhiteSpace(target.Text))
		{
			list2.Add(new LauncherEntry
			{
				Name = (string.IsNullOrWhiteSpace(name.Text) ? Path.GetFileNameWithoutExtension(target.Text.Trim()) : name.Text.Trim()),
				Target = target.Text.Trim()
			});
		}
		return (from g in list2.GroupBy<LauncherEntry, string>((LauncherEntry x) => x.Target, StringComparer.OrdinalIgnoreCase)
			select g.First()).ToList();
		void Filter()
		{
			List<LauncherItem> list3 = installed.Where((LauncherItem x) => x.Name.Contains(search.Text, StringComparison.CurrentCultureIgnoreCase) || x.Target.Contains(search.Text, StringComparison.OrdinalIgnoreCase)).ToList();
			filtering = true;
			try
			{
				list.ItemsSource = list3;
				if (multiple)
				{
					foreach (LauncherItem item in list3.Where(selected.Contains))
					{
						list.SelectedItems.Add(item);
					}
				}
			}
			finally
			{
				filtering = false;
			}
			loadStatus.Text = Localization.T("{0} 个应用 · 已选 {1} 个", list3.Count, selected.Count);
			UpdateAdd();
		}
		async Task LoadInstalled()
		{
			try
			{
				installed = (await InstalledApps.LoadAsync()).Select((LauncherEntry x) => new LauncherItem(x)).ToList();
				Filter();
			}
			catch
			{
				loadStatus.Text = Localization.T("应用列表暂不可用，请使用下方路径添加。");
			}
		}
		void UpdateAdd()
		{
			if (dialog != null)
			{
				int num = selected.Count + ((!string.IsNullOrWhiteSpace(target.Text)) ? 1 : 0);
				dialog.IsPrimaryButtonEnabled = num > 0;
				dialog.PrimaryButtonText = ((multiple && num > 0) ? Localization.T("添加（{0}）", num) : Localization.T("添加"));
			}
		}
	}
}
