using System;
using System.Drawing;
using System.Runtime.InteropServices;
using TouchAssistant.Models;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class GamepadShield : IDisposable
{
	private struct Paint
	{
		public nint Dc;

		public int Erase;

		public Win32.RECT Rect;

		public int Restore;

		public int Update;

		[MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
		public byte[] Reserved;
	}

	private readonly GamepadShieldSettings settings;

	private readonly Win32.WndProc callback;

	private readonly string cls;

	internal readonly nint Handle;

	private Win32.POINT position;

	private Win32.POINT start;

	private Win32.POINT initial;

	private uint pointer;

	private bool dragging;

	private bool editing;

	private bool resizing;

	private int initialWidth;

	private int initialHeight;

	private int width;

	private int height;

	[DllImport("user32")]
	private static extern nint BeginPaint(nint h, out Paint p);

	[DllImport("user32")]
	private static extern bool EndPaint(nint h, ref Paint p);

	[DllImport("user32")]
	private static extern bool InvalidateRect(nint h, nint rect, bool erase);

	[DllImport("user32")]
	private static extern bool SetLayeredWindowAttributes(nint h, uint color, byte alpha, uint flags);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool UnregisterClass(string name, nint module);

	public GamepadShield(GamepadShieldSettings settings, int index = 0)
	{
		this.settings = settings;
		Win32.GetCursorPos(out var p);
		Win32.RECT rcWork = Win32.Monitor(p).rcWork;
		double num = Win32.Scale(p);
		width = Math.Min(rcWork.Width, (int)(settings.Width * num));
		height = Math.Min(rcWork.Height, (int)(settings.Height * num));
		position = new Win32.POINT(settings.X ?? (rcWork.Left + (rcWork.Width - width) / 2 + (int)((double)(index * 36) * num)), settings.Y ?? (rcWork.Top + (rcWork.Height - height) / 2 + (int)((double)(index * 36) * num)));
		Constrain();
		callback = Procedure;
		cls = "TouchToys.GamepadShield." + Guid.NewGuid().ToString("N");
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = callback,
			hInstance = Win32.GetModuleHandle(null),
			hCursor = Win32.ArrowCursor,
			lpszClassName = cls
		};
		Win32.RegisterClass(ref wc);
		Handle = Win32.CreateWindowEx(134742152u, cls, "", 2147483648u, position.X, position.Y, width, height, 0, 0, wc.hInstance, 0);
		TouchMessages.Register(Handle);
		Win32.SuppressFeedback(Handle);
		SetEditing(value: false);
		OverlayOrder.Register(Handle, -20);
	}

	private void Constrain()
	{
		Win32.RECT rcWork = Win32.Monitor(position).rcWork;
		width = Math.Min(width, rcWork.Width);
		height = Math.Min(height, rcWork.Height);
		double num = Win32.Scale(position);
		settings.Width = (double)width / num;
		settings.Height = (double)height / num;
		position = new Win32.POINT(Math.Clamp(position.X, rcWork.Left, Math.Max(rcWork.Left, rcWork.Right - width)), Math.Clamp(position.Y, rcWork.Top, Math.Max(rcWork.Top, rcWork.Bottom - height)));
		settings.X = position.X;
		settings.Y = position.Y;
	}

	public void SetVisible(bool visible)
	{
		if (visible && DesktopVisibility.CanShow(Handle))
		{
			Win32.SetWindowPos(Handle, -1, position.X, position.Y, width, height, 80u);
		}
		else
		{
			Win32.ShowWindow(Handle, 0);
		}
		dragging = false;
		if (visible)
		{
			OverlayOrder.Raise(Handle);
		}
	}

	public void SetEditing(bool value)
	{
		editing = value;
		dragging = false;
		SetLayeredWindowAttributes(Handle, 0u, (byte)((!value) ? 1 : 55), 2u);
		InvalidateRect(Handle, 0, erase: false);
	}

	internal void Point(uint msg, uint id, Win32.POINT p)
	{
		if (!editing)
		{
			return;
		}
		if (msg == 582 && !dragging)
		{
			dragging = true;
			Win32.SetWindowPos(Handle, -1, 0, 0, 0, 0, 19u);
			pointer = id;
			start = p;
			initial = position;
			initialWidth = width;
			initialHeight = height;
			double num = Win32.Scale(position);
			resizing = (double)(p.X - position.X) > (double)width - 32.0 * num && (double)(p.Y - position.Y) > (double)height - 32.0 * num;
		}
		if (dragging && pointer == id)
		{
			if (resizing)
			{
				double num2 = Win32.Scale(position);
				width = Math.Clamp(initialWidth + p.X - start.X, (int)(100.0 * num2), (int)(3840.0 * num2));
				height = Math.Clamp(initialHeight + p.Y - start.Y, (int)(80.0 * num2), (int)(2160.0 * num2));
				settings.Width = (double)width / num2;
				settings.Height = (double)height / num2;
			}
			else
			{
				position = new Win32.POINT(initial.X + p.X - start.X, initial.Y + p.Y - start.Y);
			}
			Constrain();
			Win32.SetWindowPos(Handle, 0, position.X, position.Y, width, height, 20u);
			InvalidateRect(Handle, 0, erase: false);
			if (msg == 583)
			{
				dragging = false;
			}
		}
	}

	private nint Procedure(nint h, uint msg, nuint w, nint l)
	{
		if ((msg == 33 || msg == 587) ? true : false)
		{
			return 3;
		}
		bool flag;
		switch (msg)
		{
		case 132u:
		{
			int num = (int)((long)l & 0xFFFFL);
			if (num >= 32768)
			{
				num -= 65536;
			}
			int num2 = (int)(((long)l >> 16) & 0xFFFF);
			if (num2 >= 32768)
			{
				num2 -= 65536;
			}
			int num3 = num - position.X;
			int num4 = num2 - position.Y;
			double num5 = Win32.Scale(position);
			bool flag2 = (double)num4 <= 40.0 * num5;
			bool flag3 = (double)num3 > (double)width - 32.0 * num5 && (double)num4 > (double)height - 32.0 * num5;
			if (editing)
			{
				nint settingsHandle = WindowCatalog.SettingsHandle;
				if (settingsHandle != 0 && Win32.IsWindowVisible(settingsHandle) && Win32.GetWindowRect(settingsHandle, out var r) && num >= r.Left && num < r.Right && num2 >= r.Top && num2 < r.Bottom && !flag2 && !flag3)
				{
					return -1;
				}
			}
			if (editing && (flag2 || flag3))
			{
				return 1;
			}
			if (editing && (double)num4 > 40.0 * num5 && (double)num3 > 8.0 * num5 && (double)num3 < (double)width - 8.0 * num5 && (double)num4 < (double)height - 8.0 * num5)
			{
				return -1;
			}
			return 1;
		}
		case 15u:
		{
			Paint p;
			nint hdc = BeginPaint(h, out p);
			try
			{
				using Graphics graphics = Graphics.FromHdc(hdc);
				graphics.Clear(Color.Black);
				if (editing)
				{
					using Pen pen = new Pen(Color.White, 3f);
					graphics.DrawRectangle(pen, 2, 2, width - 4, height - 4);
					graphics.DrawLine(pen, width - 26, height - 6, width - 6, height - 26);
					using Font font = new Font("Microsoft YaHei UI", 18f, GraphicsUnit.Pixel);
					graphics.DrawString("防误触区域 · 拖动调整位置", font, Brushes.White, 14f, 14f);
				}
			}
			finally
			{
				EndPaint(h, ref p);
			}
			return 0;
		}
		case 576u:
			TouchMessages.Read(w, l, delegate(uint id, uint flags, Win32.POINT p3)
			{
				Point(((flags & 2) != 0) ? 582u : (((flags & 4) != 0) ? 583u : 581u), id, p3);
			});
			return 0;
		case 581u:
		case 582u:
		case 583u:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			if (Win32.GetPointerInfo((uint)((int)w & 0xFFFF), out var info))
			{
				if ((info.pointerFlags & 0x8000) != 0)
				{
					dragging = false;
				}
				else
				{
					Point(msg, info.pointerId, info.ptPixelLocation);
				}
			}
			return 0;
		}
		if (msg - 512 <= 2)
		{
			if (((ulong)Win32.GetMessageExtraInfo() & 0xFFFFFF00uL) == 4283520768u)
			{
				return 0;
			}
			Win32.GetCursorPos(out var p2);
			if (msg == 513)
			{
				Win32.SetCapture(h);
				Point(582u, 0u, p2);
			}
			else if (dragging)
			{
				Point((msg == 514) ? 583u : 581u, 0u, p2);
				if (msg == 514)
				{
					Win32.ReleaseCapture();
				}
			}
			return 0;
		}
		switch (msg)
		{
		case 516u:
		case 517u:
		case 519u:
		case 520u:
		case 522u:
		case 526u:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return 0;
		}
		if ((msg == 533 || msg == 588) ? true : false)
		{
			dragging = false;
		}
		return Win32.DefWindowProc(h, msg, w, l);
	}

	internal void CheckResizeAndMove()
	{
		SetEditing(value: true);
		Win32.POINT p = new Win32.POINT(position.X + width - 4, position.Y + height - 4);
		Point(582u, 77u, p);
		Point(581u, 77u, new Win32.POINT(p.X + 10000, p.Y + 10000));
		Point(583u, 77u, new Win32.POINT(p.X + 10000, p.Y + 10000));
		Win32.RECT rcWork = Win32.Monitor(position).rcWork;
		if (position.X + width > rcWork.Right || position.Y + height > rcWork.Bottom)
		{
			throw new InvalidOperationException("Shield resize handle left the display");
		}
		p = new Win32.POINT(position.X + width - 4, position.Y + height - 4);
		Point(582u, 78u, p);
		Point(581u, 78u, new Win32.POINT(p.X - width / 2, p.Y - height / 2));
		Point(583u, 78u, new Win32.POINT(p.X - width / 2, p.Y - height / 2));
		Win32.POINT pOINT = position;
		p = new Win32.POINT(position.X + 24, position.Y + 20);
		Point(582u, 79u, p);
		Point(581u, 79u, new Win32.POINT(p.X + 20, p.Y + 20));
		Point(583u, 79u, new Win32.POINT(p.X + 20, p.Y + 20));
		if (pOINT.X == position.X && pOINT.Y == position.Y)
		{
			throw new InvalidOperationException("Shield cannot move after resizing");
		}
	}

	public void Dispose()
	{
		OverlayOrder.Remove(Handle);
		Win32.DestroyWindow(Handle);
		UnregisterClass(cls, Win32.GetModuleHandle(null));
	}
}
