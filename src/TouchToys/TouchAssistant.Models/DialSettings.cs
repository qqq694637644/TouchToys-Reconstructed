using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TouchAssistant.Models;

public sealed class DialSettings
{
	public double Sensitivity { get; set; } = 1.0;

	public bool RotationSound { get; set; }

	public bool MouseEnabled { get; set; } = true;

	public double Size { get; set; } = 164.0;

	public double Opacity { get; set; } = 1.0;

	public double IdleOpacity { get; set; } = 0.35;

	public double IdleSeconds { get; set; } = 3.0;

	public double HoldSeconds { get; set; } = 0.2;

	public string OuterColor { get; set; } = "#000000";

	public string InnerColor { get; set; } = "#3A3A3A";

	public bool MoveOutline { get; set; } = true;

	public bool CenterToolHint { get; set; } = true;

	public bool ShakeToClose { get; set; } = true;

	public int? X { get; set; }

	public int? Y { get; set; }

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public GestureAction? Tap { get; set; }

	public GestureAction DoubleTap { get; set; } = new GestureAction();

	public List<DialTool> Tools { get; set; } = DialTool.Defaults();
}
