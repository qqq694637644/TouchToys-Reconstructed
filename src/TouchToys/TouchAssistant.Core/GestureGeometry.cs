using System;

namespace TouchAssistant.Core;

public static class GestureGeometry
{
	public static double EdgeResistance(double distance)
	{
		return distance * 0.25 / (1.0 + Math.Abs(distance) / 240.0);
	}

	public static int DialIndex(double angle, int count)
	{
		if (count > 0)
		{
			return Math.Min(count - 1, (int)(((angle + 90.0) % 360.0 + 360.0) % 360.0 / 360.0 * (double)count));
		}
		return 0;
	}

	public static bool CancelWheel(double radius, bool enabled, double multiplier = 2.0)
	{
		if (enabled)
		{
			return radius > 110.0 * multiplier;
		}
		return false;
	}

	public static int BottomInputTop(int barTop, int barHeight, int inputHeight, int top, int bottom)
	{
		return Math.Clamp(barTop + barHeight / 2 - inputHeight / 2, top, Math.Max(top, bottom - inputHeight));
	}

	public static bool StartMenuPull(double distance, double screenHeight)
	{
		return distance >= screenHeight * 0.65;
	}

	public static (double Width, double Height) CardSize(double aspect, double maxWidth, double maxHeight)
	{
		if (!double.IsFinite(aspect) || aspect <= 0.0)
		{
			aspect = 1.7777777777777777;
		}
		double num = Math.Min(maxWidth, maxHeight * aspect);
		return (Width: num, Height: num / aspect);
	}

	public static double EdgeDistance(int edge, double dx, double dy)
	{
		return edge switch
		{
			0 => dx, 
			3 => 0.0 - dx, 
			2 => 0.0 - dy, 
			_ => dy, 
		};
	}

	public static int BarTop(int screenTop, int bottomAnchor, int height)
	{
		return Math.Max(screenTop, bottomAnchor - 30 - height);
	}

	public static double Reveal(double distance, double extent)
	{
		return Math.Clamp(distance / Math.Max(1.0, extent), 0.0, 1.0);
	}

	public static bool CommitReveal(double progress, double velocity)
	{
		if (!(velocity > 0.55))
		{
			if (velocity > -0.55)
			{
				return progress >= 0.35;
			}
			return false;
		}
		return true;
	}

	public static (int X, int Y) Remap(int x, int y, int oldLeft, int oldTop, int oldWidth, int oldHeight, double oldScale, int left, int top, int width, int height, double scale)
	{
		return (X: Map(x, oldLeft, oldWidth, oldScale, left, width, scale), Y: Map(y, oldTop, oldHeight, oldScale, top, height, scale));
		static int Map(int value, int origin, int length, double dpi, int nextOrigin, int nextLength, double nextDpi)
		{
			int num = Math.Min((int)Math.Ceiling(110.0 * dpi), length / 2);
			int num2 = Math.Min((int)Math.Ceiling(110.0 * nextDpi), nextLength / 2);
			double num3 = ((length > num * 2) ? Math.Clamp((double)(value - origin - num) / (double)(length - num * 2), 0.0, 1.0) : 0.5);
			return nextOrigin + num2 + (int)Math.Round(num3 * (double)(nextLength - num2 * 2));
		}
	}

	public static int Direction(double x, double y)
	{
		return (int)Math.Floor((Math.Atan2(x, 0.0 - y) * 180.0 / Math.PI + 382.5) / 45.0) % 8;
	}

	public static (int X, int Y) CenteredOverlay(int x, int y, int width, int height, int left, int top, int screenWidth, int screenHeight)
	{
		return (X: Math.Clamp(x - width / 2, left, Math.Max(left, left + screenWidth - width)), Y: Math.Clamp(y - height / 2, top, Math.Max(top, top + screenHeight - height)));
	}

	public static int NearestEdge(int x, int y, int left, int top, int width, int height)
	{
		double num = Math.Clamp((double)(x - left) / (double)Math.Max(1, width), 0.0, 1.0);
		double num2 = Math.Clamp((double)(y - top) / (double)Math.Max(1, height), 0.0, 1.0);
		double num3 = Math.Min(num, 1.0 - num);
		if (!(Math.Min(num2, 1.0 - num2) < num3))
		{
			if (!(num < 0.5))
			{
				return 3;
			}
			return 0;
		}
		if (!(num2 < 0.5))
		{
			return 2;
		}
		if (!(num < 0.5))
		{
			return 1;
		}
		return 4;
	}

	public static bool StageDismiss(int axis, bool vertical, double dx, double dy, double outward)
	{
		if (axis == ((!vertical) ? 1 : 2) && outward >= 72.0)
		{
			return outward > 1.5 * Math.Abs(vertical ? dx : dy);
		}
		return false;
	}

	public static int SpringStep(int value, int target)
	{
		int num = target - value;
		return value + ((Math.Abs(num) <= 3) ? num : ((int)((double)num * 0.28)));
	}

	public static double Ease(double value, double target, double rate, double epsilon)
	{
		if (!(Math.Abs(target - value) <= epsilon))
		{
			return value + (target - value) * rate;
		}
		return target;
	}

	public static (int X, int Y) Constrain(int x, int y, int left, int top, int width, int height, double scale, double radius = 110.0)
	{
		int val = (int)Math.Ceiling(radius * scale);
		return (X: Math.Clamp(x, left + Math.Min(val, width / 2), left + width - Math.Min(val, width / 2)), Y: Math.Clamp(y, top + Math.Min(val, height / 2), top + height - Math.Min(val, height / 2)));
	}
}
