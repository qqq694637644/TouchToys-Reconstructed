namespace TouchAssistant.Models;

public sealed class GesturePoint
{
	public double X { get; set; }

	public double Y { get; set; }

	public GesturePoint()
	{
	}

	public GesturePoint(double x, double y)
	{
		X = x;
		Y = y;
	}
}
