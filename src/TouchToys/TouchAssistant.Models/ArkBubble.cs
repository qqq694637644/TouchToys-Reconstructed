namespace TouchAssistant.Models;

public sealed class ArkBubble
{
	public bool Pinned { get; set; }

	public double X { get; set; }

	public double Y { get; set; }

	public int? FixedX { get; set; }

	public int? FixedY { get; set; }

	public GestureAction Action { get; set; } = new GestureAction();
}
