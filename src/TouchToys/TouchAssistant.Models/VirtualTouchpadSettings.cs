using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TouchAssistant.Models;

public sealed class VirtualTouchpadSettings
{
	public Dictionary<string, GestureAction> MiddleActions { get; set; } = DefaultMiddleActions();

	public List<MultiTouchBinding> Gestures { get; set; } = DefaultGestures();

	public double GestureHoldSeconds { get; set; } = 0.5;

	public double IdleOpacity { get; set; } = 0.4;

	public bool JoystickEnabled { get; set; } = true;

	public bool RightJoystickEnabled { get; set; } = true;

	public double RightJoystickSize { get; set; } = 80.0;

	public double RightJoystickX { get; set; } = 90.0;

	public double RightJoystickY { get; set; } = 110.0;

	public GestureAction RightJoystickTap { get; set; } = new GestureAction
	{
		Kind = ActionKind.Keys,
		Keys = new List<int> { 514 }
	};

	public bool ButtonsOnTop { get; set; }

	public string? ButtonPlacement { get; set; }

	[JsonIgnore]
	public string EffectiveButtonPlacement
	{
		get
		{
			bool flag;
			switch (ButtonPlacement)
			{
			case "Top":
			case "Bottom":
			case "Left":
			case "Right":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				if (!ButtonsOnTop)
				{
					return "Bottom";
				}
				return "Top";
			}
			return ButtonPlacement;
		}
	}

	public double JoystickSize { get; set; } = 80.0;

	public double JoystickX { get; set; } = 270.0;

	public double JoystickY { get; set; } = 110.0;

	public GestureAction JoystickTap { get; set; } = new GestureAction
	{
		Kind = ActionKind.Keys,
		Keys = new List<int> { 513 }
	};

	public bool ShakeToClose { get; set; } = true;

	public bool Enabled { get; set; }

	public double Width { get; set; } = 360.0;

	public double Height { get; set; } = 260.0;

	public double Opacity { get; set; } = 0.75;

	public int? X { get; set; }

	public int? Y { get; set; }

	public static List<MultiTouchBinding> DefaultGestures()
	{
		return new List<MultiTouchBinding>
		{
			new MultiTouchBinding
			{
				Fingers = 2,
				Gesture = "PinchOut",
				Action = new GestureAction
				{
					Kind = ActionKind.Keys,
					Keys = new List<int> { 17, 516 }
				}
			},
			new MultiTouchBinding
			{
				Fingers = 2,
				Gesture = "PinchIn",
				Action = new GestureAction
				{
					Kind = ActionKind.Keys,
					Keys = new List<int> { 17, 517 }
				}
			}
		};
	}

	public static Dictionary<string, GestureAction> DefaultMiddleActions()
	{
		return new Dictionary<string, GestureAction>
		{
			["Up"] = new GestureAction
			{
				Kind = ActionKind.Keys,
				Keys = new List<int> { 516 }
			},
			["Down"] = new GestureAction
			{
				Kind = ActionKind.Keys,
				Keys = new List<int> { 517 }
			},
			["Tap"] = new GestureAction
			{
				Kind = ActionKind.Keys,
				Keys = new List<int> { 515 }
			},
			["DoubleTap"] = new GestureAction(),
			["TripleTap"] = new GestureAction(),
			["Hold"] = new GestureAction()
		};
	}
}
