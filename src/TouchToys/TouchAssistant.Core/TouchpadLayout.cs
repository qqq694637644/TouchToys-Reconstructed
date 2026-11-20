using System.Drawing;

namespace TouchAssistant.Core;

internal readonly struct TouchpadLayout
{
	public readonly RectangleF LeftButton;

	public readonly RectangleF RightButton;

	public readonly RectangleF MiddleButton;

	public readonly RectangleF Surface;

	public TouchpadLayout(double width, double height, string placement)
	{
		float num = (float)width;
		float num2 = (float)height;
		if ((placement == "Left" || placement == "Right") ? true : false)
		{
			float num3 = ((placement == "Left") ? 0f : (num - 96f));
			float num4 = (num2 - 30f) / 2f;
			LeftButton = new RectangleF(num3, 30f, 64f, num4);
			RightButton = new RectangleF(num3, 30f + num4, 64f, num4);
			MiddleButton = new RectangleF(num3 + 64f, 30f, 32f, num2 - 30f);
			Surface = new RectangleF((placement == "Left") ? 96 : 0, 30f, num - 96f, num2 - 30f);
		}
		else
		{
			float y = ((placement == "Top") ? 30f : (num2 - 64f));
			float num5 = 36f;
			float num6 = (num - num5) / 2f;
			LeftButton = new RectangleF(0f, y, num6, 64f);
			MiddleButton = new RectangleF(num6, y, num5, 64f);
			RightButton = new RectangleF(num6 + num5, y, num6, 64f);
			Surface = new RectangleF(0f, (placement == "Top") ? 94 : 30, num, num2 - 94f);
		}
	}

	public int Hit(double x, double y)
	{
		if (!MiddleButton.Contains((float)x, (float)y))
		{
			if (!LeftButton.Contains((float)x, (float)y))
			{
				if (!RightButton.Contains((float)x, (float)y))
				{
					return 0;
				}
				return 514;
			}
			return 513;
		}
		return 515;
	}
}
