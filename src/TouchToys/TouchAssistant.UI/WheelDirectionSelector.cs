using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using TouchAssistant.Models;
using TouchAssistant.Native;
using TouchAssistant.Services;
using Windows.Foundation;
using Windows.System;

namespace TouchAssistant.UI;

internal sealed class WheelDirectionSelector : UserControl
{
	private readonly SettingsStore store;

	private readonly Action<int> selected;

	private int index;

	private bool queued;

	private bool attached;

	private readonly Microsoft.UI.Xaml.Controls.Image view = new Microsoft.UI.Xaml.Controls.Image
	{
		Width = 280.0,
		Height = 280.0
	};

	private readonly Bitmap?[] icons = new Bitmap[8];

	private string iconStamp = "";

	private XamlRoot? observedRoot;

	private double renderedScale;

	public WheelDirectionSelector(SettingsStore store, Action<int> selected)
	{
		WheelDirectionSelector wheelDirectionSelector = this;
		this.store = store;
		this.selected = selected;
		base.Content = view;
		base.Width = (base.Height = 280.0);
		base.HorizontalAlignment = HorizontalAlignment.Center;
		base.IsTabStop = true;
		AutomationProperties.SetName(this, Localization.T("环形菜单预览，左右方向键选择扇面"));
		base.KeyDown += delegate(object _, KeyRoutedEventArgs e)
		{
			if (e.Key == VirtualKey.Left)
			{
				wheelDirectionSelector.Select((wheelDirectionSelector.index + 7) % 8);
				e.Handled = true;
			}
			else if (e.Key == VirtualKey.Right)
			{
				wheelDirectionSelector.Select((wheelDirectionSelector.index + 1) % 8);
				e.Handled = true;
			}
		};
		base.Tapped += delegate(object _, TappedRoutedEventArgs e)
		{
			Windows.Foundation.Point position = e.GetPosition(wheelDirectionSelector);
			double num2 = position.X - 140.0;
			double num3 = position.Y - 140.0;
			double num4 = Math.Sqrt(num2 * num2 + num3 * num3);
			if (!(num4 < 60.0) && !(num4 > 138.0))
			{
				wheelDirectionSelector.Select((int)Math.Floor((Math.Atan2(num3, num2) * 180.0 / Math.PI + 112.5 + 360.0) % 360.0 / 45.0) % 8);
				e.Handled = true;
			}
		};
		base.Loaded += delegate
		{
			if (!wheelDirectionSelector.attached)
			{
				wheelDirectionSelector.attached = true;
				store.Changed += wheelDirectionSelector.Queue;
				AccentTheme.Changed += wheelDirectionSelector.Queue;
				wheelDirectionSelector.observedRoot = wheelDirectionSelector.XamlRoot;
				if (wheelDirectionSelector.observedRoot != null)
				{
					wheelDirectionSelector.observedRoot.Changed += wheelDirectionSelector.RootChanged;
				}
			}
			wheelDirectionSelector.Queue();
		};
		base.Unloaded += delegate
		{
			wheelDirectionSelector.attached = false;
			if (wheelDirectionSelector.observedRoot != null)
			{
				wheelDirectionSelector.observedRoot.Changed -= wheelDirectionSelector.RootChanged;
			}
			wheelDirectionSelector.observedRoot = null;
			store.Changed -= wheelDirectionSelector.Queue;
			AccentTheme.Changed -= wheelDirectionSelector.Queue;
			Bitmap[] array = wheelDirectionSelector.icons;
			for (int i = 0; i < array.Length; i++)
			{
				array[i]?.Dispose();
			}
			Array.Clear(wheelDirectionSelector.icons);
			wheelDirectionSelector.iconStamp = "";
		};
		Select(0);
	}

	private void RootChanged(XamlRoot sender, XamlRootChangedEventArgs args)
	{
		if (Math.Abs(sender.RasterizationScale - renderedScale) > 0.001)
		{
			Queue();
		}
	}

	internal void Select(int value)
	{
		index = value;
		selected(value);
		Queue();
	}

	private void Queue()
	{
		if (queued)
		{
			return;
		}
		queued = true;
		base.DispatcherQueue.TryEnqueue(delegate
		{
			queued = false;
			if (attached)
			{
				Render();
			}
		});
	}

	private void Render()
	{
		try
		{
			string text = string.Join("|", store.Value.Gestures.ConvertAll((GestureAction a) => a.IconPath));
			if (text != iconStamp)
			{
				iconStamp = text;
				for (int num = 0; num < 8; num++)
				{
					icons[num]?.Dispose();
					icons[num] = null;
					try
					{
						string iconPath = store.Value.Gestures[num].IconPath;
						if (!File.Exists(iconPath))
						{
							continue;
						}
						using System.Drawing.Image image = System.Drawing.Image.FromFile(iconPath);
						if (image.Width <= 512 && image.Height <= 512)
						{
							icons[num] = new Bitmap(image);
						}
					}
					catch
					{
					}
				}
			}
			renderedScale = base.XamlRoot?.RasterizationScale ?? 1.0;
			int num2 = (int)Math.Ceiling(280.0 * renderedScale);
			using Bitmap bitmap = new Bitmap(num2, num2, PixelFormat.Format32bppPArgb);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.ScaleTransform((float)((double)num2 / 280.0), (float)((double)num2 / 280.0));
				graphics.TranslateTransform(140f, 140f);
				graphics.ScaleTransform(1.25f, 1.25f);
				WheelDrawing.Draw(graphics, store.Value.Gestures, icons, index);
				if (!store.Value.WheelMask)
				{
					using SolidBrush brush = new SolidBrush(Color.FromArgb(42, 47, 56));
					graphics.FillEllipse(brush, -29, -29, 58, 58);
				}
				using SolidBrush brush2 = new SolidBrush(Color.FromArgb(205, 207, 211));
				graphics.FillEllipse(brush2, -25, -25, 50, 50);
			}
			WriteableBitmap writeableBitmap = new WriteableBitmap(num2, num2);
			BitmapData bitmapData = bitmap.LockBits(new Rectangle(0, 0, num2, num2), ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
			try
			{
				byte[] array = new byte[num2 * num2 * 4];
				Marshal.Copy(bitmapData.Scan0, array, 0, array.Length);
				using Stream stream = writeableBitmap.PixelBuffer.AsStream();
				stream.Write(array, 0, array.Length);
			}
			finally
			{
				bitmap.UnlockBits(bitmapData);
			}
			view.Source = writeableBitmap;
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Settings.WheelPreview");
		}
	}
}
