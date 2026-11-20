using System;
using System.Collections.ObjectModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace TouchAssistant.UI;

internal sealed class SettingsTabs : Grid
{
	private readonly Button previous = Ui.Button("‹", delegate
	{
	});

	private readonly Button next = Ui.Button("›", delegate
	{
	});

	private readonly ScrollViewer headerScroll = new ScrollViewer
	{
		HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
		VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
		HorizontalScrollMode = ScrollMode.Enabled
	};

	private readonly StackPanel headers = new StackPanel
	{
		Orientation = Orientation.Horizontal,
		Spacing = 12.0
	};

	private readonly ContentControl page = new ContentControl
	{
		HorizontalContentAlignment = HorizontalAlignment.Stretch,
		VerticalContentAlignment = VerticalAlignment.Stretch
	};

	private int index;

	private SettingsTab? displayed;

	private readonly ContentControl rightHeader = new ContentControl
	{
		HorizontalAlignment = HorizontalAlignment.Right
	};

	private Grid? headingRow;

	public ObservableCollection<SettingsTab> Items { get; } = new ObservableCollection<SettingsTab>();

	public bool ShowHeaders
	{
		get
		{
			Grid? grid = headingRow;
			if ((object)grid == null)
			{
				return true;
			}
			return grid.Visibility != Visibility.Collapsed;
		}
		set
		{
			if (headingRow != null)
			{
				headingRow.Visibility = ((!value) ? Visibility.Collapsed : Visibility.Visible);
			}
		}
	}

	public object? RightHeader
	{
		get
		{
			return rightHeader.Content;
		}
		set
		{
			rightHeader.Content = value;
		}
	}

	public int SelectedIndex
	{
		get
		{
			if (Items.Count != 0)
			{
				return Math.Clamp(index, 0, Items.Count - 1);
			}
			return -1;
		}
		set
		{
			if (value != SelectedIndex)
			{
				index = value;
				Render();
				this.SelectionChanged?.Invoke(this, new SettingsTabChangedEventArgs(this));
			}
		}
	}

	public SettingsTab? SelectedItem
	{
		get
		{
			if (SelectedIndex >= 0)
			{
				return Items[SelectedIndex];
			}
			return null;
		}
	}

	public event EventHandler<SettingsTabChangedEventArgs>? SelectionChanged;

	public SettingsTabs()
	{
		base.RowDefinitions.Add(new RowDefinition
		{
			Height = GridLength.Auto
		});
		base.RowDefinitions.Add(new RowDefinition
		{
			Height = new GridLength(1.0, GridUnitType.Star)
		});
		Grid grid = new Grid
		{
			Margin = new Thickness(0.0, 0.0, 0.0, 12.0)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition());
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		headingRow = grid;
		Grid grid2 = new Grid
		{
			ColumnDefinitions = 
			{
				new ColumnDefinition
				{
					Width = GridLength.Auto
				},
				new ColumnDefinition(),
				new ColumnDefinition
				{
					Width = GridLength.Auto
				}
			}
		};
		previous.MinWidth = (next.MinWidth = 40.0);
		previous.Click += delegate
		{
			SelectedIndex = Math.Max(0, SelectedIndex - 1);
		};
		next.Click += delegate
		{
			SelectedIndex = Math.Min(Items.Count - 1, SelectedIndex + 1);
		};
		headerScroll.Content = headers;
		Grid.SetColumn(headerScroll, 1);
		Grid.SetColumn(next, 2);
		grid2.Children.Add(previous);
		grid2.Children.Add(headerScroll);
		grid2.Children.Add(next);
		headers.SizeChanged += delegate
		{
			Navigation();
		};
		headerScroll.SizeChanged += delegate
		{
			Navigation();
		};
		headerScroll.ViewChanged += delegate
		{
			Navigation();
		};
		grid.Children.Add(grid2);
		Grid.SetColumn(rightHeader, 1);
		grid.Children.Add(rightHeader);
		base.Children.Add(grid);
		Grid.SetRow(page, 1);
		base.Children.Add(page);
		Items.CollectionChanged += delegate
		{
			Render(rebuildHeaders: true);
		};
	}

	private void Navigation()
	{
		bool flag = headerScroll.ScrollableWidth > 1.0;
		Button button = previous;
		Visibility visibility = (next.Visibility = ((!flag) ? Visibility.Collapsed : Visibility.Visible));
		button.Visibility = visibility;
		previous.IsEnabled = SelectedIndex > 0;
		next.IsEnabled = SelectedIndex < Items.Count - 1;
	}

	private void RefreshPage()
	{
		page.Content = displayed?.Content;
	}

	private void Render(bool rebuildHeaders = false)
	{
		SettingsTab selectedItem = SelectedItem;
		if (displayed != selectedItem)
		{
			if (displayed != null)
			{
				displayed.Changed -= RefreshPage;
			}
			page.Content = null;
			displayed = selectedItem;
			if (displayed != null)
			{
				displayed.Changed += RefreshPage;
			}
			RefreshPage();
		}
		if (rebuildHeaders)
		{
			headers.Children.Clear();
			for (int i = 0; i < Items.Count; i++)
			{
				int target = i;
				Button button = new Button
				{
					Content = Items[i].Header,
					FontSize = 22.0,
					MinHeight = 48.0,
					Padding = new Thickness(0.0, 8.0, 4.0, 8.0),
					Background = Ui.Brush(0, 0, 0, 0),
					CornerRadius = new CornerRadius(0.0)
				};
				button.Click += delegate
				{
					SelectedIndex = target;
				};
				headers.Children.Add(button);
			}
		}
		for (int num = 0; num < headers.Children.Count; num++)
		{
			Button obj = (Button)headers.Children[num];
			obj.BorderThickness = new Thickness(0.0, 0.0, 0.0, (num == SelectedIndex) ? 3 : 0);
			obj.BorderBrush = AccentTheme.Brush;
			obj.Opacity = ((num == SelectedIndex) ? 1.0 : 0.65);
		}
		Navigation();
	}
}
