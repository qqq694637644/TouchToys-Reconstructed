using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using TouchAssistant.Models;
using TouchAssistant.Services;
using TouchAssistant.UI;
using Windows.UI;

namespace TouchAssistant.Native;

internal static class WheelDrawing
{
	internal static string ResolveLabel(GestureAction action)
	{
		return string.IsNullOrWhiteSpace(action.Label) ? ActionNames.Describe(action) : action.Label;
	}

	public static void Draw(Graphics g, IReadOnlyList<GestureAction> actions, Bitmap?[] customIcons, int selected, double wheelProgress = 1.0)
	{
		for (int i = 0; i < 8; i++)
		{
			using GraphicsPath graphicsPath = new GraphicsPath();
			float num = -112.5f + (float)(i * 45);
			graphicsPath.AddArc(-108, -108, 216, 216, num + 1f, 43f);
			graphicsPath.AddArc(-50, -50, 100, 100, num + 42.5f, -40f);
			graphicsPath.CloseFigure();
			Windows.UI.Color color = AccentTheme.Color;
			using SolidBrush brush = new SolidBrush((i == selected) ? System.Drawing.Color.FromArgb((int)(255.0 * wheelProgress), color.R, color.G, color.B) : System.Drawing.Color.FromArgb((int)(245.0 * wheelProgress), 92, 92, 92));
			g.FillPath(brush, graphicsPath);
			GestureAction gestureAction = actions[i];
			string label = ResolveLabel(gestureAction);
			if ((gestureAction.AppearanceDestination & 1) == 0)
			{
				continue;
			}
			string icon = gestureAction.Icon;
			bool flag = customIcons[i] != null || icon.Length > 0;
			double num2 = (double)(-90 + i * 45) * Math.PI / 180.0;
			float num3 = (float)(79.0 * Math.Cos(num2));
			float num4 = (float)(79.0 * Math.Sin(num2));
			float num5 = num4 - (float)((label.Length > 0) ? 6 : 0);
			using SolidBrush brush2 = new SolidBrush(System.Drawing.Color.FromArgb((int)(255.0 * wheelProgress), System.Drawing.Color.White));
			using StringFormat stringFormat = new StringFormat
			{
				Alignment = StringAlignment.Center,
				LineAlignment = StringAlignment.Center
			};
			Bitmap bitmap = customIcons[i];
			if (bitmap != null)
			{
				using ImageAttributes imageAttributes = new ImageAttributes();
				imageAttributes.SetColorMatrix(new ColorMatrix
				{
					Matrix33 = (float)wheelProgress
				});
				float num6 = Math.Min(24f / (float)bitmap.Width, 24f / (float)bitmap.Height);
				Rectangle destRect = Rectangle.Round(new RectangleF(num3 - (float)bitmap.Width * num6 / 2f, num5 - (float)bitmap.Height * num6 / 2f, (float)bitmap.Width * num6, (float)bitmap.Height * num6));
				g.DrawImage(bitmap, destRect, 0, 0, bitmap.Width, bitmap.Height, GraphicsUnit.Pixel, imageAttributes);
			}
			else if (icon.Length > 0)
			{
				using FontFamily family = new FontFamily(gestureAction.IconFont);
				using GraphicsPath graphicsPath2 = new GraphicsPath();
				graphicsPath2.AddString(icon, family, 0, 23f, new PointF(num3, num5), stringFormat);
				g.FillPath(brush2, graphicsPath2);
			}
			if (label.Length <= 0)
			{
				continue;
			}
			using FontFamily family2 = new FontFamily("Segoe UI");
			using GraphicsPath graphicsPath3 = new GraphicsPath();
			stringFormat.Trimming = StringTrimming.EllipsisCharacter;
			stringFormat.FormatFlags = StringFormatFlags.NoWrap;
			using SolidBrush brush3 = new SolidBrush(System.Drawing.Color.FromArgb((int)(255.0 * wheelProgress * gestureAction.TextOpacity), ColorTranslator.FromHtml(gestureAction.TextColor)));
			graphicsPath3.AddString(label, family2, 0, 10f, new RectangleF(num3 - 25f, flag ? (num4 + 6f) : (num4 - 7f), 50f, 15f), stringFormat);
			g.FillPath(brush3, graphicsPath3);
		}
	}
}
