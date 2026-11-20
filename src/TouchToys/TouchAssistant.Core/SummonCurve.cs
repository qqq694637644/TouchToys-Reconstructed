using System;

namespace TouchAssistant.Core;

internal static class SummonCurve
{
	private static double Smooth(double value)
	{
		double num = Math.Clamp(value, 0.0, 1.0);
		return num * num * (3.0 - 2.0 * num);
	}

	private static double Bounce(double progress)
	{
		double num = Math.Clamp(progress, 0.0, 1.0);
		if (num < 0.72)
		{
			double num2 = num / 0.72;
			return num2 * num2 * (3.0 - 2.0 * num2);
		}
		double num3 = (num - 0.72) / 0.28;
		return 1.0 + 0.035 * Math.Sin(Math.PI * num3);
	}

	public static (double Inner, double Outer) CompactAt(double progress, double innerRatio)
	{
		double num = Bounce(progress);
		return (Inner: innerRatio * num, Outer: num);
	}

	public static (double Inner, double Outer) At(double progress, double innerRatio)
	{
		double num = Math.Clamp(progress, 0.0, 1.0);
		double num2 = Smooth(num / 0.33);
		if (num >= 0.33)
		{
			double num3 = Math.Clamp((num - 0.33) / 0.22, 0.0, 1.0);
			num2 *= 1.0 + 0.15 * Math.Sin(Math.PI * num3);
		}
		if (num < 0.111)
		{
			return (Inner: 0.0, Outer: num2);
		}
		double num4 = innerRatio * Smooth((num - 0.111) / 0.44600000000000006);
		if (num >= 0.557 && num < 0.777)
		{
			double num5 = (num - 0.557) / 0.22;
			num4 *= 1.0 + 0.3 * Math.Sin(Math.PI * num5);
		}
		return (Inner: num4, Outer: num2);
	}
}
