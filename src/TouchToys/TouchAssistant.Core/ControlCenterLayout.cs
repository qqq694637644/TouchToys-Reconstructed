using System;

namespace TouchAssistant.Core;

public readonly record struct ControlCenterLayout(int TargetX, int TargetY, int Width, int Height, int OffX, int OffY, bool Vertical, bool LeftSide)
{
	public int X(double progress)
	{
		if (!Vertical)
		{
			return (int)Math.Round((double)OffX + (double)(TargetX - OffX) * Math.Clamp(progress, 0.0, 1.0));
		}
		return TargetX;
	}

	public int Y(double progress)
	{
		if (!Vertical)
		{
			return TargetY;
		}
		return (int)Math.Round((double)OffY + (double)(TargetY - OffY) * Math.Clamp(progress, 0.0, 1.0));
	}
}
