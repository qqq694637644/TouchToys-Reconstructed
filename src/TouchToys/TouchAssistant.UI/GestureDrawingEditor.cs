using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.Services;
using Windows.Foundation;
using Windows.UI;

namespace TouchAssistant.UI;

internal sealed class GestureDrawingEditor : StackPanel
{
	internal Func<string>? InspectRecording;

	public GestureDrawingEditor(MultiTouchBinding binding, SettingsStore store)
	{
		base.Spacing = 8.0;
		int fingers = binding.Fingers;
		if ((uint)(fingers - 3) <= 1u)
		{
			base.Children.Add(Ui.Text("Windows 会优先处理三指、四指触摸手势。若绘制时出现桌面或任务视图，请先在 Windows 触摸设置中关闭“三指和四指触摸手势”，或用鼠标绘制模板。", 12.0));
			base.Children.Add(Ui.Button("打开 Windows 触摸设置", delegate
			{
				try
				{
					SystemControl.Launch("ms-settings:devices-touch");
				}
				catch (Exception ex)
				{
					Diagnostics.Error(ex, "Gestures.SystemSettings");
					App.CurrentApp.Report(ex.Message);
				}
			}));
		}
		Canvas canvas = new Canvas
		{
			Height = 320.0,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			Background = new SolidColorBrush(Colors.Transparent),
			ManipulationMode = ManipulationModes.None
		};
		Grid grid = new Grid
		{
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		grid.Children.Add(canvas);
		Grid grid2 = new Grid
		{
			Width = 22.0,
			Height = 22.0,
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			Background = new SolidColorBrush(Colors.Transparent)
		};
		grid2.Children.Add(new TextBlock
		{
			Text = "◢",
			FontSize = 15.0,
			Foreground = new SolidColorBrush(Colors.Gray),
			HorizontalAlignment = HorizontalAlignment.Right,
			VerticalAlignment = VerticalAlignment.Bottom,
			IsHitTestVisible = false
		});
		Thumb thumb = new Thumb
		{
			Width = 22.0,
			Height = 22.0,
			Background = new SolidColorBrush(Colors.Transparent)
		};
		thumb.DragDelta += delegate(object _, DragDeltaEventArgs e)
		{
			double num = Math.Clamp(canvas.Height + e.VerticalChange, 220.0, 720.0);
			if (Math.Abs(num - canvas.Height) > 0.5)
			{
				canvas.Height = num;
			}
		};
		grid2.Children.Add(thumb);
		grid.Children.Add(grid2);
		base.Children.Add(new Border
		{
			Child = grid,
			BorderBrush = new SolidColorBrush(Colors.Gray),
			BorderThickness = new Thickness(1.0)
		});
		base.Children.Add(Ui.Description("拖动右下角 ◢ 可放大或缩小绘制区域。", 12.0));
		Polyline line = new Polyline
		{
			Stroke = new SolidColorBrush(Colors.DodgerBlue),
			StrokeThickness = 3.0,
			IsHitTestVisible = false
		};
		canvas.Children.Add(line);
		ShapeRecording recording = new ShapeRecording();
		List<GesturePoint> draft = recording.Path;
		TextBlock status = Ui.Text((binding.Points.Count > 1 || binding.Paths.Count > 0) ? "已保存形状；重新绘制后点击保存。" : Localization.T("用 {0} 指同时绘制，或用鼠标绘制中心轨迹。", binding.Fingers), 12.0);
		base.Children.Add(status);
		int downs = 0;
		int moves = 0;
		int ups = 0;
		int cancels = 0;
		InspectRecording = () => $"Down={downs}, Move={moves}, Up={ups}, Cancel={cancels}, Active={recording.Active}, Complete={recording.Complete}, Points={draft.Count}, Status={status.Text}";
		canvas.SizeChanged += delegate
		{
			canvas.Clip = new RectangleGeometry
			{
				Rect = new Rect(0.0, 0.0, canvas.ActualWidth, canvas.ActualHeight)
			};
		};
		canvas.Loaded += delegate
		{
			if (binding.Paths.Count > 0)
			{
				uint num = 1u;
				foreach (List<GesturePoint> path in binding.Paths)
				{
					recording.Paths[num++] = new List<GesturePoint>(path);
				}
				Redraw();
			}
			else if (binding.Points.Count >= 2 && draft.Count <= 0)
			{
				line.Points.Clear();
				List<GesturePoint> list = ShapeGesture.Normalize(binding.Points);
				if (list.Count != 0)
				{
					double num2 = (list.Min((GesturePoint p) => p.X) + list.Max((GesturePoint p) => p.X)) / 2.0;
					double num3 = (list.Min((GesturePoint p) => p.Y) + list.Max((GesturePoint p) => p.Y)) / 2.0;
					foreach (GesturePoint item in list)
					{
						line.Points.Add(new Point(canvas.ActualWidth / 2.0 + (item.X - num2) * 150.0, canvas.ActualHeight / 2.0 + (item.Y - num3) * 150.0));
					}
				}
			}
		};
		canvas.PointerPressed += delegate(object _, PointerRoutedEventArgs e)
		{
			canvas.CancelDirectManipulations();
			downs++;
			Point position = e.GetCurrentPoint(canvas).Position;
			int num = ((e.Pointer.PointerDeviceType == PointerDeviceType.Mouse) ? 1 : binding.Fingers);
			recording.Down(e.Pointer.PointerId, position.X, position.Y, Environment.TickCount64, num, store.Value.MultiTouch.MaxSpacing);
			canvas.CapturePointer(e.Pointer);
			Redraw();
			status.Text = Localization.T("正在录制：{0} / {1} 指", recording.Active, num);
			e.Handled = true;
		};
		canvas.PointerMoved += delegate(object _, PointerRoutedEventArgs e)
		{
			if (recording.Contains(e.Pointer.PointerId))
			{
				moves++;
				Point position = e.GetCurrentPoint(canvas).Position;
				recording.Move(e.Pointer.PointerId, Math.Clamp(position.X, 0.0, canvas.ActualWidth), Math.Clamp(position.Y, 0.0, canvas.ActualHeight));
				Redraw();
				e.Handled = true;
			}
		};
		HashSet<uint> released = new HashSet<uint>();
		canvas.PointerReleased += delegate(object _, PointerRoutedEventArgs e)
		{
			if (recording.Contains(e.Pointer.PointerId))
			{
				ups++;
				Point position = e.GetCurrentPoint(canvas).Position;
				recording.Move(e.Pointer.PointerId, position.X, position.Y);
				recording.Up(e.Pointer.PointerId);
				released.Add(e.Pointer.PointerId);
				canvas.ReleasePointerCapture(e.Pointer);
				released.Remove(e.Pointer.PointerId);
				Redraw();
				if (recording.Active == 0)
				{
					status.Text = Localization.T(recording.Complete ? "点击保存形状后生效。" : "请按所选手指数同时落下并完整绘制，轨迹至少 60 DIP。");
				}
				e.Handled = true;
			}
		};
		canvas.PointerCanceled += delegate
		{
			Cancel();
		};
		canvas.PointerCaptureLost += delegate(object _, PointerRoutedEventArgs e)
		{
			if (recording.Contains(e.Pointer.PointerId))
			{
				Cancel();
			}
		};
		base.Children.Add(Ui.Button("保存形状", delegate
		{
			if (!recording.Complete || recording.Active > 0)
			{
				status.Text = Localization.T("请按所选手指数完整绘制后再保存，轨迹至少 60 DIP。");
			}
			else
			{
				binding.Points = new List<GesturePoint>(draft);
				binding.Paths = ((recording.Paths.Count == binding.Fingers) ? recording.Paths.Values.Select((List<GesturePoint> p) => new List<GesturePoint>(p)).ToList() : new List<List<GesturePoint>>());
				store.Save();
				status.Text = Localization.T("形状已保存。");
			}
		}));
		base.Children.Add(Ui.Button("清除形状", delegate
		{
			recording.Reset();
			canvas.ReleasePointerCaptures();
			Redraw();
			binding.Points.Clear();
			binding.Paths.Clear();
			store.Save();
			status.Text = Localization.T("形状已清除，不会触发。");
		}));
		void Cancel()
		{
			cancels++;
			recording.Reset();
			Redraw();
			status.Text = Localization.T("绘制中断，请重新绘制。");
		}
		void Redraw()
		{
			canvas.Children.Clear();
			int num = 0;
			Color[] array = new Color[5]
			{
				Colors.DodgerBlue,
				Colors.OrangeRed,
				Colors.MediumSeaGreen,
				Colors.MediumPurple,
				Colors.Goldenrod
			};
			foreach (List<GesturePoint> value in recording.Paths.Values)
			{
				SolidColorBrush solidColorBrush = new SolidColorBrush(array[num++ % array.Length]);
				Polyline polyline = new Polyline
				{
					Stroke = solidColorBrush,
					StrokeThickness = 3.0,
					IsHitTestVisible = false
				};
				foreach (GesturePoint item2 in value)
				{
					polyline.Points.Add(new Point(item2.X, item2.Y));
				}
				canvas.Children.Add(polyline);
				if (value.Count > 0)
				{
					Ellipse ellipse = new Ellipse
					{
						Width = 8.0,
						Height = 8.0,
						Fill = solidColorBrush,
						IsHitTestVisible = false
					};
					Canvas.SetLeft(ellipse, value[value.Count - 1].X - 4.0);
					Canvas.SetTop(ellipse, value[value.Count - 1].Y - 4.0);
					canvas.Children.Add(ellipse);
				}
			}
		}
	}
}
