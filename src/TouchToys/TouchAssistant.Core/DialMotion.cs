using System;

namespace TouchAssistant.Core;

internal static class DialMotion
{
	public static double Units(double degrees, double sensitivity)
	{
		return degrees / 15.0 * Math.Clamp(sensitivity, 0.05, 4.0);
	}

	public static double VolumeDelta(double degrees, double sensitivity)
	{
		return Units(degrees, sensitivity) * 2.0;
	}

	public static double WheelDelta(double degrees, double sensitivity)
	{
		return (0.0 - Units(degrees, sensitivity)) * 120.0;
	}
}
