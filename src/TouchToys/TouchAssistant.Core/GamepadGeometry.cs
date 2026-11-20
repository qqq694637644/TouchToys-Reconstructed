using System;

namespace TouchAssistant.Core;

internal static class GamepadGeometry
{
	internal static double Reflow(double position, double oldOrigin, double oldLength, double newOrigin, double newLength, double extent)
	{
		if (oldLength <= 0.0)
		{
			return Math.Clamp(position, newOrigin, newOrigin + Math.Max(0.0, newLength - extent));
		}
		double num = Math.Clamp((position - oldOrigin) / Math.Max(1.0, oldLength - extent), 0.0, 1.0);
		return newOrigin + num * Math.Max(0.0, newLength - extent);
	}

	public static int Direction(double x, double y, double radius)
	{
		if (!(Math.Sqrt(x * x + y * y) < radius * 0.24))
		{
			return (int)Math.Floor((Math.Atan2(y, x) * 180.0 / Math.PI + 90.0 + 382.5) / 45.0) % 8;
		}
		return -1;
	}
}
