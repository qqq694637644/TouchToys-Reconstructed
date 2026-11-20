using System.Collections.Generic;

namespace TouchAssistant.Models;

public sealed class GestureAction
{
	public bool Enabled { get; set; } = true;

	public ActionKind Kind { get; set; }

	public string Command { get; set; } = "";

	public List<int> Keys { get; set; } = new List<int>();

	public string Icon { get; set; } = "";

	public string IconPath { get; set; } = "";

	public string IconFont { get; set; } = "Segoe Fluent Icons";

	public int AppearanceDestination { get; set; } = 3;

	public string Label { get; set; } = "";

	public string TextColor { get; set; } = "#FFFFFF";

	public double TextOpacity { get; set; } = 0.8;

	public static GestureAction BuiltIn(string command)
	{
		return new GestureAction
		{
			Kind = ActionKind.BuiltIn,
			Command = command
		};
	}
}
