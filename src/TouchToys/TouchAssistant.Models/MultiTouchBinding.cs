using System.Collections.Generic;

namespace TouchAssistant.Models;

public sealed class MultiTouchBinding
{
	public string Name { get; set; } = "自定义形状";

	public List<GesturePoint> Points { get; set; } = new List<GesturePoint>();

	public List<List<GesturePoint>> Paths { get; set; } = new List<List<GesturePoint>>();

	public int Fingers { get; set; } = 2;

	public string Gesture { get; set; } = "Tap";

	public GestureAction Action { get; set; } = new GestureAction();
}
