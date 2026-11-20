using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TouchAssistant.Models;

public sealed class GamepadControl
{
	public string BackgroundColor { get; set; } = "#141414";

	public string OutlineColor { get; set; } = "#FFFFFF";

	public string CenterColor { get; set; } = "#FFFFFF";

	public double Opacity { get; set; } = 1.0;

	public double IdleOpacity { get; set; } = 0.45;

	public int WorkLeft { get; set; }

	public int WorkTop { get; set; }

	public int WorkWidth { get; set; }

	public int WorkHeight { get; set; }

	public GestureAction? Appearance { get; set; }

	[JsonIgnore]
	public bool IsLayoutSelector { get; set; }

	public bool Joystick { get; set; }

	public string Name { get; set; } = "按钮";

	public double Size { get; set; } = 72.0;

	public string Style { get; set; } = "Circle";

	public double Width { get; set; }

	public double Height { get; set; }

	public double CornerRadius { get; set; } = 16.0;

	public string Text { get; set; } = "";

	public string Icon { get; set; } = "";

	public int? X { get; set; }

	public int? Y { get; set; }

	public GestureAction Action { get; set; } = new GestureAction();

	public GestureAction CenterTap { get; set; } = new GestureAction();

	public GestureAction CenterDoubleTap { get; set; } = new GestureAction();

	public List<GestureAction> Directions { get; set; } = DefaultDirections();

	public static List<GestureAction> DefaultDirections()
	{
		List<GestureAction> list = new List<GestureAction>();
		list.Add(Keys(87));
		list.Add(Keys(87, 68));
		list.Add(Keys(68));
		list.Add(Keys(68, 83));
		list.Add(Keys(83));
		list.Add(Keys(83, 65));
		list.Add(Keys(65));
		list.Add(Keys(65, 87));
		return list;
	}

	private static GestureAction Keys(params int[] values)
	{
		return new GestureAction
		{
			Kind = ActionKind.Keys,
			Keys = new List<int>(values)
		};
	}
}
