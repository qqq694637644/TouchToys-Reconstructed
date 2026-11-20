using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TouchAssistant.UI;

internal sealed class ResponsivePair : Grid
{
	public FrameworkElement Selector { get; }

	public ResponsivePair(FrameworkElement selector, FrameworkElement editor)
	{
		ResponsivePair responsivePair = this;
		Selector = selector;
		base.ColumnSpacing = 20.0;
		base.RowSpacing = 12.0;
		base.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		base.ColumnDefinitions.Add(new ColumnDefinition());
		base.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		base.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		base.Children.Add(selector);
		base.Children.Add(editor);
		selector.VerticalAlignment = VerticalAlignment.Top;
		base.SizeChanged += delegate
		{
			Arrange();
		};
		Arrange();
		void Arrange()
		{
			bool flag = responsivePair.ActualWidth >= 660.0;
			Grid.SetColumn(editor, flag ? 1 : 0);
			Grid.SetRow(editor, (!flag) ? 1 : 0);
			Grid.SetColumnSpan(selector, flag ? 1 : 2);
			Grid.SetColumnSpan(editor, flag ? 1 : 2);
		}
	}
}
