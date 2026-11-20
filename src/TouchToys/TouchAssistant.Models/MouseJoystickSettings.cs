using System.Collections.Generic;

namespace TouchAssistant.Models;

public sealed class MouseJoystickSettings
{
	public bool Migrated { get; set; }

	public MouseJoystickOptions Pointer { get; set; } = new MouseJoystickOptions();

	public MouseJoystickOptions RightDrag { get; set; } = new MouseJoystickOptions
	{
		Tap = new GestureAction
		{
			Kind = ActionKind.Keys,
			Keys = new List<int> { 514 }
		}
	};
}
