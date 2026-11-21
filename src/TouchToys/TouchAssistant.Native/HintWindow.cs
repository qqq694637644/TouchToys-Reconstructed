using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using TouchAssistant.Models;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class HintWindow : IDisposable
{
	private const int Shadow = 14;

	private readonly nint handle;

	private readonly string cls = "TouchToys.Hint." + Guid.NewGuid().ToString("N");

	private readonly Win32.WndProc callback;

	private readonly DispatcherQueueTimer expiry;

	private readonly DispatcherQueueTimer animation;

	private string text = "";

	private string glyph = "";

	private string glyphFont = IconFontResolver.PreferredFontFamilyName;

	private string iconPath = "";

	private string previousText = "";

	private string previousGlyph = "";

	private string previousGlyphFont = IconFontResolver.PreferredFontFamilyName;

	private string previousIconPath = "";

	private long animationStart;

	private bool shown;

	private bool disposed;

	private bool scrolling;

	private double currentPanelWidth;

	private double targetPanelWidth;

	private double fromPanelWidth;

	private int width;

	private int height;

	private double scale = 1.0;

	private Win32.POINT anchor;

	internal bool IsShown => shown;

	internal string RenderInfo => $"text={text};glyph={glyph};glyphFont={glyphFont};iconPath={iconPath}";

	private static bool Dark
	{
		get
		{
			if (Environment.GetEnvironmentVariable("TOUCHTOYS_HINT_DARK") == "1")
			{
				return true;
			}
			try
			{
				using RegistryKey registryKey = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
				return Convert.ToInt32(registryKey?.GetValue("SystemUsesLightTheme", 1)) == 0;
			}
			catch
			{
				return false;
			}
		}
	}

	internal event Action? Hidden;

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool UnregisterClass(string name, nint module);

	public HintWindow()
	{
		callback = (nint h, uint m, nuint w, nint l) => (m != 132) ? Win32.DefWindowProc(h, m, w, l) : (-1);
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = callback,
			hInstance = Win32.GetModuleHandle(null),
			lpszClassName = cls
		};
		Win32.RegisterClass(ref wc);
		handle = Win32.CreateWindowEx(134742184u, cls, "", 2147483648u, 0, 0, 1, 1, 0, 0, wc.hInstance, 0);
		Win32.SuppressFeedback(handle);
		OverlayOrder.Register(handle);
		expiry = DispatcherQueue.GetForCurrentThread().CreateTimer();
		expiry.IsRepeating = false;
		expiry.Tick += delegate
		{
			Hide();
		};
		animation = DispatcherQueue.GetForCurrentThread().CreateTimer();
		animation.Interval = TimeSpan.FromMilliseconds(16.0);
		animation.IsRepeating = true;
		animation.Tick += delegate
		{
			Tick();
		};
	}

	internal void Show(GestureAction action, Win32.POINT origin, double seconds = 0.0, bool showIcon = false)
	{
		if (!disposed)
		{
			expiry.Stop();
			if (shown)
			{
				previousText = text;
				previousGlyph = glyph;
				previousGlyphFont = glyphFont;
				previousIconPath = iconPath;
			}
			else
			{
				previousText = (previousGlyph = (previousIconPath = ""));
				previousGlyphFont = IconFontResolver.PreferredFontFamilyName;
			}
			text = ((action.Label.Length > 0) ? action.Label : ActionNames.Describe(action));
			glyph = (showIcon ? (action.Icon ?? "") : "");
			glyphFont = showIcon ? IconFontResolver.NormalizeIconFont(action.IconFont, glyph) : IconFontResolver.PreferredFontFamilyName;
			iconPath = (showIcon ? (action.IconPath ?? "") : "");
			scale = Win32.Scale(origin);
			fromPanelWidth = (shown ? currentPanelWidth : (64.0 * scale));
			targetPanelWidth = Math.Max(MeasurePanelWidth(text, glyph, iconPath), (previousText.Length > 0) ? MeasurePanelWidth(previousText, previousGlyph, previousIconPath) : 0);
			Win32.RECT rcMonitor = Win32.Monitor(origin).rcMonitor;
			anchor = new Win32.POINT(rcMonitor.Left + rcMonitor.Width / 2, rcMonitor.Top + rcMonitor.Height / 3 - (int)(24.0 * scale));
			shown = true;
			animationStart = Environment.TickCount64;
			scrolling = true;
			animation.Start();
			Render(0.0);
			Win32.SetWindowPos(handle, -1, 0, 0, 0, 0, 19u);
			Win32.ShowWindow(handle, 4);
			OverlayOrder.Raise(handle);
			if (seconds > 0.0)
			{
				expiry.Interval = TimeSpan.FromSeconds(seconds);
				expiry.Start();
			}
		}
	}

	private int MeasurePanelWidth(string content, string icon, string iconPath)
	{
		int num = Math.Max(11, (int)Math.Round(13.0 * scale));
		int num2 = Math.Max(14, (int)Math.Round(18.0 * scale));
		using Font font = IconFontResolver.CreateTextFont("Microsoft YaHei UI", num, GraphicsUnit.Pixel, "Microsoft YaHei", "DengXian", "Segoe UI");
		using Graphics graphics = Graphics.FromImage(new Bitmap(1, 1));
		using StringFormat stringFormat = new StringFormat
		{
			FormatFlags = StringFormatFlags.NoWrap
		};
		return (int)Math.Ceiling(graphics.MeasureString(content, font, new PointF(0f, 0f), stringFormat).Width + (float)((icon.Length > 0 || iconPath.Length > 0) ? (num2 + 10) : 0) + 40f);
	}

	private void Render(double progress)
	{
		bool dark = Dark;
		int num = Math.Max(11, (int)Math.Round(13.0 * scale));
		int num2 = Math.Max(14, (int)Math.Round(18.0 * scale));
		using Font font = IconFontResolver.CreateTextFont("Microsoft YaHei UI", num, GraphicsUnit.Pixel, "Microsoft YaHei", "DengXian", "Segoe UI");
		using Font iconFont = IconFontResolver.CreateGdiFont(glyph, num2, GraphicsUnit.Pixel, glyphFont);
		using Font previousIconFont = IconFontResolver.CreateGdiFont(previousGlyph, num2, GraphicsUnit.Pixel, previousGlyphFont);
		using Graphics graphics = Graphics.FromImage(new Bitmap(1, 1));
		using StringFormat stringFormat = new StringFormat
		{
			Alignment = StringAlignment.Near,
			LineAlignment = StringAlignment.Center,
			FormatFlags = StringFormatFlags.NoWrap,
			Trimming = StringTrimming.EllipsisCharacter
		};
		graphics.MeasureString(text, font, new PointF(0f, 0f), stringFormat);
		double num3 = Math.Clamp(progress, 0.0, 1.0);
		num3 = num3 * num3 * (3.0 - 2.0 * num3);
		int num4 = (int)Math.Round(fromPanelWidth + (targetPanelWidth - fromPanelWidth) * num3);
		currentPanelWidth = num4;
		int num5 = (int)Math.Ceiling(44.0 * scale);
		int num6 = (int)Math.Ceiling(14.0 * scale);
		width = num4 + num6 * 2;
		height = num5 + num6 * 2;
		using Bitmap bitmap = new Bitmap(width, height, PixelFormat.Format32bppPArgb);
		using (Graphics graphics2 = Graphics.FromImage(bitmap))
		{
			graphics2.SmoothingMode = SmoothingMode.AntiAlias;
			graphics2.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
			graphics2.Clear(Color.Transparent);
			RectangleF r = new RectangleF(num6, num6, num4, num5);
			float num7 = (float)num5 / 2f;
			for (int num8 = 8; num8 >= 1; num8--)
			{
				float num9 = (float)(num6 * num8) / 8f;
				using GraphicsPath path = Round(new RectangleF((float)num6 - num9, (float)num6 - num9, (float)num4 + num9 * 2f, (float)num5 + num9 * 2f), num7 + num9);
				using SolidBrush brush = new SolidBrush(Color.FromArgb(4, 0, 0, 0));
				graphics2.FillPath(brush, path);
			}
			using (GraphicsPath path2 = Round(r, num7))
			{
				using SolidBrush brush2 = new SolidBrush(dark ? Color.FromArgb(255, 28, 28, 32) : Color.FromArgb(255, 224, 224, 228));
				graphics2.FillPath(brush2, path2);
				using Pen pen = new Pen(dark ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(60, 0, 0, 0), 1f);
				graphics2.DrawPath(pen, path2);
			}
			float num10 = (float)num6 + (float)num5 / 2f + (float)scale;
			float num11 = 30f * (float)scale;
			if (previousText.Length > 0)
			{
				DrawContent(graphics2, previousText, previousGlyph, previousIconPath, num10 - num11 * (float)progress, 255 - (int)Math.Round(255.0 * progress), num4, num6, num2, font, previousIconFont, stringFormat, dark);
			}
			DrawContent(graphics2, text, glyph, iconPath, num10 + num11 * (1f - (float)progress), (int)Math.Round(255.0 * progress), num4, num6, num2, font, iconFont, stringFormat, dark);
		}
		nint dC = Win32.GetDC(0);
		nint num12 = Win32.CreateCompatibleDC(dC);
		nint hbitmap = bitmap.GetHbitmap(Color.FromArgb(0));
		nint o = Win32.SelectObject(num12, hbitmap);
		try
		{
			Win32.SIZE size = new Win32.SIZE(width, height);
			Win32.POINT dst = new Win32.POINT(anchor.X - width / 2, anchor.Y - num6);
			Win32.POINT origin = default(Win32.POINT);
			Win32.BLENDFUNCTION blend = new Win32.BLENDFUNCTION
			{
				SourceConstantAlpha = byte.MaxValue,
				AlphaFormat = 1
			};
			Win32.UpdateLayeredWindow(handle, dC, ref dst, ref size, num12, ref origin, 0u, ref blend, 2u);
		}
		finally
		{
			Win32.SelectObject(num12, o);
			Win32.DeleteObject(hbitmap);
			Win32.DeleteDC(num12);
			Win32.ReleaseDC(0, dC);
		}
	}

	private static void DrawContent(Graphics g, string content, string icon, string iconPath, float centerY, int alpha, int panelWidth, int margin, int iconPx, Font font, Font iconFont, StringFormat format, bool dark)
	{
		if (string.IsNullOrEmpty(content) || alpha <= 0)
		{
			return;
		}
		using SolidBrush brush = new SolidBrush(dark ? Color.FromArgb(alpha, 255, 255, 255) : Color.FromArgb(alpha, 24, 24, 26));
		using Graphics graphics = Graphics.FromImage(new Bitmap(1, 1));
		SizeF sizeF = graphics.MeasureString(content, font, new PointF(0f, 0f), format);
		float num = margin + 20;
		if (iconPath.Length > 0)
		{
			try
			{
				using Image image = Image.FromFile(iconPath);
				g.DrawImage(image, num, centerY - (float)iconPx / 2f, iconPx, iconPx);
				num += (float)(iconPx + 10);
			}
			catch
			{
			}
		}
		else if (icon.Length > 0)
		{
			g.DrawString(icon, iconFont, brush, new RectangleF(num, centerY - (float)iconPx / 2f, iconPx + 4, iconPx), format);
			num += (float)(iconPx + 10);
		}
		g.DrawString(content, font, brush, new RectangleF(num, centerY - Math.Max(1f, sizeF.Height) / 2f, (float)panelWidth - (num - (float)margin) - 10f, Math.Max(1f, sizeF.Height)), format);
	}

	private void Tick()
	{
		if (scrolling)
		{
			double num = Math.Clamp((double)(Environment.TickCount64 - animationStart) / 280.0, 0.0, 1.0);
			double progress = num * num * (3.0 - 2.0 * num);
			Render(progress);
			if (num >= 1.0)
			{
				scrolling = false;
				previousText = (previousGlyph = (previousIconPath = ""));
				previousGlyphFont = IconFontResolver.PreferredFontFamilyName;
				animation.Stop();
				Render(1.0);
			}
		}
	}

	private static GraphicsPath Round(RectangleF r, float radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		float num = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
		graphicsPath.AddArc(r.Left, r.Top, num, num, 180f, 90f);
		graphicsPath.AddArc(r.Right - num, r.Top, num, num, 270f, 90f);
		graphicsPath.AddArc(r.Right - num, r.Bottom - num, num, num, 0f, 90f);
		graphicsPath.AddArc(r.Left, r.Bottom - num, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	internal void Hide()
	{
		if (!disposed)
		{
			expiry.Stop();
			animation.Stop();
			scrolling = false;
			previousText = (previousGlyph = (previousIconPath = ""));
			previousGlyphFont = IconFontResolver.PreferredFontFamilyName;
			bool num = shown;
			shown = false;
			Win32.ShowWindow(handle, 0);
			if (num)
			{
				this.Hidden?.Invoke();
			}
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			Hide();
			OverlayOrder.Remove(handle);
			Win32.DestroyWindow(handle);
			UnregisterClass(cls, Win32.GetModuleHandle(null));
		}
	}
}
