using System.ComponentModel;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using TouchAssistant.Models;
using TouchAssistant.Services;

namespace TouchAssistant.UI;

public sealed class LauncherItem : INotifyPropertyChanged
{
	private bool selected;

	private bool hover;

	private bool pressed;

	public LauncherEntry Entry { get; }

	public string Name => Entry.Name;

	public string Target => Entry.Target;

	public string FallbackGlyph
	{
		get
		{
			if (string.IsNullOrWhiteSpace(Entry.IconGlyph))
			{
				if ((Entry.Keys?.Count ?? 0) <= 0)
				{
					return "\ue71d";
				}
				return "\ue765";
			}
			return Entry.IconGlyph;
		}
	}

	public ImageSource? Icon { get; private set; }

	public Visibility FallbackVisibility
	{
		get
		{
			if (!(Icon == null))
			{
				return Visibility.Collapsed;
			}
			return Visibility.Visible;
		}
	}

	public Visibility DeleteVisibility { get; private set; } = Visibility.Collapsed;

	public Visibility SelectionVisibility { get; private set; } = Visibility.Collapsed;

	public event PropertyChangedEventHandler? PropertyChanged;

	private void RefreshOutline()
	{
		SelectionVisibility = ((!selected && !hover && !pressed) ? Visibility.Collapsed : Visibility.Visible);
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("SelectionVisibility"));
	}

	public void SetSelected(bool value)
	{
		selected = value;
		RefreshOutline();
	}

	public void SetHover(bool value)
	{
		hover = value;
		RefreshOutline();
	}

	public void SetPressed(bool value)
	{
		pressed = value;
		RefreshOutline();
	}

	public LauncherItem(LauncherEntry entry)
	{
		Entry = entry;
	}

	public async Task LoadIconAsync()
	{
		ImageSource icon;
		if (!string.IsNullOrWhiteSpace(Entry.IconPath))
		{
			icon = await LauncherIcons.LoadAsync(Entry.IconPath);
		}
		else
		{
			ImageSource imageSource = (string.IsNullOrWhiteSpace(Target) ? null : (await LauncherIcons.LoadAsync(Target)));
			icon = imageSource;
		}
		Icon = icon;
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Icon"));
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("FallbackVisibility"));
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("FallbackGlyph"));
	}

	public void SetEditing(bool editing)
	{
		DeleteVisibility = ((!editing) ? Visibility.Collapsed : Visibility.Visible);
		this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("DeleteVisibility"));
	}
}
