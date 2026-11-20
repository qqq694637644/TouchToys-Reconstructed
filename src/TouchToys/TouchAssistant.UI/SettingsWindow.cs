using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TouchAssistant.Models;
using TouchAssistant.Native;
using TouchAssistant.Services;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace TouchAssistant.UI;

internal sealed class SettingsWindow : Window
{
	private readonly SettingsStore store;

	private DispatcherQueueTimer? pendingSave;

	private bool savePending;

	private readonly Action reset;

	private readonly StackPanel gamepadPage = new StackPanel
	{
		Spacing = 16.0,
		Padding = new Thickness(28.0, 0.0, 28.0, 28.0)
	};

	private ScrollViewer? gamepadScroll;

	private Border? gamepadSticky;

	private TextBox? gamepadStickyName;

	private StackPanel? gamepadStickyActions;

	private TextBox? gamepadLayoutName;

	private ComboBox? gamepadLayoutPicker;

	private bool syncingGamepadName;

	private readonly StackPanel basic = new StackPanel
	{
		Spacing = 16.0,
		Padding = new Thickness(28.0, 0.0, 28.0, 28.0)
	};

	private readonly StackPanel floating = new StackPanel
	{
		Spacing = 16.0,
		Padding = new Thickness(28.0, 0.0, 28.0, 28.0)
	};

	private readonly Action exit;

	private bool documentOpen;

	private bool closed;

	private bool syncingSwitches;

	private bool diagnosticTesting;

	private string? diagnosticExport;

	private Exception? diagnosticError;

	private readonly List<(WeakReference<ToggleSwitch> Control, Func<bool> Read)> switchBindings = new List<(WeakReference<ToggleSwitch>, Func<bool>)>();

	private readonly StackPanel gestures = new StackPanel
	{
		Spacing = 16.0,
		Padding = new Thickness(28.0, 0.0, 28.0, 28.0)
	};

	private readonly StackPanel multiTouch = new StackPanel
	{
		Spacing = 16.0,
		Padding = new Thickness(28.0, 0.0, 28.0, 28.0)
	};

	private readonly StackPanel edges = new StackPanel
	{
		Spacing = 16.0,
		Padding = new Thickness(28.0, 0.0, 28.0, 28.0)
	};

	private readonly StackPanel ark = new StackPanel
	{
		Spacing = 16.0,
		Padding = new Thickness(28.0, 0.0, 28.0, 28.0)
	};

	private readonly StackPanel dialStyle = new StackPanel
	{
		Spacing = 16.0
	};

	private readonly StackPanel dialTools = new StackPanel
	{
		Spacing = 16.0
	};

	private int arkProfileIndex;

	private DialTool? revealDialTool;

	private readonly SettingsTabs bubbleTabs = new SettingsTabs();

	private readonly LinkedList<SettingsTab> bubbleCache = new LinkedList<SettingsTab>();

	private readonly NavigationView navigation = new NavigationView
	{
		PaneDisplayMode = NavigationViewPaneDisplayMode.Left,
		IsSettingsVisible = false,
		IsBackButtonVisible = NavigationViewBackButtonVisible.Collapsed,
		OpenPaneLength = 200.0
	};

	private static readonly (string Id, string Name)[] BuiltIns = new(string, string)[21]
	{
		("Gamepad", "切换摇杆和按钮"),
		("StartMenu", "开始菜单"),
		("TaskView", "任务视图"),
		("NextApp", "切换下一个应用 · Alt + Tab"),
		("Desktop", "显示桌面"),
		("PreviousApp", "切换到上一个应用 · Alt + Shift + Tab"),
		("ControlCenter", "打开控制中心"),
		("TouchKeyboard", "切换触摸键盘"),
		("Touchpad", "切换 TT触摸板"),
		("SystemTouchpad", "切换系统原生触摸板"),
		("Wheel", "切换环形菜单"),
		("Move", "拖动模式"),
		("Stage", "侧边任务列表"),
		("Overview", "全面屏手势 · 后台任务列表"),
		("NotificationCenter", "通知中心"),
		("VirtualDial", "开关虚拟 Dial · 屏幕旋钮"),
		("ArkToggle", "开关快捷方舟"),
		("ArkMenu", "展开 / 收起随行气泡"),
		("ArkMove", "移动快捷方舟"),
		("BallToggle", "开关触控圆点"),
		("AppSettings", "应用设置")
	};

	private bool preparedClose;

	private static readonly HashSet<int> FullKeyboardCodes = BuildKeyboardCodes();

	private int dialAppIndex;

	private int bottomAppIndex;

	private readonly ContentControl dialProfileHost = new ContentControl();

	private readonly ContentControl gestureProfileHost = new ContentControl();

	private readonly ContentControl bubbleProfileHost = new ContentControl();

	private GamepadControl? selectedGamepadControl;

	private bool bubbleEventsWired;

	private bool rebuildingBubbleTabs;

	private readonly ConditionalWeakTable<MultiTouchSettings, Dictionary<MultiTouchBinding, bool>> gestureExpansion = new ConditionalWeakTable<MultiTouchSettings, Dictionary<MultiTouchBinding, bool>>();

	private int gestureEditorGeneration;

	private bool gestureRebuildQueued;

	private MultiTouchSettings EditedGestures
	{
		get
		{
			if (bottomAppIndex <= 0 || bottomAppIndex > store.Value.GestureApps.Count)
			{
				return store.Value.MultiTouch;
			}
			return store.Value.GestureApps[bottomAppIndex - 1].Options;
		}
	}

	private DialSettings EditedDial
	{
		get
		{
			if (dialAppIndex <= 0 || dialAppIndex > store.Value.DialApps.Count)
			{
				return store.Value.Dial;
			}
			return store.Value.DialApps[dialAppIndex - 1].Options;
		}
	}

	private BottomAppProfile? EditedBottom
	{
		get
		{
			if (bottomAppIndex <= 0 || bottomAppIndex > store.Value.BottomApps.Count)
			{
				return null;
			}
			return store.Value.BottomApps[bottomAppIndex - 1];
		}
	}

	private object? CurrentPage
	{
		get
		{
			UIElement uIElement = (navigation.Content as Grid)?.Children.LastOrDefault();
			if (!(uIElement is Grid grid) || uIElement is SettingsTabs)
			{
				return uIElement;
			}
			return grid.Children.LastOrDefault();
		}
	}

	public event Action<ArkProfile>? PreviewBubbles;

	private void ScheduleSave()
	{
		if (closed)
		{
			return;
		}
		savePending = true;
		if (pendingSave == null)
		{
			pendingSave = base.DispatcherQueue.CreateTimer();
			pendingSave.Interval = TimeSpan.FromMilliseconds(120.0);
			pendingSave.IsRepeating = false;
			pendingSave.Tick += delegate
			{
				FlushPendingSave();
			};
		}
		if (!pendingSave.IsRunning)
		{
			pendingSave.Start();
		}
	}

	private void FlushPendingSave()
	{
		pendingSave?.Stop();
		if (savePending)
		{
			store.Save();
			savePending = false;
		}
	}

	private static HashSet<int> BuildKeyboardCodes()
	{
		HashSet<int> hashSet = new HashSet<int>
		{
			27, 8, 9, 20, 13, 32, 16, 17, 18, 91,
			93, 192, 189, 187, 219, 221, 220, 186, 222, 188,
			190, 191, 44, 145, 19, 45, 36, 33, 46, 35,
			34, 38, 40, 37, 39, 144, 111, 106, 109, 107,
			110
		};
		for (int i = 48; i <= 57; i++)
		{
			hashSet.Add(i);
		}
		for (int j = 65; j <= 90; j++)
		{
			hashSet.Add(j);
		}
		for (int k = 96; k <= 105; k++)
		{
			hashSet.Add(k);
		}
		for (int l = 112; l <= 123; l++)
		{
			hashSet.Add(l);
		}
		return hashSet;
	}

