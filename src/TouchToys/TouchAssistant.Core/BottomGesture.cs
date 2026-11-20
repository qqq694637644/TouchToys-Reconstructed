using System;

namespace TouchAssistant.Core;

internal static class BottomGesture
{
	public const double IndicatorDistance = 12.0;

	public const double CardStart = 24.0;

	public static double BlurProgress(double distance)
	{
		return Math.Clamp((distance - 24.0) / 180.0, 0.0, 1.0);
	}

	public static bool StartMenuReached(double distance, double screenHeight, double dpi)
	{
		return distance >= 450.0;
	}

	public static int Release(double distance, double originY, double screenTop, double screenHeight, double dpi)
	{
		if (!StartMenuReached(distance + (screenTop + screenHeight - originY) / dpi, screenHeight, dpi))
		{
			return (BlurProgress(distance) > 1.0 / 3.0) ? 1 : 0;
		}
		return 2;
	}
}
