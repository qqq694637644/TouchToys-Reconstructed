using System;

namespace TouchAssistant.Core;

internal static class EdgeTolerance
{
	internal static double Pixels(double scale, int width, int height)
	{
		return Math.Max(1.0, Math.Min(16.0 * ((double.IsFinite(scale) && scale > 0.0) ? scale : 1.0), (double)Math.Max(1, Math.Min(width, height)) * 0.04));
	}
}
