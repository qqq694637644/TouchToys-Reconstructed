using System;

namespace TouchAssistant.Core;

public static class ControlCenterGeometry
{
	public const double WidthDip = 450.0;

	public const double HeightDip = 850.0;

	public const double GapDip = 12.0;

	public static ControlCenterLayout Compute(int ballX, int ballY, int workLeft, int workTop, int workRight, int workBottom, int monitorLeft, int monitorTop, int monitorRight, int monitorBottom, double scale, PanelSide side = PanelSide.Auto, double userScale = 1.0)
	{
		if (!double.IsFinite(scale) || scale <= 0.0)
		{
			scale = 1.0;
		}
		int val = (int)Math.Round(12.0 * scale);
		int num = Math.Max(1, workRight - workLeft);
		int num2 = Math.Max(1, workBottom - workTop);
		val = Math.Min(val, Math.Max(0, (Math.Min(num, num2) - 1) / 2));
		if (!double.IsFinite(userScale))
		{
			userScale = 1.0;
		}
		double num3 = Math.Min(Math.Clamp(userScale, 0.5, 3.0), Math.Min(((double)num - 2.0 * (double)val) / (450.0 * scale), ((double)num2 - 2.0 * (double)val) / (850.0 * scale)));
		int num4 = Math.Max(1, (int)Math.Floor(450.0 * scale * num3));
		int num5 = Math.Max(1, (int)Math.Floor(850.0 * scale * num3));
		bool flag = (uint)(side - 3) <= 1u;
		bool vertical = flag;
		bool flag2 = side switch
		{
			PanelSide.Left => true, 
			PanelSide.Right => false, 
			_ => ballX < (workLeft + workRight) / 2, 
		};
		int targetX = (flag2 ? (workLeft + val) : (workRight - num4 - val));
		int num6 = workTop + val;
		int offX = (flag2 ? (monitorLeft - num4) : monitorRight);
		return new ControlCenterLayout(targetX, num6, num4, num5, offX, side switch
		{
			PanelSide.Bottom => monitorBottom, 
			PanelSide.Top => monitorTop - num5, 
			_ => num6, 
		}, vertical, flag2);
	}

	public static int RevealX(ControlCenterLayout layout, double progress)
	{
		return layout.X(progress);
	}

	public static int RevealY(ControlCenterLayout layout, double progress)
	{
		return layout.Y(progress);
	}
}
