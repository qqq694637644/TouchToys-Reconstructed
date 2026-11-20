using System;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TouchAssistant.Native;
using Windows.Foundation;

namespace TouchAssistant.UI;

internal static class PanelDrag
{
	public static Point ScreenPoint(FrameworkElement surface, PointerRoutedEventArgs e)
	{
		Point position = e.GetCurrentPoint(null).Position;
		double num = surface.XamlRoot?.RasterizationScale ?? 1.0;
		UIElement uIElement = surface.XamlRoot?.Content;
		if ((object)uIElement != null && Ui.OverlayHandles.TryGetValue(uIElement, out var value) && Win32.GetWindowRect(value, out var r))
		{
			return new Point(position.X * num + (double)r.Left, position.Y * num + (double)r.Top);
		}
		return new Point(position.X * num, position.Y * num);
	}

	public static void Track(FrameworkElement surface, Func<DependencyObject?, bool> enabled, Action<DependencyObject?> begin, Action<double, double> update, Action<double, double, bool> finish, Action? tap = null, Func<DependencyObject?, double, double, bool>? recognize = null)
	{
		bool tracking = false;
		bool dragging = false;
		bool acquiring = false;
		uint pointer = 0u;
		Point origin = default(Point);
		double scale = 1.0;
		double dx = 0.0;
		double dy = 0.0;
		DependencyObject target = null;
		surface.ManipulationMode = ManipulationModes.None;
		surface.AddHandler(UIElement.PointerPressedEvent, (PointerEventHandler)delegate(object _, PointerRoutedEventArgs e)
		{
			if (!tracking && enabled(e.OriginalSource as DependencyObject) && (e.Pointer.PointerDeviceType != PointerDeviceType.Mouse || e.GetCurrentPoint(surface).Properties.IsLeftButtonPressed))
			{
				tracking = true;
				pointer = e.Pointer.PointerId;
				origin = ScreenPoint(surface, e);
				scale = surface.XamlRoot?.RasterizationScale ?? 1.0;
				dx = (dy = 0.0);
				target = e.OriginalSource as DependencyObject;
			}
		}, handledEventsToo: true);
		surface.AddHandler(UIElement.PointerMovedEvent, (PointerEventHandler)delegate(object _, PointerRoutedEventArgs e)
		{
			if (tracking && pointer == e.Pointer.PointerId)
			{
				Point point = ScreenPoint(surface, e);
				dx = (point.X - origin.X) / scale;
				dy = (point.Y - origin.Y) / scale;
				if (!dragging)
				{
					if (!enabled(target))
					{
						tracking = false;
						return;
					}
					if (Math.Max(Math.Abs(dx), Math.Abs(dy)) < 8.0 || (recognize != null && !recognize(target, dx, dy)))
					{
						return;
					}
					acquiring = true;
					bool flag;
					try
					{
						flag = surface.CapturePointer(e.Pointer);
					}
					finally
					{
						acquiring = false;
					}
					if (!flag)
					{
						tracking = false;
						return;
					}
					dragging = true;
					begin(target);
				}
				update(dx, dy);
				e.Handled = true;
			}
		}, handledEventsToo: true);
		surface.AddHandler(UIElement.PointerReleasedEvent, (PointerEventHandler)delegate(object _, PointerRoutedEventArgs e)
		{
			if (tracking && pointer == e.Pointer.PointerId)
			{
				tracking = false;
				bool num = dragging;
				dragging = false;
				if (num)
				{
					finish(dx, dy, arg3: false);
					surface.ReleasePointerCapture(e.Pointer);
					e.Handled = true;
				}
				else
				{
					tap?.Invoke();
				}
			}
		}, handledEventsToo: true);
		surface.PointerCanceled += Cancel;
		surface.PointerCaptureLost += Cancel;
		void Cancel(object sender, PointerRoutedEventArgs e)
		{
			if (!acquiring && tracking && pointer == e.Pointer.PointerId)
			{
				tracking = false;
				bool num = dragging;
				dragging = false;
				if (num)
				{
					finish(dx, dy, arg3: true);
				}
			}
		}
	}

	private static bool Interactive(DependencyObject? node, FrameworkElement root, bool allowLauncher = false)
	{
		while (node != null && (object)node != root)
		{
			bool flag = ((node is Slider || node is ScrollBar || node is TextBox) ? true : false);
			if (flag || (!allowLauncher && node is GridViewItem))
			{
				return true;
			}
			node = VisualTreeHelper.GetParent(node);
		}
		return false;
	}

	public static void AttachAny(FrameworkElement surface, Func<bool> enabled, Action begin, Action<double> update, Action<double, bool> finish)
	{
		Track(surface, (DependencyObject? n) => enabled() && !Interactive(n, surface, allowLauncher: true), delegate
		{
			begin();
		}, delegate(double x, double y)
		{
			update(Math.Max(Math.Abs(x), Math.Abs(y)));
		}, delegate(double x, double y, bool c)
		{
			finish(0.0, c);
		}, null, delegate(DependencyObject? node, double x, double y)
		{
			while (node != null && (object)node != surface)
			{
				if (node is ScrollViewer scrollViewer)
				{
					if (Math.Abs(x) > Math.Abs(y))
					{
						return true;
					}
					if (!(y > 0.0))
					{
						return scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - 0.5;
					}
					return scrollViewer.VerticalOffset <= 0.5;
				}
				node = VisualTreeHelper.GetParent(node);
			}
			return true;
		});
	}

	public static void Attach(FrameworkElement surface, Func<bool> vertical, Func<int> sign, Func<bool> enabled, Action begin, Action<double> update, Action<double, bool> finish)
	{
		Track(surface, (DependencyObject? n) => enabled() && !Interactive(n, surface), delegate
		{
			begin();
		}, delegate(double x, double y)
		{
			update((vertical() ? y : x) * (double)sign());
		}, delegate(double x, double y, bool c)
		{
			finish(0.0, c);
		});
	}
}