	private void PrepareClose()
	{
		if (!preparedClose)
		{
			FlushPendingSave();
			preparedClose = true;
			if (!Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--smoke-test") || !Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--resource-baseline"))
			{
				Ui.ReleaseTitleBar(this);
			}
		}
	}

	public new void Close()
	{
		PrepareClose();
		base.Close();
	}

	private void WindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
	{
		PrepareClose();
	}

	private void WindowActivated(object sender, WindowActivatedEventArgs args)
	{
		SyncSwitches();
	}

	[DllImport("user32", EntryPoint = "PostMessageW", SetLastError = true)]
	private static extern bool PostCloseMessage(nint window, uint message, nuint wParam, nint lParam);

	internal async Task CheckSystemCloseAsync()
	{
		if (!PostCloseMessage(Ui.Handle(this), 16u, 0u, 0))
		{
			throw new InvalidOperationException("无法发送设置窗口关闭消息");
		}
		for (int i = 0; i < 100; i++)
		{
			if (closed)
			{
				break;
			}
			await Task.Delay(20);
		}
		if (!closed || !preparedClose)
		{
			throw new InvalidOperationException("系统关闭未释放设置窗口标题栏");
		}
	}

	public SettingsWindow(SettingsStore settings, Action resetPosition, Action exitApplication)
	{
		SettingsWindow settingsWindow = this;
		store = settings;
		reset = resetPosition;
		exit = exitApplication;
		base.Title = Localization.T("TouchToys · 设置");
		Ui.SetAppIcon(this);
		base.AppWindow.Closing += WindowClosing;
		base.SystemBackdrop = new MicaBackdrop();
		Win32.GetCursorPos(out var p);
		Win32.RECT rcWork = Win32.Monitor(p).rcWork;
		double num = Win32.Scale(p);
		int num2 = Math.Min((int)(1200.0 * num), rcWork.Width - 48);
		int num3 = Math.Min((int)(900.0 * num), rcWork.Height - 48);
		base.AppWindow.MoveAndResize(new RectInt32(rcWork.Left + (rcWork.Width - num2) / 2, rcWork.Top + (rcWork.Height - num3) / 2, num2, num3));
		SettingsTabs dotTabs = new SettingsTabs
		{
			Margin = new Thickness(28.0, 0.0, 28.0, 0.0)
		};
		floating.Padding = (gestures.Padding = new Thickness(0.0));
		dotTabs.Items.Add(new SettingsTab
		{
			Header = Localization.T("圆点设置"),
			Content = new ScrollViewer
			{
				Content = floating
			}
		});
		dotTabs.Items.Add(new SettingsTab
		{
			Header = Localization.T("环形菜单设置"),
			Content = new ScrollViewer
			{
				Content = gestures
			}
		});
		SettingsTabs arkTabs = new SettingsTabs
		{
			Margin = new Thickness(28.0, 0.0, 28.0, 0.0)
		};
		ark.Padding = new Thickness(0.0);
		arkTabs.Items.Add(new SettingsTab
		{
			Header = Localization.T("方舟设置"),
			Content = new ScrollViewer
			{
				Content = ark
			}
		});
		arkTabs.Items.Add(new SettingsTab
		{
			Header = Localization.T("随行气泡设置"),
			Content = BubblePage()
		});
		SettingsTabs dialTabs = new SettingsTabs
		{
			Margin = new Thickness(28.0, 0.0, 28.0, 0.0)
		};
		dialTabs.Items.Add(new SettingsTab
		{
			Header = Localization.T("样式设置"),
			Content = new ScrollViewer
			{
				Content = dialStyle
			}
		});
		dialTabs.Items.Add(new SettingsTab
		{
			Header = Localization.T("功能设置"),
			Content = new ScrollViewer
			{
				Content = dialTools
			}
		});
		SettingsTabs gestureTabs = new SettingsTabs
		{
			Margin = new Thickness(28.0, 0.0, 28.0, 0.0)
		};
		multiTouch.Padding = (edges.Padding = new Thickness(0.0));
		gestureTabs.Items.Add(new SettingsTab
		{
			Header = Localization.T("手势设置"),
			Content = WithProfile(gestureProfileHost, new ScrollViewer
			{
				Content = multiTouch
			})
		});
		gestureTabs.Items.Add(new SettingsTab
		{
			Header = Localization.T("屏幕边缘手势"),
			Content = new ScrollViewer
			{
				Content = edges
			}
		});
		SettingsTabs inputPage = new SettingsTabs
		{
			Margin = new Thickness(28.0, 0.0, 28.0, 0.0)
		};
		SystemInputPage keyboardPage = null;
		FrameworkElement[] pages = new FrameworkElement[9]
		{
			new ScrollViewer
			{
				Content = basic
			},
			dotTabs,
			WithProfile(bubbleProfileHost, arkTabs),
			WithProfile(dialProfileHost, dialTabs),
			gestureTabs,
			new ScrollViewer
			{
				Content = inputPage
			},
			new Grid(),
			UpdatesPage(),
			BuildGamepadPage()
		};
		string[] array = new string[9] { "基础设置", "触控圆点", "快捷方舟", "虚拟 Dial", "手势设置", "键盘和鼠标", "使用说明", "更新", "虚拟摇杆和按钮" };
		string[] array2 = new string[9] { "\ue713", "\uea3a", "\ueca5", "\ue958", "\ue8ab", "\ue765", "\ue8a5", "\ue81c", "\uece9" };
		int[] array3 = new int[8] { 0, 1, 2, 3, 4, 8, 5, 7 };
		foreach (int num4 in array3)
		{
			navigation.MenuItems.Add(new NavigationViewItem
			{
				Content = Localization.T(array[num4]),
				Icon = Ui.Icon(array2[num4]),
				Tag = num4
			});
		}
		for (int j = 0; j < pages.Length; j++)
		{
			Grid grid = new Grid
			{
				RowDefinitions = 
				{
					new RowDefinition
					{
						Height = GridLength.Auto
					},
					new RowDefinition()
				}
			};
			TextBlock textBlock = Ui.Text(array[j], 28.0);
			textBlock.Margin = new Thickness(28.0, 18.0, 28.0, 16.0);
			grid.Children.Add(textBlock);
			Grid.SetRow(pages[j], 1);
			grid.Children.Add(pages[j]);
			pages[j] = grid;
		}
		TypedEventHandler<NavigationView, NavigationViewSelectionChangedEventArgs> selectPage = delegate(NavigationView _, NavigationViewSelectionChangedEventArgs e)
		{
			if (e.SelectedItem is NavigationViewItem { Tag: var tag } && tag is int num5)
			{
				if (num5 == 1 && settingsWindow.floating.Children.Count == 0)
				{
					settingsWindow.BuildFloating();
					settingsWindow.BuildGestures();
				}
				if (num5 == 4 && settingsWindow.edges.Children.Count == 0)
				{
					settingsWindow.BuildEdges();
					settingsWindow.BuildMultiTouch();
					settingsWindow.gestureProfileHost.Margin = new Thickness(0.0, 0.0, 0.0, 16.0);
				}
				if (num5 == 5)
				{
					BuildInput();
				}
				if (num5 == 8 && settingsWindow.gamepadPage.Children.Count == 0)
				{
					settingsWindow.BuildGamepad();
				}
				if (num5 == 2 && settingsWindow.ark.Children.Count == 0)
				{
					settingsWindow.BuildArk();
				}
				if (num5 == 3 && settingsWindow.dialStyle.Children.Count == 0)
				{
					settingsWindow.BuildDialStyle();
					settingsWindow.BuildDialTools();
				}
				settingsWindow.navigation.Header = null;
				settingsWindow.navigation.Content = pages[num5];
				Diagnostics.Event("Settings", "打开页面 " + num5);
			}
		};
		navigation.SelectionChanged += selectPage;
		Grid shell = new Grid();
		shell.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(32.0)
		});
		shell.RowDefinitions.Add(new RowDefinition());
		Grid grid2 = new Grid
		{
			Background = Ui.Brush(0, 0, 0, 0)
		};
		TextBlock textBlock2 = Ui.Text("TouchToys · 设置", 12.0);
		textBlock2.Margin = new Thickness(42.0, 0.0, 140.0, 0.0);
		Image item = new Image
		{
			Source = new BitmapImage(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "TouchToys.png"))),
			Width = 18.0,
			Height = 18.0,
			HorizontalAlignment = HorizontalAlignment.Left,
			Margin = new Thickness(16.0, 0.0, 0.0, 0.0)
		};
		grid2.Children.Add(item);
		textBlock2.VerticalAlignment = VerticalAlignment.Center;
		grid2.Children.Add(textBlock2);
		shell.Children.Add(grid2);
		Grid.SetRow(navigation, 1);
		shell.Children.Add(navigation);
		base.Content = shell;
		base.ExtendsContentIntoTitleBar = true;
		SetTitleBar(grid2);
		shell.ActualThemeChanged += ShellThemeChanged;
		ThemeTitle();
		navigation.SelectedItem = navigation.MenuItems[0];
		BuildBasic();
		arkTabs.SelectionChanged += delegate(object? _, SettingsTabChangedEventArgs e)
		{
			if (!settingsWindow.rebuildingBubbleTabs && e.OriginalSource == arkTabs && arkTabs.SelectedIndex == 1 && settingsWindow.bubbleTabs.Items.Count == 0)
			{
				settingsWindow.BuildBubbleTabs();
			}
		};
		store.Changed += SettingsSaved;
		base.Activated += WindowActivated;
		base.Closed += WindowClosed;
		void BuildInput()
		{
			if (!(keyboardPage != null))
			{
				keyboardPage = new SystemInputPage(App.CurrentApp.InputControl, settingsWindow.store)
				{
					Padding = new Thickness(0.0, 0.0, 0.0, 28.0)
				};
				inputPage.Items.Add(new SettingsTab
				{
					Header = Localization.T("触摸键盘 · 实验性"),
					Content = new ScrollViewer
					{
						Content = keyboardPage
					}
				});
				StackPanel stackPanel = new StackPanel
				{
					Spacing = 16.0,
					Padding = new Thickness(0.0, 0.0, 0.0, 28.0)
				};
				VirtualTouchpadSettings options = settingsWindow.store.Value.VirtualTouchpad;
				stackPanel.Children.Add(settingsWindow.SettingToggle("显示TT触摸板", () => options.Enabled, delegate(bool v)
				{
					options.Enabled = v;
				}));
				stackPanel.Children.Add(settingsWindow.SettingToggle("晃动关闭", () => options.ShakeToClose, delegate(bool v)
				{
					options.ShakeToClose = v;
				}));
				stackPanel.Children.Add(Ui.Description("拖动右下角可改变大小；按住顶部横条快速来回晃动可关闭。顶部横条移动位置；单指移动和点击，双指点击右键、双指滑动滚动，三指点击中键。一指保持、另一指滑动可左键拖动；两指保持、第三指在中间上下滑动控制滚轮。左右按键区可以按住配合拖动；放在侧边时，上方为左键、下方为右键。", 14.0));
				settingsWindow.AddSlider("触摸板不透明度", options.Opacity * 100.0, 100.0, 1.0, "%", delegate(double v)
				{
					options.Opacity = v / 100.0;
				}, 0.0, stackPanel);
				settingsWindow.AddSlider("闲置不透明度", options.IdleOpacity * 100.0, 100.0, 1.0, "%", delegate(double v)
				{
					options.IdleOpacity = v / 100.0;
				}, 0.0, stackPanel);
				settingsWindow.AddSlider("宽度", options.Width, 900.0, 1.0, "DIP", delegate(double v)
				{
					options.Width = v;
				}, 180.0, stackPanel);
				settingsWindow.AddSlider("高度", options.Height, 700.0, 1.0, "DIP", delegate(double v)
				{
					options.Height = v;
				}, 140.0, stackPanel);
				string[] placements = new string[4] { "Bottom", "Top", "Left", "Right" };
				ComboBox buttonPlacement = new ComboBox
				{
					Header = Localization.T("左右按键位置"),
					ItemsSource = new string[4] { "底部 · 左右排列", "顶部 · 左右排列", "左侧 · 上下排列", "右侧 · 上下排列" }.Select(Localization.T).ToArray(),
					SelectedIndex = Array.IndexOf(placements, options.EffectiveButtonPlacement),
					HorizontalAlignment = HorizontalAlignment.Stretch
				};
				buttonPlacement.SelectionChanged += delegate
				{
					if (buttonPlacement.SelectedIndex >= 0)
					{
						options.ButtonPlacement = placements[buttonPlacement.SelectedIndex];
						options.ButtonsOnTop = options.ButtonPlacement == "Top";
						settingsWindow.store.Save();
					}
				};
				stackPanel.Children.Insert(3, buttonPlacement);
				settingsWindow.BuildTouchpadGestures(stackPanel, options);
				inputPage.Items.Add(new SettingsTab
				{
					Header = Localization.T("TT触摸板"),
					Content = new ScrollViewer
					{
						Content = stackPanel
					}
				});
			}
		}
		void ShellThemeChanged(FrameworkElement sender, object args)
		{
			ThemeTitle();
		}
		void ThemeTitle()
		{
			bool flag = shell.ActualTheme == ElementTheme.Dark;
			settingsWindow.AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
			settingsWindow.AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
			settingsWindow.AppWindow.TitleBar.ButtonForegroundColor = (flag ? Colors.White : Colors.Black);
			settingsWindow.AppWindow.TitleBar.ButtonInactiveForegroundColor = (flag ? Colors.LightGray : Colors.DimGray);
		}
		void WindowClosed(object sender, WindowEventArgs args)
		{
			settingsWindow.FlushPendingSave();
			settingsWindow.pendingSave?.Stop();
			settingsWindow.Closed -= WindowClosed;
			settingsWindow.Activated -= settingsWindow.WindowActivated;
			settingsWindow.AppWindow.Closing -= settingsWindow.WindowClosing;
			shell.ActualThemeChanged -= ShellThemeChanged;
			settingsWindow.closed = true;
			keyboardPage?.Dispose();
			settingsWindow.store.Changed -= settingsWindow.SettingsSaved;
			settingsWindow.switchBindings.Clear();
			settingsWindow.navigation.SelectionChanged -= selectPage;
			settingsWindow.navigation.Content = null;
			settingsWindow.navigation.MenuItems.Clear();
			SettingsTabs[] array4 = new SettingsTabs[5] { dotTabs, arkTabs, dialTabs, gestureTabs, inputPage };
			for (int k = 0; k < array4.Length; k++)
			{
				array4[k].Items.Clear();
			}
			FrameworkElement[] array5 = pages;
			for (int k = 0; k < array5.Length; k++)
			{
				if (array5[k] is Grid grid3)
				{
					grid3.Children.Clear();
				}
			}
			Array.Clear(pages);
			settingsWindow.SystemBackdrop = null;
			StackPanel[] array6 = new StackPanel[9] { settingsWindow.basic, settingsWindow.floating, settingsWindow.gestures, settingsWindow.multiTouch, settingsWindow.edges, settingsWindow.ark, settingsWindow.dialStyle, settingsWindow.dialTools, settingsWindow.gamepadPage };
			for (int k = 0; k < array6.Length; k++)
			{
				array6[k].Children.Clear();
			}
			settingsWindow.bubbleTabs.Items.Clear();
			settingsWindow.bubbleCache.Clear();
			ContentControl contentControl = settingsWindow.dialProfileHost;
			ContentControl contentControl2 = settingsWindow.gestureProfileHost;
			object obj = (settingsWindow.bubbleProfileHost.Content = null);
			object content = (contentControl2.Content = obj);
			contentControl.Content = content;
			settingsWindow.Content = null;
		}
	}

	private void BuildBasic()
	{
		basic.Children.Clear();
		basic.Children.Add(Ui.Text("触控，自然一点。", 30.0));
		basic.Children.Add(Ui.Description("每次更改都会自动保存。"));
		ComboBox language = new ComboBox
		{
			Header = Localization.T("语言"),
			ItemsSource = Localization.Names,
			SelectedIndex = Math.Max(0, Array.IndexOf(Localization.Codes, store.Value.Language)),
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		language.SelectionChanged += delegate
		{
			if (language.SelectedIndex >= 0)
			{
				store.Value.Language = Localization.Codes[language.SelectedIndex];
				Localization.Use(store.Value.Language);
				store.Save();
				App.CurrentApp.ReopenSettings();
			}
		};
		basic.Children.Add(language);
		StackPanel purchasePanel = new StackPanel
		{
			Spacing = 8.0
		};
		TextBlock purchaseStatus = Ui.Description(StoreEntitlements.Status);
		purchasePanel.Children.Add(purchaseStatus);
		purchasePanel.Children.Add(Ui.Button("解锁应用绑定", async delegate
		{
			await StoreEntitlements.UnlockAsync(this);
			RefreshPurchase();
		}));
		purchasePanel.Children.Add(Ui.Button("恢复购买", async delegate
		{
			await StoreEntitlements.RefreshAsync(this);
			RefreshPurchase();
		}));
		purchasePanel.Loaded += delegate
		{
			RefreshPurchase();
			StoreEntitlements.Changed += RefreshPurchase;
		};
		purchasePanel.Unloaded += delegate
		{
			StoreEntitlements.Changed -= RefreshPurchase;
		};
		RefreshPurchase();
		basic.Children.Add(purchasePanel);
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 8.0
		};
		stackPanel.Children.Add(Ui.Text("设置导入与导出", 18.0));
		stackPanel.Children.Add(Ui.Description("包含所有应用内设置、功能绑定、快捷启动和自定义图标。不会复制应用程序或系统开机启动注册；另一台电脑上的应用路径可能需要重新选择。导入前自动备份当前设置。", 12.0));
		TextBlock transferStatus = Ui.Text("", 12.0);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		stackPanel2.Children.Add(Ui.Button("导出设置…", delegate
		{
			try
			{
				string text = Ui.SettingsFile(this, save: true);
				if (text != null)
				{
					SettingsTransfer.Export(store.Value, text);
					transferStatus.Text = Localization.T("已导出：") + text;
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Error(ex, "Settings.Export");
				transferStatus.Text = Localization.T("导出失败：") + ex.Message;
			}
		}));
		stackPanel2.Children.Add(Ui.Button("导入设置…", delegate
		{
			try
			{
				string text = Ui.SettingsFile(this, save: false);
				if (text != null)
				{
					Settings settings = SettingsTransfer.Read(text, store.StorageDirectory);
					store.Import(settings);
					BuildBasic();
					BuildFloating();
					BuildGestures();
					BuildEdges();
					BuildMultiTouch();
					BuildArk();
					BuildDialStyle();
					BuildDialTools();
					BuildGamepad();
					App.CurrentApp.Report("设置已导入，原设置已备份。");
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Error(ex, "Settings.Import");
				transferStatus.Text = Localization.T("导入失败：") + ex.Message;
			}
		}));
		stackPanel.Children.Add(stackPanel2);
		stackPanel.Children.Add(transferStatus);
		basic.Children.Add(Ui.Button("退出应用", exit));
		ToggleSwitch startup = new ToggleSwitch
		{
			Header = Localization.T("开机自启动"),
			IsOn = StartupRegistration.Enabled
		};
		BindSwitch(startup, () => StartupRegistration.Enabled, async delegate(bool v)
		{
			try
			{
				await StartupRegistration.SetEnabledAsync(v);
			}
			catch (Exception ex)
			{
				App.CurrentApp.Report(ex.Message);
				startup.IsOn = StartupRegistration.Enabled;
			}
		});
		basic.Children.Add(Ui.Card(startup));
		ToggleSwitch toggleSwitch = new ToggleSwitch
		{
			Header = Localization.T("最大化窗口右上角防误触"),
			IsOn = store.Value.CloseGuardEnabled,
			OnContent = Localization.T("仅屏蔽关闭按钮的触摸操作"),
			OffContent = Localization.T("关闭")
		};
		BindSwitch(toggleSwitch, () => store.Value.CloseGuardEnabled, delegate(bool v)
		{
			store.Value.CloseGuardEnabled = v;
		});
		basic.Children.Add(Ui.Card(toggleSwitch));
		ToggleSwitch toggleSwitch2 = new ToggleSwitch
		{
			Header = Localization.T("任务栏隐藏时显示 Wi-Fi 和电量"),
			IsOn = store.Value.ShowSystemStatusBadge
		};
		BindSwitch(toggleSwitch2, () => store.Value.ShowSystemStatusBadge, delegate(bool v)
		{
			store.Value.ShowSystemStatusBadge = v;
		});
		basic.Children.Add(Ui.Card(toggleSwitch2));
		basic.Children.Add(Ui.Description("任务栏自动隐藏时，呼出控制中心或侧边任务列表会在右下角显示 Wi-Fi 和电量。", 12.0));
		Settings s = store.Value;
		AddSlider("控制中心缩放", s.ControlCenterScale * 100.0, 300.0, 5.0, "%", delegate(double v)
		{
			s.ControlCenterScale = v / 100.0;
		}, 50.0, basic);
		basic.Children.Add(Ui.Description("控制中心会等比例缩小以适应屏幕可用空间；缩放设置在下次打开时生效。", 12.0));
		AddSlider("快捷启动应用间距", s.LauncherSpacing, 20.0, 1.0, "DIP", delegate(double v)
		{
			s.LauncherSpacing = v;
		}, 0.0, basic);
		ToggleSwitch accent = new ToggleSwitch
		{
			Header = Localization.T("高亮颜色跟随 Windows 主题色"),
			IsOn = s.UseSystemAccent
		};
		BindSwitch(accent, () => store.Value.UseSystemAccent, delegate(bool v)
		{
			store.Value.UseSystemAccent = v;
		});
		basic.Children.Add(Ui.Card(accent));
		basic.Children.Add(ColorEditor("自定义高亮颜色", s.AccentColor, delegate(string c)
		{
			s.AccentColor = c;
			s.UseSystemAccent = false;
			accent.IsOn = false;
			store.Save();
		}));
		basic.Children.Add(Ui.Button("以管理员权限重新启动…", delegate
		{
			App.CurrentApp.RestartElevated();
		}));
		basic.Children.Add(Ui.Description("仅在需要向管理员窗口发送按键时使用；Windows 会请求权限。取消后保持当前运行状态。", 12.0));
		basic.Children.Add(Ui.Card(stackPanel));
		StackPanel stackPanel3 = new StackPanel
		{
			Spacing = 10.0
		};
		stackPanel3.Children.Add(Ui.Text("诊断与反馈", 18.0));
		stackPanel3.Children.Add(Ui.Description("记录整个应用的运行事件、异常和设备状态。遇到问题时，导出诊断包并附上操作步骤。日志自动轮换保存，不会自动上传。", 12.0));
		TextBox description = new TextBox
		{
			Header = Localization.T("问题描述（可选）"),
			AcceptsReturn = true,
			TextWrapping = TextWrapping.Wrap,
			MaxLength = 2000,
			PlaceholderText = Localization.T("发生时间、正在使用的应用、手指数及操作步骤")
		};
		stackPanel3.Children.Add(description);
		TextBlock diagnosticStatus = Ui.Text("", 12.0);
		diagnosticStatus.IsTextSelectionEnabled = true;
		stackPanel3.Children.Add(diagnosticStatus);
		stackPanel3.Children.Add(Ui.Button("查看日志", delegate
		{
			try
			{
				Directory.CreateDirectory(Diagnostics.DirectoryPath);
				SystemControl.Launch(Diagnostics.DirectoryPath);
			}
			catch (Exception ex)
			{
				diagnosticStatus.Text = ex.Message;
			}
		}));
		stackPanel3.Children.Add(Ui.Button("导出诊断包", async delegate
		{
			try
			{
				Settings snapshot = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(store.Value));
				string currentStatus = App.CurrentApp.DiagnosticStatus;
				string note = description.Text;
				string text = await Task.Run(() => Diagnostics.ExportDefault(snapshot, currentStatus, note));
				diagnosticStatus.Text = Localization.T("已导出：") + text + "。检查后作为附件反馈即可。";
				diagnosticExport = text;
				if (!diagnosticTesting)
				{
					try
					{
						SystemControl.Launch(Path.GetDirectoryName(text));
						return;
					}
					catch (Exception error)
					{
						Diagnostics.Error(error, "Diagnostics.OpenFolder");
						diagnosticStatus.Text += " 文件夹未能自动打开，可复制上方路径。";
						return;
					}
				}
			}
			catch (Exception ex)
			{
				diagnosticError = ex;
				Diagnostics.Error(ex, "Diagnostics.Export");
				diagnosticStatus.Text = Localization.T("导出失败：") + ex.Message;
			}
		}));
		basic.Children.Add(Ui.Card(stackPanel3));
		basic.Children.Add(Ui.Text("TouchToys V0.8.38 · 夜冷星灼"));
		basic.Children.Add(new HyperlinkButton
		{
			Content = Localization.T("xingzhuo1989@live.cn"),
			NavigateUri = new Uri("mailto:xingzhuo1989@live.cn"),
			Padding = new Thickness(0.0),
			MinHeight = 44.0
		});
		basic.Children.Add(Ui.Button("恢复默认设置", async delegate
		{
			if (await new ContentDialog
			{
				XamlRoot = ((FrameworkElement)base.Content).XamlRoot,
				Title = Localization.T("恢复默认设置？"),
				Content = Localization.T("将重置基础参数和手势绑定；已添加的应用快捷启动保留。"),
				PrimaryButtonText = Localization.T("恢复默认"),
				CloseButtonText = Localization.T("取消")
			}.ShowAsync() == ContentDialogResult.Primary)
			{
				try
				{
					RestoreDefaultsCore();
					App.CurrentApp.ReopenSettings();
				}
				catch (Exception error)
				{
					Diagnostics.Error(error, "Settings.Reset");
					App.CurrentApp.Report("恢复默认失败，请重试或导出诊断信息。");
				}
			}
		}));
		void RefreshPurchase()
		{
			purchasePanel.Visibility = (StoreEntitlements.AppBindingsAvailable ? Visibility.Collapsed : Visibility.Visible);
			purchaseStatus.Text = Localization.T(StoreEntitlements.Status);
		}
	}

	internal async Task CheckV0830Async()
	{
		if (navigation.MenuItems.OfType<NavigationViewItem>().Any((NavigationViewItem i) => object.Equals(i.Tag, 6)))
		{
			throw new InvalidOperationException("Guide navigation still exists");
		}
		ShowPageForCapture(0);
		await Task.Delay(100);
		if (!basic.Children.OfType<ComboBox>().Any((ComboBox c) => object.Equals(c.Header, Localization.T("语言"))))
		{
			throw new InvalidOperationException("Language selector missing");
		}
		if (basic.Children.OfType<StackPanel>().Single((StackPanel p) => p.Children.OfType<Button>().Any((Button b) => object.Equals(b.Content, Localization.T("解锁应用绑定")))).Visibility == Visibility.Collapsed != StoreEntitlements.AppBindingsAvailable)
		{
			throw new InvalidOperationException("Purchase UI visibility mismatch");
		}
		ShowPageForCapture(5, 1);
		await Task.Delay(100);
		if (!(CurrentPage is ScrollViewer { Content: SettingsTabs content }) || content.Items.Count != 2)
		{
			throw new InvalidOperationException("Input tabs mismatch");
		}
		StackPanel stackPanel = (StackPanel)((ScrollViewer)content.Items[1].Content).Content;
		if (stackPanel.Children.IndexOf(stackPanel.Children.OfType<ComboBox>().Single()) != 3)
		{
			throw new InvalidOperationException("Touchpad button position not above opacity");
		}
		ShowPageForCapture(7);
		await Task.Delay(200);
	}

	internal async Task CheckV0828Async()
	{
		ShowPageForCapture(4);
		if (!(CurrentPage is SettingsTabs settingsTabs) || !(settingsTabs.Items[0].Content is Grid grid) || !grid.Children.Contains(gestureProfileHost) || settingsTabs.Items[1].Content is Grid)
		{
			throw new InvalidOperationException("手势应用选择器层级错误");
		}
		ShowPageForCapture(3);
		if (!Descendants<ToggleSwitch>(dialStyle).Any((ToggleSwitch t) => object.Equals(t.Header, "显示虚拟 Dial")))
		{
			throw new InvalidOperationException("Dial 显示开关缺失");
		}
		ShowPageForCapture(7);
		await Task.Delay(300);
		if (!Descendants<DocumentView>(base.Content).Any())
		{
			throw new InvalidOperationException("更新未使用排版视图");
		}
		ShowPageForCapture(8);
		ShowPageForCapture(2);
		int count = store.Value.Launchers.Count;
		RestoreDefaultsCore();
		if (store.Value.Launchers.Count != count || store.Value.WheelMask || store.Value.WheelCancelOutside)
		{
			throw new InvalidOperationException("重置默认值或快捷启动保留失败");
		}
		static IEnumerable<T> Descendants<T>(DependencyObject root) where T : notnull, DependencyObject
		{
			if (root is T val)
			{
				yield return val;
			}
			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
			{
				foreach (T item in Descendants<T>(VisualTreeHelper.GetChild(root, i)))
				{
					yield return item;
				}
			}
		}
	}

	internal void RestoreDefaultsCore()
	{
		switchBindings.Clear();
		dialAppIndex = (bottomAppIndex = (arkProfileIndex = 0));
		revealDialTool = null;
		selectedGamepadControl = null;
		store.RestoreDefaults(preserveLaunchers: true);
		reset();
	}

	private FrameworkElement UpdatesPage()
	{
		Grid grid = new Grid();
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition());
		if (StoreUpdates.Available)
		{
			StackPanel stackPanel = new StackPanel
			{
				Spacing = 8.0,
				Margin = new Thickness(28.0, 0.0, 28.0, 16.0)
			};
			TextBlock status = Ui.Description("");
			Button check = null;
			check = Ui.Button("检查更新", async delegate
			{
				check.IsEnabled = false;
				status.Text = Localization.T("正在检查更新…");
				try
				{
					TextBlock textBlock = status;
					textBlock.Text = Localization.T(await StoreUpdates.CheckAsync(this));
				}
				finally
				{
					check.IsEnabled = true;
				}
			});
			stackPanel.Children.Add(check);
			stackPanel.Children.Add(status);
			grid.Children.Add(stackPanel);
		}
		FrameworkElement frameworkElement = DocumentPage("CHANGELOG.md");
		Grid.SetRow(frameworkElement, 1);
		grid.Children.Add(frameworkElement);
		return grid;
	}

	private FrameworkElement DocumentPage(string file)
	{
		ContentControl host = new ContentControl
		{
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			VerticalContentAlignment = VerticalAlignment.Stretch,
			Content = Ui.Text("正在加载…")
		};
		bool loaded = false;
		host.Loaded += async delegate
		{
			if (loaded)
			{
				return;
			}
			loaded = true;
			try
			{
				string text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, file));
				if (!closed)
				{
					host.Content = new DocumentView(text);
				}
			}
			catch
			{
				if (!closed)
				{
					host.Content = Ui.Text("说明文件暂不可用。");
				}
			}
		};
		return host;
	}

	private async void ShowDocument(string title, string file)
	{
		if (documentOpen || closed)
		{
			return;
		}
		documentOpen = true;
		try
		{
			string path = Path.Combine(AppContext.BaseDirectory, file);
			string text = ((!File.Exists(path)) ? Localization.T("说明文件暂不可用，请联系作者。") : (await File.ReadAllTextAsync(path)));
			string text2 = text;
			if (!closed)
			{
				DocumentView content = new DocumentView(text2)
				{
					MaxHeight = 520.0
				};
				await new ContentDialog
				{
					XamlRoot = ((FrameworkElement)base.Content).XamlRoot,
					Title = title,
					CloseButtonText = Localization.T("关闭"),
					Content = content
				}.ShowAsync();
			}
		}
		catch (Exception ex)
		{
			if (!closed)
			{
				App.CurrentApp.Report(Localization.T("无法打开说明：") + ex.Message);
			}
		}
		finally
		{
			documentOpen = false;
		}
	}

	private void BuildFloating()
	{
		floating.Children.Clear();
		Settings s = store.Value;
		ToggleSwitch toggleSwitch = new ToggleSwitch
		{
			Header = Localization.T("显示触控圆点"),
			IsOn = s.BallEnabled
		};
		BindSwitch(toggleSwitch, () => store.Value.BallEnabled, delegate(bool v)
		{
			store.Value.BallEnabled = v;
		});
		floating.Children.Add(Ui.Card(toggleSwitch));
		ToggleSwitch toggleSwitch2 = new ToggleSwitch
		{
			Header = Localization.T("拖动模式显示高亮描边"),
			IsOn = s.MoveOutline
		};
		BindSwitch(toggleSwitch2, () => store.Value.MoveOutline, delegate(bool v)
		{
			store.Value.MoveOutline = v;
		});
		floating.Children.Add(Ui.Card(toggleSwitch2));
		ToggleSwitch toggleSwitch3 = new ToggleSwitch
		{
			Header = Localization.T("允许鼠标操作触控圆点"),
			IsOn = s.MouseEnabled,
			OnContent = Localization.T("触控和鼠标"),
			OffContent = Localization.T("仅触控 / 触控笔")
		};
		BindSwitch(toggleSwitch3, () => store.Value.MouseEnabled, delegate(bool v)
		{
			store.Value.MouseEnabled = v;
		});
		floating.Children.Add(Ui.Card(toggleSwitch3));
		floating.Children.Add(SettingToggle("晃动以关闭触控圆点", () => s.BallShakeToClose, delegate(bool v)
		{
			s.BallShakeToClose = v;
		}));
		AddSlider("触控圆点大小", s.BallSize, 128.0, 2.0, "DIP", delegate(double x)
		{
			store.Value.BallSize = x;
		}, 40.0);
		AddSlider("触控圆点透明度", s.Opacity * 100.0, 100.0, 0.01, "%", delegate(double x)
		{
			store.Value.Opacity = x / 100.0;
		});
		AddSlider("闲置时间", s.IdleSeconds, 3.0, 0.1, "秒", delegate(double x)
		{
			store.Value.IdleSeconds = x;
		});
		AddSlider("闲置时透明度", s.IdleOpacity * 100.0, 100.0, 0.01, "%", delegate(double x)
		{
			store.Value.IdleOpacity = x / 100.0;
		});
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		floating.Children.Add(stackPanel);
		stackPanel.Children.Add(Ui.Button("重置触控圆点位置", reset));
		stackPanel.Children.Add(Ui.Button("重置触控圆点大小", delegate
		{
			store.Value.BallSize = 58.0;
			store.Save();
			BuildFloating();
		}));
		string[] array = new string[5] { "单击", "双击", "三击", "拖动", "长按" };
		for (int num = 0; num < array.Length; num++)
		{
			floating.Children.Add(ActionEditor(num + 8, array[num]));
		}
		AddSlider("长按触发时间", s.HoldSeconds, 3.0, 0.1, "秒", delegate(double x)
		{
			s.HoldSeconds = x;
		});
	}

	private void AddSlider(string label, double value, double max, double step, string unit, Action<double> update, double min = 0.0, StackPanel? target = null)
	{
		label = Localization.T(label);
		unit = Localization.T(unit);
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 8.0
		};
		TextBlock title = Ui.Text($"{label}    {value:0.##} {unit}", 16.0);
		Slider slider = new Slider
		{
			Minimum = min,
			Maximum = max,
			StepFrequency = step,
			Value = value,
			MinHeight = 44.0
		};
		AutomationProperties.SetName(slider, label);
		NumberBox number = new NumberBox
		{
			Minimum = min,
			Maximum = max,
			SmallChange = step,
			LargeChange = step * 10.0,
			Value = value,
			SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline,
			MinHeight = 40.0
		};
		bool changing = false;
		slider.ValueChanged += delegate(object _, RangeBaseValueChangedEventArgs e)
		{
			Save(e.NewValue);
		};
		number.ValueChanged += delegate(NumberBox _, NumberBoxValueChangedEventArgs e)
		{
			Save(e.NewValue);
		};
		stackPanel.Children.Add(title);
		stackPanel.Children.Add(slider);
		stackPanel.Children.Add(number);
		(target ?? floating).Children.Add(Ui.Card(stackPanel));
		void Save(double input)
		{
			if (changing || !double.IsFinite(input))
			{
				return;
			}
			changing = true;
			try
			{
				double num = Math.Round(Math.Clamp(input, min, max) / step) * step;
				title.Text = $"{label}    {num:0.##} {unit}";
				Slider slider2 = slider;
				double value2 = (number.Value = num);
				slider2.Value = value2;
				update(num);
				ScheduleSave();
			}
			finally
			{
				changing = false;
			}
		}
	}

	private void BuildGestures()
	{
		gestures.Children.Clear();
		Settings s = store.Value;
		gestures.Children.Add(SettingToggle("环形菜单深色遮罩", () => s.WheelMask, delegate(bool v)
		{
			s.WheelMask = v;
		}));
		gestures.Children.Add(SettingToggle("遮罩显示时间和日期", () => s.WheelMaskClock, delegate(bool v)
		{
			s.WheelMaskClock = v;
		}));
		ToggleSwitch toggleSwitch = new ToggleSwitch
		{
			Header = Localization.T("菜单高亮时显示提示"),
			IsOn = s.WheelCenterHint
		};
		BindSwitch(toggleSwitch, () => store.Value.WheelCenterHint, delegate(bool v)
		{
			store.Value.WheelCenterHint = v;
		});
		gestures.Children.Add(Ui.Card(toggleSwitch));
		ToggleSwitch toggleSwitch2 = new ToggleSwitch
		{
			Header = Localization.T("拖动超过轮盘外缘时关闭菜单"),
			IsOn = s.WheelCancelOutside
		};
		BindSwitch(toggleSwitch2, () => store.Value.WheelCancelOutside, delegate(bool v)
		{
			store.Value.WheelCancelOutside = v;
		});
		gestures.Children.Add(Ui.Card(toggleSwitch2));
		AddSlider("关闭距离（从圆心计算）", s.WheelCancelRadius, 6.0, 0.1, "倍轮盘半径", delegate(double x)
		{
			s.WheelCancelRadius = x;
		}, 1.0, gestures);
		string[] labels = new string[8] { "↑  12 点 · 向上", "↗  1:30 · 右上", "→  3 点 · 向右", "↘  4:30 · 右下", "↓  6 点 · 向下", "↙  7:30 · 左下", "←  9 点 · 向左", "↖  10:30 · 左上" };
		ContentControl selectedEditor = new ContentControl
		{
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		WheelDirectionSelector selector = new WheelDirectionSelector(store, delegate(int i)
		{
			selectedEditor.Content = ActionEditor(i, labels[i]);
		});
		gestures.Children.Insert(0, new ResponsivePair(selector, selectedEditor));
	}

	private static Expander ColorEditor(string label, string value, Action<string> update, string? defaultColor = null)
	{
		Expander expander = new Expander
		{
			Header = Localization.T(label),
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		expander.Expanding += delegate
		{
			if (expander.Content == null)
			{
				ColorPicker picker = new ColorPicker
				{
					Color = AccentTheme.Parse(value),
					IsAlphaEnabled = false,
					IsColorSliderVisible = true,
					IsColorChannelTextInputVisible = true,
					IsHexInputVisible = true,
					MaxWidth = 360.0
				};
				picker.ColorChanged += delegate(ColorPicker colorPicker, ColorChangedEventArgs e)
				{
					update($"#{e.NewColor.R:X2}{e.NewColor.G:X2}{e.NewColor.B:X2}");
				};
				StackPanel stackPanel = new StackPanel
				{
					Spacing = 8.0
				};
				stackPanel.Children.Add(picker);
				if (defaultColor != null)
				{
					stackPanel.Children.Add(Ui.Button("重置颜色", delegate
					{
						picker.Color = AccentTheme.Parse(defaultColor);
						update(defaultColor);
					}));
				}
				expander.Content = stackPanel;
			}
		};
		return expander;
	}

	private void BuildTouchpadGestures(StackPanel panel, VirtualTouchpadSettings options)
	{
		panel.Children.Add(Ui.Description("中键区域上下滑动可滚动，单击默认为鼠标中键；可分别设置滚动、点击和长按的功能。", 12.0));
		AddSlider("触摸板长按时间", options.GestureHoldSeconds, 3.0, 0.05, "秒", delegate(double v)
		{
			options.GestureHoldSeconds = v;
		}, 0.1, panel);
		(string, string)[] middleNames = new(string, string)[6]
		{
			("Up", "向上滚动"),
			("Down", "向下滚动"),
			("Tap", "单击"),
			("DoubleTap", "双击"),
			("TripleTap", "三击"),
			("Hold", "长按")
		};
		Group("鼠标中键与滚轮", delegate
		{
			StackPanel stackPanel = new StackPanel
			{
				Spacing = 10.0
			};
			(string, string)[] array = middleNames;
			for (int i = 0; i < array.Length; i++)
			{
				var (key, label) = array[i];
				stackPanel.Children.Add(ActionEditor(0, label, edge: false, options.MiddleActions[key], appearance: false));
			}
			return stackPanel;
		});
		panel.Children.Add(Ui.Description("未绑定时保留触摸板默认操作；为某个手指数绑定滑动手势后，该指数组合用于手势，不再同时滚动或拖动鼠标。", 12.0));
		panel.Children.Add(Ui.Description("双指缩放默认使用 Ctrl＋滚轮，应用需要支持此缩放方式。", 12.0));
		(string, string)[] gestures = new(string, string)[8]
		{
			("Tap", "单击"),
			("DoubleTap", "双击"),
			("TripleTap", "三击"),
			("Hold", "长按"),
			("Up", "向上滑动"),
			("Down", "向下滑动"),
			("Left", "向左滑动"),
			("Right", "向右滑动")
		};
		for (int num = 2; num <= 5; num++)
		{
			int fingers = num;
			Group(Localization.T("{0} 指手势", fingers), delegate
			{
				StackPanel stackPanel = new StackPanel
				{
					Spacing = 10.0
				};
				IEnumerable<(string, string)> enumerable2;
				if (fingers != 2)
				{
					IEnumerable<(string, string)> enumerable = gestures;
					enumerable2 = enumerable;
				}
				else
				{
					enumerable2 = gestures.Concat(new(string, string)[2]
					{
						("PinchOut", "双指放大"),
						("PinchIn", "双指缩小")
					});
				}
				foreach (var item2 in enumerable2)
				{
					string key = item2.Item1;
					string item = item2.Item2;
					MultiTouchBinding multiTouchBinding = options.Gestures.FirstOrDefault((MultiTouchBinding b) => b.Fingers == fingers && b.Gesture == key);
					if (multiTouchBinding == null)
					{
						multiTouchBinding = new MultiTouchBinding
						{
							Fingers = fingers,
							Gesture = key
						};
						options.Gestures.Add(multiTouchBinding);
					}
					stackPanel.Children.Add(ActionEditor(0, item, edge: false, multiTouchBinding.Action, appearance: false));
				}
				return stackPanel;
			});
		}
		void Group(string title, Func<StackPanel> build)
		{
			Expander expander = new Expander
			{
				Header = Localization.T(title),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch
			};
			expander.Expanding += delegate
			{
				Expander expander2 = expander;
				if (expander2.Content == null)
				{
					object obj = (expander2.Content = build());
				}
			};
			panel.Children.Add(expander);
		}
	}

	private FrameworkElement SettingToggle(string text, Func<bool> read, Action<bool> change)
	{
		ToggleSwitch toggleSwitch = new ToggleSwitch
		{
			Header = Localization.T(text),
			IsOn = read()
		};
		BindSwitch(toggleSwitch, read, change);
		return Ui.Card(toggleSwitch);
	}

	private void BindSwitch(ToggleSwitch control, Func<bool> read, Action<bool> write)
	{
		if (control.ReadLocalValue(ToggleSwitch.OnContentProperty) == DependencyProperty.UnsetValue)
		{
			control.OnContent = Localization.T("开");
		}
		if (control.ReadLocalValue(ToggleSwitch.OffContentProperty) == DependencyProperty.UnsetValue)
		{
			control.OffContent = Localization.T("关");
		}
		switchBindings.Add((new WeakReference<ToggleSwitch>(control), read));
		control.Toggled += delegate
		{
			if (!syncingSwitches && control.IsOn != read())
			{
				write(control.IsOn);
				store.Save();
			}
		};
	}

	private void SettingsSaved()
	{
		if (!closed)
		{
			if (!base.DispatcherQueue.HasThreadAccess)
			{
				base.DispatcherQueue.TryEnqueue(SettingsSaved);
				return;
			}
			pendingSave?.Stop();
			savePending = false;
			SyncSwitches();
		}
	}

	private void SyncSwitches()
	{
		if (closed)
		{
			return;
		}
		if (!base.DispatcherQueue.HasThreadAccess)
		{
			base.DispatcherQueue.TryEnqueue(SyncSwitches);
			return;
		}
		syncingSwitches = true;
		try
		{
			for (int num = switchBindings.Count - 1; num >= 0; num--)
			{
				(WeakReference<ToggleSwitch>, Func<bool>) tuple = switchBindings[num];
				if (!tuple.Item1.TryGetTarget(out var target))
				{
					switchBindings.RemoveAt(num);
				}
				else
				{
					bool flag = tuple.Item2();
					if (target.IsOn != flag)
					{
						target.IsOn = flag;
					}
				}
			}
		}
		finally
		{
			syncingSwitches = false;
		}
	}

	private void RefreshBubbleContent(bool scrollToBottom = false)
	{
		base.DispatcherQueue.TryEnqueue(delegate
		{
			SettingsTab tab;
			ScrollViewer view;
			if (!closed)
			{
				tab = bubbleTabs.SelectedItem;
				if (tab != null)
				{
					tab.Content = null;
					LoadBubbleTab();
					if (scrollToBottom)
					{
						object content = tab.Content;
						view = content as ScrollViewer;
						if ((object)view != null)
						{
							if (view.IsLoaded)
							{
								base.DispatcherQueue.TryEnqueue(delegate
								{
									ScrollToBottom();
								});
							}
							else
							{
								view.Loaded += OnLoaded;
							}
						}
					}
				}
			}
			void OnLoaded(object sender, RoutedEventArgs args)
			{
				view.Loaded -= OnLoaded;
				base.DispatcherQueue.TryEnqueue(delegate
				{
					ScrollToBottom();
				});
			}
			void ScrollToBottom()
			{
				if (!closed && bubbleTabs.SelectedItem == tab && tab.Content == view)
				{
					view.UpdateLayout();
					view.ChangeView(null, view.ScrollableHeight, null, disableAnimation: true);
				}
			}
		});
	}

	internal async Task CheckV0827Async(Func<Window, string, Task> capture)
	{
		ShowPageForCapture(5, 1);
		await Task.Delay(150);
		if (!(CurrentPage is ScrollViewer { Content: SettingsTabs content }) || content.Items.Count != 2)
		{
			throw new InvalidOperationException("鼠标摇杆仍显示");
		}
		await capture(this, "touchpad-settings-v0830");
		store.Value.DialApps.Add(new DialAppProfile
		{
			Name = "测试应用",
			Target = "fixture.exe",
			Options = CopyConfig(store.Value.Dial)
		});
		dialAppIndex = 1;
		ShowPageForCapture(3);
		BuildDialStyle();
		BuildDialTools();
		DialSettings active = EditedDial;
		((SettingsTabs)CurrentPage).SelectedIndex = 1;
		await Task.Delay(200);
		if (active != EditedDial || dialStyle.Children.Contains(dialProfileHost) || dialTools.Children.Contains(dialProfileHost))
		{
			throw new InvalidOperationException("Dial配置未跨标签共享");
		}
		await capture(this, "dial-profile-v0827");
		store.Value.GestureApps.Add(new GestureAppProfile
		{
			Name = "测试应用",
			Target = "fixture.exe",
			Options = CopyConfig(store.Value.MultiTouch)
		});
		bottomAppIndex = 1;
		ShowPageForCapture(4);
		BuildMultiTouch();
		if (EditedGestures != store.Value.GestureApps[0].Options)
		{
			throw new InvalidOperationException("手势配置选择错误");
		}
		ShowPageForCapture(2, 1);
		BuildBubbleTabs();
		if (bubbleTabs.ShowHeaders || bubbleProfileHost.Content == null)
		{
			throw new InvalidOperationException("气泡未使用共享下拉框");
		}
		ShowPageForCapture(1, 1);
		await Task.Delay(150);
		ScrollWheelBinding();
		await Task.Delay(200);
		((WheelDirectionSelector)gestures.Children.OfType<ResponsivePair>().Single().Selector).Select(2);
		store.Value.Gestures[2].Label = "实时预览";
		store.Save();
		await Task.Delay(120);
		await capture(this, "wheel-preview-v0827");
		ShowPageForCapture(5, 1);
		await capture(this, "touchpad-settings-v0827");
	}

	internal void CheckV0825Bindings()
	{
		store.Value.DialApps.Add(new DialAppProfile
		{
			Name = "测试应用",
			Target = "test.exe",
			Options = CopyConfig(store.Value.Dial)
		});
		dialAppIndex = store.Value.DialApps.Count;
		BuildDialStyle();
		BuildDialTools();
		DialSettings editedDial = EditedDial;
		List<DialAppProfile> dialApps = store.Value.DialApps;
		if (editedDial != dialApps[dialApps.Count - 1].Options)
		{
			throw new InvalidOperationException("Dial编辑器未切换应用配置");
		}
		store.Value.BottomApps.Add(new BottomAppProfile
		{
			Name = "测试应用",
			Target = "test.exe"
		});
		bottomAppIndex = store.Value.BottomApps.Count;
		BuildEdges();
		if (EditedBottom == null)
		{
			throw new InvalidOperationException("全面屏应用配置未显示");
		}
		BuildGamepad();
		if (!gamepadPage.Children.OfType<Button>().Any((Button b) => object.Equals(b.Content, "绑定当前布局到应用")))
		{
			throw new InvalidOperationException("缺少布局应用绑定入口");
		}
	}

	private static FrameworkElement WithProfile(ContentControl host, FrameworkElement tabs)
	{
		Grid obj = new Grid
		{
			RowDefinitions = 
			{
				new RowDefinition
				{
					Height = GridLength.Auto
				},
				new RowDefinition()
			}
		};
		host.Margin = new Thickness(28.0, 0.0, 28.0, 16.0);
		host.HorizontalContentAlignment = HorizontalAlignment.Stretch;
		obj.Children.Add(host);
		Grid.SetRow(tabs, 1);
		obj.Children.Add(tabs);
		return obj;
	}

	private static T CopyConfig<T>(T value)
	{
		return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
	}

	private FrameworkElement ProfilePicker(bool dial)
	{
		string[] array = new string[1] { Localization.T("全局配置") }.Concat(dial ? store.Value.DialApps.Select((DialAppProfile p) => p.Name) : store.Value.GestureApps.Select((GestureAppProfile p) => p.Name)).ToArray();
		int num = Math.Clamp(dial ? dialAppIndex : bottomAppIndex, 0, array.Length - 1);
		return ApplicationPicker(array, num, delegate(int i)
		{
			if (dial)
			{
				dialAppIndex = i;
			}
			else
			{
				bottomAppIndex = i;
			}
			Rebuild();
		}, async delegate
		{
			LauncherEntry launcherEntry = await Ui.PickLauncherAsync(((FrameworkElement)base.Content).XamlRoot, this);
			if (launcherEntry != null)
			{
				string target = (string.IsNullOrWhiteSpace(launcherEntry.MatchTarget) ? launcherEntry.Target : launcherEntry.MatchTarget);
				if (dial)
				{
					int num2 = store.Value.DialApps.FindIndex((DialAppProfile p) => WindowCatalog.NormalizeTarget(p.Target) == WindowCatalog.NormalizeTarget(target));
					if (num2 < 0)
					{
						store.Value.DialApps.Add(new DialAppProfile
						{
							Name = launcherEntry.Name,
							Target = target,
							Options = CopyConfig(EditedDial)
						});
						num2 = store.Value.DialApps.Count - 1;
					}
					dialAppIndex = num2 + 1;
				}
				else
				{
					int num3 = store.Value.GestureApps.FindIndex((GestureAppProfile p) => WindowCatalog.NormalizeTarget(p.Target) == WindowCatalog.NormalizeTarget(target));
					if (num3 < 0)
					{
						store.Value.GestureApps.Add(new GestureAppProfile
						{
							Name = launcherEntry.Name,
							Target = target,
							Options = CopyConfig(EditedGestures)
						});
						num3 = store.Value.GestureApps.Count - 1;
					}
					bottomAppIndex = num3 + 1;
				}
				store.Save();
				Rebuild();
			}
		}, (num == 0) ? null : ((Action)delegate
		{
			if (dial)
			{
				store.Value.DialApps.RemoveAt(dialAppIndex - 1);
				dialAppIndex = 0;
			}
			else
			{
				store.Value.GestureApps.RemoveAt(bottomAppIndex - 1);
				bottomAppIndex = 0;
			}
			store.Save();
			Rebuild();
		}));
		void Rebuild()
		{
			if (dial)
			{
				BuildDialStyle();
				BuildDialTools();
			}
			else
			{
				BuildMultiTouch();
			}
		}
	}

	private FrameworkElement ApplicationPicker(string[] names, int selected, Action<int> select, Action bind, Action? remove = null)
	{
		StackPanel obj = new StackPanel
		{
			Spacing = 6.0
		};
		Grid grid = new Grid
		{
			ColumnSpacing = 8.0
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		ComboBox picker = new ComboBox
		{
			ItemsSource = names,
			SelectedIndex = selected,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			MinHeight = 48.0
		};
		AutomationProperties.SetName(picker, Localization.T("应用配置"));
		picker.SelectionChanged += delegate
		{
			if (picker.SelectedIndex >= 0 && picker.SelectedIndex != selected)
			{
				select(picker.SelectedIndex);
			}
		};
		grid.Children.Add(picker);
		Button button = Ui.Button("绑定应用", async delegate
		{
			bool flag = StoreEntitlements.AppBindingsAvailable;
			if (!flag)
			{
				flag = await StoreEntitlements.UnlockAsync(this);
			}
			if (flag)
			{
				bind();
			}
			else
			{
				App.CurrentApp.Report(StoreEntitlements.Status);
			}
		});
		Grid.SetColumn(button, 1);
		grid.Children.Add(button);
		if (remove != null)
		{
			Button button2 = Ui.Button("解除绑定", remove);
			Grid.SetColumn(button2, 2);
			grid.Children.Add(button2);
		}
		obj.Children.Add(grid);
		obj.Children.Add(Ui.Description("切换应用自动使用专属配置，未绑定的应用使用全局配置。"));
		return obj;
	}

	internal void SelectGamepadControl(GamepadControl control)
	{
		selectedGamepadControl = control;
		int num = store.Value.Gamepad.Layouts.FindIndex((GamepadLayout l) => l.Controls.Contains(control));
		if (num >= 0)
		{
			store.Value.Gamepad.SelectLayout(num);
		}
		ShowPageForCapture(8);
		BuildGamepad();
	}

	internal void RefreshGamepad()
	{
		BuildGamepad();
	}

	private FrameworkElement BuildGamepadPage()
	{
		gamepadScroll = new ScrollViewer
		{
			Content = gamepadPage
		};
		gamepadStickyName = new TextBox
		{
			PlaceholderText = Localization.T("布局名称"),
			MinWidth = 240.0,
			MaxWidth = 420.0,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		gamepadStickyName.TextChanged += delegate
		{
			if (!syncingGamepadName)
			{
				GamepadSettings gamepad = store.Value.Gamepad;
				if (gamepad.Layouts.Count > gamepad.ActiveLayout)
				{
					gamepad.Layouts[gamepad.ActiveLayout].Name = gamepadStickyName.Text;
					store.Save();
					if (gamepadLayoutName != null)
					{
						gamepadLayoutName.Text = gamepadStickyName.Text;
					}
					if (gamepadLayoutPicker != null)
					{
						gamepadLayoutPicker.ItemsSource = gamepad.Layouts.Select((GamepadLayout l) => Localization.T(l.Name)).ToArray();
						gamepadLayoutPicker.SelectedIndex = gamepad.ActiveLayout;
					}
				}
			}
		};
		gamepadStickyActions = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6.0
		};
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 6.0
		};
		stackPanel.Children.Add(gamepadStickyName);
		stackPanel.Children.Add(new ScrollViewer
		{
			Content = gamepadStickyActions,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
		});
		gamepadSticky = new Border
		{
			Child = stackPanel,
			Visibility = Visibility.Collapsed,
			VerticalAlignment = VerticalAlignment.Top,
			Padding = new Thickness(28.0, 8.0, 28.0, 8.0),
			BorderBrush = new SolidColorBrush(Color.FromArgb(60, 128, 128, 128)),
			BorderThickness = new Thickness(0.0, 0.0, 0.0, 1.0)
		};
		RefreshStickyBackground();
		gamepadPage.ActualThemeChanged += delegate
		{
			RefreshStickyBackground();
		};
		gamepadScroll.ViewChanged += delegate
		{
			UpdateGamepadSticky();
		};
		return new Grid
		{
			Children = 
			{
				(UIElement)gamepadScroll,
				(UIElement)gamepadSticky
			}
		};
		void RefreshStickyBackground()
		{
			gamepadSticky.Background = new SolidColorBrush((gamepadPage.ActualTheme == ElementTheme.Dark) ? Color.FromArgb(byte.MaxValue, 32, 32, 32) : Color.FromArgb(byte.MaxValue, 243, 243, 243));
		}
	}

	private void UpdateGamepadSticky()
	{
		if (!(gamepadLayoutName == null) && !(gamepadScroll == null) && !(gamepadSticky == null))
		{
			try
			{
				Point point = gamepadLayoutName.TransformToVisual(gamepadScroll).TransformPoint(new Point(0f, 0f));
				gamepadSticky.Visibility = ((!(point.Y < 0.0)) ? Visibility.Collapsed : Visibility.Visible);
			}
			catch
			{
				gamepadSticky.Visibility = Visibility.Collapsed;
			}
			if (gamepadStickyName != null && store.Value.Gamepad.Layouts.Count > store.Value.Gamepad.ActiveLayout)
			{
				syncingGamepadName = true;
				gamepadStickyName.Text = Localization.T(store.Value.Gamepad.Layouts[store.Value.Gamepad.ActiveLayout].Name);
				syncingGamepadName = false;
			}
		}
	}

	private void BuildGamepad()
	{
		gamepadPage.Children.Clear();
		GamepadSettings options = store.Value.Gamepad;
		options.EnsureLayouts();
		ComboBox layoutPicker = new ComboBox
		{
			Header = Localization.T("当前布局"),
			ItemsSource = options.Layouts.Select((GamepadLayout l) => Localization.T(l.Name)).ToArray(),
			SelectedIndex = options.ActiveLayout,
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		gamepadLayoutPicker = layoutPicker;
		layoutPicker.SelectionChanged += delegate
		{
			if (layoutPicker.SelectedIndex >= 0 && layoutPicker.SelectedIndex != options.ActiveLayout)
			{
				options.SelectLayout(layoutPicker.SelectedIndex);
				store.Save();
				BuildGamepad();
			}
		};
		gamepadPage.Children.Add(layoutPicker);
		GamepadLayout selectedLayout = options.Layouts[options.ActiveLayout];
		string[] names = ((!string.IsNullOrEmpty(selectedLayout.Target)) ? new string[2]
		{
			Localization.T("全局配置"),
			selectedLayout.Target
		} : new string[1] { Localization.T("全局配置") });
		gamepadPage.Children.Add(ApplicationPicker(names, (!string.IsNullOrEmpty(selectedLayout.Target)) ? 1 : 0, delegate(int i)
		{
			if (i == 0)
			{
				selectedLayout.Target = "";
				store.Save();
				BuildGamepad();
			}
		}, async delegate
		{
			LauncherEntry launcherEntry = await Ui.PickLauncherAsync(((FrameworkElement)base.Content).XamlRoot, this);
			if (launcherEntry != null)
			{
				string target = (string.IsNullOrWhiteSpace(launcherEntry.MatchTarget) ? launcherEntry.Target : launcherEntry.MatchTarget);
				if (options.Layouts.Any((GamepadLayout l) => l != options.Layouts[options.ActiveLayout] && WindowCatalog.NormalizeTarget(l.Target ?? "") == WindowCatalog.NormalizeTarget(target)))
				{
					App.CurrentApp.Report("此应用已经绑定其它布局。");
				}
				else
				{
					if (string.IsNullOrEmpty(options.Layouts[options.ActiveLayout].Target) && !options.Layouts.Any((GamepadLayout l) => l != options.Layouts[options.ActiveLayout] && string.IsNullOrEmpty(l.Target)))
					{
						GamepadLayout gamepadLayout = CopyConfig(options.Layouts[options.ActiveLayout]);
						gamepadLayout.Name = "全局布局";
						options.Layouts.Add(gamepadLayout);
					}
					options.Layouts[options.ActiveLayout].Target = target;
					store.Save();
					BuildGamepad();
				}
			}
		}, string.IsNullOrEmpty(selectedLayout.Target) ? null : ((Action)delegate
		{
			selectedLayout.Target = "";
			store.Save();
			BuildGamepad();
		})));
		TextBox layoutName = new TextBox
		{
			Header = Localization.T("布局名称"),
			Text = Localization.T(options.Layouts[options.ActiveLayout].Name)
		};
		gamepadLayoutName = layoutName;
		layoutName.LostFocus += delegate
		{
			if (!(layoutName.Text == Localization.T(options.Layouts[options.ActiveLayout].Name)))
			{
				options.Layouts[options.ActiveLayout].Name = layoutName.Text;
				store.Save();
				layoutPicker.ItemsSource = options.Layouts.Select((GamepadLayout l) => Localization.T(l.Name)).ToArray();
				layoutPicker.SelectedIndex = options.ActiveLayout;
				if (gamepadStickyName != null)
				{
					syncingGamepadName = true;
					gamepadStickyName.Text = layoutName.Text;
					syncingGamepadName = false;
				}
			}
		};
		gamepadPage.Children.Add(layoutName);
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6.0
		};
		stackPanel.Children.Add(Ui.Button("编辑布局", delegate
		{
			App.CurrentApp.EditGamepad(editing: true);
		}));
		stackPanel.Children.Add(Ui.Button("保存布局", delegate
		{
			App.CurrentApp.EditGamepad(editing: false);
			App.CurrentApp.Report("布局已保存。");
		}));
		stackPanel.Children.Add(Ui.Button("另存为新布局", delegate
		{
			options.AddLayout(Localization.T("布局 {0}", options.Layouts.Count + 1));
			store.Save();
			BuildGamepad();
		}));
		stackPanel.Children.Add(Ui.Button("删除当前布局", delegate
		{
			if (options.Layouts.Count <= 1)
			{
				App.CurrentApp.Report("至少保留一个布局。");
			}
			else
			{
				options.Layouts.RemoveAt(options.ActiveLayout);
				options.ActiveLayout = 0;
				options.Controls = options.Layouts[0].Controls;
				store.Save();
				BuildGamepad();
			}
		}));
		stackPanel.Children.Add(Ui.Button("新建布局", delegate
		{
			options.AddLayout(Localization.T("布局 {0}", options.Layouts.Count + 1), empty: true);
			store.Save();
			BuildGamepad();
		}));
		stackPanel.Children.Add(Ui.Button("导出布局…", delegate
		{
			try
			{
				string text = Ui.LayoutFile(this, save: true);
				if (text != null)
				{
					GamepadLayoutTransfer.Export(options.Layouts, text);
					App.CurrentApp.Report("布局已导出。");
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Error(ex, "GamepadLayout.Export");
				App.CurrentApp.Report(Localization.T("导出失败：") + ex.Message);
			}
		}));
		stackPanel.Children.Add(Ui.Button("导入布局…", delegate
		{
			try
			{
				string text = Ui.LayoutFile(this, save: false);
				if (text != null)
				{
					List<GamepadLayout> list = GamepadLayoutTransfer.Read(text);
					if (options.Layouts.Count + list.Count > 64)
					{
						App.CurrentApp.Report("最多保留 64 个布局。");
					}
					else
					{
						HashSet<string> hashSet = new HashSet<string>(options.Layouts.Select((GamepadLayout l) => l.Name), StringComparer.OrdinalIgnoreCase);
						foreach (GamepadLayout item in list)
						{
							string text2 = item.Name;
							int num4 = 2;
							while (hashSet.Contains(text2))
							{
								text2 = Localization.T("{0} 副本", item.Name) + " " + num4;
								num4++;
							}
							item.Name = text2;
							hashSet.Add(text2);
							options.Layouts.Add(item);
						}
						options.SelectLayout(options.Layouts.Count - 1);
						store.Save();
						BuildGamepad();
						App.CurrentApp.Report("布局已导入。");
					}
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Error(ex, "GamepadLayout.Import");
				App.CurrentApp.Report(Localization.T("导入失败：") + ex.Message);
			}
		}));
		foreach (Button item2 in stackPanel.Children.OfType<Button>())
		{
			item2.FontSize = 12.0;
			item2.Padding = new Thickness(10.0, 8.0, 10.0, 8.0);
		}
		gamepadPage.Children.Add(stackPanel);
		if (gamepadStickyActions != null)
		{
			gamepadStickyActions.Children.Clear();
			foreach (Button source in stackPanel.Children.OfType<Button>())
			{
				Button button = new Button
				{
					Content = source.Content,
					FontSize = 12.0,
					Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
					MinHeight = 32.0
				};
				button.Click += delegate
				{
					((IInvokeProvider)new ButtonAutomationPeer(source).GetPattern(PatternInterface.Invoke)).Invoke();
				};
				gamepadStickyActions.Children.Add(button);
			}
		}
		gamepadPage.Children.Add(SettingToggle("显示虚拟摇杆和按钮", () => options.Enabled, delegate(bool v)
		{
			options.Enabled = v;
		}));
		gamepadPage.Children.Add(SettingToggle("输入时切换英文输入法", () => options.AutoSwitchEnglish, delegate(bool v)
		{
			options.AutoSwitchEnglish = v;
		}));
		gamepadPage.Children.Add(Ui.Description("仅当前台窗口不是英文布局、且 IME 处于中文等原生输入模式时切换一次；已是英文布局或英文输入模式时不动。关闭后可完全手动切换。", 12.0));
		gamepadPage.Children.Add(SettingToggle("按键滑动输入", () => options.SlideInput, delegate(bool v)
		{
			options.SlideInput = v;
		}));
		gamepadPage.Children.Add(SettingToggle("快速重按（22ms 间隔）", () => options.FastRepeat, delegate(bool v)
		{
			options.FastRepeat = v;
			KeyboardInput.MinimumNeutralMs = (v ? 22 : 0);
		}));
		gamepadPage.Children.Add(SettingToggle("显示选择布局", () => options.ShowLayoutSelector, delegate(bool v)
		{
			options.ShowLayoutSelector = v;
		}));
		gamepadPage.Children.Add(Ui.Description("选择布局按钮可点击切换；编辑布局时可拖动位置，拖动右下角调整大小。", 12.0));
		GamepadLayout layout = options.Layouts[options.ActiveLayout];
		gamepadPage.Children.Add(Ui.Description("编辑布局后可拖动控件，完成后保存。快捷键按住时保持，松开时释放。防误触区域拦截空白处点击，编辑时显示边界。"));
		bool[] array = new bool[2] { true, false };
		foreach (bool joystick in array)
		{
			gamepadPage.Children.Add(Ui.Text(joystick ? "摇杆" : "按钮", 18.0));
			GamepadControl[] array2 = options.Controls.Where((GamepadControl c) => c.Joystick == joystick).ToArray();
			foreach (GamepadControl control in array2)
			{
				Grid header = new Grid
				{
					ColumnSpacing = 8.0,
					Tag = control.Name
				};
				header.ColumnDefinitions.Add(new ColumnDefinition());
				header.ColumnDefinitions.Add(new ColumnDefinition
				{
					Width = GridLength.Auto
				});
				TextBlock title = Ui.Text(control.Name, 16.0);
				title.VerticalAlignment = VerticalAlignment.Center;
				header.Children.Add(title);
				StackPanel stackPanel2 = new StackPanel
				{
					Orientation = Orientation.Horizontal,
					Spacing = 4.0,
					VerticalAlignment = VerticalAlignment.Center
				};
				Grid.SetColumn(stackPanel2, 1);
				header.Children.Add(stackPanel2);
				Button button2 = Ui.Button("复制", delegate
				{
					if (options.Controls.Count < 64)
					{
						GamepadControl gamepadControl = JsonSerializer.Deserialize<GamepadControl>(JsonSerializer.Serialize(control));
						gamepadControl.Name = Localization.T("{0} 副本", gamepadControl.Name);
						if (gamepadControl.X.HasValue)
						{
							gamepadControl.X += 24;
						}
						if (gamepadControl.Y.HasValue)
						{
							gamepadControl.Y += 24;
						}
						options.Controls.Add(gamepadControl);
						selectedGamepadControl = gamepadControl;
						store.Save();
						BuildGamepad();
					}
				});
				Button button3 = Ui.Button("删除", delegate
				{
					options.Controls.Remove(control);
					store.Save();
					BuildGamepad();
				});
				Button[] array3 = new Button[2] { button2, button3 };
				foreach (Button button4 in array3)
				{
					button4.FontSize = 12.0;
					button4.Padding = new Thickness(10.0, 6.0, 10.0, 6.0);
					button4.MinHeight = 32.0;
					stackPanel2.Children.Add(button4);
				}
				Expander expander = new Expander
				{
					Header = header,
					HorizontalAlignment = HorizontalAlignment.Stretch,
					HorizontalContentAlignment = HorizontalAlignment.Stretch
				};
				gamepadPage.Children.Add(expander);
				expander.Expanding += delegate
				{
					string[] labels;
					ContentControl editor;
					Button[] arrows;
					if (expander.Content == null)
					{
						StackPanel stackPanel4 = new StackPanel
						{
							Spacing = 12.0
						};
						TextBox name2 = new TextBox
						{
							Header = Localization.T("名称"),
							Text = control.Name
						};
						name2.LostFocus += delegate
						{
							control.Name = name2.Text;
							title.Text = control.Name;
							header.Tag = control.Name;
							store.Save();
						};
						stackPanel4.Children.Add(name2);
						StackPanel stackPanel5 = new StackPanel
						{
							Spacing = 12.0
						};
						AddSlider("不透明度", control.Opacity * 100.0, 100.0, 1.0, "%", delegate(double v)
						{
							control.Opacity = v / 100.0;
						}, 0.0, stackPanel5);
						AddSlider("闲置不透明度", control.IdleOpacity * 100.0, 100.0, 1.0, "%", delegate(double v)
						{
							control.IdleOpacity = v / 100.0;
						}, 0.0, stackPanel5);
						stackPanel5.Children.Add(ColorEditor("背景颜色", control.BackgroundColor, delegate(string v)
						{
							control.BackgroundColor = v;
							store.Save();
						}));
						stackPanel5.Children.Add(ColorEditor("描边颜色", control.OutlineColor, delegate(string v)
						{
							control.OutlineColor = v;
							store.Save();
						}, "#FFFFFF"));
						if (control.Joystick)
						{
							stackPanel5.Children.Add(ColorEditor("中心圆圈颜色", control.CenterColor, delegate(string v)
							{
								control.CenterColor = v;
								store.Save();
							}, "#FFFFFF"));
						}
						if (control.Joystick)
						{
							AddSlider("大小", control.Size, 400.0, 1.0, "DIP", delegate(double v)
							{
								control.Size = v;
							}, 48.0, stackPanel5);
						}
						if (control.Joystick)
						{
							stackPanel4.Children.Add(ActionEditor(0, "中心单击", edge: false, control.CenterTap, appearance: false));
							stackPanel4.Children.Add(ActionEditor(0, "中心双击", edge: false, control.CenterDoubleTap, appearance: false));
							string[] array5 = new string[8] { "↑", "↗", "→", "↘", "↓", "↙", "←", "↖" };
							labels = new string[8] { "上方向", "右上方向", "右方向", "右下方向", "下方向", "左下方向", "左方向", "左上方向" };
							int[] array6 = new int[8] { 0, 0, 1, 2, 2, 2, 1, 0 };
							int[] array7 = new int[8] { 1, 2, 2, 2, 1, 0, 0, 0 };
							Grid grid = new Grid
							{
								Width = 216.0,
								Height = 216.0,
								HorizontalAlignment = HorizontalAlignment.Center
							};
							for (int num4 = 0; num4 < 3; num4++)
							{
								grid.RowDefinitions.Add(new RowDefinition());
								grid.ColumnDefinitions.Add(new ColumnDefinition());
							}
							FontIcon fontIcon = Ui.Icon("\uece9", 32.0);
							Grid.SetRow(fontIcon, 1);
							Grid.SetColumn(fontIcon, 1);
							grid.Children.Add(fontIcon);
							editor = new ContentControl
							{
								HorizontalContentAlignment = HorizontalAlignment.Stretch,
								HorizontalAlignment = HorizontalAlignment.Stretch
							};
							arrows = new Button[8];
							for (int num5 = 0; num5 < 8; num5++)
							{
								int index = num5;
								Button button5 = Ui.Button(array5[num5], delegate
								{
									SelectDirection(index);
								});
								double width = (button5.Height = 64.0);
								button5.Width = width;
								button5.MinWidth = 64.0;
								button5.FontSize = 24.0;
								button5.CornerRadius = new CornerRadius(32.0);
								button5.Padding = new Thickness(4.0);
								button5.Margin = new Thickness(3.0);
								Grid.SetRow(button5, array6[num5]);
								Grid.SetColumn(button5, array7[num5]);
								arrows[num5] = button5;
								grid.Children.Add(button5);
							}
							stackPanel4.Children.Add(new ResponsivePair(grid, editor));
							SelectDirection(0);
						}
						else
						{
							stackPanel5.Children.Add(Ui.Text("圆形和圆角方形使用宽度作为边长；圆角按钮可分别设置宽高。", 12.0));
							ComboBox style = new ComboBox
							{
								HorizontalAlignment = HorizontalAlignment.Stretch,
								Header = Localization.T("按钮样式"),
								ItemsSource = new string[3] { "圆形", "圆角方形", "圆角按钮" }.Select(Localization.T).ToArray(),
								SelectedIndex = ((control.Style == "Square") ? 1 : ((control.Style == "Rounded") ? 2 : 0))
							};
							style.SelectionChanged += delegate
							{
								control.Style = (new string[3] { "Circle", "Square", "Rounded" })[Math.Max(0, style.SelectedIndex)];
								store.Save();
							};
							stackPanel5.Children.Add(style);
							AddSlider("宽度", control.Width, 600.0, 1.0, "DIP", delegate(double v)
							{
								control.Width = v;
							}, 20.0, stackPanel5);
							AddSlider("高度", control.Height, 400.0, 1.0, "DIP", delegate(double v)
							{
								control.Height = v;
							}, 20.0, stackPanel5);
							AddSlider("圆角", control.CornerRadius, 200.0, 1.0, "DIP", delegate(double v)
							{
								control.CornerRadius = v;
							}, 0.0, stackPanel5);
							stackPanel4.Children.Add(ActionEditor(0, "按下功能", edge: false, control.Action, appearance: false));
						}
						stackPanel5.Children.Add(ActionEditor(0, "显示样式", edge: false, control.Appearance, appearance: true, null, styleOnly: true, bareStyle: true));
						stackPanel4.Children.Add(new Expander
						{
							Header = Localization.T("样式设置"),
							Content = stackPanel5,
							HorizontalAlignment = HorizontalAlignment.Stretch,
							HorizontalContentAlignment = HorizontalAlignment.Stretch
						});
						expander.Content = stackPanel4;
					}
					void SelectDirection(int num7)
					{
						for (int i = 0; i < 8; i++)
						{
							arrows[i].Background = ((i == num7) ? Ui.Brush(0, 120, 212) : Ui.Brush(128, 128, 128, 25));
							if (i == num7)
							{
								arrows[i].Foreground = Ui.Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue);
							}
							else
							{
								arrows[i].ClearValue(Control.ForegroundProperty);
							}
						}
						editor.Content = ActionEditor(0, Localization.T(labels[num7]), edge: false, control.Directions[num7], appearance: false);
					}
				};
				if (selectedGamepadControl == control)
				{
					expander.IsExpanded = true;
					base.DispatcherQueue.TryEnqueue(delegate
					{
						expander.StartBringIntoView();
					});
				}
			}
			gamepadPage.Children.Add(Ui.Button(joystick ? "添加摇杆" : "添加按钮", delegate
			{
				Add(joystick);
			}));
		}
		gamepadPage.Children.Add(Ui.Text("防误触区域", 18.0));
		GamepadShieldSettings[] array4 = layout.Shields.ToArray();
		foreach (GamepadShieldSettings shield in array4)
		{
			TextBlock title2;
			Grid header2 = RemovableHeader(shield.Name, delegate
			{
				layout.Shields.Remove(shield);
				store.Save();
				BuildGamepad();
			}, out title2);
			Expander expander2 = new Expander
			{
				Header = header2,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch
			};
			StackPanel stackPanel3 = new StackPanel
			{
				Spacing = 10.0
			};
			TextBox name = new TextBox
			{
				Header = Localization.T("名称"),
				Text = shield.Name
			};
			name.LostFocus += delegate
			{
				shield.Name = name.Text;
				title2.Text = name.Text;
				store.Save();
			};
			stackPanel3.Children.Add(name);
			stackPanel3.Children.Add(SettingToggle("启用", () => shield.Enabled, delegate(bool v)
			{
				shield.Enabled = v;
			}));
			AddSlider("区域宽度", shield.Width, 3840.0, 1.0, "DIP", delegate(double v)
			{
				shield.Width = v;
			}, 100.0, stackPanel3);
			AddSlider("区域高度", shield.Height, 2160.0, 1.0, "DIP", delegate(double v)
			{
				shield.Height = v;
			}, 80.0, stackPanel3);
			expander2.Content = stackPanel3;
			gamepadPage.Children.Add(expander2);
		}
		gamepadPage.Children.Add(Ui.Button("添加防误触区域", delegate
		{
			if (layout.Shields.Count >= 32)
			{
				App.CurrentApp.Report("最多添加32个防误触区域。");
			}
			else
			{
				layout.Shields.Add(new GamepadShieldSettings
				{
					Enabled = true,
					Name = Localization.T("防误触区域 {0}", layout.Shields.Count + 1)
				});
				store.Save();
				BuildGamepad();
			}
		}));
		UpdateGamepadSticky();
		void Add(bool stick)
		{
			if (options.Controls.Count >= 64)
			{
				App.CurrentApp.Report("最多可添加 64 个控件。");
			}
			else
			{
				int num4 = (stick ? (options.Controls.Count((GamepadControl c) => c.Joystick) + 1) : (options.Controls.Count((GamepadControl c) => !c.Joystick) + 1));
				options.Controls.Add(new GamepadControl
				{
					Joystick = stick,
					Size = (stick ? 180 : 72),
					Name = Localization.T(stick ? "摇杆 {0}" : "按钮 {0}", num4)
				});
				selectedGamepadControl = options.Controls.Last();
				store.Save();
				BuildGamepad();
			}
		}
	}

	private void BuildDialStyle()
	{
		dialStyle.Children.Clear();
		DialSettings s = EditedDial;
		dialProfileHost.Content = ProfilePicker(dial: true);
		dialStyle.Children.Add(SettingToggle("显示虚拟 Dial", () => App.CurrentApp.DialVisible, delegate(bool v)
		{
			if (v != App.CurrentApp.DialVisible)
			{
				App.CurrentApp.ShowDial();
			}
		}));
		dialStyle.Children.Add(SettingToggle("拖动模式显示高亮描边", () => s.MoveOutline, delegate(bool v)
		{
			s.MoveOutline = v;
		}));
		dialStyle.Children.Add(SettingToggle("允许鼠标操作", () => s.MouseEnabled, delegate(bool v)
		{
			s.MouseEnabled = v;
		}));
		dialStyle.Children.Add(SettingToggle("晃动以关闭虚拟 Dial", () => s.ShakeToClose, delegate(bool v)
		{
			s.ShakeToClose = v;
		}));
		dialStyle.Children.Add(SettingToggle("触发显示提示", () => s.CenterToolHint, delegate(bool v)
		{
			s.CenterToolHint = v;
		}));
		dialStyle.Children.Add(SettingToggle("旋钮音效", () => s.RotationSound, delegate(bool v)
		{
			s.RotationSound = v;
		}));
		AddSlider("旋转灵敏度", s.Sensitivity, 4.0, 0.05, "倍", delegate(double v)
		{
			s.Sensitivity = v;
		}, 0.05, dialStyle);
		AddSlider("旋钮大小", s.Size, 320.0, 2.0, "DIP", delegate(double v)
		{
			s.Size = v;
		}, 80.0, dialStyle);
		AddSlider("透明度", s.Opacity * 100.0, 100.0, 0.01, "%", delegate(double v)
		{
			s.Opacity = v / 100.0;
		}, 0.0, dialStyle);
		AddSlider("闲置时间", s.IdleSeconds, 10.0, 0.1, "秒", delegate(double v)
		{
			s.IdleSeconds = v;
		}, 0.0, dialStyle);
		AddSlider("闲置透明度", s.IdleOpacity * 100.0, 100.0, 0.01, "%", delegate(double v)
		{
			s.IdleOpacity = v / 100.0;
		}, 0.0, dialStyle);
		dialStyle.Children.Add(ColorEditor("外圈颜色", s.OuterColor, delegate(string v)
		{
			s.OuterColor = v;
			store.Save();
		}, "#000000"));
		dialStyle.Children.Add(ColorEditor("中心颜色", s.InnerColor, delegate(string v)
		{
			s.InnerColor = v;
			store.Save();
		}, "#3A3A3A"));
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		dialStyle.Children.Add(stackPanel);
		stackPanel.Children.Add(Ui.Button("重置旋钮位置", delegate
		{
			DialSettings dialSettings = s;
			int? x = (s.Y = null);
			dialSettings.X = x;
			store.Save();
		}));
		stackPanel.Children.Add(Ui.Button("重置旋钮大小", delegate
		{
			s.Size = 164.0;
			store.Save();
			BuildDialStyle();
		}));
	}

	private void BuildDialTools()
	{
		dialTools.Children.Clear();
		DialSettings s = EditedDial;
		dialProfileHost.Content = ProfilePicker(dial: true);
		dialTools.Children.Add(Ui.Description("软件屏幕旋钮：旋转旋钮触发对应功能，拖动中间或双指拖动旋钮进行移动，长按中心或双击旋钮进入工具选择模式，单击旋钮确认。", 12.0));
		AddSlider("长按进入工具选择的时间", s.HoldSeconds, 3.0, 0.1, "秒", delegate(double v)
		{
			s.HoldSeconds = v;
		}, 0.1, dialTools);
		dialTools.Children.Add(Ui.Description("每个操作可设置提示名称和图标；名称留空时显示绑定功能。"));
		foreach (DialTool tool in s.Tools)
		{
			Expander editor = DialToolEditor(tool);
			editor.Tag = tool;
			dialTools.Children.Add(editor);
			if (tool != revealDialTool)
			{
				continue;
			}
			editor.IsExpanded = true;
			editor.Loaded += delegate
			{
				base.DispatcherQueue.TryEnqueue(delegate
				{
					if (editor.IsLoaded)
					{
						editor.StartBringIntoView();
					}
				});
			};
		}
		Button button = Ui.Button(Localization.T("＋ 添加工具"), delegate
		{
			DialTool item = new DialTool
			{
				Name = Localization.T("工具 {0}", s.Tools.Count + 1)
			};
			s.Tools.Add(item);
			revealDialTool = item;
			store.Save();
			BuildDialTools();
		});
		button.HorizontalAlignment = HorizontalAlignment.Left;
		dialTools.Children.Add(button);
	}

	private Expander DialToolEditor(DialTool item)
	{
		List<DialTool> tools = EditedDial.Tools;
		int num = tools.IndexOf(item);
		Grid grid = new Grid
		{
			ColumnSpacing = 6.0
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		TextBlock title = Ui.Text($"{num + 1}. {item.Name}", 16.0);
		title.VerticalAlignment = VerticalAlignment.Center;
		grid.Children.Add(title);
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 4.0
		};
		Grid.SetColumn(stackPanel, 1);
		grid.Children.Add(stackPanel);
		Button button = Ui.Button("上移", delegate
		{
			Move(-1);
		});
		button.IsEnabled = num > 0;
		stackPanel.Children.Add(button);
		Button button2 = Ui.Button("下移", delegate
		{
			Move(1);
		});
		button2.IsEnabled = num < tools.Count - 1;
		stackPanel.Children.Add(button2);
		stackPanel.Children.Add(Ui.Button("删除", delegate
		{
			tools.Remove(item);
			store.Save();
			BuildDialTools();
		}));
		Expander expander = new Expander
		{
			Header = grid,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			HorizontalContentAlignment = HorizontalAlignment.Stretch,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		foreach (Button item2 in stackPanel.Children.OfType<Button>())
		{
			item2.FontSize = 12.0;
			item2.Padding = new Thickness(10.0, 6.0, 10.0, 6.0);
			item2.MinHeight = 32.0;
		}
		expander.Expanding += delegate
		{
			StackPanel bindings;
			if (expander.Content == null)
			{
				StackPanel stackPanel2 = new StackPanel
				{
					Spacing = 12.0
				};
				TextBox name = new TextBox
				{
					Header = Localization.T("工具名称"),
					Text = item.Name,
					MaxLength = 40
				};
				name.TextChanged += delegate
				{
					item.Name = name.Text;
					title.Text = $"{tools.IndexOf(item) + 1}. {item.Name}";
					ScheduleSave();
				};
				stackPanel2.Children.Add(name);
				string[] ids = new string[5] { "Custom", "Volume", "Scroll", "Zoom", "Undo" };
				ComboBox preset = new ComboBox
				{
					Header = Localization.T("旋转行为"),
					ItemsSource = new string[5] { "自定义功能", "音量", "滚动", "缩放", "撤销 / 重做" }.Select(Localization.T).ToArray(),
					SelectedIndex = Math.Max(0, Array.IndexOf(ids, item.Preset)),
					HorizontalAlignment = HorizontalAlignment.Stretch
				};
				stackPanel2.Children.Add(preset);
				stackPanel2.Children.Add(ActionEditor(0, "单击中心", edge: false, item.Tap));
				bindings = new StackPanel
				{
					Spacing = 12.0
				};
				stackPanel2.Children.Add(bindings);
				preset.SelectionChanged += delegate
				{
					if (preset.SelectedIndex >= 0)
					{
						item.Preset = ids[preset.SelectedIndex];
						store.Save();
						Bindings();
					}
				};
				Bindings();
				expander.Content = stackPanel2;
			}
			void Bindings()
			{
				bindings.Children.Clear();
				bindings.Children.Add(ActionEditor(0, "顺时针", edge: false, item.Clockwise, appearance: true, null, item.Preset != "Custom"));
				bindings.Children.Add(ActionEditor(0, "逆时针", edge: false, item.CounterClockwise, appearance: true, null, item.Preset != "Custom"));
			}
		};
		return expander;
		void Move(int offset)
		{
			int num2 = tools.IndexOf(item);
			int num3 = num2 + offset;
			if (num3 >= 0 && num3 < tools.Count)
			{
				tools.RemoveAt(num2);
				tools.Insert(num3, item);
				store.Save();
				BuildDialTools();
			}
		}
	}

	private void BuildArk()
	{
		ark.Children.Clear();
		arkProfileIndex = Math.Clamp(arkProfileIndex, 0, store.Value.ArkProfiles.Count - 1);
		ArkProfile arkProfile = store.Value.ArkProfiles[arkProfileIndex];
		ArkOptions s = arkProfile.Options;
		ToggleSwitch toggleSwitch = new ToggleSwitch
		{
			Header = Localization.T("显示快捷方舟"),
			IsOn = s.ArkEnabled
		};
		BindSwitch(toggleSwitch, () => s.ArkEnabled, delegate(bool v)
		{
			s.ArkEnabled = v;
		});
		ark.Children.Add(Ui.Card(toggleSwitch));
		ToggleSwitch toggleSwitch2 = new ToggleSwitch
		{
			Header = Localization.T("拖动模式显示高亮描边"),
			IsOn = s.ArkMoveOutline
		};
		BindSwitch(toggleSwitch2, () => s.ArkMoveOutline, delegate(bool v)
		{
			s.ArkMoveOutline = v;
		});
		ark.Children.Add(Ui.Card(toggleSwitch2));
		ToggleSwitch toggleSwitch3 = new ToggleSwitch
		{
			Header = Localization.T("允许鼠标操作快捷方舟"),
			IsOn = s.ArkMouseEnabled
		};
		BindSwitch(toggleSwitch3, () => s.ArkMouseEnabled, delegate(bool v)
		{
			s.ArkMouseEnabled = v;
		});
		ark.Children.Add(Ui.Card(toggleSwitch3));
		ark.Children.Add(SettingToggle("晃动以关闭快捷方舟", () => s.ArkShakeToClose, delegate(bool v)
		{
			s.ArkShakeToClose = v;
		}));
		AddSlider("方舟大小", s.ArkSize, 128.0, 2.0, "DIP", delegate(double v)
		{
			s.ArkSize = v;
		}, 40.0, ark);
		AddSlider("方舟透明度", s.ArkOpacity * 100.0, 100.0, 0.01, "%", delegate(double v)
		{
			s.ArkOpacity = v / 100.0;
		}, 0.0, ark);
		AddSlider("闲置时间", s.ArkIdleSeconds, 10.0, 0.1, "秒", delegate(double v)
		{
			s.ArkIdleSeconds = v;
		}, 0.0, ark);
		AddSlider("闲置透明度", s.ArkIdleOpacity * 100.0, 100.0, 0.01, "%", delegate(double v)
		{
			s.ArkIdleOpacity = v / 100.0;
		}, 0.0, ark);
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		ark.Children.Add(stackPanel);
		stackPanel.Children.Add(Ui.Button("重置方舟位置", delegate
		{
			ArkOptions arkOptions = s;
			int? arkX = (s.ArkY = null);
			arkOptions.ArkX = arkX;
			store.Save();
		}));
		stackPanel.Children.Add(Ui.Button("重置方舟大小", delegate
		{
			s.ArkSize = 58.0;
			store.Save();
			BuildArk();
		}));
		ArkProfile global = arkProfile;
		ark.Children.Add(ColorEditor("方舟背景颜色", global.Color, delegate(string c)
		{
			global.Color = c;
			store.Save();
		}, "#30343B"));
		ark.Children.Add(Ui.Text("方舟手势", 18.0));
		ark.Children.Add(ActionEditor(0, "单击", edge: false, global.Tap, appearance: false));
		ark.Children.Add(ActionEditor(0, "双击", edge: false, global.DoubleTap, appearance: false));
		ark.Children.Add(ActionEditor(0, "三击", edge: false, global.TripleTap, appearance: false));
		ark.Children.Add(ActionEditor(0, "拖动", edge: false, global.Drag, appearance: false));
		ark.Children.Add(Ui.Text("长按方舟永久显示全部气泡，再次点击方舟取消。", 12.0));
		AddSlider("长按触发时间", s.ArkHoldSeconds, 3.0, 0.1, "秒", delegate(double v)
		{
			s.ArkHoldSeconds = v;
		}, 0.0, ark);
		BuildBubbleTabs();
	}

	private FrameworkElement BubblePage()
	{
		bubbleTabs.ShowHeaders = false;
		return bubbleTabs;
	}

	private void AddProfile()
	{
		store.Value.ArkProfiles.Add(new ArkProfile
		{
			Name = "未绑定应用"
		});
		arkProfileIndex = store.Value.ArkProfiles.Count - 1;
		store.Save();
		BuildBubbleTabs();
	}

	private void BuildBubbleTabs()
	{
		bubbleProfileHost.Content = ApplicationPicker(store.Value.ArkProfiles.Select((ArkProfile p, int i) => (i != 0) ? Localization.T(p.Name) : Localization.T("全局配置")).ToArray(), arkProfileIndex, delegate(int i)
		{
			arkProfileIndex = i;
			BuildArk();
		}, async delegate
		{
			LauncherEntry launcherEntry = await Ui.PickLauncherAsync(((FrameworkElement)base.Content).XamlRoot, this);
			if (launcherEntry != null)
			{
				string target = (string.IsNullOrWhiteSpace(launcherEntry.MatchTarget) ? launcherEntry.Target : launcherEntry.MatchTarget);
				int num2 = store.Value.ArkProfiles.FindIndex((ArkProfile p) => WindowCatalog.NormalizeTarget(p.Target) == WindowCatalog.NormalizeTarget(target));
				if (num2 < 0)
				{
					ArkProfile arkProfile = CopyConfig(store.Value.ArkProfiles[arkProfileIndex]);
					arkProfile.Name = launcherEntry.Name;
					arkProfile.Target = target;
					store.Value.ArkProfiles.Add(arkProfile);
					num2 = store.Value.ArkProfiles.Count - 1;
				}
				arkProfileIndex = num2;
				store.Save();
				BuildArk();
			}
		}, (arkProfileIndex == 0) ? null : ((Action)delegate
		{
			store.Value.ArkProfiles.RemoveAt(arkProfileIndex);
			arkProfileIndex = 0;
			store.Save();
			BuildArk();
		}));
		rebuildingBubbleTabs = true;
		try
		{
			bubbleCache.Clear();
			bubbleTabs.Items.Clear();
			for (int num = 0; num < store.Value.ArkProfiles.Count; num++)
			{
				bubbleTabs.Items.Add(new SettingsTab
				{
					Header = Localization.T((num == 0) ? "全局设置" : store.Value.ArkProfiles[num].Name)
				});
			}
			bubbleTabs.SelectedIndex = Math.Clamp(arkProfileIndex, 0, store.Value.ArkProfiles.Count - 1);
		}
		finally
		{
			rebuildingBubbleTabs = false;
		}
		if (!bubbleEventsWired)
		{
			bubbleEventsWired = true;
			bubbleTabs.SelectionChanged += delegate(object? _, SettingsTabChangedEventArgs e)
			{
				if (!rebuildingBubbleTabs && e.OriginalSource == bubbleTabs && bubbleTabs.SelectedIndex >= 0)
				{
					arkProfileIndex = bubbleTabs.SelectedIndex;
					base.DispatcherQueue.TryEnqueue(delegate
					{
						if (!closed)
						{
							LoadBubbleTab();
						}
					});
				}
			};
		}
		LoadBubbleTab();
	}

	private void LoadBubbleTab()
	{
		int selectedIndex = bubbleTabs.SelectedIndex;
		if (selectedIndex < 0 || selectedIndex >= store.Value.ArkProfiles.Count)
		{
			return;
		}
		SettingsTab selectedItem = bubbleTabs.SelectedItem;
		if (selectedItem == null)
		{
			return;
		}
		bubbleCache.Remove(selectedItem);
		bubbleCache.AddLast(selectedItem);
		while (bubbleCache.Count > 3)
		{
			SettingsTab value = bubbleCache.First.Value;
			bubbleCache.RemoveFirst();
			value.Content = null;
		}
		if (selectedItem.Content != null)
		{
			return;
		}
		Settings s = store.Value;
		ArkProfile profile = s.ArkProfiles[selectedIndex];
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 12.0,
			Padding = new Thickness(0.0)
		};
		if (selectedIndex > 0)
		{
			stackPanel.Children.Add(ColorEditor("专属方舟背景颜色", profile.Color, delegate(string c)
			{
				profile.Color = c;
				store.Save();
			}, "#30343B"));
			AddSlider("专属方舟不透明度", profile.Opacity * 100.0, 100.0, 0.01, "%", delegate(double v)
			{
				profile.Opacity = v / 100.0;
			}, 0.0, stackPanel);
			stackPanel.Children.Add(Ui.Button("删除此应用配置", delegate
			{
				s.ArkProfiles.Remove(profile);
				arkProfileIndex = 0;
				store.Save();
				BuildBubbleTabs();
			}));
		}
		stackPanel.Children.Add(SettingToggle("气泡跟随方舟移动", () => profile.Follow ?? true, delegate(bool v)
		{
			profile.Follow = v;
		}));
		stackPanel.Children.Add(ColorEditor("气泡背景颜色", profile.BubbleColor, delegate(string c)
		{
			profile.BubbleColor = c;
			store.Save();
		}, "#30343B"));
		AddSlider("气泡不透明度", profile.BubbleOpacity * 100.0, 100.0, 0.01, "%", delegate(double v)
		{
			profile.BubbleOpacity = v / 100.0;
		}, 0.0, stackPanel);
		stackPanel.Children.Add(Ui.Description("长按气泡固定显示，再次长按取消。未填写文字时自动显示快捷键或功能名。", 12.0));
		for (int num = 0; num < profile.Buttons.Count; num++)
		{
			ArkBubble bubble = profile.Buttons[num];
			string label = Localization.T("随行气泡 {0}", num + 1);
			TextBlock title;
			Grid header = RemovableHeader(Localization.T(label), delegate
			{
				profile.Buttons.Remove(bubble);
				store.Save();
				RefreshBubbleContent();
			}, out title);
			Expander expander = new Expander
			{
				Header = header,
				IsExpanded = false,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch
			};
			expander.Expanding += delegate
			{
				if (expander.Content == null)
				{
					StackPanel stackPanel2 = new StackPanel
					{
						Orientation = Orientation.Horizontal,
						Spacing = 8.0
					};
					ToggleSwitch toggleSwitch = new ToggleSwitch
					{
						IsOn = bubble.Pinned,
						OnContent = Localization.T("永久显示"),
						OffContent = Localization.T("永久显示")
					};
					BindSwitch(toggleSwitch, () => bubble.Pinned, delegate(bool v)
					{
						bubble.Pinned = v;
					});
					ToolTipService.SetToolTip(toggleSwitch, Localization.T("永久显示时双击气泡可持续按住绑定的键盘按键，再次双击释放。"));
					stackPanel2.Children.Add(toggleSwitch);
					expander.Content = ActionEditor(0, label, edge: false, bubble.Action, appearance: true, stackPanel2);
				}
			};
			expander.Collapsed += delegate
			{
				expander.Content = null;
			};
			stackPanel.Children.Add(expander);
		}
		stackPanel.Children.Add(Ui.Button("＋ 添加随行气泡", delegate
		{
			profile.Buttons.Add(new ArkBubble
			{
				X = 70.0,
				Y = profile.Buttons.Count % 8 * 54,
				Action = new GestureAction()
			});
			store.Save();
			RefreshBubbleContent(scrollToBottom: true);
			this.PreviewBubbles?.Invoke(profile);
		}));
		selectedItem.Content = new ScrollViewer
		{
			Content = stackPanel
		};
	}

	internal void CheckNavigation()
	{
		if ((((IntPtr)ReadStyle(Ui.Handle(this), -20)).ToInt64() & 8) != 0L)
		{
			throw new InvalidOperationException("设置窗口仍然永久置顶。");
		}
		if (navigation.MenuItems.Count != 8)
		{
			throw new InvalidOperationException("设置侧边栏分类数量错误。");
		}
		navigation.SelectedItem = navigation.MenuItems[1];
		if (!(CurrentPage is SettingsTabs settingsTabs) || settingsTabs.Items.Count != 2)
		{
			throw new InvalidOperationException("圆点页面没有两个子页面。");
		}
		navigation.SelectedItem = navigation.MenuItems[2];
		if (!(CurrentPage is SettingsTabs settingsTabs2) || settingsTabs2.Items.Count != 2)
		{
			throw new InvalidOperationException("方舟页面没有两个子页面。");
		}
		navigation.SelectedItem = navigation.MenuItems[3];
		if (!(CurrentPage is SettingsTabs settingsTabs3) || settingsTabs3.Items.Count != 2)
		{
			throw new InvalidOperationException("Dial 页面没有两个子页面。");
		}
		navigation.SelectedItem = navigation.MenuItems[4];
		if (!(CurrentPage is SettingsTabs settingsTabs4) || settingsTabs4.Items.Count != 2)
		{
			throw new InvalidOperationException("手势设置没有两个标签页。");
		}
		settingsTabs4.SelectedIndex = 1;
		settingsTabs4.SelectedIndex = 0;
		navigation.SelectedItem = navigation.MenuItems[2];
		int count = store.Value.ArkProfiles.Count;
		AddProfile();
		if (store.Value.ArkProfiles.Count != count + 1 || bubbleTabs.SelectedIndex != count)
		{
			throw new InvalidOperationException("新增应用标签未保持选中。");
		}
		store.Value.ArkProfiles.RemoveAt(count);
		arkProfileIndex = 0;
		store.Save();
		BuildBubbleTabs();
		navigation.SelectedItem = navigation.MenuItems[0];
	}

	internal async Task CheckV0810Async(Action capture)
	{
		if (navigation.MenuItems.OfType<NavigationViewItem>().Any((NavigationViewItem x) => object.Equals(x.Content, "播放器设置")))
		{
			throw new InvalidOperationException("播放器入口未删除。");
		}
		ShowPageForCapture(3, 1);
		DialTool fixture = new DialTool
		{
			Name = "行为配置验证",
			Preset = "Custom",
			Tap = new GestureAction
			{
				Kind = ActionKind.Keys
			}
		};
		store.Value.Dial.Tools.Add(fixture);
		store.Save();
		try
		{
			Expander editor = DialToolEditor(fixture);
			dialTools.Children.Insert(0, editor);
			editor.IsExpanded = true;
			dialTools.UpdateLayout();
			await Task.Delay(200);
			StackPanel detail = (StackPanel)editor.Content;
			Border border = (Border)detail.Children[2];
			StackPanel element = (StackPanel)border.Child;
			string[] array = new string[3] { "Ctrl", "Shift", "Alt" };
			foreach (string name in array)
			{
				Press(Descendants<Button>(element).Single((Button b) => object.Equals(b.Content, name)));
			}
			if (!fixture.Tap.Keys.SequenceEqual(new int[3] { 17, 16, 18 }))
			{
				throw new InvalidOperationException("快捷修饰键未加入组合。");
			}
			TextBox textBox = Descendants<TextBox>(element).Single((TextBox b) => b.IsReadOnly);
			Button button = Descendants<Button>(element).Single((Button b) => object.Equals(b.Content, "Ctrl"));
			if (VisualTreeHelper.GetParent(button) != VisualTreeHelper.GetParent(textBox) || Grid.GetColumn(button) >= Grid.GetColumn(textBox))
			{
				throw new InvalidOperationException("修饰键和输入框没有同排排列。");
			}
			foreach (Border card in new Border[1] { border }.Concat(((StackPanel)detail.Children[3]).Children.OfType<Border>()))
			{
				Expander style = Descendants<Expander>(card).Single();
				style.IsExpanded = true;
				card.UpdateLayout();
				await Task.Delay(80);
				if (!Descendants<TextBox>(card).Any((TextBox x) => object.Equals(x.Header, "显示文字")) || !Descendants<ComboBox>(card).Any((ComboBox x) => object.Equals(x.Header, "图标")))
				{
					throw new InvalidOperationException("行为缺少文字/图标编辑器。");
				}
				style.IsExpanded = false;
			}
			capture();
			((ComboBox)detail.Children[1]).SelectedIndex = 1;
			detail.UpdateLayout();
			await Task.Delay(80);
			if (((StackPanel)detail.Children[3]).Children.Count != 2)
			{
				throw new InvalidOperationException("预设旋转行为没有独立样式配置。");
			}
		}
		finally
		{
			store.Value.Dial.Tools.Remove(fixture);
			store.Save();
			BuildDialTools();
		}
		static IEnumerable<T> Descendants<T>(DependencyObject dependencyObject) where T : notnull, DependencyObject
		{
			if (dependencyObject is T val)
			{
				yield return val;
			}
			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(dependencyObject); i++)
			{
				foreach (T item in Descendants<T>(VisualTreeHelper.GetChild(dependencyObject, i)))
				{
					yield return item;
				}
			}
		}
		static void Press(Button b)
		{
			((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();
		}
	}

	internal async Task CheckDialEditorAsync()
	{
		ShowPageForCapture(3, 1);
		((IInvokeProvider)new ButtonAutomationPeer(dialTools.Children.OfType<Button>().Single()).GetPattern(PatternInterface.Invoke)).Invoke();
		await Task.Delay(150);
		DialTool added = store.Value.Dial.Tools.Last();
		try
		{
			ScrollViewer obj = (ScrollViewer)((SettingsTabs)CurrentPage).Items[1].Content;
			obj.UpdateLayout();
			obj.ChangeView(null, double.MaxValue, null, disableAnimation: true);
			await Task.Delay(150);
			Expander editor = dialTools.Children.OfType<Expander>().Single((Expander e) => e.Tag == added);
			editor.StartBringIntoView();
			editor.UpdateLayout();
			await Task.Delay(100);
			if (!editor.IsExpanded)
			{
				throw new InvalidOperationException("新增 Dial 工具未自动展开配置。");
			}
			await Task.Delay(100);
			if (!(editor.Content is StackPanel stackPanel) || stackPanel.Children.Count < 3)
			{
				throw new InvalidOperationException("Dial 自定义工具绑定编辑器未展开。");
			}
		}
		finally
		{
			store.Value.Dial.Tools.Remove(added);
			store.Save();
			BuildDialTools();
		}
	}

	internal async Task CheckV083Async()
	{
		ShowPageForCapture(2, 1);
		await Task.Delay(200);
		LoadBubbleTab();
		SettingsTab tab = bubbleTabs.SelectedItem;
		ArkProfile profile = store.Value.ArkProfiles[bubbleTabs.SelectedIndex];
		ArkBubble[] original = profile.Buttons.ToArray();
		try
		{
			FrameworkElement obj = (FrameworkElement)tab.Content;
			obj.UpdateLayout();
			Press(Descendants<Button>(obj).Single((Button b) => object.Equals(b.Content, "＋ 添加随行气泡")));
			await Task.Delay(200);
			if (tab != bubbleTabs.SelectedItem || tab.Content == null || profile.Buttons.Count != original.Length + 1)
			{
				throw new InvalidOperationException("Adding a bubble lost the selected page.");
			}
			Expander expander = ((StackPanel)((ScrollViewer)tab.Content).Content).Children.OfType<Expander>().Last();
			expander.IsExpanded = true;
			((FrameworkElement)tab.Content).UpdateLayout();
			Press(Descendants<Button>(expander).First((Button b) => object.Equals(b.Content, "删除")));
			await Task.Delay(200);
			if (tab.Content == null || profile.Buttons.Count != original.Length)
			{
				throw new InvalidOperationException("Removing a bubble lost page content.");
			}
			object cached = tab.Content;
			ShowPageForCapture(0);
			ShowPageForCapture(2, 1);
			await Task.Delay(100);
			if (cached != tab.Content)
			{
				throw new InvalidOperationException("Navigation unnecessarily rebuilt the bubble profile.");
			}
		}
		finally
		{
			profile.Buttons.Clear();
			profile.Buttons.AddRange(original);
			store.Save();
			RefreshBubbleContent();
		}
		BuildFloating();
		BuildArk();
		bool ballEnabled = store.Value.BallEnabled;
		bool arkEnabled = store.Value.ArkEnabled;
		try
		{
			store.Value.BallEnabled = !ballEnabled;
			store.Value.ArkEnabled = !arkEnabled;
			store.Save();
			string[] array = new string[2] { "显示触控圆点", "显示快捷方舟" };
			foreach (string title in array)
			{
				ToggleSwitch target2;
				(WeakReference<ToggleSwitch>, Func<bool>) tuple = switchBindings.Last<(WeakReference<ToggleSwitch>, Func<bool>)>(((WeakReference<ToggleSwitch> Control, Func<bool> Read) x) => x.Control.TryGetTarget(out target2) && object.Equals(target2.Header, title));
				if (!tuple.Item1.TryGetTarget(out var target) || target.IsOn != tuple.Item2())
				{
					throw new InvalidOperationException("External state change did not synchronize switch: " + title);
				}
			}
		}
		finally
		{
			store.Value.BallEnabled = ballEnabled;
			store.Value.ArkEnabled = arkEnabled;
			store.Save();
		}
		Ui.PresentTemporarilyTopmost(this);
		if ((((IntPtr)ReadStyle(Ui.Handle(this), -20)).ToInt64() & 8) == 0L)
		{
			throw new InvalidOperationException("Settings was not temporarily raised.");
		}
		await Task.Delay(1000);
		if ((((IntPtr)ReadStyle(Ui.Handle(this), -20)).ToInt64() & 8) != 0L)
		{
			throw new InvalidOperationException("Settings stayed topmost.");
		}
		static IEnumerable<T> Descendants<T>(DependencyObject root) where T : notnull, DependencyObject
		{
			if (root is T val)
			{
				yield return val;
			}
			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
			{
				foreach (T item in Descendants<T>(VisualTreeHelper.GetChild(root, i)))
				{
					yield return item;
				}
			}
		}
		static void Press(Button button)
		{
			((IInvokeProvider)new ButtonAutomationPeer(button).GetPattern(PatternInterface.Invoke)).Invoke();
		}
	}

	internal void ShowPageForCapture(int page, int tab = 0)
	{
		navigation.SelectedItem = navigation.MenuItems.OfType<NavigationViewItem>().Single((NavigationViewItem i) => object.Equals(i.Tag, page));
		if (CurrentPage is SettingsTabs settingsTabs)
		{
			settingsTabs.SelectedIndex = tab;
		}
		else if (CurrentPage is ScrollViewer { Content: SettingsTabs content })
		{
			content.SelectedIndex = tab;
		}
	}

	[DllImport("user32")]
	private static extern bool ClientToScreen(nint window, ref Win32.POINT point);

	internal void DarkCapture(bool dark)
	{
		((FrameworkElement)base.Content).RequestedTheme = (dark ? ElementTheme.Dark : ElementTheme.Default);
	}

	private static nint ReadStyle(nint h, int index)
	{
		return WindowLong.Get(h, index);
	}

	internal async Task CheckDiagnosticsAsync()
	{
		diagnosticTesting = true;
		diagnosticExport = null;
		diagnosticError = null;
		ShowPageForCapture(0);
		try
		{
			((IInvokeProvider)new ButtonAutomationPeer((from b in basic.Children.OfType<Border>()
				select b.Child).OfType<StackPanel>().Single((StackPanel p) => p.Children.OfType<Button>().Any((Button b) => object.Equals(b.Content, "导出诊断包"))).Children.OfType<Button>().Single((Button b) => object.Equals(b.Content, "导出诊断包"))).GetPattern(PatternInterface.Invoke)).Invoke();
			for (int i = 0; i < 100; i++)
			{
				if (diagnosticExport != null)
				{
					break;
				}
				if (diagnosticError != null)
				{
					break;
				}
				await Task.Delay(50);
			}
			if (diagnosticError != null)
			{
				throw new InvalidOperationException("诊断包导出失败", diagnosticError);
			}
			if (diagnosticExport == null)
			{
				throw new InvalidOperationException("诊断导出按钮没有生成文件。");
			}
			using ZipArchive zipArchive = ZipFile.OpenRead(diagnosticExport);
			if (zipArchive.GetEntry("application.json") == null || zipArchive.GetEntry("application.log") == null || zipArchive.GetEntry("summary.json") == null)
			{
				throw new InvalidOperationException("诊断包缺少应用级信息。");
			}
		}
		finally
		{
			diagnosticTesting = false;
			if (diagnosticExport != null && File.Exists(diagnosticExport))
			{
				File.Delete(diagnosticExport);
			}
		}
	}

	internal async Task CheckGestureEditorAsync(Action capture)
	{
		ShowPageForCapture(4);
		int before = store.Value.MultiTouch.Bindings.Count;
		((IInvokeProvider)new ButtonAutomationPeer(multiTouch.Children.OfType<Button>().Single((Button b) => object.Equals(b.Content, "添加手势绑定"))).GetPattern(PatternInterface.Invoke)).Invoke();
		multiTouch.UpdateLayout();
		await Task.Delay(100);
		MultiTouchBinding binding = store.Value.MultiTouch.Bindings.Last();
		try
		{
			if (!(multiTouch.Children.Last() is Button button) || !object.Equals(button.Content, "添加手势绑定"))
			{
				throw new InvalidOperationException("新增绑定未位于添加按钮上方。");
			}
			if (store.Value.MultiTouch.Bindings.Count != before + 1)
			{
				throw new InvalidOperationException("自定义手势添加失败。");
			}
			binding.Gesture = "Custom";
			BuildMultiTouch();
			multiTouch.UpdateLayout();
			Descendants<ComboBox>(multiTouch).First((ComboBox b) => object.Equals(b.Header, "手指数")).SelectedItem = 5;
			multiTouch.UpdateLayout();
			await Task.Delay(100);
			if (binding.Fingers != 5)
			{
				throw new InvalidOperationException("五指手势选择失败。");
			}
			Descendants<ComboBox>(multiTouch).First((ComboBox b) => object.Equals(b.Header, "手指数")).SelectedItem = 1;
			multiTouch.UpdateLayout();
			await Task.Delay(100);
			if (Descendants<ComboBox>(multiTouch).First((ComboBox b) => object.Equals(b.Header, "手势")).Items.Count != 2)
			{
				throw new InvalidOperationException("单指显示了不支持的点击或滑动选项。");
			}
			binding.Fingers = 2;
			binding.Points = new List<GesturePoint>
			{
				new GesturePoint(0.0, 0.0),
				new GesturePoint(0.0, 100.0),
				new GesturePoint(100.0, 100.0)
			};
			store.Save();
			BuildMultiTouch();
			multiTouch.UpdateLayout();
			await Task.Delay(100);
			GestureDrawingEditor editor = Descendants<GestureDrawingEditor>(multiTouch).Single();
			editor.StartBringIntoView();
			await Task.Delay(250);
			if (Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--panel-touch-smoke" || a == "--touch-smoke"))
			{
				binding.Points.Clear();
				Canvas canvas = Descendants<Canvas>(editor).Single();
				Point point = canvas.TransformToVisual((FrameworkElement)base.Content).TransformPoint(new Point(32f, 32f));
				double rasterizationScale = canvas.XamlRoot.RasterizationScale;
				Win32.POINT point2 = new Win32.POINT((int)(point.X * rasterizationScale), (int)(point.Y * rasterizationScale));
				ClientToScreen(Ui.Handle(this), ref point2);
				await TouchSmoke.DrawShape(point2, 2, rasterizationScale, Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--v087-smoke"));
				((IInvokeProvider)new ButtonAutomationPeer(editor.Children.OfType<Button>().Single((Button b) => object.Equals(b.Content, "保存形状"))).GetPattern(PatternInterface.Invoke)).Invoke();
				await Task.Delay(100);
				if (binding.Paths.Count != 2 || !binding.Paths.Any((List<GesturePoint> p) => p.Count > 2))
				{
					capture();
					throw new InvalidOperationException("双指触摸绘制未能保存模板。" + editor.InspectRecording?.Invoke());
				}
			}
			capture();
		}
		finally
		{
			store.Value.MultiTouch.Bindings.Remove(binding);
			store.Save();
			BuildMultiTouch();
		}
		static IEnumerable<T> Descendants<T>(DependencyObject element) where T : notnull, DependencyObject
		{
			if (element is T val)
			{
				yield return val;
			}
			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++)
			{
				foreach (T item in Descendants<T>(VisualTreeHelper.GetChild(element, i)))
				{
					yield return item;
				}
			}
		}
	}

	private static Grid RemovableHeader(string label, Action remove, out TextBlock title)
	{
		Grid obj = new Grid
		{
			ColumnSpacing = 8.0,
			ColumnDefinitions = 
			{
				new ColumnDefinition(),
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			}
		};
		title = Ui.Text(label, 16.0);
		title.VerticalAlignment = VerticalAlignment.Center;
		obj.Children.Add(title);
		Button button = Ui.Button("删除", remove);
		button.FontSize = 12.0;
		button.Padding = new Thickness(10.0, 6.0, 10.0, 6.0);
		button.MinHeight = 32.0;
		AutomationProperties.SetName(button, Localization.T("删除") + " " + label);
		Grid.SetColumn(button, 1);
		obj.Children.Add(button);
		return obj;
	}

	private void QueueGestureRebuild()
	{
		if (!gestureRebuildQueued)
		{
			gestureRebuildQueued = true;
			if (!base.DispatcherQueue.TryEnqueue(delegate
			{
				gestureRebuildQueued = false;
				if (!closed)
				{
					BuildMultiTouch();
				}
			}))
			{
				gestureRebuildQueued = false;
			}
		}
	}

	private void BuildMultiTouch()
	{
		int generation = ++gestureEditorGeneration;
		multiTouch.Children.Clear();
		MultiTouchSettings options = EditedGestures;
		Dictionary<MultiTouchBinding, bool> expansion = gestureExpansion.GetOrCreateValue(options);
		gestureProfileHost.Content = ProfilePicker(dial: false);
		multiTouch.Children.Add(SettingToggle("启用全局手势", () => store.Value.MultiTouch.Enabled, delegate(bool v)
		{
			store.Value.MultiTouch.Enabled = v;
		}));
		AddSlider("匹配严格度（30 宽松 — 90 严格）", options.MatchStrictness, 90.0, 1.0, "", delegate(double v)
		{
			options.MatchStrictness = v;
		}, 30.0, multiTouch);
		AddSlider("最大手指间距（任意两指起点）", options.MaxSpacing, 3000.0, 1.0, "DIP", delegate(double v)
		{
			options.MaxSpacing = v;
		}, 40.0, multiTouch);
		AddSlider("长按时间", options.HoldSeconds, 3.0, 0.01, "秒", delegate(double v)
		{
			options.HoldSeconds = v;
		}, 0.1, multiTouch);
		multiTouch.Children.Add(Ui.Description("各指需在 220 毫秒内落下；同一屏幕、最后一指松开后触发。支持双击、三击，连击间隔 400 毫秒；滑动支持上、下、左、右。", 12.0));
		multiTouch.Children.Add(Ui.Description("单指仅支持长按和自定义形状。按所选手指数同时落下，每指分别显示和识别轨迹，支持一指点击、另一指滑动；鼠标仍可描绘共同中心轨迹。允许少量形状偏差。形状保留方向，松开最后一指后触发。", 12.0));
		foreach (MultiTouchBinding item in options.Bindings.OrderBy((MultiTouchBinding b) => b.Fingers))
		{
			MultiTouchBinding binding = item;
			TextBlock title;
			Grid header = RemovableHeader(BindingTitle(), delegate
			{
				options.Bindings.Remove(binding);
				expansion.Remove(binding);
				store.Save();
				BuildMultiTouch();
			}, out title);
			Expander groupEditor = new Expander
			{
				Header = header,
				Content = (expansion.GetValueOrDefault(binding, defaultValue: true) ? BuildGroup() : null),
				IsExpanded = expansion.GetValueOrDefault(binding, defaultValue: true),
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch
			};
			groupEditor.Expanding += delegate
			{
				expansion[binding] = true;
				Expander expander = groupEditor;
				if (expander.Content == null)
				{
					object obj = (expander.Content = BuildGroup());
				}
			};
			groupEditor.Collapsed += delegate
			{
				expansion[binding] = false;
				groupEditor.Content = null;
			};
			multiTouch.Children.Add(groupEditor);
			string BindingTitle()
			{
				return Localization.T("{0} 指 · {1}", binding.Fingers, (string.IsNullOrWhiteSpace(binding.Name) || binding.Name == "自定义形状") ? Localization.T(Label(binding.Gesture)) : binding.Name);
			}
			FrameworkElement BuildGroup()
			{
				StackPanel stackPanel = new StackPanel
				{
					Spacing = 8.0
				};
				StackPanel stackPanel2 = new StackPanel
				{
					Spacing = 8.0
				};
				TextBox nameBox = new TextBox
				{
					Header = Localization.T("手势名称"),
					PlaceholderText = Localization.T("{0} 指 · {1}", binding.Fingers, Localization.T(Label(binding.Gesture))),
					Text = ((binding.Name == "自定义形状") ? "" : binding.Name),
					MaxLength = 40
				};
				nameBox.TextChanged += delegate
				{
					binding.Name = nameBox.Text;
					title.Text = BindingTitle();
					ScheduleSave();
				};
				stackPanel2.Children.Add(nameBox);
				ComboBox count = new ComboBox
				{
					HorizontalAlignment = HorizontalAlignment.Stretch,
					Header = Localization.T("手指数"),
					ItemsSource = new int[5] { 1, 2, 3, 4, 5 },
					SelectedItem = binding.Fingers
				};
				string[] kinds = MultiTouchSettings.Gestures.Where((string k) => MultiTouchSettings.Allowed(binding.Fingers, k)).ToArray();
				ComboBox gesture = new ComboBox
				{
					HorizontalAlignment = HorizontalAlignment.Stretch,
					Header = Localization.T("手势"),
					ItemsSource = kinds.Select((string k) => Localization.T(Label(k))).ToArray(),
					SelectedIndex = Array.IndexOf(kinds, binding.Gesture)
				};
				count.SelectionChanged += delegate
				{
					if (count.SelectedItem is int fingers)
					{
						Update(fingers, MultiTouchSettings.Allowed(fingers, binding.Gesture) ? binding.Gesture : "Hold");
					}
				};
				gesture.SelectionChanged += delegate
				{
					if (gesture.SelectedIndex >= 0)
					{
						Update(binding.Fingers, kinds[gesture.SelectedIndex]);
					}
				};
				stackPanel2.Children.Add(count);
				stackPanel2.Children.Add(gesture);
				if (binding.Gesture == "Custom")
				{
					stackPanel2.Children.Add(new GestureDrawingEditor(binding, store));
				}
				stackPanel2.Children.Add(ActionEditor(0, "绑定功能", edge: false, binding.Action, appearance: false));
				stackPanel.Children.Add(Ui.Card(stackPanel2));
				return stackPanel;
			}
			void Update(int fingers, string kind)
			{
				if (generation == gestureEditorGeneration && options.Bindings.Contains(binding) && (binding.Fingers != fingers || !(binding.Gesture == kind)))
				{
					gestureEditorGeneration++;
					if (kind != "Custom" && options.Bindings.Any((MultiTouchBinding b) => b != binding && b.Fingers == fingers && b.Gesture == kind))
					{
						App.CurrentApp.Report("该手势已有绑定，请修改已有项。");
						QueueGestureRebuild();
					}
					else
					{
						binding.Fingers = fingers;
						binding.Gesture = kind;
						expansion[binding] = true;
						store.Save();
						QueueGestureRebuild();
					}
				}
			}
		}
		multiTouch.Children.Add(Ui.Button("添加手势绑定", delegate
		{
			int fingers;
			for (fingers = 1; fingers <= 5; fingers++)
			{
				foreach (string kind in MultiTouchSettings.Gestures.Where((string k) => k != "Custom" && MultiTouchSettings.Allowed(fingers, k)))
				{
					if (!options.Bindings.Any((MultiTouchBinding b) => b.Fingers == fingers && b.Gesture == kind))
					{
						options.Bindings.Add(new MultiTouchBinding
						{
							Fingers = fingers,
							Gesture = kind
						});
						store.Save();
						BuildMultiTouch();
						return;
					}
				}
			}
			options.Bindings.Add(new MultiTouchBinding
			{
				Fingers = 1,
				Gesture = "Custom"
			});
			store.Save();
			BuildMultiTouch();
		}));
		static string Label(string kind)
		{
			return kind switch
			{
				"Tap" => "点击", 
				"DoubleTap" => "双击", 
				"TripleTap" => "三击", 
				"Hold" => "长按", 
				"Up" => "上滑", 
				"Down" => "下滑", 
				"Left" => "左滑", 
				"Right" => "右滑", 
				_ => "自定义形状", 
			};
		}
	}

	internal void CheckV0822Settings()
	{
		ShowPageForCapture(8);
		foreach (Expander item in gamepadPage.Children.OfType<Expander>().Where(delegate(Expander e)
		{
			if (e.Header is FrameworkElement { Tag: var tag })
			{
				string name = tag as string;
				if (name != null)
				{
					return store.Value.Gamepad.Controls.Any((GamepadControl c) => c.Name == name);
				}
			}
			return false;
		}))
		{
			item.IsExpanded = true;
			StackPanel stackPanel = (StackPanel)item.Content;
			Expander expander = stackPanel.Children.OfType<Expander>().Single((Expander e) => object.Equals(e.Header, "样式设置"));
			int num = stackPanel.Children.IndexOf(expander);
			if (num < 1 || !stackPanel.Children.Take(num).Any((UIElement e) => (e is Border || e is ResponsivePair) ? true : false))
			{
				throw new InvalidOperationException("功能设置不在样式之前");
			}
			expander.IsExpanded = true;
		}
		ShowPageForCapture(5);
		if (!(CurrentPage is ScrollViewer { Content: SettingsTabs content }) || content.Items.Count != 2)
		{
			throw new InvalidOperationException("键盘和TT触摸板未分离");
		}
		content.SelectedIndex = 1;
	}

	internal async Task CheckV0821Settings()
	{
		ShowPageForCapture(8);
		GamepadSettings options = store.Value.Gamepad;
		GamepadControl original = options.Controls.Last();
		SelectGamepadControl(original);
		await Task.Delay(80);
		Expander expander = gamepadPage.Children.OfType<Expander>().Single((Expander e) => e.Header is FrameworkElement frameworkElement && object.Equals(frameworkElement.Tag, original.Name));
		if (!expander.IsExpanded || !(expander.Content is StackPanel))
		{
			throw new InvalidOperationException("编辑控件没有定位到设置");
		}
		Press(((StackPanel)((Grid)expander.Header).Children[1]).Children.OfType<Button>().Single((Button b) => object.Equals(b.Content, "复制")));
		await Task.Delay(80);
		GamepadControl gamepadControl = options.Controls.Last();
		if (gamepadControl == original || gamepadControl.Action == original.Action || gamepadControl.Name != original.Name + " 副本")
		{
			throw new InvalidOperationException("复制控件没有独立绑定");
		}
		Press(gamepadPage.Children.OfType<Button>().Single((Button b) => object.Equals(b.Content, "添加按钮")));
		await Task.Delay(100);
		if (selectedGamepadControl != options.Controls.Last() || !gamepadPage.Children.OfType<Expander>().Any((Expander e) => e.IsExpanded && e.Header is FrameworkElement frameworkElement && object.Equals(frameworkElement.Tag, selectedGamepadControl.Name)))
		{
			throw new InvalidOperationException("新增按钮没有保持展开定位");
		}
		ShowPageForCapture(1, 1);
		base.AppWindow.Resize(new SizeInt32((int)((double)(1250 * Win32.GetDpiForWindow(Ui.Handle(this))) / 96.0), 900));
		await Task.Delay(180);
		ResponsivePair pair = gestures.Children.OfType<ResponsivePair>().Single();
		if (Grid.GetColumn((FrameworkElement)pair.Children[1]) != 1)
		{
			throw new InvalidOperationException("宽屏没有左右排列，内容宽度=" + pair.ActualWidth);
		}
		base.AppWindow.Resize(new SizeInt32(720, 800));
		await Task.Delay(180);
		if (Grid.GetRow((FrameworkElement)pair.Children[1]) != 1)
		{
			throw new InvalidOperationException("窄屏没有上下排列");
		}
		ShowPageForCapture(2, 1);
		ArkProfile profile = store.Value.ArkProfiles[0];
		int before = profile.Buttons.Count;
		Press(((StackPanel)((ScrollViewer)bubbleTabs.SelectedItem.Content).Content).Children.OfType<Button>().Single((Button b) => object.Equals(b.Content, "＋ 添加随行气泡")));
		await Task.Delay(200);
		if (profile.Buttons.Count != before + 1)
		{
			throw new InvalidOperationException("新增气泡失败");
		}
		static void Press(Button b)
		{
			((IInvokeProvider)new ButtonAutomationPeer(b).GetPattern(PatternInterface.Invoke)).Invoke();
		}
	}

	internal void ScrollGamepadDirection()
	{
		gamepadPage.Children.OfType<Expander>().First().StartBringIntoView();
	}

	internal void ScrollWheelBinding()
	{
		gestures.Children.OfType<ResponsivePair>().Single().Selector.StartBringIntoView();
	}

	internal void CheckV0820Settings()
	{
		ShowPageForCapture(1, 1);
		WheelDirectionSelector wheelDirectionSelector = (WheelDirectionSelector)gestures.Children.OfType<ResponsivePair>().Single().Selector;
		for (int i = 0; i < 8; i++)
		{
			wheelDirectionSelector.Select(i);
		}
		wheelDirectionSelector.Select(0);
		ShowPageForCapture(8);
		string[] array = (from b in gamepadPage.Children.OfType<StackPanel>().First().Children.OfType<Button>()
			select b.Content?.ToString()).ToArray();
		if (array[0] != "编辑布局" || array[1] != "保存布局" || array[^1] != "新建布局")
		{
			throw new InvalidOperationException("布局按钮顺序错误");
		}
		UIElement[] array2 = gamepadPage.Children.ToArray();
		string[] array3 = new string[3] { "添加摇杆", "添加按钮", "添加防误触区域" };
		foreach (string text in array3)
		{
			int num2 = Array.FindIndex(array2, (UIElement x) => x is Button button && object.Equals(button.Content, text));
			if (num2 < 1 || !(array2[num2 - 1] is Expander))
			{
				throw new InvalidOperationException(text + "没有位于已有控件下方");
			}
		}
	}

	internal void CheckV0819Settings()
	{
		ShowPageForCapture(8);
		int[] array = (from n in navigation.MenuItems.OfType<NavigationViewItem>()
			select (int)n.Tag).ToArray();
		if (Array.IndexOf(array, 8) + 1 != Array.IndexOf(array, 5))
		{
			throw new InvalidOperationException("菜单顺序错误");
		}
		Expander[] array2 = gamepadPage.Children.OfType<Expander>().ToArray();
		Expander[] array3 = array2;
		for (int num = 0; num < array3.Length; num++)
		{
			array3[num].IsExpanded = true;
		}
		if (array2.Any((Expander e) => e.Content == null))
		{
			throw new InvalidOperationException("绑定编辑器未展开");
		}
	}

	internal void CheckV0818Settings()
	{
		ShowPageForCapture(5);
		if (!((CurrentPage as ScrollViewer)?.Content is SettingsTabs settingsTabs) || settingsTabs.Items.Count != 1 || !object.Equals(settingsTabs.Items[0].Header, "触摸键盘 · 实验性"))
		{
			throw new InvalidOperationException("键盘页面仍包含触摸板或缺少实验性标记。");
		}
		ShowPageForCapture(3);
		if (!dialStyle.Children.OfType<Grid>().Any() && dialStyle.Children.Count < 10)
		{
			throw new InvalidOperationException("旋钮设置不完整。");
		}
		ShowPageForCapture(4);
		ShowPageForCapture(1, 1);
		ShowPageForCapture(8);
		if (gamepadPage.Children.OfType<Expander>().Count() != store.Value.Gamepad.Controls.Count)
		{
			throw new InvalidOperationException("摇杆和按钮编辑器数量错误。");
		}
	}

	internal async Task CheckIndicatorSettingsAsync()
	{
		ShowPageForCapture(4);
		((FrameworkElement)base.Content).UpdateLayout();
		Descendants<SettingsTabs>(base.Content).Single((SettingsTabs t) => t.Items.Any((SettingsTab i) => object.Equals(i.Header, "屏幕边缘手势"))).SelectedIndex = 1;
		await Task.Delay(100);
		string[] array = new string[5] { "向左滑动", "向右滑动", "单击", "双击", "向下滑动" };
		foreach (string name in array)
		{
			if (!Descendants<TextBlock>(edges).Any((TextBlock t) => t.Text == name))
			{
				throw new InvalidOperationException("缺少指示器绑定：" + name);
			}
		}
		Descendants<TextBlock>(edges).Single((TextBlock t) => t.Text == "全面屏指示器").StartBringIntoView(new BringIntoViewOptions
		{
			AnimationDesired = false
		});
		await Task.Delay(150);
		static IEnumerable<T> Descendants<T>(DependencyObject root) where T : notnull, DependencyObject
		{
			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
			{
				DependencyObject child = VisualTreeHelper.GetChild(root, i);
				if (child is T val)
				{
					yield return val;
				}
				foreach (T item in Descendants<T>(child))
				{
					yield return item;
				}
			}
		}
	}

	private void BuildEdges()
	{
		edges.Children.Clear();
		edges.Children.Add(SettingToggle("启用顶部／底部屏幕边缘手势", () => store.Value.MultiTouch.EdgeEnabled, delegate(bool v)
		{
			store.Value.MultiTouch.EdgeEnabled = v;
			BuildEdges();
		}));
		edges.Children.Add(Ui.Description("从屏幕最上沿向下滑动，或从最下沿向上滑动。", 12.0));
		string[] array = new string[5] { "左侧向内滑动", "右上向内滑动", "底部向上滑动", "右侧向内滑动", "左上向内滑动" };
		for (int num = 0; num < array.Length; num++)
		{
			Border item = ActionEditor(num, array[num], edge: true);
			edges.Children.Add(item);
		}
		edges.Children.Add(Ui.Description("底部绑定全面屏任务列表：短滑唤醒指示器，继续上滑展开卡片；背景模糊超过三分之一后松手保留列表，从屏幕底边向上拖动到 450 DIP 立即打开开始菜单。指示器左右滑动默认切换应用，闲置 3 秒隐藏。关闭开关保留绑定。", 12.0));
		edges.Children.Add(Ui.Text("全面屏指示器", 20.0));
		(string, string)[] array2 = new(string, string)[5]
		{
			("Left", "向左滑动"),
			("Right", "向右滑动"),
			("Tap", "单击"),
			("DoubleTap", "双击"),
			("Down", "向下滑动")
		};
		for (int num2 = 0; num2 < array2.Length; num2++)
		{
			var (key, label) = array2[num2];
			edges.Children.Add(ActionEditor(0, label, edge: false, store.Value.IndicatorActions[key], appearance: false));
		}
	}

	private Border ActionEditor(int index, string label, bool edge = false, GestureAction? custom = null, bool appearance = true, FrameworkElement? accessory = null, bool styleOnly = false, bool bareStyle = false)
	{
		List<GestureAction> list = (edge ? store.Value.EdgeGestures : store.Value.Gestures);
		GestureAction action = custom ?? list[index];
		StackPanel stack = new StackPanel
		{
			Spacing = 12.0
		};
		Grid grid = new Grid
		{
			ColumnSpacing = 12.0
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		TextBlock textBlock = Ui.Text(label, 18.0);
		textBlock.VerticalAlignment = VerticalAlignment.Center;
		grid.Children.Add(textBlock);
		Button button = Ui.Button("恢复默认", delegate
		{
			if (edge)
			{
				store.Value.EdgeGestures[index] = Settings.EdgeDefaults()[index];
			}
			else
			{
				store.Value.Gestures[index] = Settings.Defaults()[index];
			}
			store.Save();
			if (edge)
			{
				BuildEdges();
			}
			else if (index < 8)
			{
				BuildGestures();
			}
			else
			{
				BuildFloating();
			}
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		ToggleSwitch enabled = new ToggleSwitch
		{
			IsOn = action.Enabled,
			OnContent = "",
			OffContent = "",
			MinWidth = 44.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		AutomationProperties.SetName(enabled, Localization.T("{0} 开关", Localization.T(label)));
		enabled.Toggled += delegate
		{
			action.Enabled = enabled.IsOn;
			store.Save();
		};
		Grid.SetColumn(enabled, 2);
		grid.Children.Add(enabled);
		Grid.SetColumn(button, 1);
		if (custom == null)
		{
			grid.Children.Add(button);
		}
		if (accessory != null)
		{
			Grid.SetColumn(accessory, 1);
			grid.Children.Add(accessory);
		}
		stack.Children.Add(grid);
		StackPanel style = new StackPanel
		{
			Spacing = 12.0
		};
		Action resetStyle = delegate
		{
			action.Label = "";
			action.Icon = "";
			action.IconPath = "";
		};
		if (bareStyle)
		{
			BuildStyle();
			return Ui.Card(style);
		}
		ComboBox kind = new ComboBox
		{
			ItemsSource = new string[4] { "不绑定", "内置功能", "启动应用", "自定义按键" }.Select(Localization.T).ToArray(),
			SelectedIndex = (int)action.Kind,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			MinHeight = 44.0
		};
		StackPanel detail = new StackPanel
		{
			Spacing = 12.0
		};
		if (!styleOnly)
		{
			stack.Children.Add(kind);
			stack.Children.Add(detail);
		}
		kind.SelectionChanged += delegate
		{
			resetStyle();
			action.Kind = (ActionKind)kind.SelectedIndex;
			action.Command = ((action.Kind == ActionKind.BuiltIn) ? "ControlCenter" : "");
			store.Save();
			Rebuild();
		};
		if (appearance && ((!edge && index < 8) || custom != null))
		{
			Expander expander = new Expander
			{
				Header = Localization.T("样式设置"),
				Content = style,
				HorizontalContentAlignment = HorizontalAlignment.Stretch,
				HorizontalAlignment = HorizontalAlignment.Stretch
			};
			expander.Expanding += delegate
			{
				BuildStyle();
			};
			stack.Children.Add(expander);
		}
		Rebuild();
		return Ui.Card(stack);
		void BuildStyle()
		{
			if (style.Children.Count <= 0 && appearance && ((!edge && index < 8) || custom != null))
			{
				if (custom == null && !edge && index < 8)
				{
					ComboBox destination = new ComboBox
					{
						Header = Localization.T("自定义图标与文字显示位置"),
						ItemsSource = new string[4] { "不显示", "菜单扇区", "屏幕中间提示区", "两者都显示" }.Select(Localization.T).ToArray(),
						SelectedIndex = Math.Clamp(action.AppearanceDestination, 0, 3),
						HorizontalAlignment = HorizontalAlignment.Stretch
					};
					destination.SelectionChanged += delegate
					{
						action.AppearanceDestination = destination.SelectedIndex;
						store.Save();
					};
					style.Children.Add(destination);
				}
				(string Name, string Glyph)[] icons = new(string, string)[25]
				{
					("无图标", ""),
					("任务视图", "\ue7f4"),
					("切换应用", "\ue8b0"),
					("桌面", "\ue770"),
					("控制中心", "\ue713"),
					("键盘", "\ue765"),
					("复制", "\ue8c8"),
					("粘贴", "\ue77f"),
					("撤销", "\ue7a7"),
					("搜索", "\ue721"),
					("主页", "\ue80f"),
					("音量", "\ue767"),
					("静音", "\ue74f"),
					("播放", "\ue768"),
					("上一首", "\ue892"),
					("下一首", "\ue893"),
					("截图", "\ue8c6"),
					("相机", "\ue722"),
					("锁定", "\ue72e"),
					("文件夹", "\ue8b7"),
					("星标", "\ue734"),
					("上", "\ue74a"),
					("下", "\ue74b"),
					("左", "\ue72b"),
					("右", "\ue72a")
				};
				ComboBox picker = new ComboBox
				{
					Header = Localization.T("图标"),
					HorizontalAlignment = HorizontalAlignment.Stretch,
					MinHeight = 44.0
				};
				(string, string)[] array = icons;
				for (int num = 0; num < array.Length; num++)
				{
					(string, string) tuple = array[num];
					StackPanel stackPanel = new StackPanel
					{
						Orientation = Orientation.Horizontal,
						Spacing = 12.0
					};
					stackPanel.Children.Add(Ui.Icon(tuple.Item2));
					stackPanel.Children.Add(Ui.Text(tuple.Item1));
					picker.Items.Add(stackPanel);
				}
				picker.SelectedIndex = Math.Max(0, Array.FindIndex(icons, ((string Name, string Glyph) x) => x.Glyph == action.Icon));
				picker.SelectionChanged += delegate
				{
					if (picker.SelectedIndex >= 0)
					{
						action.Icon = icons[picker.SelectedIndex].Glyph;
						action.IconPath = "";
						action.IconFont = IconFontResolver.PreferredFontFamilyName;
						store.Save();
					}
				};
				style.Children.Add(picker);
				style.Children.Add(Ui.Button("上传图标 / 选择系统图标字体…", async delegate
				{
					await SectorIconPicker.ShowAsync(this, stack.XamlRoot, action, store);
				}));
				TextBox caption = new TextBox
				{
					Header = Localization.T("显示文字"),
					PlaceholderText = Localization.T("留空使用绑定的功能名 / 快捷键"),
					Text = action.Label,
					MaxLength = 20
				};
				resetStyle = delegate
				{
					action.Label = "";
					action.Icon = "";
					action.IconPath = "";
					caption.Text = "";
					picker.SelectedIndex = 0;
				};
				caption.TextChanged += delegate
				{
					action.Label = caption.Text;
					ScheduleSave();
				};
				style.Children.Add(caption);
				style.Children.Add(ColorEditor("文字颜色", action.TextColor, delegate(string c)
				{
					action.TextColor = c;
					store.Save();
				}));
				AddSlider("文字不透明度", action.TextOpacity * 100.0, 100.0, 1.0, "%", delegate(double x)
				{
					action.TextOpacity = x / 100.0;
				}, 0.0, style);
			}
		}
		void Rebuild()
		{
			detail.Children.Clear();
			TextBox entry;
			if (action.Kind == ActionKind.BuiltIn)
			{
				ComboBox command = new ComboBox
				{
					ItemsSource = BuiltIns.Select(((string Id, string Name) x) => Localization.T(x.Name)).ToArray(),
					SelectedIndex = Array.FindIndex(BuiltIns, ((string Id, string Name) x) => x.Id == action.Command),
					HorizontalAlignment = HorizontalAlignment.Stretch,
					MinHeight = 44.0
				};
				command.SelectionChanged += delegate
				{
					if (command.SelectedIndex >= 0)
					{
						action.Command = BuiltIns[command.SelectedIndex].Id;
						resetStyle();
						store.Save();
					}
				};
				detail.Children.Add(command);
			}
			else if (action.Kind == ActionKind.Launch)
			{
				TextBox path = new TextBox
				{
					Text = action.Command,
					PlaceholderText = Localization.T("EXE 路径 / UWP AUMID / URI"),
					MinHeight = 44.0
				};
				path.TextChanged += delegate
				{
					action.Command = path.Text;
					resetStyle();
					ScheduleSave();
				};
				StackPanel stackPanel = new StackPanel
				{
					Orientation = Orientation.Horizontal,
					Spacing = 8.0
				};
				stackPanel.Children.Add(Ui.Button("选择应用…", async delegate
				{
					LauncherEntry launcherEntry = await Ui.PickLauncherAsync(stack.XamlRoot, this);
					if (launcherEntry != null)
					{
						path.Text = launcherEntry.Target;
					}
				}));
				stackPanel.Children.Add(Ui.Button("浏览 EXE…", async delegate
				{
					string text = await TouchFilePicker.Pick(this, "选择应用程序", ".exe");
					if (text != null)
					{
						path.Text = text;
					}
				}));
				detail.Children.Add(stackPanel);
				detail.Children.Add(path);
			}
			else if (action.Kind == ActionKind.Keys)
			{
				(string Name, int Code)[] array = KeyCatalog.Special.Where(delegate((string Name, int Code) x)
				{
					int item = x.Code;
					return (uint)(item - 16) > 2u && !FullKeyboardCodes.Contains(x.Code);
				}).ToArray();
				entry = new TextBox
				{
					Text = KeyCatalog.Format(action.Keys),
					IsReadOnly = true,
					PlaceholderText = Localization.T("点击这里，然后持续按下所需按键"),
					MinHeight = 48.0,
					TextWrapping = TextWrapping.Wrap
				};
				entry.PreviewKeyDown += delegate(object _, KeyRoutedEventArgs e)
				{
					Add((int)e.Key);
					e.Handled = true;
				};
				entry.KeyUp += delegate(object _, KeyRoutedEventArgs e)
				{
					e.Handled = true;
				};
				entry.LostFocus += delegate
				{
					store.Save();
				};
				detail.Children.Add(entry);
				detail.Children.Add(Ui.Button("清空", delegate
				{
					action.Keys.Clear();
					entry.Text = "";
					store.Save();
				}));
				WrapPanel wrapPanel = new WrapPanel
				{
					HorizontalSpacing = 4.0,
					VerticalSpacing = 4.0,
					HorizontalAlignment = HorizontalAlignment.Left
				};
				(string, int)[] array2 = array;
				for (int num = 0; num < array2.Length; num++)
				{
					(string Name, int Code) special = array2[num];
					Button button2 = new Button
					{
						Content = new TextBlock
						{
							Text = Localization.T(special.Name),
							FontSize = 10.0,
							TextTrimming = TextTrimming.CharacterEllipsis
						},
						Width = 74.0,
						Height = 32.0,
						MinHeight = 32.0,
						Padding = new Thickness(4.0, 0.0, 4.0, 0.0),
						CornerRadius = new CornerRadius(6.0)
					};
					button2.Click += delegate
					{
						Add(special.Code);
					};
					wrapPanel.Children.Add(button2);
				}
				TextBlock textBlock2 = Ui.Text("额外功能键", 12.0);
				textBlock2.HorizontalAlignment = HorizontalAlignment.Left;
				detail.Children.Add(textBlock2);
				detail.Children.Add(wrapPanel);
				detail.Children.Add(SimulatedKeyboard(Add));
				detail.Children.Add(Ui.Description("所有按键作为一组组合键按下，再逆序释放。Win 等系统保留键可能仍触发 Windows 行为，可通过上方功能键添加。亮度键通过显示器接口执行。", 12.0));
			}
			void Add(int key)
			{
				key = KeyCatalog.Normalize(key);
				if (!action.Keys.Contains(key))
				{
					action.Keys.Add(key);
				}
				entry.Text = KeyCatalog.Format(action.Keys);
				resetStyle();
				store.Save();
			}
		}
	}

	internal static FrameworkElement SimulatedKeyboard(Action<int> add)
	{
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 4.0
		};
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 3.0
		};
		(string, int)[] array = new(string, int)[21]
		{
			("~", 192),
			("!", 49),
			("@", 50),
			("#", 51),
			("$", 52),
			("%", 53),
			("^", 54),
			("&", 55),
			("*", 56),
			("(", 57),
			(")", 48),
			("_", 189),
			("+", 187),
			("{", 219),
			("}", 221),
			("|", 220),
			(":", 186),
			("\"", 222),
			("<", 188),
			(">", 190),
			("?", 191)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(string, int) tuple = array[i];
			stackPanel2.Children.Add(ShiftKey(tuple.Item1, tuple.Item2));
		}
		stackPanel.Children.Add(stackPanel2);
		StackPanel stackPanel3 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 3.0
		};
		stackPanel3.Children.Add(Key("Esc", 27, 60.0));
		for (int j = 1; j <= 12; j++)
		{
			stackPanel3.Children.Add(Key("F" + j, 111 + j));
		}
		stackPanel3.Children.Add(Key("PrtSc", 44, 64.0));
		stackPanel3.Children.Add(Key("ScrLk", 145, 64.0));
		stackPanel3.Children.Add(Key("Pause", 19, 64.0));
		stackPanel.Children.Add(stackPanel3);
		StackPanel stackPanel4 = new StackPanel
		{
			Spacing = 3.0
		};
		stackPanel4.Children.Add(Row(new(string, int, double)[14]
		{
			("`", 192, 55.0),
			("1", 49, 55.0),
			("2", 50, 55.0),
			("3", 51, 55.0),
			("4", 52, 55.0),
			("5", 53, 55.0),
			("6", 54, 55.0),
			("7", 55, 55.0),
			("8", 56, 55.0),
			("9", 57, 55.0),
			("0", 48, 55.0),
			("-", 189, 55.0),
			("=", 187, 55.0),
			("Back", 8, 76.0)
		}));
		stackPanel4.Children.Add(Row(new(string, int, double)[14]
		{
			("Tab", 9, 70.0),
			("Q", 81, 55.0),
			("W", 87, 55.0),
			("E", 69, 55.0),
			("R", 82, 55.0),
			("T", 84, 55.0),
			("Y", 89, 55.0),
			("U", 85, 55.0),
			("I", 73, 55.0),
			("O", 79, 55.0),
			("P", 80, 55.0),
			("[", 219, 55.0),
			("]", 221, 55.0),
			("\\", 220, 66.0)
		}));
		stackPanel4.Children.Add(Row(new(string, int, double)[13]
		{
			("Caps", 20, 78.0),
			("A", 65, 55.0),
			("S", 83, 55.0),
			("D", 68, 55.0),
			("F", 70, 55.0),
			("G", 71, 55.0),
			("H", 72, 55.0),
			("J", 74, 55.0),
			("K", 75, 55.0),
			("L", 76, 55.0),
			(";", 186, 55.0),
			("'", 222, 55.0),
			("Enter", 13, 86.0)
		}));
		stackPanel4.Children.Add(Row(new(string, int, double)[12]
		{
			("Shift", 16, 92.0),
			("Z", 90, 55.0),
			("X", 88, 55.0),
			("C", 67, 55.0),
			("V", 86, 55.0),
			("B", 66, 55.0),
			("N", 78, 55.0),
			("M", 77, 55.0),
			(",", 188, 55.0),
			(".", 190, 55.0),
			("/", 191, 55.0),
			("Shift", 16, 92.0)
		}));
		stackPanel4.Children.Add(Row(new(string, int, double)[8]
		{
			("Ctrl", 17, 68.0),
			("Win", 91, 60.0),
			("Alt", 18, 60.0),
			("Space", 32, 300.0),
			("Alt", 18, 60.0),
			("Win", 91, 60.0),
			("Menu", 93, 68.0),
			("Ctrl", 17, 68.0)
		}));
		StackPanel stackPanel5 = new StackPanel
		{
			Spacing = 3.0
		};
		stackPanel5.Children.Add(Row(new(string, int, double)[3]
		{
			("Ins", 45, 55.0),
			("Home", 36, 55.0),
			("PgUp", 33, 55.0)
		}));
		stackPanel5.Children.Add(Row(new(string, int, double)[3]
		{
			("Del", 46, 55.0),
			("End", 35, 55.0),
			("PgDn", 34, 55.0)
		}));
		stackPanel5.Children.Add(new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 3.0,
			Children = 
			{
				(UIElement)Blank(),
				(UIElement)Key("↑", 38),
				(UIElement)Blank()
			}
		});
		stackPanel5.Children.Add(Row(new(string, int, double)[3]
		{
			("←", 37, 55.0),
			("↓", 40, 55.0),
			("→", 39, 55.0)
		}));
		StackPanel stackPanel6 = new StackPanel
		{
			Spacing = 3.0
		};
		stackPanel6.Children.Add(Row(new(string, int, double)[4]
		{
			("Num", 144, 55.0),
			("/", 111, 55.0),
			("*", 106, 55.0),
			("-", 109, 55.0)
		}));
		stackPanel6.Children.Add(Row(new(string, int, double)[4]
		{
			("7", 103, 55.0),
			("8", 104, 55.0),
			("9", 105, 55.0),
			("+", 107, 55.0)
		}));
		StackPanel stackPanel7 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 3.0
		};
		stackPanel7.Children.Add(Key("4", 100));
		stackPanel7.Children.Add(Key("5", 101));
		stackPanel7.Children.Add(Key("6", 102));
		stackPanel7.Children.Add(Blank());
		stackPanel6.Children.Add(stackPanel7);
		stackPanel6.Children.Add(Row(new(string, int, double)[4]
		{
			("1", 97, 55.0),
			("2", 98, 55.0),
			("3", 99, 55.0),
			("Enter", 13, 55.0)
		}));
		StackPanel stackPanel8 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 3.0
		};
		stackPanel8.Children.Add(Key("0", 96, 113.0));
		stackPanel8.Children.Add(Key(".", 110));
		stackPanel8.Children.Add(Blank());
		stackPanel6.Children.Add(stackPanel8);
		StackPanel stackPanel9 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 10.0
		};
		stackPanel9.Children.Add(stackPanel4);
		stackPanel9.Children.Add(stackPanel5);
		stackPanel9.Children.Add(stackPanel6);
		stackPanel.Children.Add(stackPanel9);
		return new ScrollViewer
		{
			Content = stackPanel,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalScrollMode = ScrollMode.Enabled,
			VerticalScrollMode = ScrollMode.Disabled
		};
		static FrameworkElement Blank(double width = 55.0)
		{
			return new Border
			{
				Width = width,
				Height = 42.0,
				Opacity = 0.0
			};
		}
		Button Key(string label, int code, double width = 55.0)
		{
			Button button = MakeKey(label, width);
			button.Click += delegate
			{
				add(code);
			};
			return button;
		}
		static Button MakeKey(string label, double width = 55.0)
		{
			return new Button
			{
				Content = new TextBlock
				{
					Text = label,
					FontSize = 12.0,
					TextAlignment = TextAlignment.Center
				},
				Width = width,
				Height = 42.0,
				MinHeight = 42.0,
				Padding = new Thickness(2.0, 0.0, 2.0, 0.0),
				CornerRadius = new CornerRadius(7.0)
			};
		}
		FrameworkElement Row(params (string Label, int Code, double Width)[] keys)
		{
			StackPanel stackPanel10 = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 3.0
			};
			for (int k = 0; k < keys.Length; k++)
			{
				(string, int, double) tuple2 = keys[k];
				stackPanel10.Children.Add(Key(tuple2.Item1, tuple2.Item2, tuple2.Item3));
			}
			return stackPanel10;
		}
		Button ShiftKey(string label, int code, double width = 55.0)
		{
			Button button = MakeKey(label, width);
			button.Click += delegate
			{
				add(16);
				add(code);
			};
			return button;
		}
	}
}
