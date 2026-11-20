using System;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TouchAssistant.Models;
using TouchAssistant.Native;
using TouchAssistant.Services;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace TouchAssistant.UI;

internal sealed class RectangleHintWindow : Window, IDisposable
{
	private bool disposed;

	private readonly Grid root = new Grid
	{
		HorizontalAlignment = HorizontalAlignment.Center,
		VerticalAlignment = VerticalAlignment.Center
	};

	private StackPanel current = new StackPanel();

	private StackPanel previous = new StackPanel();

	private readonly DispatcherQueueTimer animation;

	private readonly DispatcherQueueTimer expiry;

	private long started;

	private bool shown;

	private double widthFrom;

	private double widthTo;

	private double hostWidth;

	private double dpi = 1.0;

	private int anchorX;

	private int anchorY;

	private GestureAction? queuedAction;

	private Win32.POINT queuedOrigin;

	private double queuedSeconds;

	internal bool IsShown => shown;

	internal (double PreviousY, double CurrentY, double PreviousOpacity, double CurrentOpacity) AnimationState => (PreviousY: ((TranslateTransform)previous.RenderTransform).Y, CurrentY: ((TranslateTransform)current.RenderTransform).Y, PreviousOpacity: previous.Opacity, CurrentOpacity: current.Opacity);

	internal event Action? Hidden;

	public RectangleHintWindow()
	{
		root.Children.Add(previous);
		root.Children.Add(current);
		root.ActualThemeChanged += delegate
		{
			UpdateInk();
		};
		base.Content = root;
		HintBackdrop hintBackdrop = new HintBackdrop();
		hintBackdrop.SetOpacity(0.74);
		base.SystemBackdrop = hintBackdrop;
		Ui.ConfigureOverlay(this, systemFrame: false, clipFrame: false);
		uint value = 1u;
		DwmSetWindowAttribute(Ui.Handle(this), 2u, ref value, 4u);
		DwmSetWindowAttribute(Ui.Handle(this), 33u, ref value, 4u);
		DwmSetWindowAttribute(Ui.Handle(this), 20u, ref value, 4u);
		Passive(Ui.Handle(this));
		OverlayOrder.Register(Ui.Handle(this));
		animation = base.DispatcherQueue.CreateTimer();
		animation.Interval = TimeSpan.FromMilliseconds(16.0);
		animation.Tick += delegate
		{
			Tick();
		};
		expiry = base.DispatcherQueue.CreateTimer();
		expiry.IsRepeating = false;
		expiry.Tick += delegate
		{
			Hide();
		};
		DesktopVisibility.Changed += DesktopChanged;
	}

	[DllImport("dwmapi")]
	private static extern int DwmSetWindowAttribute(nint h, uint attribute, ref uint value, uint size);

	private static nint GetStyle(nint h, int index)
	{
		return WindowLong.Get(h, index);
	}

	private static nint SetStyle(nint h, int index, nint style)
	{
		return WindowLong.Set(h, index, style);
	}

	[DllImport("user32")]
	private static extern int SetWindowRgn(nint h, nint region, bool redraw);

	private void SetWidth(double width)
	{
		root.Width = width;
		root.Height = 44.0;
		root.Clip = new RectangleGeometry
		{
			Rect = new Rect(0.0, 0.0, width, 44.0)
		};
		int left = (int)Math.Round((hostWidth - width) * dpi / 2.0);
		int top = (int)Math.Round(2.0 * dpi);
		int right = (int)Math.Round((hostWidth + width) * dpi / 2.0);
		int bottom = (int)Math.Round(46.0 * dpi);
		nint num = CreateRoundRectRgn(left, top, right, bottom, (int)(20.0 * dpi), (int)(20.0 * dpi));
		if (SetWindowRgn(Ui.Handle(this), num, redraw: true) == 0)
		{
			Win32.DeleteObject(num);
		}
	}

