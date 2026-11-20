using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.Native;
using TouchAssistant.Services;
using Windows.Devices.Radios;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace TouchAssistant.UI;

internal sealed class ControlCenterWindow : Window
{
	private readonly SettingsStore store;

	private readonly SystemControl system;

	private readonly Microsoft.UI.Dispatching.DispatcherQueue dispatcher;

	private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer poll;

	private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer slide;

	private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer brightnessDelay;

	private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer volumeDelay;

	private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer foregroundDelay;

	private readonly Grid root = new Grid
	{
		HorizontalAlignment = HorizontalAlignment.Stretch,
		VerticalAlignment = VerticalAlignment.Stretch,
		Background = Ui.Brush(0, 0, 0, 0)
	};

	private readonly Grid viewport = new Grid();

	private readonly Viewbox fittedContent = new Viewbox
	{
		Stretch = Stretch.Uniform,
		HorizontalAlignment = HorizontalAlignment.Center,
		VerticalAlignment = VerticalAlignment.Center
	};

	private ControlCenterLayout layout;

	private PanelShadow? shadow;

	private readonly TextBlock time = Ui.Text("", 58.0);

	private readonly TextBlock date = Ui.Text("", 17.0);

	private readonly TextBlock mediaTitle = Ui.Text("暂无媒体", 15.0);

	private readonly TextBlock status = Ui.Text("", 12.0);

	private readonly Button wifi;

	private readonly Button bluetooth;

	private readonly Button energy;

	private readonly Button flight;

	private readonly Button hotspot;

	private readonly Button single;

	private readonly Button play;

	private readonly Button rotation;

	private readonly Button stage;

	private readonly Button topEdge;

	private readonly Button bottomEdge;

	private readonly Button guardToggle;

	private readonly Button arkToggle;

	private readonly Button ballToggle;

	private readonly Button mute;

	private readonly Button dialToggle;

	private readonly Button globalGestureToggle;

	private readonly Slider volume;

	private readonly Slider brightness;

	private readonly FontIcon volumeIcon = Ui.Icon("\ue767");

	private readonly FontIcon brightnessIcon = Ui.Icon("\ue706");

	private readonly Microsoft.UI.Xaml.Controls.Image mediaArtwork = new Microsoft.UI.Xaml.Controls.Image
	{
		Stretch = Stretch.UniformToFill,
		IsHitTestVisible = false
	};

	private string artworkKey = "";

	private readonly Border mediaScrim = new Border
	{
		Tag = "MediaScrim",
		Background = Ui.Brush(10, 10, 14, 175),
		Margin = new Thickness(-8.0),
		IsHitTestVisible = false,
		Visibility = Visibility.Collapsed
	};

	private readonly GridView apps = new GridView();

	private readonly ObservableCollection<LauncherItem> launchers;

	private readonly UISettings themeSettings = new UISettings();

	private readonly TextBlock editHint = Ui.Text("长按应用可排序或删除", 12.0);

	private OutsideInteractionMonitor? outsideMonitor;

	private nint previousForeground;

	private bool mediaBusy;

	private bool shown;

	private bool closing;

	private bool syncing;

	private bool refreshBusy;

	private bool editing;

	private bool modal;

	private bool disposed;

	private int width;

	private int height;

	private double startProgress;

	private double dragStart;

	private double panelDpi;

	private bool interactiveOpening;

	private bool needsPresent;

	private bool topReveal;

	private EventHandler<object>? warmupRendering;

	private double progress;

	private readonly List<FrameworkElement> revealItems = new List<FrameworkElement>();

	private DateTime animationStart;

	private DateTime lastVolumeInput;

	private DateTime lastBrightnessInput;

	private DateTime lastMediaInput;

	private uint? launcherPointer;

	private Pointer? reorderPointer;

	private LauncherItem? reorderItem;

	private bool reordering;

	private string reorderDebug = "";

	private LauncherItem[]? orderBeforeDrag;

	private double revealOpacity = 1.0;

	private bool importingLaunchers;

	private Button? gamepadToggle;

	public bool IsOpen
	{
		get
		{
			if (shown)
			{
				return !closing;
			}
			return false;
		}
	}

	internal bool CanReleaseResources
	{
		get
		{
			if (!shown && !modal && !refreshBusy)
			{
				return !mediaBusy;
			}
			return false;
		}
	}

	internal bool IgnoreExternalDismissal { get; set; }

	internal double ContentOpacity => revealOpacity;

	internal double ShadowOpacity => shadow?.Opacity ?? 0.0;

	internal string BackdropDiagnostics => $"{(base.SystemBackdrop as PanelBackdrop)?.Diagnostics}; Open={IsOpen}; Progress={progress:F2}; Position={base.AppWindow.Position.X},{base.AppWindow.Position.Y}; Size={base.AppWindow.Size.Width},{base.AppWindow.Size.Height}; Layout={root.ActualWidth:F0},{root.ActualHeight:F0}; Items={WiredLauncherDeleteButtons}; Apps={apps.ActualWidth:F0},{apps.ActualHeight:F0}; Count={apps.Items.Count}; Container={apps.ContainerFromIndex(0)?.GetType().Name}";

	internal int WiredLauncherDeleteButtons => launchers.Count((LauncherItem item) => apps.ContainerFromItem(item) is GridViewItem { ContentTemplateRoot: FrameworkElement contentTemplateRoot } && contentTemplateRoot.FindName("DeleteLauncher") is Button button && button.Tag == item);

	internal int LoadedLauncherIcons => launchers.Count((LauncherItem item) => item.Icon != null);

	internal bool UsesContinuousScrolling
	{
		get
		{
			if (apps.ItemsSource == launchers)
			{
				return ScrollViewer.GetVerticalScrollMode(apps) == ScrollMode.Enabled;
			}
			return false;
		}
	}

	public event Action? Dismissed;

	public event Action? Shown;

	public event Action? Presented;

	internal void CheckIdleTimers()
	{
		if (IsOpen || poll.IsRunning || slide.IsRunning || brightnessDelay.IsRunning || volumeDelay.IsRunning || foregroundDelay.IsRunning)
		{
			throw new InvalidOperationException("控制中心隐藏后仍保留活动计时器");
		}
	}

	private void CancelWarmup()
	{
		if (warmupRendering != null)
		{
			CompositionTarget.Rendering -= warmupRendering;
			warmupRendering = null;
		}
	}

	private void PrepareFirstFrame()
	{
		CancelWarmup();
		int frames = 0;
		warmupRendering = delegate
		{
			if (++frames >= 2)
			{
				CancelWarmup();
				if (shown && !disposed)
				{
					needsPresent = false;
					if (!interactiveOpening && !closing)
					{
						startProgress = (progress = 0.0);
						animationStart = DateTime.UtcNow;
					}
					PositionPanel();
				}
			}
		};
		CompositionTarget.Rendering += warmupRendering;
		ApplyWindowAlpha(0.0);
		Ui.Present(this);
		RaiseTopmost();
	}

	private void CollectRevealItems()
	{
		revealItems.Clear();
		Visit(root);
		void Visit(DependencyObject node)
		{
			if (node is FrameworkElement frameworkElement)
			{
				if (!(frameworkElement is ButtonBase) && !(frameworkElement is GridViewItem) && (object)frameworkElement != time && (object)frameworkElement != date)
				{
					switch (frameworkElement.Tag as string)
					{
					case "MediaCard":
					case "RevealSlider":
					case "RevealLabel":
						break;
					default:
						goto IL_006f;
					}
				}
				revealItems.Add(frameworkElement);
				return;
			}
			goto IL_006f;
			IL_006f:
			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
			{
				Visit(VisualTreeHelper.GetChild(node, i));
			}
		}
	}

	private void AnimateRevealItems(double amount)
	{
		for (int i = 0; i < revealItems.Count; i++)
		{
			FrameworkElement frameworkElement = revealItems[i];
			double num = 0.16 * (double)i / (double)Math.Max(1, revealItems.Count - 1);
			double num2 = Math.Clamp((amount - num) / (1.0 - num), 0.0, 1.0);
			double num3 = num2 * num2 * (3.0 - 2.0 * num2);
			Visual elementVisual = ElementCompositionPreview.GetElementVisual(frameworkElement);
			elementVisual.CenterPoint = new Vector3((float)(frameworkElement.ActualWidth / 2.0), (float)(frameworkElement.ActualHeight / 2.0), 0f);
			float num4 = (float)(0.92 + 0.08 * num3);
			elementVisual.Scale = new Vector3(num4, num4, 1f);
			elementVisual.Opacity = (float)num3;
		}
	}

