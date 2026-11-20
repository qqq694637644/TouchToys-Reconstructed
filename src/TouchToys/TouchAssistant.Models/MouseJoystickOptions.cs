using System.Collections.Generic;

namespace TouchAssistant.Models;

public sealed class MouseJoystickOptions
{
	public bool Enabled { get; set; }

	public bool ShakeToClose { get; set; } = true;

	public int? X { get; set; }

	public int? Y { get; set; }

	public double Size { get; set; } = 80.0;

	public double Opacity { get; set; } = 0.75;

	public GestureAction Tap { get; set; } = new GestureAction
	{
		Kind = ActionKind.Keys,
		Keys = new List<int> { 513 }
	};
}