	[DllImport("gdi32")]
	private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int width, int height);

	private static void Passive(nint h)
	{
		SetStyle(h, -20, (nint)(((IntPtr)GetStyle(h, -20)).ToInt64() | 0x8000020));
	}

	public void Show(GestureAction action, Win32.POINT origin, double seconds)
	{
		if (disposed)
		{
			return;
		}
		if (!DesktopVisibility.CanShow(Ui.Handle(this)))
		{
			Hide();
			return;
		}
		string value = ((action.Label.Length > 0) ? action.Label : ActionNames.Describe(action));
		expiry.Stop();
		if (seconds > 0.0)
		{
			expiry.Interval = TimeSpan.FromSeconds(seconds);
			expiry.Start();
		}
		if (shown && animation.IsRunning)
		{
			queuedAction = action;
			queuedOrigin = origin;
			queuedSeconds = seconds;
			return;
		}
		root.Children.Clear();
		previous = (shown ? current : new StackPanel());
		current = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			RenderTransform = new TranslateTransform()
		};
		Microsoft.UI.Xaml.Media.Brush foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
		if (action.IconPath.Length > 0)
		{
			try
			{
				current.Children.Add(new Microsoft.UI.Xaml.Controls.Image
				{
					Source = new BitmapImage(new Uri(action.IconPath)),
					Width = 20.0,
					Height = 20.0
				});
			}
			catch
			{
			}
		}
		else
		{
			string icon = action.Icon;
			if (icon != null && icon.Length > 0)
			{
				current.Children.Add(new FontIcon
				{
					Glyph = icon,
					FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(action.IconFont),
					FontSize = 18.0,
					Foreground = foreground
				});
			}
		}
		TextBlock textBlock = Ui.Text(value, 13.0);
		textBlock.Foreground = foreground;
		textBlock.TextWrapping = TextWrapping.NoWrap;
		textBlock.TextTrimming = TextTrimming.CharacterEllipsis;
		textBlock.VerticalAlignment = VerticalAlignment.Center;
		current.Children.Add(textBlock);
		previous.RenderTransform = new TranslateTransform();
		previous.Opacity = (shown ? 1 : 0);
		root.Children.Add(previous);
		root.Children.Add(current);
		textBlock.Measure(new Windows.Foundation.Size(double.PositiveInfinity, 44.0));
		Win32.RECT rcMonitor = Win32.Monitor(origin).rcMonitor;
		dpi = Win32.Scale(origin);
		double num = 32 + ((current.Children.Count > 1) ? 28 : 0);
		double num2 = Math.Min(Math.Max(64.0, textBlock.DesiredSize.Width + num), Math.Min(680.0, (double)(rcMonitor.Width - 64) / dpi));
		textBlock.MaxWidth = Math.Max(20.0, num2 - num);
		widthFrom = (shown ? root.Width : num2);
		widthTo = num2;
		hostWidth = Math.Min(720.0, (double)(rcMonitor.Width - 32) / dpi);
		anchorX = rcMonitor.Left + rcMonitor.Width / 2;
		anchorY = rcMonitor.Top + rcMonitor.Height / 3 - (int)(24.0 * dpi);
		if (!shown || base.AppWindow.Size.Width != (int)Math.Ceiling(hostWidth * dpi) || base.AppWindow.Position.X != anchorX - (int)(hostWidth * dpi / 2.0) || base.AppWindow.Position.Y != anchorY)
		{
			base.AppWindow.MoveAndResize(new RectInt32(anchorX - (int)(hostWidth * dpi / 2.0), anchorY, (int)Math.Ceiling(hostWidth * dpi), (int)Math.Ceiling(48.0 * dpi)));
		}
		SetWidth(widthFrom);
		UpdateInk();
		started = Environment.TickCount64;
		bool num3 = !shown;
		shown = true;
		Tick();
		if (num3)
		{
			(base.SystemBackdrop as HintBackdrop)?.PrepareForShow();
			Ui.Present(this);
			OverlayOrder.Raise(Ui.Handle(this));
		}
		animation.Start();
	}

	private void UpdateInk()
	{
		SolidColorBrush foreground = new SolidColorBrush((root.ActualTheme == ElementTheme.Dark) ? Colors.White : Colors.Black);
		StackPanel[] array = new StackPanel[2] { current, previous };
		for (int i = 0; i < array.Length; i++)
		{
			foreach (UIElement child in array[i].Children)
			{
				if (child is TextBlock textBlock)
				{
					textBlock.Foreground = foreground;
				}
				else if (child is FontIcon fontIcon)
				{
					fontIcon.Foreground = foreground;
				}
			}
		}
	}

	private void Tick()
	{
		if (disposed || !shown)
		{
			animation.Stop();
			return;
		}
		double num = Math.Clamp((double)(Environment.TickCount64 - started) / 280.0, 0.0, 1.0);
		double num2 = num * num * (3.0 - 2.0 * num);
		SetWidth(widthFrom + (widthTo - widthFrom) * num2);
		previous.Opacity = 1.0 - num2;
		current.Opacity = num2;
		((TranslateTransform)previous.RenderTransform).Y = -30.0 * num2;
		((TranslateTransform)current.RenderTransform).Y = 30.0 * (1.0 - num2);
		if (num >= 1.0)
		{
			animation.Stop();
			previous.Children.Clear();
			GestureAction gestureAction = queuedAction;
			if (gestureAction != null)
			{
				queuedAction = null;
				Show(gestureAction, queuedOrigin, queuedSeconds);
			}
		}
	}

	internal async Task CheckThemeAsync()
	{
		ElementTheme[] array = new ElementTheme[2]
		{
			ElementTheme.Light,
			ElementTheme.Dark
		};
		foreach (ElementTheme theme in array)
		{
			root.RequestedTheme = theme;
			await Task.Delay(100);
			UpdateInk();
			Windows.UI.Color color = ((theme == ElementTheme.Dark) ? Colors.White : Colors.Black);
			foreach (UIElement child in current.Children)
			{
				if (child is TextBlock textBlock && ((SolidColorBrush)textBlock.Foreground).Color != color)
				{
					throw new InvalidOperationException("提示文字没有随主题变化。");
				}
			}
		}
		root.RequestedTheme = ElementTheme.Default;
	}

	internal async Task CheckWidthAnimation()
	{
		Show(new GestureAction
		{
			Label = "短"
		}, new Win32.POINT(400, 400), 0.0);
		await Task.Delay(320);
		double before = root.Width;
		int nativeWidth = base.AppWindow.Size.Width;
		Show(new GestureAction
		{
			Label = "这是一个较长的菜单功能提示"
		}, new Win32.POINT(400, 400), 0.0);
		if (Math.Abs(root.Width - before) > 3.0)
		{
			throw new InvalidOperationException("宽度直接跳变");
		}
		await Task.Delay(110);
		if (root.Width <= before || root.Width >= widthTo)
		{
			throw new InvalidOperationException("缺少宽度缓动");
		}
		await Task.Delay(250);
		if (Math.Abs(root.Width - widthTo) > 1.0 || base.AppWindow.Size.Width != nativeWidth)
		{
			throw new InvalidOperationException("提示窗口和可见背景尺寸不符");
		}
	}

	internal async Task CheckV0822()
	{
		Window fixture = new Window
		{
			Content = new Grid
			{
				Background = new SolidColorBrush(Windows.UI.Color.FromArgb(byte.MaxValue, 32, 110, 170))
			}
		};
		Ui.ConfigureOverlay(fixture);
		Win32.RECT rcMonitor = Win32.Monitor(new Win32.POINT(400, 400)).rcMonitor;
		fixture.AppWindow.MoveAndResize(new RectInt32(rcMonitor.Left + rcMonitor.Width / 2 - 600, rcMonitor.Top + rcMonitor.Height / 3 - 100, 1200, 220));
		Ui.Present(fixture);
		await Task.Delay(150);
		try
		{
			await CheckWidthAnimation();
			await CheckTransitionPixels();
			Hide();
			Show(GestureAction.BuiltIn("AppSettings"), new Win32.POINT(400, 400), 0.0);
			await Task.Delay(320);
			if (current.Children.OfType<FontIcon>().Any())
			{
				throw new InvalidOperationException("非控制中心提示自动加了图标");
			}
			Show(new GestureAction
			{
				Label = "应用设置",
				Icon = "\ue713"
			}, new Win32.POINT(400, 400), 0.0);
			await Task.Delay(320);
			if (!current.Children.OfType<FontIcon>().Any())
			{
				throw new InvalidOperationException("显式图标丢失");
			}
			if (!Win32.GetWindowRect(Ui.Handle(this), out var r))
			{
				return;
			}
			using Bitmap bitmap = new Bitmap(r.Width, r.Height);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(r.Width, r.Height));
			}
			bitmap.Save(Path.Combine(AppContext.BaseDirectory, "hint-v0822.png"));
		}
		finally
		{
			fixture.Close();
		}
	}

	private async Task CheckTransitionPixels()
	{
		string[] array = new string[3] { "短", "这是用于检查提示宽度变化的长文字", "短" };
		foreach (string text in array)
		{
			Show(new GestureAction
			{
				Label = text
			}, new Win32.POINT(400, 400), 0.0);
			for (int j = 0; j < 8; j++)
			{
				await Task.Delay(40);
				if (!Win32.GetWindowRect(Ui.Handle(this), out var r))
				{
					throw new InvalidOperationException("提示窗口不可用");
				}
				using Bitmap bitmap = new Bitmap(r.Width, r.Height);
				using (Graphics graphics = Graphics.FromImage(bitmap))
				{
					graphics.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(r.Width, r.Height));
				}
				System.Drawing.Color pixel = bitmap.GetPixel(8, r.Height / 2);
				if (pixel.R > 60 || pixel.B < 120)
				{
					throw new InvalidOperationException("提示外部残留不透明背景");
				}
				if (j == 3)
				{
					bitmap.Save(Path.Combine(AppContext.BaseDirectory, $"hint-transition-{text.Length}.png"));
				}
			}
		}
	}

	internal void CheckAdaptiveWidth()
	{
		double width = root.Width;
		if (!current.Children.OfType<FontIcon>().Any())
		{
			throw new InvalidOperationException("缺少默认功能图标");
		}
		Hide();
		Show(new GestureAction
		{
			Label = new string('长', 45)
		}, default(Win32.POINT), 0.0);
		if (root.Width <= width || root.Width <= 320.0)
		{
			throw new InvalidOperationException("提示未自适应宽度");
		}
	}

	private void DesktopChanged()
	{
		if (DesktopVisibility.Locked)
		{
			Hide();
		}
	}

	public void Hide()
	{
		animation.Stop();
		expiry.Stop();
		bool num = shown;
		shown = false;
		queuedAction = null;
		nint h = Ui.Handle(this);
		DesktopVisibility.ForgetWindow(h);
		base.AppWindow.Hide();
		Win32.ShowWindow(h, 0);
		root.Children.Clear();
		current = new StackPanel();
		previous = new StackPanel();
		if (num)
		{
			this.Hidden?.Invoke();
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			DesktopVisibility.Changed -= DesktopChanged;
			Hide();
			OverlayOrder.Remove(Ui.Handle(this));
			Ui.ReleaseTitleBar(this);
			base.SystemBackdrop = null;
			base.Content = null;
			Close();
		}
	}
}