	public ControlCenterWindow(SettingsStore settings, SystemControl service)
	{
		store = settings;
		system = service;
		dispatcher = base.DispatcherQueue;
		base.Title = Localization.T("TouchToys · 控制中心");
		base.SystemBackdrop = new PanelBackdrop();
		fittedContent.Child = root;
		viewport.Children.Add(fittedContent);
		base.Content = viewport;
		Ui.ConfigureOverlay(this, systemFrame: true);
		ApplyWindowAlpha(0.0);
		Grid grid = new Grid
		{
			Padding = new Thickness(24.0, 22.0, 24.0, 16.0),
			RowSpacing = 8.0
		};
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition());
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		grid.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 16.0
		};
		grid.Children.Add(stackPanel);
		root.Children.Add(grid);
		ApplyTheme();
		themeSettings.ColorValuesChanged += ThemeChanged;
		SystemEvents.UserPreferenceChanged += PreferenceChanged;
		time.HorizontalAlignment = (date.HorizontalAlignment = HorizontalAlignment.Center);
		time.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(IconFontResolver.ResolveTextFontFamily("Segoe UI Variable Display", "Segoe UI", "Arial"));
		time.FontWeight = FontWeights.Bold;
		time.TextAlignment = TextAlignment.Center;
		stackPanel.Children.Add(time);
		date.Margin = new Thickness(0.0, -18.0, 0.0, 6.0);
		stackPanel.Children.Add(date);
		Grid grid2 = new Grid
		{
			ColumnSpacing = 8.0
		};
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(166.0)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		Grid media = new Grid
		{
			Tag = "MediaCard",
			Padding = new Thickness(8.0),
			Height = 166.0,
			Background = Ui.Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue, 230),
			CornerRadius = new CornerRadius(14.0)
		};
		media.RowDefinitions.Add(new RowDefinition());
		media.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		Grid.SetRowSpan(mediaArtwork, 2);
		mediaArtwork.Opacity = 0.9;
		mediaArtwork.Margin = new Thickness(-8.0);
		media.Children.Add(mediaArtwork);
		Border border = mediaScrim;
		Grid.SetRowSpan(border, 2);
		media.Children.Add(border);
		media.SizeChanged += delegate
		{
			Visual elementVisual = ElementCompositionPreview.GetElementVisual(media);
			CompositionRoundedRectangleGeometry compositionRoundedRectangleGeometry = elementVisual.Compositor.CreateRoundedRectangleGeometry();
			compositionRoundedRectangleGeometry.Size = new Vector2((float)media.ActualWidth, (float)media.ActualHeight);
			compositionRoundedRectangleGeometry.CornerRadius = new Vector2(14f);
			elementVisual.Clip = elementVisual.Compositor.CreateGeometricClip(compositionRoundedRectangleGeometry);
		};
		mediaTitle.Foreground = Ui.Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue);
		mediaTitle.HorizontalAlignment = HorizontalAlignment.Center;
		mediaTitle.VerticalAlignment = VerticalAlignment.Center;
		mediaTitle.TextAlignment = TextAlignment.Center;
		mediaTitle.MaxLines = 3;
		mediaTitle.TextTrimming = TextTrimming.CharacterEllipsis;
		media.Children.Add(mediaTitle);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 3.0,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		stackPanel2.Children.Add(Ui.IconButton("\ue892", "上一首", delegate
		{
			Media("Previous");
		}, 48.0, delegate
		{
			OpenSettings("ms-settings:sound");
		}));
		play = Ui.IconButton("\ue768", "播放 / 暂停", delegate
		{
			Media("Toggle");
		}, 48.0, delegate
		{
			OpenSettings("ms-settings:sound");
		});
		stackPanel2.Children.Add(play);
		stackPanel2.Children.Add(Ui.IconButton("\ue893", "下一首", delegate
		{
			Media("Next");
		}, 48.0, delegate
		{
			OpenSettings("ms-settings:sound");
		}));
		Grid.SetRow(stackPanel2, 1);
		media.Children.Add(stackPanel2);
		grid2.Children.Add(media);
		Grid switches = new Grid
		{
			ColumnSpacing = 8.0,
			RowSpacing = 8.0
		};
		for (int num = 0; num < 4; num++)
		{
			switches.ColumnDefinitions.Add(new ColumnDefinition());
		}
		for (int num2 = 0; num2 < 3; num2++)
		{
			switches.RowDefinitions.Add(new RowDefinition
			{
				Height = new GridLength(50.0)
			});
		}
		Grid.SetColumn(switches, 1);
		grid2.Children.Add(switches);
		wifi = Toggle("\ue701", "Wi-Fi", async delegate
		{
			await system.ToggleRadioAsync(RadioKind.WiFi);
		});
		Place(wifi, 0, 0);
		bluetooth = Toggle("\ue702", "蓝牙", async delegate
		{
			await system.ToggleRadioAsync(RadioKind.Bluetooth);
		});
		Place(bluetooth, 1, 0);
		energy = Toggle("\ue8be", "始终使用节能模式", async delegate
		{
			if (!(await SystemSwitches.SetEnergySaverAsync(!system.EnergySaver)))
			{
				systemNotice("系统未允许直接切换节能模式，请长按打开设置。");
			}
		});
		Place(energy, 2, 0);
		flight = Toggle("\ue709", "飞行模式", system.ToggleFlightAsync);
		Place(flight, 3, 0);
		hotspot = Toggle("\ue88a", "移动热点", system.ToggleHotspotAsync);
		Place(hotspot, 0, 1);
		single = Toggle("\ue1e3", "单击打开项目", delegate
		{
			ShellPreferences.SingleClick = !ShellPreferences.SingleClick;
			return Task.CompletedTask;
		});
		Place(single, 1, 1);
		Place(Ui.IconButton("\ue72e", "锁定屏幕", delegate
		{
			HidePanel();
			Win32.LockWorkStation();
		}, 50.0, delegate
		{
			OpenSettings("ms-settings:lockscreen");
		}), 0, 2);
		rotation = Toggle("\ue755", "旋转锁定", async delegate
		{
			if (!(await SystemSwitches.SetRotationLockAsync(!ShellPreferences.Rotation.Locked)))
			{
				systemNotice("系统未接受旋转锁定更改，请长按打开显示设置。");
			}
		});
		Place(rotation, 1, 2);
		volume = new Slider
		{
			Orientation = Orientation.Vertical,
			Minimum = 0.0,
			Maximum = 100.0,
			StepFrequency = 1.0,
			Height = 88.0,
			Width = 48.0,
			VerticalAlignment = VerticalAlignment.Top
		};
		brightness = new Slider
		{
			Orientation = Orientation.Vertical,
			Minimum = 0.0,
			Maximum = 100.0,
			StepFrequency = 1.0,
			Height = 88.0,
			Width = 48.0,
			VerticalAlignment = VerticalAlignment.Top
		};
		Place(SliderTile(brightness, brightnessIcon, "屏幕亮度"), 2, 1, 2);
		Place(SliderTile(volume, volumeIcon, "设备音量"), 3, 1, 2);
		stackPanel.Children.Add(grid2);
		Grid grid3 = new Grid
		{
			ColumnSpacing = 8.0,
			Margin = new Thickness(0.0, -8.0, 0.0, 0.0)
		};
		grid3.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(166.0)
		});
		grid3.ColumnDefinitions.Add(new ColumnDefinition());
		Grid grid4 = new Grid();
		Grid grid5 = new Grid
		{
			ColumnSpacing = 8.0
		};
		for (int num3 = 0; num3 < 3; num3++)
		{
			grid4.ColumnDefinitions.Add(new ColumnDefinition());
		}
		for (int num4 = 0; num4 < 4; num4++)
		{
			grid5.ColumnDefinitions.Add(new ColumnDefinition());
		}
		grid3.Children.Add(grid4);
		Grid.SetColumn(grid5, 1);
		grid3.Children.Add(grid5);
		(string, string, Action)[] array = new(string, string, Action)[7]
		{
			("\ue8c6", "截图", delegate
			{
				SystemControl.SendKeys(new int[3] { 91, 16, 83 });
			}),
			("\uebc6", "投放", delegate
			{
				SystemControl.SendKeys(new int[2] { 91, 75 });
			}),
			("\ue8ef", "计算器", delegate
			{
				SystemControl.Launch("calculator:");
			}),
			("\ue722", "相机", delegate
			{
				SystemControl.Launch("microsoft.windows.camera:");
			}),
			("\ue720", "录音", SystemControl.LaunchRecorder),
			("\ue9d9", "任务管理器", delegate
			{
				SystemControl.Launch("taskmgr.exe");
			}),
			("\ue713", "系统设置", delegate
			{
				SystemControl.Launch("ms-settings:");
			})
		};
		stage = Toggle("\ue90c", "侧边任务列表", delegate
		{
			store.Value.EdgeGestures[0] = ((store.Value.EdgeGestures[0].Kind == ActionKind.None) ? GestureAction.BuiltIn("Stage") : new GestureAction());
			store.Save();
			App.CurrentApp.FunctionState("侧边任务列表", store.Value.EdgeGestures[0].Kind != ActionKind.None, "\ue90c");
			return Task.CompletedTask;
		});
		StackPanel stackPanel3 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0,
			HorizontalAlignment = HorizontalAlignment.Left
		};
		(string, string, Action)[] array2 = new(string, string, Action)[3]
		{
			("\ue7f4", "侧边任务列表", delegate
			{
			}),
			("\ue823", "闹钟", delegate
			{
				ClockLauncher.OpenAsync();
			}),
			("\ued14", "扫码", delegate
			{
				App.CurrentApp.ShowScanner();
			})
		};
		stackPanel3.Children.Add(Ui.IconButton("\uefa5", "切换 TT触摸板", delegate
		{
			RunShortcut(delegate
			{
				App.CurrentApp.ToggleTouchpad();
			});
		}, 50.0));
		stackPanel3.Children.Add(Ui.IconButton("\ue765", "系统触摸键盘", delegate
		{
			RunShortcut(delegate
			{
				App.CurrentApp.ToggleTouchKeyboard();
			});
		}, 50.0));
		for (int num5 = 1; num5 < array2.Length; num5++)
		{
			(string Glyph, string Name, Action Action) item = array2[num5];
			stackPanel3.Children.Add(Ui.IconButton(item.Glyph, item.Name, delegate
			{
				RunShortcut(item.Action);
			}, 50.0));
		}
		for (int num6 = 0; num6 < array.Length - 1; num6++)
		{
			(string Glyph, string Name, Action Action) a = array[num6];
			string uri = a.Name switch
			{
				"截图" => "ms-settings:easeofaccess-keyboard", 
				"投放" => "ms-settings:connecteddevices", 
				"相机" => "ms-settings:camera", 
				"录音" => "ms-settings:sound", 
				"系统设置" => "ms-settings:", 
				_ => null, 
			};
			Button button = Ui.IconButton(a.Glyph, a.Name, delegate
			{
				RunShortcut(a.Action);
			}, (num6 < 3) ? 48 : 50, (uri == null) ? null : ((Action)delegate
			{
				OpenSettings(uri);
			}));
			Grid.SetColumn(button, (num6 < 3) ? num6 : (num6 - 3));
			if (num6 < 3)
			{
				button.HorizontalAlignment = num6 switch
				{
					2 => HorizontalAlignment.Right, 
					0 => HorizontalAlignment.Left, 
					_ => HorizontalAlignment.Center, 
				};
				grid4.Children.Add(button);
			}
			else
			{
				grid5.Children.Add(button);
			}
		}
		mute = Ui.IconButton("\ue74f", "一键静音", delegate
		{
			system.ToggleMute();
			Refresh();
		}, 50.0);
		Grid.SetColumn(mute, 3);
		grid5.Children.Add(mute);
		Button item2 = Ui.IconButton("\ue9e9", "应用设置", delegate
		{
			RunShortcut(delegate
			{
				App.CurrentApp.OpenSettings();
			});
		}, 50.0);
		topEdge = Toggle("\ue90d", "右上角下拉控制中心", delegate
		{
			bool flag = store.Value.MultiTouch.EdgeEnabled && store.Value.EdgeGestures[1].Kind != ActionKind.None;
			store.Value.EdgeGestures[1] = (flag ? new GestureAction() : GestureAction.BuiltIn("ControlCenter"));
			if (!flag)
			{
				store.Value.MultiTouch.EdgeEnabled = true;
			}
			store.Save();
			App.CurrentApp.FunctionState("右上下拉控制中心", !flag, "\ue90d");
			return Task.CompletedTask;
		});
		bottomEdge = Toggle("\ue90e", "全面屏手势", delegate
		{
			bool flag = store.Value.MultiTouch.EdgeEnabled && store.Value.EdgeGestures[2].Kind != ActionKind.None;
			store.Value.EdgeGestures[2] = (flag ? new GestureAction() : GestureAction.BuiltIn("Overview"));
			if (!flag)
			{
				store.Value.MultiTouch.EdgeEnabled = true;
			}
			store.Save();
			App.CurrentApp.FunctionState("全面屏手势", !flag, "\ue90e");
			return Task.CompletedTask;
		});
		stackPanel3.Children.Add(stage);
		stackPanel3.Children.Add(topEdge);
		stackPanel3.Children.Add(bottomEdge);
		guardToggle = Toggle("\uf439", "仅屏蔽关闭按钮的触摸操作", delegate
		{
			store.Value.CloseGuardEnabled = !store.Value.CloseGuardEnabled;
			store.Save();
			App.CurrentApp.FunctionState("防误触", store.Value.CloseGuardEnabled, "\uf439");
			return Task.CompletedTask;
		});
		stackPanel3.Children.Add(guardToggle);
		arkToggle = Toggle("\ueca5", "快捷方舟", delegate
		{
			ApplicationBindings.ArkOptions(store.Value).ArkEnabled = !ApplicationBindings.ArkOptions(store.Value).ArkEnabled;
			store.Save();
			return Task.CompletedTask;
		});
		stackPanel3.Children.Add(arkToggle);
		ballToggle = Toggle("\uea3a", "触控圆点", delegate
		{
			store.Value.BallEnabled = !store.Value.BallEnabled;
			store.Save();
			return Task.CompletedTask;
		});
		stackPanel3.Children.Add(ballToggle);
		dialToggle = Toggle("\ue958", "虚拟 Dial", delegate
		{
			App.CurrentApp.ShowDial();
			SyncDialState();
			return Task.CompletedTask;
		});
		stackPanel3.Children.Add(dialToggle);
		globalGestureToggle = Ui.IconButton("\uebfc", "全局手势", delegate
		{
			try
			{
				store.Value.MultiTouch.Enabled = !store.Value.MultiTouch.Enabled;
				store.Save();
			}
			catch (Exception ex)
			{
				Diagnostics.Error(ex, "ControlCenter.GlobalGestures");
				systemNotice(ex.Message);
			}
			finally
			{
				SyncGlobalGestureState();
			}
		}, 50.0);
		stackPanel3.Children.Add(globalGestureToggle);
		gamepadToggle = Toggle("\uece9", "切换摇杆和按钮", delegate
		{
			App.CurrentApp.ToggleGamepad(fromCenter: true);
			return Task.CompletedTask;
		});
		stackPanel3.Children.Add(gamepadToggle);
		stackPanel3.Children.Add(item2);
		stackPanel3.Children.Add(Ui.IconButton("\ue713", "系统设置", delegate
		{
			RunShortcut(delegate
			{
				SystemControl.Launch("ms-settings:");
			});
		}, 50.0));
		StackPanel stackPanel4 = new StackPanel
		{
			Spacing = 8.0,
			Margin = new Thickness(0.0, -8.0, 0.0, 0.0)
		};
		FrameworkElement[] array3 = stackPanel3.Children.Cast<FrameworkElement>().ToArray();
		stackPanel3.Children.Clear();
		for (int num7 = 0; num7 < (array3.Length + 6) / 7; num7++)
		{
			Grid grid6 = new Grid
			{
				ColumnSpacing = 8.0
			};
			grid6.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = new GridLength(166.0)
			});
			grid6.ColumnDefinitions.Add(new ColumnDefinition());
			Grid grid7 = new Grid();
			Grid grid8 = new Grid
			{
				ColumnSpacing = 8.0
			};
			for (int num8 = 0; num8 < 3; num8++)
			{
				grid7.ColumnDefinitions.Add(new ColumnDefinition());
			}
			for (int num9 = 0; num9 < 4; num9++)
			{
				grid8.ColumnDefinitions.Add(new ColumnDefinition());
			}
			grid6.Children.Add(grid7);
			Grid.SetColumn(grid8, 1);
			grid6.Children.Add(grid8);
			for (int num10 = 0; num10 < 7 && num7 * 7 + num10 < array3.Length; num10++)
			{
				int num11 = num7 * 7 + num10;
				int num12 = num10;
				FrameworkElement frameworkElement = array3[num11];
				Grid.SetColumn(frameworkElement, (num12 < 3) ? num12 : (num12 - 3));
				if ((object)frameworkElement != null)
				{
					FrameworkElement frameworkElement2 = frameworkElement;
					frameworkElement2.HorizontalAlignment = ((num12 >= 3) ? HorizontalAlignment.Center : (num12 switch
					{
						2 => HorizontalAlignment.Right, 
						0 => HorizontalAlignment.Left, 
						_ => HorizontalAlignment.Center, 
					}));
				}
				((num12 < 3) ? grid7 : grid8).Children.Add(frameworkElement);
			}
			stackPanel4.Children.Add(grid6);
		}
		stackPanel.Children.Add(grid3);
		stackPanel.Children.Add(stackPanel4);
		Grid grid9 = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition(),
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			}
		};
		TextBlock textBlock = Ui.Text("快捷启动", 16.0);
		textBlock.Tag = "RevealLabel";
		textBlock.VerticalAlignment = VerticalAlignment.Center;
		grid9.Children.Add(textBlock);
		Button button2 = Ui.IconButton("\ue109", "添加应用", async delegate
		{
			await AddApp();
		}, 32.0);
		((FontIcon)button2.Content).FontSize = 16.0;
		button2.Tag = "TransparentButton";
		button2.Background = Ui.Brush(0, 0, 0, 0);
		Grid.SetColumn(button2, 1);
		grid9.Children.Add(button2);
		stackPanel.Children.Add(grid9);
		launchers = new ObservableCollection<LauncherItem>(store.Value.Launchers.Select((LauncherEntry x) => new LauncherItem(x)));
		apps.ItemsSource = launchers;
		apps.IsItemClickEnabled = true;
		apps.IsSwipeEnabled = false;
		apps.SelectionMode = ListViewSelectionMode.None;
		apps.CanDragItems = false;
		apps.CanReorderItems = false;
		apps.AllowDrop = true;
		apps.ItemsPanel = (ItemsPanelTemplate)XamlReader.Load("<ItemsPanelTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><ItemsWrapGrid Orientation='Horizontal' MaximumRowsOrColumns='5'/></ItemsPanelTemplate>");
		apps.ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'><Grid Width='68' Height='68' Background='Transparent' CornerRadius='13'><StackPanel VerticalAlignment='Center' Spacing='5'><Grid Height='30'><FontIcon Visibility='{Binding FallbackVisibility}' FontFamily='{Binding FallbackFontFamily}' Glyph='{Binding FallbackGlyph}' FontSize='26'/><Image Source='{Binding Icon}' Width='30' Height='30'/></Grid><TextBlock Text='{Binding Name}' FontSize='10' TextAlignment='Center' TextTrimming='CharacterEllipsis' Margin='4,0'/></StackPanel><Border BorderBrush='{StaticResource AppAccentBrush}' BorderThickness='2' CornerRadius='13' IsHitTestVisible='False' Visibility='{Binding SelectionVisibility}'/><Button x:Name='DeleteLauncher' Visibility='{Binding DeleteVisibility}' HorizontalAlignment='Right' VerticalAlignment='Top' Width='32' Height='32' MinWidth='32' MinHeight='32' Padding='0' CornerRadius='16' Background='#E04848' Foreground='White' BorderThickness='0' AutomationProperties.Name='删除应用'><FontIcon FontFamily='{Binding DeleteFontFamily}' Glyph='&#xE711;' FontSize='12'/></Button></Grid></DataTemplate>");
		apps.ContainerContentChanging += delegate(ListViewBase sender, ContainerContentChangingEventArgs e)
		{
			if (!e.InRecycleQueue && e.Phase == 0)
			{
				if (e.Item is LauncherItem launcherItem)
				{
					launcherItem.LoadIconAsync();
				}
				WireLauncher(e.ItemContainer, e.Item as LauncherItem);
				e.RegisterUpdateCallback(delegate(ListViewBase _, ContainerContentChangingEventArgs updated)
				{
					WireLauncher(updated.ItemContainer, updated.Item as LauncherItem);
				});
			}
		};
		apps.Loaded += delegate
		{
			WireVisibleLaunchers();
		};
		ApplyLauncherSpacing();
		apps.SelectionChanged += delegate
		{
			UpdateSelection();
		};
		apps.ItemClick += delegate(object _, ItemClickEventArgs e)
		{
			if (!editing && shown && !closing)
			{
				RunShortcut(delegate
				{
					LauncherItem launcherItem = (LauncherItem)e.ClickedItem;
					if ((launcherItem.Entry.Keys?.Count ?? 0) > 0)
					{
						KeyboardInput.Tap(launcherItem.Entry.Keys);
					}
					else
					{
						SystemControl.Launch(launcherItem.Target);
					}
				});
			}
		};
		apps.Holding += delegate(object _, HoldingRoutedEventArgs e)
		{
			if (e.HoldingState == HoldingState.Started)
			{
				SetEditMode(value: true);
				SelectFromSource(e.OriginalSource);
				if (reorderPointer != null && reorderItem != null)
				{
					apps.CancelDirectManipulations();
					reordering = apps.CapturePointer(reorderPointer);
					reorderDebug += $" hold capture={reordering} item={reorderItem?.Name};";
				}
				e.Handled = true;
			}
		};
		apps.AddHandler(UIElement.PointerMovedEvent, (PointerEventHandler)delegate(object _, PointerRoutedEventArgs e)
		{
			if (reordering && reorderItem != null && reorderPointer?.PointerId == e.Pointer.PointerId)
			{
				Windows.Foundation.Point position = e.GetCurrentPoint(apps).Position;
				if (reorderDebug.Length < 2500)
				{
					reorderDebug += $" move={position.X:F0},{position.Y:F0};";
				}
				for (int i = 0; i < launchers.Count; i++)
				{
					if (apps.ContainerFromIndex(i) is FrameworkElement frameworkElement3)
					{
						Windows.Foundation.Point point = frameworkElement3.TransformToVisual(apps).TransformPoint(default(Windows.Foundation.Point));
						if (position.X >= point.X && position.X < point.X + frameworkElement3.ActualWidth && position.Y >= point.Y && position.Y < point.Y + frameworkElement3.ActualHeight)
						{
							int num13 = launchers.IndexOf(reorderItem);
							if (num13 >= 0 && num13 != i)
							{
								launchers.Move(num13, i);
								apps.SelectedItem = reorderItem;
							}
							break;
						}
					}
				}
				e.Handled = true;
			}
		}, handledEventsToo: true);
		apps.AddHandler(UIElement.PointerReleasedEvent, new PointerEventHandler(FinishReorder), handledEventsToo: true);
		apps.PointerCanceled += FinishReorder;
		apps.PointerCaptureLost += delegate(object _, PointerRoutedEventArgs e)
		{
			if (reordering)
			{
				reorderDebug += " lost;";
				FinishReorder(apps, e);
			}
		};
		apps.RightTapped += delegate(object _, RightTappedRoutedEventArgs e)
		{
			SetEditMode(value: true);
			SelectFromSource(e.OriginalSource);
			e.Handled = true;
		};
		apps.DragItemsStarting += delegate
		{
			orderBeforeDrag = launchers.ToArray();
		};
		apps.DragItemsCompleted += delegate
		{
			if (orderBeforeDrag != null && !orderBeforeDrag.SequenceEqual(launchers))
			{
				DismissLauncherHint();
			}
			orderBeforeDrag = null;
			SaveApps();
		};
		launchers.CollectionChanged += delegate
		{
			if (!reordering)
			{
				SaveApps();
			}
		};
		Grid.SetRow(apps, 1);
		grid.Children.Add(apps);
		ScrollViewer.SetVerticalScrollMode(apps, ScrollMode.Enabled);
		ScrollViewer.SetVerticalScrollBarVisibility(apps, ScrollBarVisibility.Auto);
		ScrollViewer.SetHorizontalScrollMode(apps, ScrollMode.Disabled);
		StackPanel stackPanel5 = new StackPanel
		{
			Spacing = 4.0,
			Visibility = Visibility.Collapsed
		};
		editHint.Opacity = 0.21;
		editHint.Visibility = (store.Value.LauncherHintDismissed ? Visibility.Collapsed : Visibility.Visible);
		stackPanel5.Children.Add(editHint);
		status.Opacity = 0.6;
		stackPanel5.Children.Add(status);
		Grid.SetRow(stackPanel5, 3);
		grid.Children.Add(stackPanel5);
		root.Tapped += delegate(object _, TappedRoutedEventArgs e)
		{
			object originalSource = e.OriginalSource;
			bool flag = ((originalSource is Grid || originalSource is StackPanel || originalSource is ScrollViewer || originalSource is Border) ? true : false);
			if (flag && !AncestorIsInteractive(e.OriginalSource as DependencyObject))
			{
				if (editing)
				{
					SetEditMode(value: false);
				}
				else
				{
					HidePanel();
				}
			}
		};
		PanelDrag.AttachAny(root, () => !editing && !modal, delegate
		{
			slide?.Stop();
			dragStart = progress;
		}, delegate(double distance)
		{
			progress = Math.Clamp(dragStart - distance * panelDpi / (double)Math.Max(1, Math.Max(height, width)), 0.0, 1.0);
			PositionPanel();
		}, delegate(double velocity, bool cancelled)
		{
			if (cancelled)
			{
				EndGesture(1.0);
			}
			else if (progress < 0.92 || velocity > 0.3)
			{
				HidePanel();
			}
			else
			{
				EndGesture(1.0);
			}
		});
		Microsoft.UI.Dispatching.DispatcherQueue dispatcherQueue = base.DispatcherQueue;
		poll = dispatcherQueue.CreateTimer();
		poll.Interval = TimeSpan.FromSeconds(2.0);
		poll.Tick += async delegate
		{
			await Refresh();
		};
		slide = dispatcherQueue.CreateTimer();
		slide.Interval = TimeSpan.FromMilliseconds(16.0);
		slide.Tick += Animate;
		brightnessDelay = dispatcherQueue.CreateTimer();
		brightnessDelay.IsRepeating = false;
		brightnessDelay.Interval = TimeSpan.FromMilliseconds(100.0);
		brightnessDelay.Tick += async delegate
		{
			await system.SetBrightnessAsync(brightness.Value);
		};
		foregroundDelay = dispatcherQueue.CreateTimer();
		foregroundDelay.IsRepeating = false;
		foregroundDelay.Interval = TimeSpan.FromMilliseconds(150.0);
		foregroundDelay.Tick += delegate
		{
			nint foregroundWindow = Win32.GetForegroundWindow();
			Win32.GetWindowThreadProcessId(foregroundWindow, out var process);
			if (!IgnoreExternalDismissal && shown && !modal && foregroundWindow != 0 && process != Environment.ProcessId)
			{
				HidePanel();
			}
		};
		volumeDelay = dispatcherQueue.CreateTimer();
		volumeDelay.IsRepeating = false;
		volumeDelay.Interval = TimeSpan.FromMilliseconds(30.0);
		volumeDelay.Tick += delegate
		{
			system.SetVolume(volume.Value);
		};
		volume.ValueChanged += delegate(object _, RangeBaseValueChangedEventArgs e)
		{
			if (!syncing)
			{
				lastVolumeInput = DateTime.UtcNow;
				if (!volumeDelay.IsRunning)
				{
					volumeDelay.Start();
				}
			}
			Ui.SetIconGlyph(volumeIcon, (e.NewValue == 0.0) ? "\ue74f" : ((e.NewValue < 50.0) ? "\ue993" : "\ue767"));
		};
		brightness.ValueChanged += delegate(object _, RangeBaseValueChangedEventArgs e)
		{
			Ui.SetIconGlyph(brightnessIcon, (e.NewValue < 34.0) ? "\uec8a" : ((e.NewValue < 67.0) ? "\ue793" : "\ue706"));
			brightnessIcon.FontSize = ((e.NewValue < 34.0) ? 16 : ((e.NewValue < 67.0) ? 19 : 22));
			if (!syncing)
			{
				lastBrightnessInput = DateTime.UtcNow;
				brightnessDelay.Stop();
				brightnessDelay.Start();
			}
		};
		system.PlaybackChanged += PlaybackUpdated;
		system.StateChanged += StateUpdated;
		store.Replaced += SettingsReplaced;
		store.Changed += GestureSettingChanged;
		base.Closed += delegate
		{
			disposed = true;
			CancelWarmup();
			themeSettings.ColorValuesChanged -= ThemeChanged;
			SystemEvents.UserPreferenceChanged -= PreferenceChanged;
			system.PlaybackChanged -= PlaybackUpdated;
			system.StateChanged -= StateUpdated;
			store.Replaced -= SettingsReplaced;
			store.Changed -= GestureSettingChanged;
			poll.Stop();
			slide.Stop();
			brightnessDelay.Stop();
			volumeDelay.Stop();
			RemoveHook();
			mediaArtwork.Source = null;
			shadow?.Hide();
			shadow?.Dispose();
			shadow = null;
			base.SystemBackdrop = null;
			base.Content = null;
		};
		void FinishReorder(object sender, PointerRoutedEventArgs e)
		{
			if (reorderPointer?.PointerId == e.Pointer.PointerId)
			{
				bool num13 = reordering;
				reordering = false;
				reorderItem = null;
				reorderPointer = null;
				launcherPointer = null;
				apps.ReleasePointerCapture(e.Pointer);
				if (num13)
				{
					DismissLauncherHint();
					SaveApps();
					e.Handled = true;
				}
				UpdateSelection();
			}
		}
		void Place(FrameworkElement element, int col, int row, int span = 1)
		{
			Grid.SetColumn(element, col);
			Grid.SetRow(element, row);
			Grid.SetRowSpan(element, span);
			switches.Children.Add(element);
		}
	}

	private void ThemeChanged(UISettings sender, object args)
	{
		dispatcher.TryEnqueue(delegate
		{
			if (!disposed)
			{
				ApplyTheme();
			}
		});
	}

	private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
	{
		dispatcher.TryEnqueue(delegate
		{
			if (!disposed)
			{
				ApplyTheme();
			}
		});
	}

	private void ApplyTheme()
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
		bool flag = Convert.ToInt32(registryKey?.GetValue("SystemUsesLightTheme", 1)) == 0;
		bool flag2 = Convert.ToInt32(registryKey?.GetValue("EnableTransparency", 1)) != 0;
		Grid grid = viewport;
		ElementTheme requestedTheme = (root.RequestedTheme = ((!flag) ? ElementTheme.Light : ElementTheme.Dark));
		grid.RequestedTheme = requestedTheme;
		viewport.Background = Ui.Brush(0, 0, 0, 0);
		root.Background = (flag2 ? Ui.Brush(0, 0, 0, 0) : (flag ? Ui.Brush(32, 32, 32) : Ui.Brush(243, 243, 243)));
		if (base.Content != null)
		{
			ThemeChildren(root, flag);
		}
		mediaTitle.Foreground = ((mediaArtwork.Source != null) ? Ui.Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue) : (flag ? Ui.Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue) : Ui.Brush(30, 36, 44)));
		(base.SystemBackdrop as PanelBackdrop)?.Refresh();
	}

	private static void ThemeChildren(DependencyObject parent, bool dark)
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			DependencyObject child = VisualTreeHelper.GetChild(parent, i);
			if (child is Border { Tag: string tag } border && tag == "MediaScrim")
			{
				border.Background = Ui.Brush(10, 10, 14, 175);
			}
			if (child is Grid { Tag: string tag2 } grid && tag2 == "MediaCard")
			{
				grid.Background = (dark ? Ui.Brush(65, 65, 65, 230) : Ui.Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue, 230));
			}
			if (child is Button button && button.Content is FontIcon)
			{
				button.Background = (object.Equals(button.Tag, "TransparentButton") ? Ui.Brush(0, 0, 0, 0) : (dark ? Ui.Brush(65, 65, 65, 230) : Ui.Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue, 230)));
				button.Foreground = (dark ? Ui.Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue) : Ui.Brush(30, 36, 44));
			}
			ThemeChildren(child, dark);
		}
	}

	private static bool AncestorIsInteractive(DependencyObject? source)
	{
		while (source != null)
		{
			if ((source is Button || source is Slider || source is GridViewItem) ? true : false)
			{
				return true;
			}
			source = VisualTreeHelper.GetParent(source);
		}
		return false;
	}

	private void SelectFromSource(object source)
	{
		if (source is FrameworkElement { DataContext: LauncherItem dataContext })
		{
			apps.SelectedItem = dataContext;
		}
	}

	private void ApplyLauncherSpacing()
	{
		apps.ItemContainerStyle = (Style)XamlReader.Load(string.Format(CultureInfo.InvariantCulture, "<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='GridViewItem'><Setter Property='Margin' Value='0,0,{0},{0}'/><Setter Property='Padding' Value='0'/><Setter Property='CornerRadius' Value='13'/><Setter Property='MinWidth' Value='68'/><Setter Property='MinHeight' Value='68'/></Style>", store.Value.LauncherSpacing));
	}

	private void SetEditMode(bool value)
	{
		editing = value;
		apps.SelectionMode = (value ? ListViewSelectionMode.Single : ListViewSelectionMode.None);
		GridView gridView = apps;
		bool canDragItems = (apps.CanReorderItems = false);
		gridView.CanDragItems = canDragItems;
		Scrolling(apps);
		foreach (LauncherItem launcher in launchers)
		{
			launcher.SetEditing(value);
		}
		UpdateSelection();
		editHint.Text = Localization.T(value ? "编辑中 · 拖动排序，点击右上角删除；点击空白处完成" : "长按应用可排序或删除");
		SaveApps();
		void Scrolling(DependencyObject node)
		{
			if (node is ScrollViewer scrollViewer)
			{
				scrollViewer.VerticalScrollMode = ((!value) ? ScrollMode.Enabled : ScrollMode.Disabled);
				if (value)
				{
					scrollViewer.CancelDirectManipulations();
				}
			}
			for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
			{
				Scrolling(VisualTreeHelper.GetChild(node, i));
			}
		}
	}

	private void UpdateSelection()
	{
		foreach (LauncherItem launcher in launchers)
		{
			launcher.SetHover(value: false);
			launcher.SetPressed(value: false);
			launcher.SetSelected(editing && apps.SelectedItem == launcher);
		}
	}

	private void WireVisibleLaunchers()
	{
		foreach (LauncherItem launcher in launchers)
		{
			if (apps.ContainerFromItem(launcher) is SelectorItem container)
			{
				WireLauncher(container, launcher);
			}
		}
	}

	private void WireLauncher(SelectorItem container, LauncherItem? item)
	{
		if (item != null)
		{
			container.ApplyTemplate();
			ConfigureSelectionPresenter(container);
			if (container.ContentTemplateRoot is FrameworkElement frameworkElement && frameworkElement.FindName("DeleteLauncher") is Button button && button.Tag != item)
			{
				frameworkElement.PointerEntered -= LauncherEntered;
				frameworkElement.PointerEntered += LauncherEntered;
				frameworkElement.PointerExited -= LauncherExited;
				frameworkElement.PointerExited += LauncherExited;
				frameworkElement.PointerPressed -= LauncherPressed;
				frameworkElement.PointerPressed += LauncherPressed;
				frameworkElement.PointerReleased -= LauncherReleased;
				frameworkElement.PointerReleased += LauncherReleased;
				frameworkElement.PointerCanceled -= LauncherReleased;
				frameworkElement.PointerCanceled += LauncherReleased;
				frameworkElement.PointerCaptureLost -= LauncherReleased;
				frameworkElement.PointerCaptureLost += LauncherReleased;
				button.Tag = item;
				button.Click -= DeleteLauncher;
				button.Click += DeleteLauncher;
			}
		}
	}

	private static void ConfigureSelectionPresenter(DependencyObject parent)
	{
		if (parent is ListViewItemPresenter listViewItemPresenter)
		{
			listViewItemPresenter.SelectedBorderThickness = new Thickness(0.0);
			listViewItemPresenter.SelectionCheckMarkVisualEnabled = false;
			Microsoft.UI.Xaml.Media.Brush pointerOverBackground = (listViewItemPresenter.PressedBackground = Ui.Brush(0, 0, 0, 0));
			listViewItemPresenter.PointerOverBackground = pointerOverBackground;
			Microsoft.UI.Xaml.Media.Brush brush2 = (listViewItemPresenter.SelectedPressedBackground = Ui.Brush(0, 0, 0, 0));
			pointerOverBackground = (listViewItemPresenter.SelectedPointerOverBackground = brush2);
			listViewItemPresenter.SelectedBackground = pointerOverBackground;
		}
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
		{
			ConfigureSelectionPresenter(VisualTreeHelper.GetChild(parent, i));
		}
	}

	private void HighlightLauncher(LauncherItem active, bool pressed)
	{
		foreach (LauncherItem launcher in launchers)
		{
			launcher.SetSelected(value: false);
			launcher.SetHover(!pressed && launcher == active);
			launcher.SetPressed(pressed && launcher == active);
		}
	}

	private void LauncherEntered(object sender, PointerRoutedEventArgs e)
	{
		if (!launcherPointer.HasValue && e.Pointer.PointerDeviceType == PointerDeviceType.Mouse && sender is FrameworkElement { DataContext: LauncherItem dataContext })
		{
			HighlightLauncher(dataContext, pressed: false);
		}
	}

	private void LauncherExited(object sender, PointerRoutedEventArgs e)
	{
		if (sender is FrameworkElement { DataContext: LauncherItem dataContext })
		{
			dataContext.SetHover(value: false);
			dataContext.SetPressed(value: false);
		}
	}

	private void LauncherPressed(object sender, PointerRoutedEventArgs e)
	{
		if (!launcherPointer.HasValue && sender is FrameworkElement { DataContext: LauncherItem dataContext })
		{
			reorderDebug = "pressed;";
			launcherPointer = e.Pointer.PointerId;
			reorderPointer = e.Pointer;
			reorderItem = dataContext;
			HighlightLauncher(dataContext, pressed: true);
			if (editing)
			{
				apps.CancelDirectManipulations();
				reordering = apps.CapturePointer(e.Pointer);
				e.Handled = true;
			}
		}
	}

	private void LauncherReleased(object sender, PointerRoutedEventArgs e)
	{
		if (launcherPointer == e.Pointer.PointerId)
		{
			launcherPointer = null;
			UpdateSelection();
		}
	}

	private void DismissLauncherHint()
	{
		store.Value.LauncherHintDismissed = true;
		editHint.Visibility = Visibility.Collapsed;
		store.Save();
	}

	private void DeleteLauncher(object sender, RoutedEventArgs e)
	{
		if (editing && sender is FrameworkElement { Tag: LauncherItem tag })
		{
			launchers.Remove(tag);
			DismissLauncherHint();
		}
	}

	internal void CheckLauncherHighlight()
	{
		if (launchers.Count < 2)
		{
			throw new InvalidOperationException("快捷启动测试项不足。");
		}
		HighlightLauncher(launchers[0], pressed: false);
		HighlightLauncher(launchers[1], pressed: true);
		if (launchers.Count((LauncherItem item) => item.SelectionVisibility == Visibility.Visible) != 1 || launchers[1].SelectionVisibility != Visibility.Visible)
		{
			throw new InvalidOperationException("快捷启动出现多个活动描边。");
		}
		UpdateSelection();
		if (launchers.Any((LauncherItem item) => item.SelectionVisibility == Visibility.Visible))
		{
			throw new InvalidOperationException("触摸结束后残留活动描边。");
		}
	}

	internal async Task CheckBatchPickerAsync()
	{
		Task<List<LauncherEntry>> pick = Ui.PickLaunchersAsync(root.XamlRoot, this);
		ContentDialog dialog = null;
		ListView list = null;
		for (int i = 0; i < 150; i++)
		{
			await Task.Delay(100);
			dialog = VisualTreeHelper.GetOpenPopupsForXamlRoot(root.XamlRoot).SelectMany((Popup p) => Find<ContentDialog>(p.Child)).FirstOrDefault() ?? Find<ContentDialog>(root.XamlRoot.Content).FirstOrDefault();
			if (dialog != null)
			{
				list = Find<ListView>(dialog).FirstOrDefault();
				ListView listView = list;
				if ((object)listView != null && listView.Items.Count >= 2)
				{
					break;
				}
			}
		}
		if (dialog == null || list == null || list.Items.Count < 2)
		{
			throw new InvalidOperationException("多选应用列表没有加载。");
		}
		try
		{
			list.SelectedItems.Add(list.Items[0]);
			list.SelectedItems.Add(list.Items[1]);
			TextBox textBox = Find<TextBox>(dialog).Single((TextBox t) => t.PlaceholderText == "搜索已安装应用");
			textBox.Text = "__no_matching_fixture__";
			textBox.Text = "";
			if (list.SelectedItems.Count != 2)
			{
				throw new InvalidOperationException("搜索丢失多选状态。");
			}
			dialog.UpdateLayout();
			((IInvokeProvider)new ButtonAutomationPeer(Find<Button>(dialog).Single((Button b) => object.Equals(b.Content, "添加（2）"))).GetPattern(PatternInterface.Invoke)).Invoke();
			if ((await pick).Count != 2)
			{
				throw new InvalidOperationException("没有批量返回两个应用。");
			}
		}
		finally
		{
			dialog.Hide();
		}
		static IEnumerable<T> Find<T>(DependencyObject node) where T : notnull, DependencyObject
		{
			if (node is T val)
			{
				yield return val;
			}
			for (int j = 0; j < VisualTreeHelper.GetChildrenCount(node); j++)
			{
				foreach (T item in Find<T>(VisualTreeHelper.GetChild(node, j)))
				{
					yield return item;
				}
			}
		}
	}

	internal async Task CheckV087LaunchersAsync()
	{
		if (launchers.Count < 2)
		{
			throw new InvalidOperationException("需要两个快捷启动测试项。");
		}
		SetEditMode(value: false);
		root.UpdateLayout();
		WireVisibleLaunchers();
		LauncherItem[] original = launchers.ToArray();
		try
		{
			await TouchSmoke.Swipe(Point(0), Point(1), 900);
			await Task.Delay(120);
			if (launchers[0] == original[0])
			{
				throw new InvalidOperationException($"长按当前手指没有完成排序，Editing={editing},Reordering={reordering},Pointer={launcherPointer},Trace={reorderDebug}");
			}
			if (store.Value.Launchers[0] != launchers[0].Entry)
			{
				throw new InvalidOperationException("排序未保存。");
			}
		}
		finally
		{
			reordering = false;
			reorderPointer = null;
			reorderItem = null;
			launcherPointer = null;
			SetEditMode(value: false);
			launchers.Clear();
			LauncherItem[] array = original;
			foreach (LauncherItem item in array)
			{
				launchers.Add(item);
			}
		}
		Win32.POINT Point(int index)
		{
			FrameworkElement frameworkElement = (FrameworkElement)apps.ContainerFromIndex(index);
			Windows.Foundation.Point point = frameworkElement.TransformToVisual(viewport).TransformPoint(new Windows.Foundation.Point(frameworkElement.ActualWidth / 2.0, frameworkElement.ActualHeight / 2.0));
			Win32.GetWindowRect(Ui.Handle(this), out var r);
			return new Win32.POINT(r.Left + (int)(point.X * panelDpi), r.Top + (int)(point.Y * panelDpi));
		}
	}

	internal async Task CheckThemeChangesAsync()
	{
		for (int i = 0; i < 8; i++)
		{
			root.RequestedTheme = ((i % 2 == 0) ? ElementTheme.Light : ElementTheme.Dark);
			await Task.Delay(60);
			(base.SystemBackdrop as PanelBackdrop)?.Refresh();
		}
		ApplyTheme();
	}

	internal async Task CheckLauncherScrollingAsync()
	{
		LauncherItem[] original = launchers.ToArray();
		try
		{
			for (int i = 0; i < 32; i++)
			{
				launchers.Add(new LauncherItem(new LauncherEntry
				{
					Name = "滚动测试 " + i,
					Target = "notepad.exe"
				}));
			}
			apps.UpdateLayout();
			await Task.Delay(150);
			ScrollViewer scroller = FindScroller(apps) ?? throw new InvalidOperationException("快捷启动滚动容器缺失。");
			double headerY = time.TransformToVisual(root).TransformPoint(default(Windows.Foundation.Point)).Y;
			if (scroller.ScrollableHeight <= 0.0)
			{
				throw new InvalidOperationException("快捷启动未产生独立滚动范围。");
			}
			scroller.ChangeView(null, scroller.ScrollableHeight, null, disableAnimation: true);
			await Task.Delay(150);
			if (scroller.VerticalOffset <= 0.0 || Math.Abs(time.TransformToVisual(root).TransformPoint(default(Windows.Foundation.Point)).Y - headerY) > 0.1)
			{
				throw new InvalidOperationException("快捷启动滚动或固定控制区检查失败。");
			}
			scroller.ChangeView(null, 0.0, null, disableAnimation: true);
		}
		finally
		{
			launchers.Clear();
			LauncherItem[] array = original;
			foreach (LauncherItem item in array)
			{
				launchers.Add(item);
			}
		}
		static ScrollViewer? FindScroller(DependencyObject node)
		{
			if (node is ScrollViewer result)
			{
				return result;
			}
			for (int k = 0; k < VisualTreeHelper.GetChildrenCount(node); k++)
			{
				ScrollViewer scrollViewer = FindScroller(VisualTreeHelper.GetChild(node, k));
				if (scrollViewer != null)
				{
					return scrollViewer;
				}
			}
			return null;
		}
	}

	private void SettingsReplaced()
	{
		importingLaunchers = true;
		try
		{
			apps.ReleasePointerCaptures();
			reordering = false;
			reorderItem = null;
			reorderPointer = null;
			launcherPointer = null;
			launchers.Clear();
			foreach (LauncherEntry launcher in store.Value.Launchers)
			{
				launchers.Add(new LauncherItem(launcher));
			}
			SetEditMode(value: false);
			ApplyLauncherSpacing();
			ApplyCachedStates();
		}
		finally
		{
			importingLaunchers = false;
		}
	}

	private void SaveApps()
	{
		if (!importingLaunchers)
		{
			store.Value.Launchers = launchers.Select((LauncherItem x) => x.Entry).ToList();
			store.Save();
		}
	}

	private async Task AddApp()
	{
		modal = true;
		if (outsideMonitor != null)
		{
			outsideMonitor.Suspended = true;
		}
		TextBox icon;
		ComboBox iconFont;
		TextBox iconCode;
		ComboBox iconRange;
		WrapPanel iconWrap;
		List<int> keys;
		TextBox keyBox;
		TextBox appSearch;
		ListView appList;
		List<LauncherItem> appItems;
		try
		{
			TextBox name = new TextBox
			{
				Header = Localization.T("名称"),
				PlaceholderText = Localization.T("例如：截图、返回桌面")
			};
			icon = new TextBox
			{
				Header = Localization.T("图标字符"),
				PlaceholderText = Localization.T("可从系统图标字体选择")
			};
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 12.0
			};
			stackPanel.Children.Add(icon);
			iconFont = new ComboBox
			{
				Header = Localization.T("系统图标字体"),
				ItemsSource = IconFontResolver.GetAvailableIconFontFamilies(),
				SelectedIndex = 0,
				MinWidth = 200.0
			};
			iconCode = new TextBox
			{
				Header = Localization.T("Unicode 编码"),
				PlaceholderText = "E90C",
				Width = 150.0
			};
			iconRange = new ComboBox
			{
				Header = Localization.T("字符范围"),
				ItemsSource = (from i in Enumerable.Range(0, 25)
					select $"{57344 + i * 256:X4}–{57599 + i * 256:X4}").ToArray(),
				SelectedIndex = 9,
				MinWidth = 200.0
			};
			iconWrap = new WrapPanel
			{
				HorizontalSpacing = 2.0,
				VerticalSpacing = 2.0,
				HorizontalAlignment = HorizontalAlignment.Left,
				Width = 600.0
			};
			iconFont.SelectionChanged += delegate
			{
				FillIcons();
			};
			iconRange.SelectionChanged += delegate
			{
				FillIcons();
			};
			FillIcons();
			iconCode.TextChanged += delegate
			{
				if (int.TryParse(iconCode.Text.Trim().Replace("U+", "", StringComparison.OrdinalIgnoreCase), NumberStyles.HexNumber, null, out var result) && result >= 32 && result <= 65535 && (result < 55296 || result > 57343))
				{
					icon.Text = ((char)result).ToString();
				}
			};
			StackPanel stackPanel2 = new StackPanel
			{
				Spacing = 8.0
			};
			StackPanel stackPanel3 = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8.0
			};
			stackPanel3.Children.Add(iconFont);
			stackPanel3.Children.Add(iconCode);
			stackPanel3.Children.Add(iconRange);
			stackPanel2.Children.Add(stackPanel3);
			stackPanel2.Children.Add(new ScrollViewer
			{
				Content = iconWrap,
				MaxHeight = 230.0,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto
			});
			Expander item = new Expander
			{
				Header = Localization.T("从系统图标字体选择"),
				Content = stackPanel2,
				HorizontalAlignment = HorizontalAlignment.Stretch,
				HorizontalContentAlignment = HorizontalAlignment.Stretch
			};
			ComboBox type = new ComboBox
			{
				Header = Localization.T("添加类型"),
				ItemsSource = new string[2]
				{
					Localization.T("启动应用"),
					Localization.T("自定义键")
				},
				SelectedIndex = 0,
				MinWidth = 180.0
			};
			keys = new List<int>();
			keyBox = new TextBox
			{
				Header = Localization.T("快捷键"),
				IsReadOnly = true,
				PlaceholderText = Localization.T("点击后按下组合键")
			};
			keyBox.PreviewKeyDown += delegate(object _, KeyRoutedEventArgs e)
			{
				if (e.Key != VirtualKey.Tab)
				{
					AddKey((int)e.Key);
					e.Handled = true;
				}
			};
			TextBox target = new TextBox
			{
				Header = Localization.T("应用路径"),
				PlaceholderText = Localization.T("EXE 路径 / UWP AUMID / URI")
			};
			appSearch = new TextBox
			{
				PlaceholderText = Localization.T("搜索已安装应用"),
				MinHeight = 40.0
			};
			appList = new ListView
			{
				Height = 180.0,
				SelectionMode = ListViewSelectionMode.Single
			};
			appList.ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'><Grid Height='44' ColumnSpacing='10'><Grid.ColumnDefinitions><ColumnDefinition Width='34'/><ColumnDefinition Width='*'/></Grid.ColumnDefinitions><Grid><FontIcon Glyph='{Binding FallbackGlyph}' FontFamily='{Binding FallbackFontFamily}' FontSize='22'/><Image Source='{Binding Icon}' Width='28' Height='28'/></Grid><TextBlock Grid.Column='1' Text='{Binding Name}' VerticalAlignment='Center' TextTrimming='CharacterEllipsis'/></Grid></DataTemplate>");
			appList.ContainerContentChanging += delegate(ListViewBase sender, ContainerContentChangingEventArgs e)
			{
				if (!e.InRecycleQueue && e.Item is LauncherItem launcherItem2)
				{
					launcherItem2.LoadIconAsync();
				}
			};
			appItems = new List<LauncherItem>();
			appSearch.TextChanged += delegate
			{
				FilterApps();
			};
			appList.SelectionChanged += delegate
			{
				if (appList.SelectedItem is LauncherItem launcherItem2)
				{
					target.Text = launcherItem2.Target;
					if (string.IsNullOrWhiteSpace(name.Text))
					{
						name.Text = launcherItem2.Name;
					}
				}
			};
			LoadInstalledApps();
			StackPanel appPanel = new StackPanel
			{
				Spacing = 10.0
			};
			appPanel.Children.Add(appSearch);
			appPanel.Children.Add(appList);
			appPanel.Children.Add(target);
			appPanel.Children.Add(Ui.Button(Localization.T("浏览 EXE…"), async delegate
			{
				string text = await TouchFilePicker.Pick(this, Localization.T("选择应用程序"), ".exe");
				if (!string.IsNullOrWhiteSpace(text))
				{
					target.Text = text;
					if (string.IsNullOrWhiteSpace(name.Text))
					{
						name.Text = Path.GetFileNameWithoutExtension(text);
					}
				}
			}));
			StackPanel stackPanel4 = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8.0
			};
			stackPanel4.Children.Add(Ui.Button(Localization.T("清空快捷键"), delegate
			{
				keys.Clear();
				keyBox.Text = "";
			}));
			FrameworkElement frameworkElement = SettingsWindow.SimulatedKeyboard(AddKey);
			frameworkElement.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
			double val = frameworkElement.DesiredSize.Width;
			WrapPanel wrapPanel = new WrapPanel
			{
				HorizontalSpacing = 4.0,
				VerticalSpacing = 4.0,
				HorizontalAlignment = HorizontalAlignment.Left,
				Width = Math.Max(720.0, val)
			};
			(string, int)[] special = KeyCatalog.Special;
			for (int num = 0; num < special.Length; num++)
			{
				(string Name, int Code) special2 = special[num];
				Button button = new Button
				{
					Content = new TextBlock
					{
						Text = Localization.T(special2.Name),
						FontSize = 10.0,
						TextTrimming = TextTrimming.CharacterEllipsis
					},
					Width = 74.0,
					Height = 32.0,
					MinHeight = 32.0,
					Padding = new Thickness(4.0, 0.0, 4.0, 0.0),
					CornerRadius = new CornerRadius(6.0)
				};
				button.Click += delegate
				{
					AddKey(special2.Code);
				};
				wrapPanel.Children.Add(button);
			}
			StackPanel stackPanel5 = new StackPanel
			{
				Spacing = 6.0,
				HorizontalAlignment = HorizontalAlignment.Left,
				MinWidth = wrapPanel.Width
			};
			stackPanel5.Children.Add(new TextBlock
			{
				Text = Localization.T("额外功能键"),
				FontSize = 12.0
			});
			stackPanel5.Children.Add(wrapPanel);
			stackPanel5.Children.Add(frameworkElement);
			StackPanel keyPanel = new StackPanel
			{
				Spacing = 10.0
			};
			keyPanel.Children.Add(keyBox);
			keyPanel.Children.Add(stackPanel4);
			keyPanel.Children.Add(new ScrollViewer
			{
				Content = stackPanel5,
				MaxHeight = 420.0,
				HorizontalScrollMode = ScrollMode.Enabled,
				VerticalScrollMode = ScrollMode.Enabled,
				HorizontalScrollBarVisibility = ScrollBarVisibility.Visible,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto
			});
			type.SelectionChanged += delegate
			{
				appPanel.Visibility = ((type.SelectedIndex != 0) ? Visibility.Collapsed : Visibility.Visible);
				keyPanel.Visibility = ((type.SelectedIndex != 1) ? Visibility.Collapsed : Visibility.Visible);
			};
			appPanel.Visibility = Visibility.Visible;
			keyPanel.Visibility = Visibility.Collapsed;
			StackPanel stackPanel6 = new StackPanel
			{
				Spacing = 12.0
			};
			stackPanel6.Children.Add(name);
			stackPanel6.Children.Add(stackPanel);
			stackPanel6.Children.Add(item);
			stackPanel6.Children.Add(type);
			stackPanel6.Children.Add(appPanel);
			stackPanel6.Children.Add(keyPanel);
			stackPanel6.Children.Add(Ui.Description(Localization.T("选择应用或自定义键；快捷键按组合键顺序触发。名称和图标会显示在快捷启动中。"), 12.0));
			if (await new ContentDialog
			{
				XamlRoot = root.XamlRoot,
				Title = Localization.T("添加快捷按钮"),
				PrimaryButtonText = Localization.T("添加"),
				CloseButtonText = Localization.T("取消"),
				DefaultButton = ContentDialogButton.Primary,
				MaxWidth = 920.0,
				Content = new ScrollViewer
				{
					Content = stackPanel6,
					MaxHeight = 700.0,
					HorizontalScrollMode = ScrollMode.Disabled,
					HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
					VerticalScrollBarVisibility = ScrollBarVisibility.Auto
				}
			}.ShowAsync() == ContentDialogResult.Primary)
			{
				bool flag = type.SelectedIndex == 1;
				LauncherEntry launcherEntry = new LauncherEntry
				{
					Name = ((!string.IsNullOrWhiteSpace(name.Text)) ? name.Text.Trim() : (flag ? KeyCatalog.Format(keys) : Path.GetFileNameWithoutExtension(target.Text))),
					Target = (flag ? "" : target.Text.Trim()),
					IconGlyph = icon.Text.Trim(),
					IconFont = IconFontResolver.NormalizeIconFont(iconFont.SelectedItem as string, icon.Text.Trim()),
					Keys = (flag ? new List<int>(keys) : new List<int>())
				};
				if (flag ? (launcherEntry.Keys.Count == 0) : string.IsNullOrWhiteSpace(launcherEntry.Target))
				{
					App.CurrentApp.Report(Localization.T(flag ? "请按下快捷键。" : "请选择应用。"));
					return;
				}
				LauncherItem launcherItem = new LauncherItem(launcherEntry);
				launcherItem.SetEditing(editing);
				launchers.Add(launcherItem);
				launcherItem.LoadIconAsync();
			}
		}
		finally
		{
			modal = false;
			if (outsideMonitor != null)
			{
				outsideMonitor.Suspended = false;
			}
		}
		void AddKey(int key)
		{
			key = KeyCatalog.Normalize(key);
			if (!keys.Contains(key))
			{
				keys.Add(key);
			}
			keyBox.Text = KeyCatalog.Format(keys);
		}
		void FillIcons()
		{
			iconWrap.Children.Clear();
			string familyName = (iconFont.SelectedItem as string) ?? IconFontResolver.PreferredFontFamilyName;
			for (int i = 0; i < 256; i++)
			{
				int value = 57344 + Math.Max(0, iconRange.SelectedIndex) * 256 + i;
				FontIcon content = new FontIcon
				{
					Glyph = char.ConvertFromUtf32(value),
					FontFamily = IconFontResolver.CreateXamlFontFamily(char.ConvertFromUtf32(value), familyName),
					FontSize = 22.0,
					Width = 36.0,
					Height = 36.0,
					Tag = value
				};
				Button button2 = new Button
				{
					Content = content,
					Width = 38.0,
					Height = 38.0,
					MinWidth = 38.0,
					MinHeight = 38.0,
					Padding = new Thickness(0.0),
					CornerRadius = new CornerRadius(6.0)
				};
				ToolTipService.SetToolTip(button2, $"U+{value:X4}");
				button2.Click += delegate
				{
					icon.Text = char.ConvertFromUtf32(value);
					iconCode.Text = value.ToString("X4");
				};
				iconWrap.Children.Add(button2);
			}
		}
		void FilterApps()
		{
			appList.ItemsSource = appItems.Where((LauncherItem x) => string.IsNullOrWhiteSpace(appSearch.Text) || x.Name.Contains(appSearch.Text, StringComparison.CurrentCultureIgnoreCase) || x.Target.Contains(appSearch.Text, StringComparison.OrdinalIgnoreCase)).ToList();
		}
		async Task LoadInstalledApps()
		{
			try
			{
				List<LauncherItem> list = appItems;
				list.AddRange((await InstalledApps.LoadAsync()).Select((LauncherEntry x) => new LauncherItem(x)));
				FilterApps();
			}
			catch
			{
				appSearch.PlaceholderText = Localization.T("应用列表不可用，请直接填写路径");
			}
		}
	}

	private void PlaybackUpdated()
	{
		dispatcher.TryEnqueue(delegate
		{
			if (!disposed && shown && !mediaBusy)
			{
				Ui.SetIconGlyph((FontIcon)play.Content, system.IsPlaying ? "\ue769" : "\ue768");
			}
		});
	}

	private async Task Media(string operation)
	{
		if (mediaBusy)
		{
			return;
		}
		mediaBusy = true;
		lastMediaInput = DateTime.UtcNow;
		string previous = ((FontIcon)play.Content).Glyph;
		if (operation == "Toggle")
		{
			Ui.SetIconGlyph((FontIcon)play.Content, (previous == "\ue768") ? "\ue769" : "\ue768");
		}
		try
		{
			if (!(await system.MediaAsync(operation)))
			{
				Ui.SetIconGlyph((FontIcon)play.Content, previous);
			}
		}
		finally
		{
			mediaBusy = false;
		}
	}

	private Button Toggle(string glyph, string name, Func<Task> action)
	{
		string settings = name switch
		{
			"Wi-Fi" => "ms-settings:network-wifi", 
			"蓝牙" => "ms-settings:bluetooth", 
			"移动热点" => "ms-settings:network-mobilehotspot", 
			"飞行模式" => "ms-settings:network-airplanemode", 
			"始终使用节能模式" => "ms-settings:powersleep", 
			"旋转锁定" => "ms-settings:display", 
			"单击打开项目" => "control.exe folders", 
			_ => null, 
		};
		Button button = null;
		button = Ui.IconButton(glyph, name, async delegate
		{
			button.IsEnabled = false;
			try
			{
				await App.CurrentApp.FromControlCenter(action);
				await Refresh();
			}
			catch (Exception ex)
			{
				systemNotice(ex.Message);
			}
			finally
			{
				button.IsEnabled = true;
			}
		}, 50.0, (settings == null) ? null : ((Action)delegate
		{
			OpenSettings(settings);
		}));
		return button;
	}

	private void systemNotice(string message)
	{
		App.CurrentApp.Report(message);
	}

	public void ShowNotice(string message)
	{
		App.CurrentApp.Report(message);
	}

	private Task OpenSettings(string uri)
	{
		RunShortcut(delegate
		{
			if (uri == "control.exe folders")
			{
				Process.Start(new ProcessStartInfo("control.exe", "folders")
				{
					UseShellExecute = true
				});
			}
			else
			{
				SystemControl.Launch(uri);
			}
		});
		return Task.CompletedTask;
	}

	private Border SliderTile(Slider slider, FontIcon icon, string name)
	{
		Grid grid = new Grid
		{
			Tag = "RevealSlider",
			Height = 108.0,
			Width = 50.0,
			Background = Ui.Brush(76, 88, 106, 105),
			CornerRadius = new CornerRadius(12.0)
		};
		Border fill = new Border
		{
			Background = Ui.Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue),
			CornerRadius = new CornerRadius(12.0),
			Height = slider.Value / 100.0 * 108.0,
			VerticalAlignment = VerticalAlignment.Bottom,
			IsHitTestVisible = false
		};
		grid.Children.Add(fill);
		slider.IsThumbToolTipEnabled = false;
		slider.Opacity = 0.0;
		slider.Height = 108.0;
		slider.Width = 50.0;
		grid.Children.Add(slider);
		bool dragging = false;
		grid.AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)delegate(object _, PointerRoutedEventArgs e)
		{
			if (slider.IsEnabled)
			{
				dragging = true;
				grid.CapturePointer(e.Pointer);
				SetValue(e);
				e.Handled = true;
			}
		}, handledEventsToo: true);
		grid.PointerMoved += delegate(object _, PointerRoutedEventArgs e)
		{
			if (dragging)
			{
				SetValue(e);
				e.Handled = true;
			}
		};
		grid.PointerReleased += delegate(object _, PointerRoutedEventArgs e)
		{
			if (dragging)
			{
				SetValue(e);
				dragging = false;
				grid.ReleasePointerCapture(e.Pointer);
				e.Handled = true;
			}
		};
		grid.PointerCanceled += delegate
		{
			dragging = false;
		};
		grid.PointerCaptureLost += delegate
		{
			dragging = false;
		};
		root.ActualThemeChanged += delegate
		{
			UpdateContrast();
		};
		slider.ValueChanged += delegate(object _, RangeBaseValueChangedEventArgs e)
		{
			fill.Height = e.NewValue / 100.0 * 108.0;
			UpdateContrast();
		};
		slider.IsEnabledChanged += delegate
		{
			grid.Opacity = (slider.IsEnabled ? 1.0 : 0.45);
		};
		UpdateContrast();
		icon.VerticalAlignment = VerticalAlignment.Bottom;
		icon.Margin = new Thickness(0.0, 0.0, 0.0, 14.0);
		icon.IsHitTestVisible = false;
		grid.Children.Add(icon);
		AutomationProperties.SetName(slider, name);
		return new Border
		{
			Child = grid,
			CornerRadius = new CornerRadius(12.0)
		};
		void SetValue(PointerRoutedEventArgs args)
		{
			if (slider.IsEnabled)
			{
				slider.Value = Math.Round(Math.Clamp(1.0 - args.GetCurrentPoint(grid).Position.Y / 108.0, 0.0, 1.0) * 100.0);
			}
		}
		void UpdateContrast()
		{
			icon.Foreground = ((slider.Value > 30.0 || root.RequestedTheme != ElementTheme.Dark) ? Ui.Brush(30, 36, 44) : Ui.Brush(240, 240, 240));
		}
	}

	internal async Task CheckCompactLaunchersAsync()
	{
		LauncherEntry[] old = launchers.Select((LauncherItem x) => x.Entry).ToArray();
		try
		{
			while (launchers.Count < 5)
			{
				launchers.Add(new LauncherItem(new LauncherEntry
				{
					Name = "验证应用 " + launchers.Count,
					Target = "notepad.exe"
				}));
			}
			apps.UpdateLayout();
			await Task.Delay(200);
			apps.ScrollIntoView(launchers[4]);
			await Task.Delay(100);
			apps.UpdateLayout();
			double? num = null;
			double num2 = -1.0;
			for (int num3 = 0; num3 < 5; num3++)
			{
				Windows.Foundation.Point point = ((apps.ContainerFromIndex(num3) as FrameworkElement) ?? throw new InvalidOperationException("应用图标未生成")).TransformToVisual(apps).TransformPoint(new Windows.Foundation.Point(0f, 0f));
				if (num.HasValue && Math.Abs(point.Y - num.Value) > 1.0)
				{
					throw new InvalidOperationException("快捷启动一排未显示五个应用");
				}
				if (num3 > 0 && point.X <= num2)
				{
					throw new InvalidOperationException("快捷启动顺序错误");
				}
				num = point.Y;
				num2 = point.X;
			}
			if (((FontIcon)single.Content).Glyph != "\ue1e3" || ((FontIcon)dialToggle.Content).Glyph != "\ue958" || ((FontIcon)arkToggle.Content).Glyph != "\ueca5" || ((FontIcon)guardToggle.Content).Glyph != "\uf439")
			{
				throw new InvalidOperationException("系统功能图标未更新");
			}
		}
		finally
		{
			launchers.Clear();
			LauncherEntry[] array = old;
			foreach (LauncherEntry entry in array)
			{
				launchers.Add(new LauncherItem(entry));
			}
		}
	}

	internal void SyncDialState()
	{
		State(dialToggle, App.CurrentApp.DialVisible);
	}

	[DllImport("user32")]
	private static extern bool GetClientRect(nint hwnd, out Win32.RECT rect);

	internal void CheckImmediateState()
	{
		if (!(stage.Background is SolidColorBrush solidColorBrush) || solidColorBrush.Color != AccentTheme.Brush.Color)
		{
			throw new InvalidOperationException("首次呈现没有同步已知按钮状态。");
		}
	}

	internal async Task CheckV0810Async(Action capture)
	{
		poll.Stop();
		for (int i = 0; i < 200; i++)
		{
			if (!refreshBusy)
			{
				break;
			}
			await Task.Delay(20);
		}
		if (!GetClientRect(Ui.Handle(this), out var rect) || rect.Width != base.AppWindow.Size.Width || rect.Height != base.AppWindow.Size.Height)
		{
			throw new InvalidOperationException("面板仍然保留非客户区白边。");
		}
		await Task.Delay(100);
		Ui.DwmFlush();
		capture();
		Settings settings = JsonSerializer.Deserialize<Settings>(JsonSerializer.Serialize(store.Value));
		settings.Launchers = new List<LauncherEntry>
		{
			new LauncherEntry
			{
				Name = "导入列表验证",
				Target = "fixture:imported"
			}
		};
		store.Import(settings);
		if (launchers.Count != 1 || launchers[0].Entry.Target != "fixture:imported" || store.Value.Launchers.Count != 1)
		{
			throw new InvalidOperationException("导入后快捷启动没有同步，或被旧列表覆盖。");
		}
	}

	private void State(Button b, bool on)
	{
		bool flag = root.RequestedTheme == ElementTheme.Dark;
		Windows.UI.Color color = (on ? AccentTheme.Brush.Color : (flag ? Windows.UI.Color.FromArgb(230, 65, 65, 65) : Windows.UI.Color.FromArgb(230, byte.MaxValue, byte.MaxValue, byte.MaxValue)));
		Windows.UI.Color color2 = ((on || flag) ? Colors.White : Windows.UI.Color.FromArgb(byte.MaxValue, 30, 36, 44));
		if (!(b.Background is SolidColorBrush solidColorBrush) || solidColorBrush.Color != color)
		{
			b.Background = new SolidColorBrush(color);
		}
		if (!(b.Foreground is SolidColorBrush solidColorBrush2) || solidColorBrush2.Color != color2)
		{
			b.Foreground = new SolidColorBrush(color2);
		}
		string[] array = new string[2] { "ButtonBackgroundPointerOver", "ButtonBackgroundPressed" };
		foreach (string key in array)
		{
			if (!b.Resources.ContainsKey(key) || b.Resources[key] != b.Background)
			{
				b.Resources[key] = b.Background;
			}
		}
	}

	private async Task LoadArtwork(IRandomAccessStreamReference reference, string key)
	{
		using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();
		BitmapImage bitmap = new BitmapImage
		{
			DecodePixelWidth = 332,
			DecodePixelHeight = 332
		};
		await bitmap.SetSourceAsync(stream);
		if (!disposed && shown)
		{
			mediaArtwork.Source = bitmap;
			mediaTitle.Foreground = Ui.Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue);
			mediaScrim.Visibility = Visibility.Visible;
			artworkKey = key;
		}
	}

	internal async Task CheckArtworkAsync(Action capture)
	{
		poll.Stop();
		for (int i = 0; i < 100; i++)
		{
			if (!refreshBusy)
			{
				break;
			}
			await Task.Delay(20);
		}
		using MemoryStream png = new MemoryStream();
		using (Bitmap bitmap = new Bitmap(64, 64))
		{
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.Clear(System.Drawing.Color.SlateBlue);
				graphics.FillEllipse(Brushes.CornflowerBlue, 6, 6, 52, 52);
				graphics.FillEllipse(Brushes.Gold, 26, 26, 12, 12);
			}
			bitmap.Save(png, ImageFormat.Png);
		}
		using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
		using (DataWriter writer = new DataWriter(stream.GetOutputStreamAt(0uL)))
		{
			writer.WriteBytes(png.ToArray());
			await writer.StoreAsync();
		}
		await LoadArtwork(RandomAccessStreamReference.CreateFromStream(stream), "fixture");
		mediaTitle.Text = Localization.T("媒体封面测试");
		root.UpdateLayout();
		await Task.Delay(120);
		if (!(mediaArtwork.Source is BitmapImage { PixelWidth: not 0 }) || mediaArtwork.ActualWidth < 1.0)
		{
			throw new InvalidOperationException("媒体封面未显示。");
		}
		capture();
		mediaArtwork.Source = null;
		mediaScrim.Visibility = Visibility.Collapsed;
		artworkKey = "";
		mediaTitle.Text = system.MediaTitle;
		if (shown)
		{
			poll.Start();
		}
	}

	private void StateUpdated()
	{
		dispatcher.TryEnqueue(delegate
		{
			if (!disposed && shown)
			{
				ApplyCachedStates();
			}
		});
	}

	private void GestureSettingChanged()
	{
		if (dispatcher.HasThreadAccess)
		{
			SyncGlobalGestureState();
			if (gamepadToggle != null)
			{
				State(gamepadToggle, store.Value.Gamepad.Enabled);
			}
		}
		else
		{
			dispatcher.TryEnqueue(SyncGlobalGestureState);
		}
	}

	internal async Task CheckV0812Async(Action capture)
	{
		poll.Stop();
		for (int i = 0; i < 200; i++)
		{
			if (!refreshBusy)
			{
				break;
			}
			await Task.Delay(20);
		}
		Grid grid = viewport;
		ElementTheme requestedTheme = (root.RequestedTheme = ElementTheme.Dark);
		grid.RequestedTheme = requestedTheme;
		ThemeChildren(root, dark: true);
		(base.SystemBackdrop as PanelBackdrop)?.Refresh();
		await Task.Delay(300);
		capture();
	}

	private void SyncGlobalGestureState()
	{
		if (!disposed)
		{
			bool enabled = store.Value.MultiTouch.Enabled;
			State(globalGestureToggle, enabled);
			string value = Localization.T(enabled ? "全局手势 · 已开启" : "全局手势 · 已关闭");
			ToolTipService.SetToolTip(globalGestureToggle, value);
			AutomationProperties.SetName(globalGestureToggle, value);
		}
	}

	private void ApplyCachedStates()
	{
		if (disposed)
		{
			return;
		}
		try
		{
			mediaTitle.Text = system.MediaTitle;
			if (!mediaBusy && (DateTime.UtcNow - lastMediaInput).TotalSeconds > 1.0)
			{
				Ui.SetIconGlyph((FontIcon)play.Content, system.IsPlaying ? "\ue769" : "\ue768");
			}
			State(wifi, system.HasWifi && system.Wifi);
			State(bluetooth, system.HasBluetooth && system.Bluetooth);
			State(energy, system.EnergySaver);
			State(hotspot, system.Hotspot);
			State(single, ShellPreferences.SingleClick);
			(bool, bool) tuple = ShellPreferences.Rotation;
			State(rotation, tuple.Item1 && tuple.Item2);
			ToolTipService.SetToolTip(rotation, Localization.T(tuple.Item1 ? "旋转锁定 · 长按显示设置" : "旋转锁定不可用 · 点击显示设置"));
			State(flight, system.FlightMode == true);
			State(stage, store.Value.EdgeGestures[0].Kind != ActionKind.None);
			State(topEdge, store.Value.MultiTouch.EdgeEnabled && store.Value.EdgeGestures[1].Kind != ActionKind.None);
			State(bottomEdge, store.Value.MultiTouch.EdgeEnabled && store.Value.EdgeGestures[2].Kind != ActionKind.None);
			State(mute, system.Muted == true);
			SyncDialState();
			SyncGlobalGestureState();
			State(guardToggle, store.Value.CloseGuardEnabled);
			State(arkToggle, ApplicationBindings.ArkOptions(store.Value).ArkEnabled);
			State(ballToggle, store.Value.BallEnabled);
			ToolTipService.SetToolTip(flight, Localization.T(system.FlightMode.HasValue ? "飞行模式 · 长按打开设置" : "飞行模式 · 此设备可能需要系统设置"));
			syncing = true;
			if ((DateTime.UtcNow - lastVolumeInput).TotalMilliseconds > 750.0)
			{
				volume.Value = system.Volume;
			}
			brightness.IsEnabled = system.Brightness.HasValue;
			if ((DateTime.UtcNow - lastBrightnessInput).TotalMilliseconds > 1000.0 && system.Brightness.HasValue)
			{
				brightness.Value = system.Brightness.Value;
			}
			syncing = false;
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "ControlCenter.CachedState");
		}
		finally
		{
			syncing = false;
		}
	}

	private void ApplyClock()
	{
		time.Text = DateTime.Now.ToString("HH:mm");
		date.Text = DateTime.Now.ToString(Localization.Language.StartsWith("zh", StringComparison.Ordinal) ? "yyyy年 M月 d日 dddd" : "D", CultureInfo.GetCultureInfo(Localization.Language));
	}

	private async Task Refresh()
	{
		if (refreshBusy || !shown)
		{
			return;
		}
		refreshBusy = true;
		try
		{
			ApplyClock();
			ApplyCachedStates();
			await system.RefreshAsync();
			if (disposed || !shown)
			{
				return;
			}
			if (system.MediaArtwork == null)
			{
				mediaTitle.Foreground = ((root.RequestedTheme == ElementTheme.Dark) ? Ui.Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue) : Ui.Brush(30, 36, 44));
				mediaArtwork.Source = null;
				mediaScrim.Visibility = Visibility.Collapsed;
				artworkKey = "";
			}
			else if (artworkKey != system.MediaArtworkKey || mediaArtwork.Source == null)
			{
				try
				{
					await LoadArtwork(system.MediaArtwork, system.MediaArtworkKey);
				}
				catch (Exception error)
				{
					mediaArtwork.Source = null;
					mediaScrim.Visibility = Visibility.Collapsed;
					Diagnostics.Error(error, "Media.Artwork");
				}
			}
			ApplyCachedStates();
		}
		catch (Exception ex)
		{
			Diagnostics.Error(ex, "ControlCenter.Refresh");
			status.Text = Localization.T("部分设备状态暂不可用：") + ex.Message;
		}
		finally
		{
			syncing = false;
			refreshBusy = false;
		}
	}

	public void ShowPanel(Win32.POINT ball, bool interactive = false, bool fromTop = false, EdgeKind? entry = null)
	{
		if (shown && !closing)
		{
			return;
		}
		status.Text = "";
		if (store.Value.ControlCenterOpenCount < 3)
		{
			store.Value.ControlCenterOpenCount++;
			if (store.Value.ControlCenterOpenCount == 3)
			{
				store.Value.LauncherHintDismissed = true;
				editHint.Text = "";
			}
			store.Save();
		}
		slide.Stop();
		RemoveHook();
		previousForeground = Win32.GetForegroundWindow();
		ApplyWindowAlpha(0.0);
		base.AppWindow.Hide();
		interactiveOpening = interactive;
		needsPresent = true;
		bool flag;
		switch (entry)
		{
		case EdgeKind.TopRight:
		case EdgeKind.TopLeft:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		topReveal = flag || fromTop;
		Win32.MONITORINFO mONITORINFO = Win32.Monitor(ball);
		double num = (panelDpi = Win32.Scale(ball));
		PanelSide panelSide;
		switch (entry)
		{
		case EdgeKind.Left:
			panelSide = PanelSide.Left;
			break;
		case EdgeKind.Right:
			panelSide = PanelSide.Right;
			break;
		case EdgeKind.Bottom:
			panelSide = PanelSide.Bottom;
			break;
		case EdgeKind.TopRight:
		case EdgeKind.TopLeft:
			panelSide = PanelSide.Top;
			break;
		default:
			panelSide = (fromTop ? PanelSide.Top : PanelSide.Auto);
			break;
		}
		PanelSide side = panelSide;
		layout = ControlCenterGeometry.Compute(ball.X, ball.Y, mONITORINFO.rcWork.Left, mONITORINFO.rcWork.Top, mONITORINFO.rcWork.Right, mONITORINFO.rcWork.Bottom, mONITORINFO.rcMonitor.Left, mONITORINFO.rcMonitor.Top, mONITORINFO.rcMonitor.Right, mONITORINFO.rcMonitor.Bottom, num, side, store.Value.ControlCenterScale);
		width = layout.Width;
		height = layout.Height;
		base.AppWindow.MoveAndResize(new RectInt32(layout.X(1.0), layout.Y(1.0), width, height));
		ApplyLauncherSpacing();
		root.Width = 450.0;
		root.Height = 850.0;
		root.UpdateLayout();
		Diagnostics.State("ControlCenter.Layout", $"DPI={num:F2}; RequestedScale={store.Value.ControlCenterScale:F2}; Pixels={width}x{height}; Work={mONITORINFO.rcWork.Width}x{mONITORINFO.rcWork.Height}");
		ApplyTheme();
		CollectRevealItems();
		shown = true;
		closing = false;
		startProgress = (progress = 0.0);
		animationStart = DateTime.UtcNow;
		(base.SystemBackdrop as PanelBackdrop)?.PrepareForShow();
		if (shadow == null)
		{
			shadow = new PanelShadow();
		}
		shadow.SetOpacity(0.0);
		shadow.Show(layout.X(1.0), layout.Y(1.0), width, height, num);
		ApplyRevealOpacity(0.0);
		ApplyClock();
		ApplyCachedStates();
		if (gamepadToggle != null)
		{
			State(gamepadToggle, store.Value.Gamepad.Enabled);
		}
		PrepareFirstFrame();
		this.Shown?.Invoke();
		play.Focus(FocusState.Programmatic);
		WireVisibleLaunchers();
		if (!interactive)
		{
			outsideMonitor = new OutsideInteractionMonitor(Ui.Handle(this), delegate(bool focusChange)
			{
				dispatcher.TryEnqueue(delegate
				{
					if (!IgnoreExternalDismissal)
					{
						if (focusChange)
						{
							foregroundDelay.Stop();
							foregroundDelay.Start();
						}
						else
						{
							HidePanel();
						}
					}
				});
			});
		}
		if (!interactive)
		{
			slide.Start();
		}
		poll.Start();
		Refresh();
	}

	internal void HideForSwitch()
	{
		if (shown)
		{
			CancelWarmup();
			slide.Stop();
			poll.Stop();
			RemoveHook();
			shown = false;
			closing = false;
			shadow?.Hide();
			base.AppWindow.Hide();
			this.Dismissed?.Invoke();
		}
	}

	public void HidePanel()
	{
		if (shown && !closing && !modal)
		{
			closing = true;
			launcherPointer = null;
			startProgress = progress;
			SetEditMode(value: false);
			poll.Stop();
			RemoveHook();
			animationStart = DateTime.UtcNow;
			slide.Start();
		}
	}

	private void Animate(Microsoft.UI.Dispatching.DispatcherQueueTimer sender, object args)
	{
		double num = Math.Clamp((DateTime.UtcNow - animationStart).TotalMilliseconds / (double)(closing ? 280 : 360), 0.0, 1.0);
		double num2 = num * num * (3.0 - 2.0 * num);
		progress = startProgress + ((double)((!closing) ? 1 : 0) - startProgress) * num2;
		PositionPanel();
		if (num >= 1.0)
		{
			slide.Stop();
			if (closing)
			{
				CancelWarmup();
				shown = false;
				shadow?.Hide();
				base.AppWindow.Hide();
				this.Dismissed?.Invoke();
			}
			else
			{
				PositionPanel();
				RaiseTopmost();
				this.Presented?.Invoke();
				WireVisibleLaunchers();
			}
		}
	}

	[DllImport("user32")]
	private static extern bool SetLayeredWindowAttributes(nint hwnd, uint color, byte alpha, uint flags);

	private void ApplyWindowAlpha(double opacity)
	{
		nint num = Ui.Handle(this);
		long num2 = ((IntPtr)WindowLong.Get(num, -20)).ToInt64();
		bool num3 = (num2 & 0x80000) == 0;
		if (num3)
		{
			WindowLong.Set(num, -20, (nint)(num2 | 0x80000));
		}
		revealOpacity = Math.Clamp(opacity, 0.0, 1.0);
		SetLayeredWindowAttributes(num, 0u, (byte)Math.Round(revealOpacity * 255.0), 2u);
		if (num3)
		{
			Win32.SetWindowPos(num, 0, 0, 0, 0, 0, 39u);
		}
	}

	private void ApplyRevealOpacity(double amount)
	{
		double num = Math.Clamp(amount, 0.0, 1.0);
		viewport.Opacity = 1.0;
		root.Opacity = 1.0;
		ApplyWindowAlpha(num);
		shadow?.SetOpacity(num);
		AnimateRevealItems(num);
	}

	private void PositionPanel()
	{
		if (!needsPresent)
		{
			ApplyRevealOpacity(progress);
		}
	}

	private void RaiseTopmost()
	{
		Win32.SetWindowPos(Ui.Handle(this), -1, 0, 0, 0, 0, 19u);
	}

	internal void UpdateGesture(double distance)
	{
		if (shown)
		{
			slide.Stop();
			progress = (topReveal ? Math.Clamp((distance - 8.0) / 120.0, 0.0, 1.0) : GestureGeometry.Reveal(distance * panelDpi, layout.Vertical ? height : width));
			PositionPanel();
		}
	}

	internal void EndGesture(double velocity, bool cancelled = false)
	{
		if (!shown)
		{
			return;
		}
		if (cancelled || !(progress >= 0.25))
		{
			HidePanel();
			return;
		}
		closing = false;
		startProgress = progress;
		animationStart = DateTime.UtcNow;
		slide.Start();
		if (!interactiveOpening)
		{
			return;
		}
		interactiveOpening = false;
		outsideMonitor = new OutsideInteractionMonitor(Ui.Handle(this), delegate
		{
			dispatcher.TryEnqueue(delegate
			{
				if (!IgnoreExternalDismissal)
				{
					HidePanel();
				}
			});
		});
	}

	internal async Task CheckV0829VisualAsync(Action<string> capture)
	{
		EdgeKind[] array = new EdgeKind[2]
		{
			EdgeKind.Left,
			EdgeKind.Right
		};
		for (int i = 0; i < array.Length; i++)
		{
			EdgeKind edge = array[i];
			HidePanel();
			await Task.Delay(300);
			ShowPanel(new Win32.POINT(500, 400), interactive: true, fromTop: false, edge);
			Activate();
			UpdateGesture((double)width / panelDpi * 0.5);
			await Task.Delay(200);
			capture("center-half-" + edge);
			EndGesture(0.0);
			await Task.Delay(320);
		}
		bool[] array2 = new bool[2] { false, true };
		foreach (bool dark in array2)
		{
			Grid grid = viewport;
			ElementTheme requestedTheme = (root.RequestedTheme = ((!dark) ? ElementTheme.Light : ElementTheme.Dark));
			grid.RequestedTheme = requestedTheme;
			ThemeChildren(root, dark);
			await CheckArtworkAsync(delegate
			{
				if (!(mediaTitle.Foreground is SolidColorBrush { Color: { R: byte.MaxValue } }) || !(mediaScrim.Background is SolidColorBrush { Color: { R: <=20 } }))
				{
					throw new InvalidOperationException("Media contrast mismatch");
				}
				capture("media-" + (dark ? "dark" : "light"));
			});
		}
		HidePanel();
		await Task.Delay(300);
	}

	internal async Task CheckRevealAsync(Win32.POINT origin, bool horizontalOnly = false)
	{
		EdgeKind[] array = ((!horizontalOnly) ? new EdgeKind[4]
		{
			EdgeKind.TopRight,
			EdgeKind.Left,
			EdgeKind.Right,
			EdgeKind.Bottom
		} : new EdgeKind[2]
		{
			EdgeKind.Left,
			EdgeKind.Right
		});
		for (int i = 0; i < array.Length; i++)
		{
			EdgeKind edge = array[i];
			HidePanel();
			await Task.Delay(280);
			ShowPanel(origin, interactive: true, edge == EdgeKind.TopRight, edge);
			double span = (double)(layout.Vertical ? height : width) / panelDpi;
			UpdateGesture(span * 0.24);
			await Task.Delay(40);
			root.UpdateLayout();
			Windows.Foundation.Point point = root.TransformToVisual(null).TransformPoint(new Windows.Foundation.Point(0f, 0f));
			if (Math.Abs(point.X) > 1.0 || Math.Abs(point.Y) > 1.0)
			{
				throw new InvalidOperationException("部分展开时内容偏离背景：" + edge.ToString() + $" actual={point.X},{point.Y}");
			}
			if (base.AppWindow.Size.Width != width || base.AppWindow.Size.Height != height || Math.Abs(root.Width - 450.0) > 0.01 || Math.Abs(root.Height - 850.0) > 0.01)
			{
				throw new InvalidOperationException("动画过程中窗口或内容尺寸发生变化。");
			}
			EndGesture(5.0);
			await Task.Delay(300);
			if (IsOpen)
			{
				throw new InvalidOperationException("不足四分之一的快速滑动未收起：" + edge);
			}
			ShowPanel(origin, interactive: true, edge == EdgeKind.TopRight, edge);
			UpdateGesture(span * 0.3);
			UpdateGesture(span * 0.1);
			EndGesture(0.0);
			await Task.Delay(300);
			if (IsOpen)
			{
				throw new InvalidOperationException("反向拖回后没有收起：" + edge);
			}
			ShowPanel(origin, interactive: true, edge == EdgeKind.TopRight, edge);
			UpdateGesture(span * 0.26);
			EndGesture(0.0);
			await Task.Delay(320);
			if (!IsOpen || base.AppWindow.Position.X != layout.TargetX || base.AppWindow.Position.Y != layout.TargetY)
			{
				throw new InvalidOperationException("面板方向或终点错误：" + edge);
			}
			root.UpdateLayout();
			Rect rect = root.TransformToVisual(viewport).TransformBounds(new Rect(0.0, 0.0, root.ActualWidth, root.ActualHeight));
			if (Math.Abs(rect.Width * panelDpi - (double)width) > 2.0 || Math.Abs(rect.Height * panelDpi - (double)height) > 2.0)
			{
				throw new InvalidOperationException("面板内容与背景尺寸错位。");
			}
		}
		HidePanel();
		await Task.Delay(300);
	}

	private void RunShortcut(Action action)
	{
		HidePanel();
		if (previousForeground != 0)
		{
			Win32.SetForegroundWindow(previousForeground);
		}
		base.DispatcherQueue.TryEnqueue(async delegate
		{
			await Task.Delay(250);
			try
			{
				App.CurrentApp.FromControlCenter(action);
			}
			catch (Exception ex)
			{
				App.CurrentApp.Report(ex.Message);
			}
		});
	}

	private void RemoveHook()
	{
		foregroundDelay.Stop();
		outsideMonitor?.Dispose();
		outsideMonitor = null;
	}
}
