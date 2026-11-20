using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace TouchAssistant.UI;

internal sealed class WrapPanel : Panel
{
	public double HorizontalSpacing { get; set; } = 4.0;

	public double VerticalSpacing { get; set; } = 4.0;

	protected override Size MeasureOverride(Size availableSize)
	{
		double num = (double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width);
		double num2 = 0.0;
		double num3 = 0.0;
		double num4 = 0.0;
		double val = 0.0;
		foreach (UIElement child in base.Children)
		{
			child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			Size desiredSize = child.DesiredSize;
			if (num2 > 0.0 && num2 + desiredSize.Width > num)
			{
				val = Math.Max(val, num2 - HorizontalSpacing);
				num2 = 0.0;
				num3 += num4 + VerticalSpacing;
				num4 = 0.0;
			}
			num2 += desiredSize.Width + HorizontalSpacing;
			num4 = Math.Max(num4, desiredSize.Height);
		}
		val = Math.Max(val, num2 - HorizontalSpacing);
		return new Size(Math.Min(num, val), num3 + num4);
	}

	protected override Size ArrangeOverride(Size finalSize)
	{
		double num = 0.0;
		double num2 = 0.0;
		double num3 = 0.0;
		double val = 0.0;
		foreach (UIElement child in base.Children)
		{
			Size desiredSize = child.DesiredSize;
			if (num > 0.0 && num + desiredSize.Width > finalSize.Width)
			{
				val = Math.Max(val, num - HorizontalSpacing);
				num = 0.0;
				num2 += num3 + VerticalSpacing;
				num3 = 0.0;
			}
			child.Arrange(new Rect(num, num2, desiredSize.Width, desiredSize.Height));
			num += desiredSize.Width + HorizontalSpacing;
			num3 = Math.Max(num3, desiredSize.Height);
		}
		return new Size(Math.Max(val, num - HorizontalSpacing), num2 + num3);
	}
}
