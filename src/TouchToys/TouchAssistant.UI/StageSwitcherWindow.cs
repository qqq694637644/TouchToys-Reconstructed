using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.Native;
using TouchAssistant.Services;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI.ViewManagement;

namespace TouchAssistant.UI;

internal sealed class StageSwitcherWindow : Window
{
	private readonly Grid root = new Grid
	{
		Padding = new Thickness(24.0),
		Background = Ui.Brush(0, 0, 0, 0)
	};

	private readonly Grid cards = new Grid
	{
		ColumnSpacing = 26.0,
		RowSpacing = 30.0,
		VerticalAlignment = VerticalAlignment.Center
	};

	private readonly List<Border> ordered = new List<Border>();

	private readonly HashSet<Border> previewFallbacks = new HashSet<Border>();

	private readonly Dictionary<Border, (RunningWindow Item, Image Image, StackPanel Placeholder, Border Caption)> previewTargets = new Dictionary<Border, (RunningWindow, Image, StackPanel, Border)>();

	private readonly DispatcherQueueTimer previewTimer;

	private DwmThumbnail? captureHost;

	private int previewGeneration;

	private bool captureBusy;

	private bool previewRequested;

	private readonly ScrollViewer scroll;

	private readonly TextBlock hint = Ui.Text("点击切换 · 上滑卡片关闭 · 空白处下滑收起", 13.0);

	private readonly bool overview;

	private readonly SettingsStore? store;

	private OutsideInteractionMonitor? outside;

	private CancellationTokenSource? loading;

	private readonly RevealMotion motion;

	private readonly DispatcherQueueTimer bounce;

	private readonly TranslateTransform elasticity = new TranslateTransform();

	private readonly ScrollMomentum momentum = new ScrollMomentum();

	private readonly DispatcherQueueTimer inertia;

	private bool scrollDragging;

	private bool scrollHorizontal;

	private int x;

	private int y;

	private int width;

	private int height;

	private int travel;

	private double dpi;

	private double dragStart;

	private double scrollStart;

	private int axis;

	private EdgeKind revealEdge;

	private Border? dragged;

	private long lastDrag;

	private bool open;

	private bool closed;

	private readonly UISettings themeSettings = new UISettings();

	public bool IsOpen => open;

	internal bool CanReleaseResources
	{
		get
		{
			if (!open)
			{
				return !captureBusy;
			}
			return false;
		}
	}

	internal EdgeKind EntryEdge => revealEdge;

	internal double RevealProgress => motion.Progress;

	internal double HorizontalOffset => scroll.HorizontalOffset;

	internal int LivePreviewCount => previewTargets.Values.Count<(RunningWindow, Image, StackPanel, Border)>(((RunningWindow Item, Image Image, StackPanel Placeholder, Border Caption) p) => p.Image.Source != null);

	public bool IsOverview => overview;

	internal bool IgnoreExternalDismissal { get; set; }

	public event Action? Dismissed;

	public event Action? Shown;

	private void SchedulePreviews()
	{
		if (open)
		{
			if (captureBusy)
			{
				previewRequested = true;
				return;
			}
			previewTimer.Stop();
			previewTimer.Start();
		}
	}

	internal void CheckPreviewOpacity()
	{
		if (root.Opacity != 1.0 || ordered.Exists((Border c) => c.Opacity != 1.0))
		{
			throw new InvalidOperationException("任务卡片发生透明度变化。");
		}
	}

	internal void CheckPreviewGeometry()
	{
		foreach (KeyValuePair<Border, (RunningWindow, Image, StackPanel, Border)> previewTarget in previewTargets)
		{
			DependencyObject dependencyObject = previewTarget.Value.Item2;
			while (dependencyObject != null && (object)dependencyObject != previewTarget.Key)
			{
				dependencyObject = VisualTreeHelper.GetParent(dependencyObject);
			}
			if (dependencyObject == null || ElementCompositionPreview.GetElementVisual(previewTarget.Key).Clip == null)
			{
				throw new InvalidOperationException("缩略图未包含在圆角卡片内。");
			}
		}
	}

	internal async Task CheckInertiaAsync()
	{
		EnsureScrollTestCards();
		await Task.Delay(100);
		scroll.ChangeView(0.0, 0.0, null, disableAnimation: true);
		scrollStart = 0.0;
		scrollDragging = false;
		axis = 0;
		dragged = null;
		dragStart = 1.0;
		momentum.Reset(0.0, Environment.TickCount64);
		for (int i = 1; i <= 5; i++)
		{
			await Task.Delay(20);
			Drag(overview ? (-i * 25) : 0, (!overview) ? (-i * 25) : 0);
		}
		double before = (overview ? scroll.HorizontalOffset : scroll.VerticalOffset);
		EndDrag(overview ? (-125) : 0, (!overview) ? (-125) : 0, cancelled: false);
		await Task.Delay(100);
		double num = (overview ? scroll.HorizontalOffset : scroll.VerticalOffset);
		if (num <= before + 1.0)
		{
			throw new InvalidOperationException("松手后任务列表没有惯性滑动。");
		}
		momentum.Stop();
		inertia.Stop();
		double stopped = num;
		await Task.Delay(60);
		if (Math.Abs((overview ? scroll.HorizontalOffset : scroll.VerticalOffset) - stopped) > 1.0)
		{
			throw new InvalidOperationException("中止惯性后仍在滑动。");
		}
		Hide();
		await Task.Delay(300);
		if (inertia.IsRunning)
		{
			throw new InvalidOperationException("关闭后惯性计时器仍运行。");
		}
	}

