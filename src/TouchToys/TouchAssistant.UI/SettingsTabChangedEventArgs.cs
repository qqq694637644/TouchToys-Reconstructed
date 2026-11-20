using System;

namespace TouchAssistant.UI;

internal sealed class SettingsTabChangedEventArgs : EventArgs
{
	public object OriginalSource { get; }

	public SettingsTabChangedEventArgs(object source)
	{
		OriginalSource = source;
	}
}
