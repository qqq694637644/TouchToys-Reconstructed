using System;

namespace TouchAssistant.UI;

internal sealed class SettingsTab
{
	private object? content;

	public object? Header { get; set; }

	public object? Content
	{
		get
		{
			return content;
		}
		set
		{
			content = value;
			this.Changed?.Invoke();
		}
	}

	internal event Action? Changed;
}
