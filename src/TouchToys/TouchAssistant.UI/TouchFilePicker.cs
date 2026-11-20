using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using TouchAssistant.Native;
using TouchAssistant.Services;
using Windows.Graphics;
using Windows.System;

namespace TouchAssistant.UI;

internal sealed class TouchFilePicker : Window
{
	private sealed record Entry(string Path, bool Folder)
	{
		public string Label => (Folder ? "\ud83d\udcc1  " : "\ud83d\udcc4  ") + System.IO.Path.GetFileName(Path.TrimEnd('\\'));
	}

	private readonly TaskCompletionSource<string?> completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly ListView list = new ListView
	{
		SelectionMode = ListViewSelectionMode.Single,
		IsItemClickEnabled = true
	};

	private readonly TextBox address = new TextBox
	{
		MinHeight = 48.0,
		PlaceholderText = Localization.T("文件夹或文件的完整路径")
	};

	private readonly TextBlock status = Ui.Text("", 12.0);

	private readonly Button choose = new Button
	{
		Content = Localization.T("选择文件"),
		MinHeight = 56.0,
		IsEnabled = false
	};

	private readonly string[] extensions;

	private string directory = "";

	private bool closed;

	private bool busy;

	private readonly nint owner;

	[DllImport("user32")]
	private static extern bool EnableWindow(nint h, bool enable);

	private static nint SetWindowLong(nint h, int index, nint value)
	{
		return WindowLong.Set(h, index, value);
	}