	internal async Task CheckPreviewScrollAsync()
	{
		bool flag = overview;
		if (!flag)
		{
			EdgeKind edgeKind = revealEdge;
			bool flag2 = (((uint)(edgeKind - 1) <= 1u || edgeKind == EdgeKind.TopLeft) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			scroll.ChangeView(Math.Min(220.0, scroll.ScrollableWidth), null, null, disableAnimation: true);
		}
		else
		{
			scroll.ChangeView(null, Math.Min(220.0, scroll.ScrollableHeight), null, disableAnimation: true);
		}
		await Task.Delay(50);
		CheckPreviewGeometry();
		CheckPreviewOpacity();
		if (captureHost != null)
		{
			throw new InvalidOperationException("滑动中仍保留独立预览窗口。");
		}
		scroll.ChangeView(0.0, 0.0, null, disableAnimation: true);
		await WaitForPreviews();
		CheckPreviewGeometry();
	}

	internal async Task WaitForPreviews()
	{
		for (int i = 0; i < 100; i++)
		{
			if (!captureBusy && !previewTimer.IsRunning)
			{
				break;
			}
			await Task.Delay(50);
		}
		if (captureBusy || captureHost != null)
		{
			throw new InvalidOperationException("预览缓存未在限定时间内结束。");
		}
	}

	internal void CheckNoCardInstructions()
	{
		foreach (var value in previewTargets.Values)
		{
			foreach (UIElement child in value.Placeholder.Children)
			{
				if (child is TextBlock)
				{
					throw new InvalidOperationException("卡片仍包含操作说明。");
				}
			}
		}
		DwmThumbnail dwmThumbnail = captureHost;
		if (dwmThumbnail != null && dwmThumbnail.Bounds.Left > -10000)
		{
			throw new InvalidOperationException("取图宿主进入了可见屏幕。");
		}
	}

	internal string PreviewGeometry()
	{
		return JsonSerializer.Serialize(new
		{
			Dpi = dpi,
			XamlDpi = root.XamlRoot?.RasterizationScale,
			Cached = LivePreviewCount,
			TemporaryHost = (captureHost != null)
		});
	}

	internal void EnsureScrollTestCards()
	{
		for (int i = ordered.Count; i < 24; i++)
		{
			ordered.Add(new Border
			{
				Width = 240.0,
				Height = 160.0,
				CornerRadius = new CornerRadius(14.0),
				Background = Ui.Brush(80, 87, 102),
				Child = Ui.Text("滚动验证卡片", 16.0),
				Tag = new RunningWindow(0, "滚动验证卡片", ""),
				RenderTransform = new TranslateTransform()
			});
		}
		Reflow();
		root.UpdateLayout();
	}

	internal Win32.POINT CardPoint()
	{
		Border border = ((ordered.Count > 2) ? ordered[2] : ordered[0]);
		Point point = border.TransformToVisual(root).TransformPoint(new Point(border.ActualWidth / 2.0, border.ActualHeight / 2.0));
		return new Win32.POINT(x + (int)(point.X * dpi), y + (int)(point.Y * dpi));
	}

	[DllImport("dwmapi")]
	private static extern int DwmSetWindowAttribute(nint h, uint attribute, ref uint value, uint size);

	public StageSwitcherWindow(bool overview = false, SettingsStore? settings = null)
	{
		StageSwitcherWindow stageSwitcherWindow = this;
		this.overview = overview;
		store = settings;
		base.Title = Localization.T(overview ? "TouchToys · 后台任务" : "TouchToys · 侧边任务列表");
		base.Content = new Grid
		{
			Children = { (UIElement)root }
		};
		base.SystemBackdrop = new PanelBackdrop
		{
			PreserveAcrylicOpacity = overview
		};
		Ui.ConfigureOverlay(this, systemFrame: false, !overview);
		if (overview)
		{
			uint value = 1u;
			DwmSetWindowAttribute(Ui.Handle(this), 33u, ref value, 4u);
		}
		scroll = new ScrollViewer
		{
			Content = cards,
			VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
			HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
			HorizontalScrollMode = ScrollMode.Disabled,
			VerticalScrollMode = ScrollMode.Disabled,
			ManipulationMode = ManipulationModes.None,
			RenderTransform = new TranslateTransform()
		};
		root.Children.Add(scroll);
		cards.RenderTransform = elasticity;
		scroll.HorizontalContentAlignment = HorizontalAlignment.Stretch;
		scroll.Loaded += delegate
		{
			AllowOverflow(stageSwitcherWindow.scroll);
		};
		previewTimer = base.DispatcherQueue.CreateTimer();
		previewTimer.Interval = TimeSpan.FromMilliseconds(120.0);
		previewTimer.IsRepeating = false;
		previewTimer.Tick += async delegate
		{
			await stageSwitcherWindow.UpdatePreviews();
		};
		scroll.ViewChanged += delegate
		{
			stageSwitcherWindow.SchedulePreviews();
		};
		cards.SizeChanged += delegate
		{
			stageSwitcherWindow.SchedulePreviews();
		};
		bounce = base.DispatcherQueue.CreateTimer();
		bounce.Interval = TimeSpan.FromMilliseconds(16.0);
		bounce.Tick += delegate
		{
			stageSwitcherWindow.elasticity.X = GestureGeometry.Ease(stageSwitcherWindow.elasticity.X, 0.0, 0.24, 0.15);
			stageSwitcherWindow.elasticity.Y = GestureGeometry.Ease(stageSwitcherWindow.elasticity.Y, 0.0, 0.24, 0.15);
			stageSwitcherWindow.SchedulePreviews();
			if (stageSwitcherWindow.elasticity.X == 0.0 && stageSwitcherWindow.elasticity.Y == 0.0)
			{
				stageSwitcherWindow.bounce.Stop();
			}
		};
		inertia = base.DispatcherQueue.CreateTimer();
		inertia.Interval = TimeSpan.FromMilliseconds(16.0);
		inertia.Tick += delegate
		{
			if (!stageSwitcherWindow.open || !stageSwitcherWindow.momentum.Running)
			{
				stageSwitcherWindow.inertia.Stop();
			}
			else
			{
				double num = (stageSwitcherWindow.scrollHorizontal ? stageSwitcherWindow.scroll.HorizontalOffset : stageSwitcherWindow.scroll.VerticalOffset);
				double max = (stageSwitcherWindow.scrollHorizontal ? stageSwitcherWindow.scroll.ScrollableWidth : stageSwitcherWindow.scroll.ScrollableHeight);
				double num2 = num + stageSwitcherWindow.momentum.Step(Environment.TickCount64);
				double num3 = Math.Clamp(num2, 0.0, max);
				if (stageSwitcherWindow.scrollHorizontal)
				{
					stageSwitcherWindow.scroll.ChangeView(num3, null, null, disableAnimation: true);
				}
				else
				{
					stageSwitcherWindow.scroll.ChangeView(null, num3, null, disableAnimation: true);
				}
				stageSwitcherWindow.lastDrag = Environment.TickCount64;
				if (num3 != num2)
				{
					stageSwitcherWindow.momentum.Stop();
					stageSwitcherWindow.inertia.Stop();
				}
			}
		};
		root.AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)delegate
		{
			stageSwitcherWindow.momentum.Stop();
			stageSwitcherWindow.inertia.Stop();
		}, handledEventsToo: true);
		hint.Opacity = 0.45;
		hint.HorizontalAlignment = HorizontalAlignment.Center;
		hint.VerticalAlignment = VerticalAlignment.Bottom;
		motion = new RevealMotion(base.DispatcherQueue, ApplyProgress, FinishHide);
		PanelDrag.Track(root, (DependencyObject? _) => stageSwitcherWindow.open, delegate(DependencyObject? source)
		{
			stageSwitcherWindow.momentum.Reset(0.0, Environment.TickCount64);
			stageSwitcherWindow.inertia.Stop();
			stageSwitcherWindow.scrollDragging = false;
			stageSwitcherWindow.bounce.Stop();
			stageSwitcherWindow.dragged = stageSwitcherWindow.FindCard(source);
			stageSwitcherWindow.axis = 0;
			stageSwitcherWindow.dragStart = stageSwitcherWindow.motion.Progress;
			bool flag = overview;
			if (!flag)
			{
				EdgeKind edgeKind = stageSwitcherWindow.revealEdge;
				bool flag2 = (((uint)(edgeKind - 1) <= 1u || edgeKind == EdgeKind.TopLeft) ? true : false);
				flag = flag2;
			}
			stageSwitcherWindow.scrollStart = (flag ? stageSwitcherWindow.scroll.HorizontalOffset : stageSwitcherWindow.scroll.VerticalOffset);
			stageSwitcherWindow.motion.Set(stageSwitcherWindow.dragStart);
		}, Drag, EndDrag);
		root.Tapped += delegate(object _, TappedRoutedEventArgs e)
		{
			if (Environment.TickCount64 - stageSwitcherWindow.lastDrag >= 400)
			{
				if (stageSwitcherWindow.FindCard(e.OriginalSource as DependencyObject)?.Tag is RunningWindow task)
				{
					stageSwitcherWindow.FinishHide();
					WindowCatalog.ActivateTask(task, !overview);
				}
				else
				{
					stageSwitcherWindow.Hide();
				}
				e.Handled = true;
			}
		};
		WindowPreview.Prepared += PreviewPrepared;
		themeSettings.ColorValuesChanged += ThemeChanged;
		SystemEvents.UserPreferenceChanged += PreferenceChanged;
		base.Closed += delegate
		{
			stageSwitcherWindow.closed = true;
			WindowPreview.Prepared -= stageSwitcherWindow.PreviewPrepared;
			stageSwitcherWindow.themeSettings.ColorValuesChanged -= stageSwitcherWindow.ThemeChanged;
			SystemEvents.UserPreferenceChanged -= stageSwitcherWindow.PreferenceChanged;
			stageSwitcherWindow.FinishHide();
			stageSwitcherWindow.loading?.Dispose();
			stageSwitcherWindow.SystemBackdrop = null;
			stageSwitcherWindow.Content = null;
		};
	}

	private void PreviewPrepared(nint handle)
	{
		if (!open || closed)
		{
			return;
		}
		ImageSource imageSource = WindowPreview.Cached(handle);
		if ((object)imageSource == null)
		{
			return;
		}
		foreach (var value in previewTargets.Values)
		{
			if (value.Item.Handle == handle)
			{
				value.Image.Source = imageSource;
				value.Placeholder.Visibility = Visibility.Collapsed;
			}
		}
	}

	private static void AllowOverflow(DependencyObject node)
	{
		if (node is ScrollContentPresenter scrollContentPresenter)
		{
			scrollContentPresenter.CanContentRenderOutsideBounds = true;
		}
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
		{
			AllowOverflow(VisualTreeHelper.GetChild(node, i));
		}
	}

	private void ScrollWithResistance(double requested, bool horizontal)
	{
		scrollDragging = true;
		scrollHorizontal = horizontal;
		momentum.Sample(requested - scrollStart, Environment.TickCount64);
		double max = (horizontal ? scroll.ScrollableWidth : scroll.ScrollableHeight);
		double num = Math.Clamp(requested, 0.0, max);
		double num2 = GestureGeometry.EdgeResistance(num - requested);
		if (horizontal)
		{
			elasticity.X = num2;
			scroll.ChangeView(num, null, null, disableAnimation: true);
		}
		else
		{
			elasticity.Y = num2;
			scroll.ChangeView(null, num, null, disableAnimation: true);
		}
	}

	private Border? FindCard(DependencyObject? node)
	{
		while (node != null && (object)node != root)
		{
			if (node is Border border && border.Tag is RunningWindow)
			{
				return border;
			}
			node = VisualTreeHelper.GetParent(node);
		}
		return null;
	}

	private void Drag(double dx, double dy)
	{
		lastDrag = Environment.TickCount64;
		if (axis == 0)
		{
			axis = ((Math.Abs(dx) > Math.Abs(dy)) ? 1 : 2);
		}
		if (overview)
		{
			if (axis == 1)
			{
				ScrollWithResistance(scrollStart - dx, horizontal: true);
			}
			else if (dragged != null)
			{
				((TranslateTransform)dragged.RenderTransform).Y = Math.Min(0.0, dy);
				dragged.Opacity = Math.Clamp(1.0 + dy / 240.0, 0.05, 1.0);
				SchedulePreviews();
			}
			else if (dragged == null && dy > 0.0)
			{
				motion.Set(Math.Clamp(dragStart - dy / 240.0, 0.0, 1.0));
			}
			return;
		}
		EdgeKind edgeKind = revealEdge;
		bool flag = (((uint)(edgeKind - 1) <= 1u || edgeKind == EdgeKind.TopLeft) ? true : false);
		bool flag2 = flag;
		if (axis == ((!flag2) ? 1 : 2) && Outward(dx, dy) > 0.0)
		{
			motion.Set(Math.Clamp(dragStart - Outward(dx, dy) / 200.0, 0.0, 1.0));
		}
		else if (flag2)
		{
			ScrollWithResistance(scrollStart - dx, horizontal: true);
		}
		else if (axis == 2)
		{
			ScrollWithResistance(scrollStart - dy, horizontal: false);
		}
	}

	private async void EndDrag(double dx, double dy, bool cancelled)
	{
		lastDrag = Environment.TickCount64;
		bounce.Start();
		Border card = dragged;
		dragged = null;
		if (scrollDragging && !cancelled && elasticity.X == 0.0 && elasticity.Y == 0.0)
		{
			momentum.Release(Environment.TickCount64);
			if (momentum.Running)
			{
				inertia.Start();
			}
		}
		if (cancelled)
		{
			if (card != null)
			{
				((TranslateTransform)card.RenderTransform).Y = 0.0;
				card.Opacity = 1.0;
			}
			motion.To(1.0);
			return;
		}
		if (overview && axis == 2 && card != null && dy < -72.0)
		{
			object tag = card.Tag;
			if (tag is RunningWindow item)
			{
				if (WindowCatalog.RequestClose(item.Handle))
				{
					if (store != null)
					{
						store.Value.OverviewHintDismissed = true;
						store.Save();
						hint.Visibility = Visibility.Collapsed;
					}
					CancellationToken token = loading?.Token ?? default(CancellationToken);
					double initialOpacity = card.Opacity;
					for (int frame = 1; frame <= 10; frame++)
					{
						await Task.Delay(16);
						if (closed || token.IsCancellationRequested)
						{
							return;
						}
						card.Opacity = initialOpacity * (1.0 - (double)frame / 10.0);
					}
					await Task.Delay(190);
					if (closed || token.IsCancellationRequested)
					{
						return;
					}
					if (!WindowCatalog.IsWindow(item.Handle))
					{
						ordered.Remove(card);
						Reflow();
					}
					else
					{
						FinishHide();
						WindowCatalog.Activate(item.Handle);
					}
				}
				else
				{
					App.CurrentApp.Report("无法关闭此窗口，可能需要管理员权限。");
				}
				goto IL_043b;
			}
		}
		bool flag = overview && card == null && axis == 2 && dy > 48.0;
		if (!flag)
		{
			bool flag2 = !overview;
			if (flag2)
			{
				int num = axis;
				EdgeKind edgeKind = revealEdge;
				bool vertical = (((uint)(edgeKind - 1) <= 1u || edgeKind == EdgeKind.TopLeft) ? true : false);
				flag2 = GestureGeometry.StageDismiss(num, vertical, dx, dy, Outward(dx, dy));
			}
			flag = flag2;
		}
		if (flag)
		{
			Hide();
			return;
		}
		goto IL_043b;
		IL_043b:
		if (card != null)
		{
			((TranslateTransform)card.RenderTransform).Y = 0.0;
			card.Opacity = 1.0;
		}
		if (open)
		{
			motion.To(1.0);
		}
	}

	private void Reflow()
	{
		cards.Children.Clear();
		cards.ColumnDefinitions.Clear();
		cards.RowDefinitions.Clear();
		bool flag = overview;
		if (!flag)
		{
			EdgeKind edgeKind = revealEdge;
			bool flag2 = (((uint)(edgeKind - 1) <= 1u || edgeKind == EdgeKind.TopLeft) ? true : false);
			flag = flag2;
		}
		bool flag3 = flag;
		int num = (overview ? 2 : (flag3 ? 1 : ordered.Count));
		int num2 = (overview ? ((ordered.Count + 1) / 2) : ((!flag3) ? 1 : ordered.Count));
		for (int i = 0; i < num; i++)
		{
			cards.RowDefinitions.Add(new RowDefinition
			{
				Height = GridLength.Auto
			});
		}
		for (int j = 0; j < num2; j++)
		{
			cards.ColumnDefinitions.Add(new ColumnDefinition
			{
				Width = (flag3 ? new GridLength(Math.Min(300.0, Math.Max(210.0, (double)width / dpi * 0.24))) : new GridLength(1.0, GridUnitType.Star))
			});
		}
		for (int k = 0; k < ordered.Count; k++)
		{
			Border border = ordered[k];
			Grid.SetRow(border, overview ? (k % 2) : ((!flag3) ? k : 0));
			Grid.SetColumn(border, overview ? (k / 2) : (flag3 ? k : 0));
			cards.Children.Add(border);
		}
	}

	private void ThemeChanged(UISettings sender, object args)
	{
		base.DispatcherQueue.TryEnqueue(delegate
		{
			if (!closed)
			{
				ApplyTheme();
			}
		});
	}

	private void PreferenceChanged(object sender, UserPreferenceChangedEventArgs args)
	{
		base.DispatcherQueue.TryEnqueue(delegate
		{
			if (!closed)
			{
				ApplyTheme();
			}
		});
	}

	internal void CheckDarkOverview()
	{
		ApplyTheme();
		if (!overview || root.RequestedTheme != ElementTheme.Dark || ((FrameworkElement)base.Content).RequestedTheme != ElementTheme.Dark)
		{
			throw new InvalidOperationException("后台任务背景未固定深色");
		}
	}

	private void ApplyTheme()
	{
		using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
		bool flag = overview || Convert.ToInt32(registryKey?.GetValue("SystemUsesLightTheme", 1)) == 0;
		FrameworkElement obj = (FrameworkElement)base.Content;
		ElementTheme requestedTheme = (root.RequestedTheme = ((!flag) ? ElementTheme.Light : ElementTheme.Dark));
		obj.RequestedTheme = requestedTheme;
		root.Background = ((Convert.ToInt32(registryKey?.GetValue("EnableTransparency", 1)) != 0) ? Ui.Brush(0, 0, 0, 0) : (flag ? Ui.Brush(32, 32, 32) : Ui.Brush(243, 243, 243)));
		(base.SystemBackdrop as PanelBackdrop)?.Refresh();
	}

	public void Show(Win32.POINT edge, bool interactive = false, EdgeKind? direction = null)
	{
		if (open)
		{
			if (!interactive)
			{
				Hide();
			}
			return;
		}
		WindowPreview.Prewarm(overview);
		loading?.Cancel();
		loading?.Dispose();
		loading = new CancellationTokenSource();
		ordered.Clear();
		ApplyTheme();
		Win32.MONITORINFO mONITORINFO = Win32.Monitor(edge);
		Win32.RECT rECT = (overview ? mONITORINFO.rcMonitor : mONITORINFO.rcWork);
		dpi = Win32.Scale(edge);
		width = (overview ? rECT.Width : Math.Min(rECT.Width, (int)(266.0 * dpi)));
		height = rECT.Height;
		travel = (overview ? ((int)((double)rECT.Height * 0.65)) : width);
		x = rECT.Left;
		y = rECT.Top;
		revealEdge = (EdgeKind)(((int?)direction) ?? GestureGeometry.NearestEdge(edge.X, edge.Y, rECT.Left, rECT.Top, rECT.Width, rECT.Height));
		if (!overview && revealEdge == EdgeKind.Left)
		{
			x = mONITORINFO.rcMonitor.Left;
		}
		if (!overview && revealEdge == EdgeKind.Right)
		{
			x = mONITORINFO.rcMonitor.Right - width;
		}
		bool flag = !overview;
		if (flag)
		{
			EdgeKind edgeKind = revealEdge;
			bool flag2 = (((uint)(edgeKind - 1) <= 1u || edgeKind == EdgeKind.TopLeft) ? true : false);
			flag = flag2;
		}
		if (flag)
		{
			width = rECT.Width;
			height = Math.Min(rECT.Height, (int)(320.0 * dpi));
			travel = height;
			if (revealEdge == EdgeKind.Bottom)
			{
				y = rECT.Bottom - height;
			}
		}
		TextBlock textBlock = hint;
		SettingsStore? settingsStore = store;
		textBlock.Visibility = ((settingsStore != null && settingsStore.Value.OverviewHintDismissed) ? Visibility.Collapsed : Visibility.Visible);
		base.AppWindow.MoveAndResize(new RectInt32(overview ? x : (x - travel), y, width, height));
		foreach (RunningWindow item in overview ? WindowCatalog.Read() : WindowCatalog.ReadStageTasks())
		{
			ordered.Add(MakeCard(item, loading.Token));
		}
		Reflow();
		open = true;
		motion.Set(0.0);
		if (!interactive)
		{
			Activate();
		}
		(base.SystemBackdrop as PanelBackdrop)?.PrepareForShow();
		Ui.Present(this);
		scroll.ChangeView(0.0, 0.0, null, disableAnimation: true);
		SchedulePreviews();
		this.Shown?.Invoke();
		OverlayOrder.Register(Ui.Handle(this));
		if (!interactive)
		{
			InstallOutside();
			motion.To(1.0);
		}
	}

	private Border MakeCard(RunningWindow item, CancellationToken token)
	{
		double maxWidth = (overview ? Math.Min(300.0, Math.Max(210.0, (double)width / dpi * 0.24)) : 218.0);
		double maxHeight = (overview ? Math.Min(200.0, Math.Max(100.0, ((double)height / dpi - 180.0) / 2.0)) : 150.0);
		(double Width, double Height) tuple = GestureGeometry.CardSize(WindowCatalog.Aspect(item.Handle), maxWidth, maxHeight);
		double item2 = tuple.Width;
		double item3 = tuple.Height;
		Grid grid = new Grid();
		List<WindowTile> tiles = item.Tiles;
		if (tiles != null && tiles.Count > 1)
		{
			for (int num = Math.Min(3, item.Tiles.Count) - 1; num > 0; num--)
			{
				RunningWindow window = item.Tiles[num].Window;
				Image image = new Image
				{
					Stretch = Stretch.UniformToFill
				};
				Border item4 = new Border
				{
					Child = image,
					Background = Ui.Brush(75, 78, 85),
					CornerRadius = new CornerRadius(12.0),
					Margin = new Thickness(0.0, 0.0, 6 * (3 - num), 6 * (3 - num)),
					RenderTransform = new TranslateTransform
					{
						X = num * 5,
						Y = num * 5
					}
				};
				grid.Children.Add(item4);
				LoadStack(window, image, token);
			}
		}
		Image image2 = new Image
		{
			Stretch = Stretch.UniformToFill,
			Margin = new Thickness(0.0, 0.0, 0.0, 34.0)
		};
		Image image3 = new Image
		{
			MaxWidth = 48.0,
			MaxHeight = 48.0,
			Stretch = Stretch.None
		};
		StackPanel stackPanel = new StackPanel
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 8.0
		};
		stackPanel.Children.Add(image3);
		grid.Children.Add(stackPanel);
		grid.Children.Add(image2);
		image2.Source = WindowPreview.Cached(item.Handle);
		if (image2.Source != null)
		{
			stackPanel.Visibility = Visibility.Collapsed;
		}
		Border border = new Border
		{
			VerticalAlignment = VerticalAlignment.Bottom,
			Background = Ui.Brush(20, 20, 20, 160),
			Padding = new Thickness(8.0, 5.0, 8.0, 5.0)
		};
		Grid grid2 = new Grid
		{
			ColumnSpacing = 8.0
		};
		grid2.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(26.0)
		});
		grid2.ColumnDefinitions.Add(new ColumnDefinition());
		Image image4 = new Image
		{
			Width = 24.0,
			Height = 24.0
		};
		grid2.Children.Add(image4);
		tiles = item.Tiles;
		if (tiles != null && tiles.Count > 1)
		{
			grid2.ColumnDefinitions[0].Width = new GridLength(44.0);
			for (int i = 1; i < Math.Min(3, item.Tiles.Count); i++)
			{
				Image image5 = new Image
				{
					Width = 20.0,
					Height = 20.0,
					HorizontalAlignment = HorizontalAlignment.Left,
					Margin = new Thickness(i * 9, 5.0, 0.0, 0.0)
				};
				grid2.Children.Add(image5);
				LoadStackIcon(item.Tiles[i].Window, image5, token);
			}
		}
		TextBlock textBlock = Ui.Text(item.Title, 11.0);
		textBlock.Foreground = Ui.Brush(byte.MaxValue, byte.MaxValue, byte.MaxValue);
		textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
		textBlock.TextWrapping = TextWrapping.NoWrap;
		textBlock.VerticalAlignment = VerticalAlignment.Center;
		Grid.SetColumn(textBlock, 1);
		grid2.Children.Add(textBlock);
		border.Child = grid2;
		grid.Children.Add(border);
		Border card = new Border
		{
			Width = item2,
			Height = item3,
			Child = grid,
			CornerRadius = new CornerRadius(14.0),
			Background = Ui.Brush(80, 87, 102, 210),
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Tag = item,
			RenderTransform = new TranslateTransform(),
			ManipulationMode = ManipulationModes.None
		};
		card.SizeChanged += delegate
		{
			Visual elementVisual = ElementCompositionPreview.GetElementVisual(card);
			CompositionRoundedRectangleGeometry compositionRoundedRectangleGeometry = elementVisual.Compositor.CreateRoundedRectangleGeometry();
			compositionRoundedRectangleGeometry.Size = new Vector2((float)card.ActualWidth, (float)card.ActualHeight);
			compositionRoundedRectangleGeometry.CornerRadius = new Vector2(14f);
			elementVisual.Clip = elementVisual.Compositor.CreateGeometricClip(compositionRoundedRectangleGeometry);
		};
		previewTargets[card] = (item, image2, stackPanel, border);
		AutomationProperties.SetName(card, item.Title);
		Load(card, item, image2, image4, image3, stackPanel, token);
		return card;
	}

	private static async Task LoadStack(RunningWindow item, Image image, CancellationToken token)
	{
		ImageSource source = await WindowPreview.LoadAsync(item.Handle, token);
		if (!token.IsCancellationRequested)
		{
			image.Source = source;
		}
	}

	private static async Task LoadStackIcon(RunningWindow item, Image image, CancellationToken token)
	{
		try
		{
			ImageSource source = await LauncherIcons.LoadWindowAsync(item);
			if (!token.IsCancellationRequested)
			{
				image.Source = source;
			}
		}
		catch
		{
		}
	}

	private async Task Load(Border card, RunningWindow item, Image preview, Image icon, Image fallback, StackPanel placeholder, CancellationToken token)
	{
		_ = 1;
		try
		{
			Task<ImageSource?> thumbnail = WindowPreview.CachedAsync(item.Handle);
			ImageSource imageSource = await LauncherIcons.LoadWindowAsync(item);
			if (!token.IsCancellationRequested && imageSource != null)
			{
				ImageSource source = (fallback.Source = imageSource);
				icon.Source = source;
				if (imageSource is BitmapSource bitmapSource)
				{
					double num = fallback.XamlRoot?.RasterizationScale ?? 1.0;
					fallback.Width = Math.Min(48.0, (double)bitmapSource.PixelWidth / num);
					fallback.Height = Math.Min(48.0, (double)bitmapSource.PixelHeight / num);
					fallback.Stretch = Stretch.Uniform;
				}
			}
			ImageSource imageSource3 = await thumbnail;
			if (!token.IsCancellationRequested && imageSource3 != null)
			{
				if (preview.Source == null)
				{
					preview.Source = imageSource3;
				}
				placeholder.Visibility = Visibility.Collapsed;
			}
		}
		catch
		{
		}
	}

	private async Task UpdatePreviews()
	{
		if (captureBusy || !open || !(base.Content is FrameworkElement { IsLoaded: not false }))
		{
			return;
		}
		captureBusy = true;
		int generation = previewGeneration;
		CancellationToken token = loading?.Token ?? default(CancellationToken);
		try
		{
			await WindowPreview.PendingWarm;
			Border[] array = ordered.ToArray();
			foreach (Border card in array)
			{
				if (token.IsCancellationRequested || generation != previewGeneration)
				{
					break;
				}
				if (previewFallbacks.Contains(card) || !previewTargets.TryGetValue(card, out var target) || card.ActualWidth <= 0.0)
				{
					continue;
				}
				Point point = card.TransformToVisual((FrameworkElement)base.Content).TransformPoint(default(Point));
				int num = base.AppWindow.Position.X + (int)Math.Round(point.X * dpi);
				int num2 = base.AppWindow.Position.Y + (int)Math.Round(point.Y * dpi);
				int num3 = (int)Math.Round(card.ActualWidth * dpi);
				int num4 = (int)Math.Round(card.ActualHeight * dpi);
				Math.Ceiling(target.Caption.ActualHeight * dpi);
				Win32.RECT rECT = new Win32.RECT
				{
					Left = base.AppWindow.Position.X,
					Top = base.AppWindow.Position.Y,
					Right = base.AppWindow.Position.X + width,
					Bottom = base.AppWindow.Position.Y + height
				};
				if (num + num3 <= rECT.Left || num2 + num4 <= rECT.Top || num >= rECT.Right || num2 >= rECT.Bottom)
				{
					continue;
				}
				if (WindowPreview.IsFresh(target.Item.Handle))
				{
					ImageSource imageSource = WindowPreview.Cached(target.Item.Handle);
					if ((object)imageSource != null)
					{
						target.Image.Source = imageSource;
						target.Placeholder.Visibility = Visibility.Collapsed;
						previewFallbacks.Add(card);
						continue;
					}
				}
				if (motion.Progress < 0.999)
				{
					continue;
				}
				using (DwmThumbnail preview = DwmThumbnail.Create(target.Item.Handle))
				{
					captureHost = preview;
					if (preview == null)
					{
						await LoadFallback(target, token);
						goto IL_06fc;
					}
					double val = WindowCatalog.Aspect(target.Item.Handle);
					preview.PrepareOffscreen(Math.Max(1, (int)(480.0 * Math.Min(1.0, val))), Math.Max(1, (int)(480.0 / Math.Max(1.0, val))));
					if (generation != previewGeneration || token.IsCancellationRequested)
					{
						break;
					}
					byte[] array2;
					try
					{
						array2 = await WindowSnapshot.Capture(preview.Handle, token);
					}
					catch (OperationCanceledException)
					{
						throw;
					}
					catch (Exception error)
					{
						Diagnostics.Error(error, "WindowPreview.Capture");
						array2 = null;
					}
					if (array2 == null)
					{
						await LoadFallback(target, token);
						previewFallbacks.Add(card);
						continue;
					}
					if (generation == previewGeneration && !token.IsCancellationRequested)
					{
						WindowPreview.Store(target.Item.Handle, array2);
						ImageSource source = await WindowPreview.CachedAsync(target.Item.Handle);
						if (generation == previewGeneration && !token.IsCancellationRequested)
						{
							target.Image.Margin = new Thickness(0.0, 0.0, 0.0, target.Caption.ActualHeight);
							target.Image.Stretch = Stretch.UniformToFill;
							target.Image.Source = source;
							target.Placeholder.Visibility = Visibility.Collapsed;
							goto IL_06fc;
						}
					}
					goto end_IL_03ac;
					IL_06fc:
					previewFallbacks.Add(card);
					captureHost = null;
					goto IL_072f;
					end_IL_03ac:;
				}
				break;
				IL_072f:
				target = default((RunningWindow, Image, StackPanel, Border));
			}
		}
		catch (OperationCanceledException)
		{
		}
		catch (Exception error2)
		{
			Diagnostics.Error(error2, "WindowPreview.Snapshot");
		}
		finally
		{
			captureHost?.Dispose();
			captureHost = null;
			captureBusy = false;
			if (open && (generation != previewGeneration || previewRequested))
			{
				previewRequested = false;
				previewTimer.Start();
			}
		}
	}

	private static async Task LoadFallback((RunningWindow Item, Image Image, StackPanel Placeholder, Border Caption) target, CancellationToken token)
	{
		ImageSource imageSource = await WindowPreview.LoadAsync(target.Item.Handle, token);
		if (!token.IsCancellationRequested && imageSource != null)
		{
			target.Image.Source = imageSource;
			target.Placeholder.Visibility = Visibility.Collapsed;
		}
	}

	private double Outward(double dx, double dy)
	{
		switch (revealEdge)
		{
		case EdgeKind.Right:
			return dx;
		case EdgeKind.TopRight:
		case EdgeKind.TopLeft:
			return 0.0 - dy;
		case EdgeKind.Bottom:
			return dy;
		default:
			return 0.0 - dx;
		}
	}

	private void ApplyProgress(double progress)
	{
		int num = 0;
		int num2 = 0;
		if (!overview)
		{
			int num3 = (int)((double)travel * (1.0 - progress));
			switch (revealEdge)
			{
			case EdgeKind.Right:
				num = num3;
				break;
			case EdgeKind.TopRight:
			case EdgeKind.TopLeft:
				num2 = -num3;
				break;
			case EdgeKind.Bottom:
				num2 = num3;
				break;
			default:
				num = -num3;
				break;
			}
		}
		base.AppWindow.Move(new PointInt32(x + num, y + num2));
		if (overview)
		{
			(base.SystemBackdrop as PanelBackdrop)?.SetOpacity(progress);
			for (int i = 0; i < ordered.Count; i++)
			{
				double num4 = Math.Min(0.24, (double)i * 0.025);
				double num5 = Math.Clamp((progress - num4) / (1.0 - num4), 0.0, 1.0);
				((TranslateTransform)ordered[i].RenderTransform).X = (double)(-width) / dpi * (1.0 - num5);
			}
		}
		if (progress >= 0.999)
		{
			SchedulePreviews();
		}
		outside?.RefreshBounds();
	}

	private void InstallOutside()
	{
		if (!IgnoreExternalDismissal && outside == null)
		{
			outside = new OutsideInteractionMonitor(Ui.Handle(this), delegate
			{
				base.DispatcherQueue.TryEnqueue(Hide);
			});
		}
	}

	public void UpdateBottomGesture(double blurProgress)
	{
		if (open)
		{
			motion.Set(blurProgress);
		}
	}

	public void CompleteBottomGesture(bool keep)
	{
		if (open)
		{
			if (keep)
			{
				InstallOutside();
			}
			motion.To(keep ? 1 : 0);
		}
	}

	public void UpdateGesture(double distance)
	{
		if (open)
		{
			motion.Set(GestureGeometry.Reveal(distance * dpi, travel));
		}
	}

	public void EndGesture(double velocity, bool cancelled = false)
	{
		if (open)
		{
			bool flag = !cancelled && (overview ? (motion.Progress >= 0.25) : GestureGeometry.CommitReveal(motion.Progress, velocity));
			if (flag)
			{
				InstallOutside();
			}
			motion.To(flag ? 1 : 0);
		}
	}

	public void Hide()
	{
		momentum.Stop();
		inertia.Stop();
		if (open)
		{
			outside?.Dispose();
			outside = null;
			motion.To(0.0);
		}
	}

	private void FinishHide()
	{
		momentum.Stop();
		inertia.Stop();
		WindowPreview.TrimWhenIdle();
		previewTimer.Stop();
		previewGeneration++;
		previewRequested = false;
		captureHost?.Dispose();
		captureHost = null;
		bounce.Stop();
		TranslateTransform translateTransform = elasticity;
		double num = (elasticity.Y = 0.0);
		translateTransform.X = num;
		bool num3 = open;
		motion.Stop();
		open = false;
		outside?.Dispose();
		outside = null;
		loading?.Cancel();
		previewTargets.Clear();
		previewFallbacks.Clear();
		cards.Children.Clear();
		ordered.Clear();
		OverlayOrder.Remove(Ui.Handle(this));
		if (!closed)
		{
			base.AppWindow.Hide();
		}
		if (num3)
		{
			this.Dismissed?.Invoke();
		}
	}
}
