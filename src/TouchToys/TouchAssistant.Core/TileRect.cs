using System;

namespace TouchAssistant.Core;

internal readonly record struct TileRect(double X, double Y, double Width, double Height)
{
	public double Right => X + Width;

	public double Bottom => Y + Height;

	public double Area => Math.Max(0.0, Width) * Math.Max(0.0, Height);

	public double Overlap(TileRect other)
	{
		return Math.Max(0.0, Math.Min(Right, other.Right) - Math.Max(X, other.X)) * Math.Max(0.0, Math.Min(Bottom, other.Bottom) - Math.Max(Y, other.Y));
	}
}