	private TouchFilePicker(Window parent, string title, string[] extensions)
	{
		this.extensions = extensions;
		base.Title = Localization.T(title);
		Ui.SetAppIcon(this);
		base.SystemBackdrop = new MicaBackdrop();
		owner = Ui.Handle(parent);
		SetWindowLong(Ui.Handle(this), -8, owner);
		Grid grid = new Grid
		{
			Padding = new Thickness(20.0),
			RowSpacing = 12.0
		};
		for (int i = 0; i < 5; i++)
		{
			grid.RowDefinitions.Add(new RowDefinition
			{
				Height = ((i == 3) ? new GridLength(1.0, GridUnitType.Star) : GridLength.Auto)
			});
		}
		TextBlock item = Ui.Text(title, 24.0);
		grid.Children.Add(item);
		StackPanel nav = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		Shortcut("上一级", delegate
		{
			object obj;
			if (directory.Length != 0)
			{
				obj = Directory.GetParent(directory)?.FullName;
				if (obj == null)
				{
					return directory;
				}
			}
			else
			{
				obj = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
			}
			return (string)obj;
		});
		Shortcut("桌面", () => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
		Shortcut("文档", () => Environment.GetFolderPath(Environment.SpecialFolder.Personal));
		Shortcut("下载", () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"));
		Button button = Ui.Button("磁盘", delegate
		{
			if (!busy)
			{
				list.ItemsSource = (from d in DriveInfo.GetDrives()
					select new Entry(d.Name, Folder: true)).ToList();
				choose.IsEnabled = false;
			}
		});
		button.MinHeight = 48.0;
		nav.Children.Add(button);
		Grid.SetRow(nav, 1);
		grid.Children.Add(nav);
		Grid grid2 = new Grid
		{
			ColumnSpacing = 8.0
		};
		grid2.ColumnDefinitions.Add(new ColumnDefinition());
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid2.Children.Add(address);
		Button button2 = Ui.Button("前往", async delegate
		{
			await Navigate(address.Text);
		});
		button2.MinHeight = 48.0;
		Grid.SetColumn(button2, 1);
		grid2.Children.Add(button2);
		Grid.SetRow(grid2, 2);
		grid.Children.Add(grid2);
		address.KeyDown += async delegate(object _, KeyRoutedEventArgs e)
		{
			if (e.Key == VirtualKey.Enter)
			{
				await Navigate(address.Text);
			}
		};
		list.ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><TextBlock Text='{Binding Label}' FontSize='18' MinHeight='56' Padding='12,16' TextTrimming='CharacterEllipsis'/></DataTemplate>");
		list.ItemClick += async delegate(object _, ItemClickEventArgs e)
		{
			if (e.ClickedItem is Entry { Folder: not false } entry)
			{
				await Navigate(entry.Path);
			}
		};
		list.SelectionChanged += delegate
		{
			choose.IsEnabled = list.SelectedItem is Entry entry && !entry.Folder;
		};
		Grid.SetRow(list, 3);
		grid.Children.Add(list);
		Grid grid3 = new Grid
		{
			RowSpacing = 8.0,
			ColumnSpacing = 12.0
		};
		grid3.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid3.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid3.ColumnDefinitions.Add(new ColumnDefinition());
		grid3.ColumnDefinitions.Add(new ColumnDefinition());
		Grid.SetColumnSpan(status, 2);
		grid3.Children.Add(status);
		choose.Click += delegate
		{
			if (list.SelectedItem is Entry { Folder: false } entry && File.Exists(entry.Path))
			{
				completion.TrySetResult(entry.Path);
				Close();
			}
		};
		choose.MinHeight = 56.0;
		choose.IsEnabled = false;
		Grid.SetRow(choose, 1);
		grid3.Children.Add(choose);
		Button button3 = Ui.Button("取消", delegate
		{
			Close();
		});
		button3.MinHeight = 56.0;
		Grid.SetRow(button3, 1);
		Grid.SetColumn(button3, 1);
		grid3.Children.Add(button3);
		Grid.SetRow(grid3, 4);
		grid.Children.Add(grid3);
		base.Content = grid;
		Win32.GetWindowRect(owner, out var r);
		Win32.POINT p = new Win32.POINT(r.Left, r.Top);
		Win32.RECT rcWork = Win32.Monitor(p).rcWork;
		double num = Win32.Scale(p);
		int num2 = Math.Min(rcWork.Width, (int)(760.0 * num));
		int num3 = Math.Min(rcWork.Height, (int)(680.0 * num));
		base.AppWindow.MoveAndResize(new RectInt32(rcWork.Left + (rcWork.Width - num2) / 2, rcWork.Top + (rcWork.Height - num3) / 2, num2, num3));
		base.Closed += delegate
		{
			closed = true;
			EnableWindow(owner, enable: true);
			completion.TrySetResult(null);
		};
		void Shortcut(string label, Func<string> path)
		{
			Button button4 = Ui.Button(label, async delegate
			{
				await Navigate(path());
			});
			button4.MinHeight = 48.0;
			nav.Children.Add(button4);
		}
	}

	private async Task Navigate(string path)
	{
		if (closed || busy)
		{
			return;
		}
		try
		{
			path = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
			if (File.Exists(path))
			{
				if (!extensions.Contains<string>(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
				{
					status.Text = Localization.T("请选择允许的文件类型：") + string.Join("、", extensions);
					return;
				}
				completion.TrySetResult(path);
				Close();
				return;
			}
			busy = true;
			this.list.IsEnabled = false;
			choose.IsEnabled = false;
			status.Text = Localization.T("正在读取…");
			List<Entry> list = await Task.Run(() => (from p in (from p in Directory.EnumerateFileSystemEntries(path)
					where Directory.Exists(p) || extensions.Contains<string>(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase)
					select p).Take(2000)
				select new Entry(p, Directory.Exists(p)) into e
				orderby e.Folder descending
				select e).ThenBy<Entry, string>((Entry e) => e.Label, StringComparer.CurrentCultureIgnoreCase).ToList());
			if (!closed)
			{
				TouchFilePicker touchFilePicker = this;
				string text = (address.Text = path);
				touchFilePicker.directory = text;
				this.list.ItemsSource = list;
				status.Text = ((list.Count >= 2000) ? "仅显示前 2000 项，可输入完整路径定位文件。" : "单击进入文件夹；选中文件后点击“选择文件”。");
			}
		}
		catch (Exception ex)
		{
			if (!closed)
			{
				status.Text = Localization.T("无法打开：") + ex.Message;
			}
		}
		finally
		{
			busy = false;
			if (!closed)
			{
				this.list.IsEnabled = true;
			}
		}
	}

	internal static async Task CheckAsync(Window owner, string path, Func<Window, string, Task> capture)
	{
		TouchFilePicker picker = new TouchFilePicker(owner, "选择二维码图片", new string[1] { ".png" });
		EnableWindow(Ui.Handle(owner), enable: false);
		try
		{
			Ui.Present(picker);
			picker.Activate();
			await picker.Navigate(Path.GetDirectoryName(path));
			await Task.Delay(200);
			if (picker.list.Items.Count == 0 || picker.choose.MinHeight < 48.0)
			{
				throw new InvalidOperationException("触控文件列表为空或按钮过小。");
			}
			await capture(picker, "touch-picker-v0818");
			picker.list.SelectedItem = picker.list.Items.Cast<Entry>().Single((Entry e) => e.Path == path);
			if (!picker.choose.IsEnabled)
			{
				throw new InvalidOperationException("选择文件后确认按钮未启用。");
			}
			((IInvokeProvider)new ButtonAutomationPeer(picker.choose).GetPattern(PatternInterface.Invoke)).Invoke();
			if (await picker.completion.Task != path)
			{
				throw new InvalidOperationException("选图返回路径错误。");
			}
		}
		finally
		{
			if (!picker.closed)
			{
				picker.Close();
			}
		}
		TouchFilePicker touchFilePicker = new TouchFilePicker(owner, "选择应用程序", new string[1] { ".exe" });
		EnableWindow(Ui.Handle(owner), enable: false);
		touchFilePicker.Close();
		if (await touchFilePicker.completion.Task != null)
		{
			throw new InvalidOperationException("取消结果错误。");
		}
	}

	public static async Task<string?> Pick(Window owner, string title, params string[] extensions)
	{
		TouchFilePicker picker = new TouchFilePicker(owner, title, extensions);
		EnableWindow(Ui.Handle(owner), enable: false);
		try
		{
			Ui.Present(picker);
			picker.Activate();
			picker.Navigate(Environment.GetFolderPath(extensions.Contains(".exe") ? Environment.SpecialFolder.ProgramFiles : Environment.SpecialFolder.MyPictures));
			return await picker.completion.Task;
		}
		finally
		{
			if (!picker.closed)
			{
				picker.Close();
			}
		}
	}
}
