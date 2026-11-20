using System.Collections.Generic;

namespace TouchAssistant.Models;

public sealed class BottomAppProfile
{
	public string Name { get; set; } = "应用";

	public string Target { get; set; } = "";

	public GestureAction Bottom { get; set; } = GestureAction.BuiltIn("Overview");

	public Dictionary<string, GestureAction> Indicator { get; set; } = Settings.IndicatorDefaults();
}
