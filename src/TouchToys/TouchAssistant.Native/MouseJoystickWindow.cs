using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class MouseJoystickWindow : IDisposable
{
	private readonly Action<PadEvent>? testOutput;

	private readonly SettingsStore store;

	private readonly bool right;

	private readonly MouseStick stick = new MouseStick();

	private readonly DispatcherQueueTimer timer;

	private readonly Win32.WndProc callback;

	private readonly nint handle;

	private readonly string cls = "TouchToys.MouseStick." + Guid.NewGuid().ToString("N");

	private readonly MousePassthrough mousePass;

	private Win32.POINT position;

	private double scale = 1.0;

	private double remainderX;

	private double remainderY;

	private bool shown;

	private bool held;

	private bool resetting;

	private string stamp = "";

	private const int GripHeight = 24;

	private readonly ShakeDetector shake = new ShakeDetector();

	private uint? gripPointer;

	private Win32.POINT gripStart;

	private Win32.POINT gripPosition;

	private bool disposed;

	private int GripPixels => (int)Math.Ceiling(24.0 * scale);

	private MouseJoystickOptions Options
	{
		get
		{
			if (!right)
			{
				return store.Value.MouseJoysticks.Pointer;
			}
			return store.Value.MouseJoysticks.RightDrag;
		}
	}

	public event Action<GestureAction, Win32.POINT>? Trigger;

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool UnregisterClass(string name, nint module);

	public MouseJoystickWindow(SettingsStore store, bool right, Action<PadEvent>? testOutput = null)
	{
		MouseJoystickWindow mouseJoystickWindow = this;
		this.testOutput = testOutput;
		this.store = store;
		this.right = right;
		callback = Procedure;
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = callback,
			hInstance = Win32.GetModuleHandle(null),
			lpszClassName = cls
		};
		Win32.RegisterClass(ref wc);
		handle = Win32.CreateWindowEx(134742152u, cls, right ? "右键鼠标摇杆" : "鼠标摇杆", 2147483648u, 0, 0, 80, 80, 0, 0, wc.hInstance, 0);
		Win32.SuppressFeedback(handle);
		TouchMessages.Register(handle);
		OverlayOrder.Register(handle, -5);
		mousePass = new MousePassthrough(handle, () => true, () => mouseJoystickWindow.shown && (mouseJoystickWindow.stick.Active || mouseJoystickWindow.gripPointer.HasValue));
		timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
		timer.Interval = TimeSpan.FromMilliseconds(16.0);
		timer.Tick += delegate
		{
			mouseJoystickWindow.remainderX += mouseJoystickWindow.stick.DX * 12.0 * mouseJoystickWindow.scale;
			mouseJoystickWindow.remainderY += mouseJoystickWindow.stick.DY * 12.0 * mouseJoystickWindow.scale;
			int num = (int)mouseJoystickWindow.remainderX;
			int num2 = (int)mouseJoystickWindow.remainderY;
			mouseJoystickWindow.remainderX -= (double)num;
			mouseJoystickWindow.remainderY -= (double)num2;
			if (testOutput != null)
			{
				testOutput(new PadEvent("Move", num, num2));
			}
			else
			{
				KeyboardInput.Move(num, num2);
			}
		};
		stick.Changed += Changed;
		stick.Saved += delegate
		{
			mouseJoystickWindow.Options.X = mouseJoystickWindow.position.X;
			mouseJoystickWindow.Options.Y = mouseJoystickWindow.position.Y;
			mouseJoystickWindow.Options.Size = mouseJoystickWindow.stick.Size;
			store.Save();
		};
		stick.Tap += delegate
		{
			mouseJoystickWindow.Trigger?.Invoke(mouseJoystickWindow.Options.Tap, new Win32.POINT(mouseJoystickWindow.position.X + (int)(mouseJoystickWindow.Options.Size * mouseJoystickWindow.scale / 2.0), mouseJoystickWindow.position.Y + (int)(mouseJoystickWindow.Options.Size * mouseJoystickWindow.scale / 2.0)));
		};
		store.Changed += Sync;
		Sync();
	}

	private void Sync()
	{
		if (!disposed)
		{
			MouseJoystickOptions options = Options;
			string text = $"{options.X}:{options.Y}:{options.Size}:{options.Opacity}";
			if (!(text == stamp))
			{
				stamp = text;
				Cancel();
				Win32.GetCursorPos(out var p);
				Win32.RECT rcWork = Win32.Monitor(p).rcWork;
				position = new Win32.POINT(options.X ?? (right ? (rcWork.Left + 40) : (rcWork.Right - 160)), options.Y ?? (rcWork.Bottom - 220));
				scale = Win32.Scale(position);
				stick.Size = options.Size;
				Constrain();
				stick.X = (double)position.X / scale + stick.Size / 2.0;
				stick.Y = (double)position.Y / scale + stick.Size / 2.0;
				Draw();
			}
		}
	}

	private void Constrain()
	{
		Win32.RECT rcWork = Win32.Monitor(position).rcWork;
		int num = (int)Math.Ceiling(stick.Size * scale);
		int num2 = rcWork.Top + GripPixels;
		position = new Win32.POINT(Math.Clamp(position.X, rcWork.Left, Math.Max(rcWork.Left, rcWork.Right - num)), Math.Clamp(position.Y, num2, Math.Max(num2, rcWork.Bottom - num)));
	}

	private void Changed()
	{
		if (resetting)
		{
			return;
		}
		position = new Win32.POINT((int)Math.Round((stick.X - stick.Size / 2.0) * scale), (int)Math.Round((stick.Y - stick.Size / 2.0) * scale));
		Constrain();
		stick.X = (double)position.X / scale + stick.Size / 2.0;
		stick.Y = (double)position.Y / scale + stick.Size / 2.0;
		if (right && held != stick.Moving)
		{
			held = stick.Moving;
			if (testOutput != null)
			{
				testOutput(new PadEvent(held ? "Down" : "Up", 0.0, 0.0, 514));
			}
			else
			{
				KeyboardInput.Held(new int[1] { 514 }, held);
			}
		}
		if (stick.Moving)
		{
			timer.Start();
		}
		else
		{
			timer.Stop();
		}
		Draw();
	}

	private void Cancel()
	{
		gripPointer = null;
		shake.Reset(0.0, 0.0, 0L);
		timer.Stop();
		resetting = true;
		try
		{
			stick.Reset();
		}
		finally
		{
			resetting = false;
		}
		if (held)
		{
			held = false;
			if (testOutput != null)
			{
				testOutput(new PadEvent("Up", 0.0, 0.0, 514));
			}
			else
			{
				KeyboardInput.Held(new int[1] { 514 }, down: false);
			}
		}
		remainderX = (remainderY = 0.0);
	}

	public void SetVisible(bool visible)
	{
		if (shown != visible)
		{
			shown = visible;
			if (!visible)
			{
				Cancel();
			}
			Win32.ShowWindow(handle, visible ? 4 : 0);
			if (visible)
			{
				Draw();
				OverlayOrder.Raise(handle);
			}
		}
	}

	private void Point(uint m, uint id, Win32.POINT p, bool cancelled = false)
	{
		if (cancelled || !shown || DesktopVisibility.Locked)
		{
			Cancel();
		}
		else
		{
			if (gripPointer.HasValue && gripPointer != id)
			{
				return;
			}
			if (m == 582 && !stick.Active && testOutput == null)
			{
				MousePassthrough.RestoreMousePosition();
			}
			if (m == 582 && p.Y < position.Y && p.Y >= position.Y - GripPixels && p.X >= position.X && (double)p.X <= (double)position.X + stick.Size * scale)
			{
				Cancel();
				gripPointer = id;
				gripStart = p;
				gripPosition = position;
				shake.Reset((double)p.X / scale, (double)p.Y / scale, Environment.TickCount64);
				Draw();
			}
			else if (gripPointer == id)
			{
				if (m == 581 && Options.ShakeToClose && shake.Add((double)p.X / scale, (double)p.Y / scale, Environment.TickCount64))
				{
					Options.Enabled = false;
					SetVisible(visible: false);
					store.Save();
					return;
				}
				position = new Win32.POINT(gripPosition.X + p.X - gripStart.X, gripPosition.Y + p.Y - gripStart.Y);
				Constrain();
				stick.X = (double)position.X / scale + stick.Size / 2.0;
				stick.Y = (double)position.Y / scale + stick.Size / 2.0;
				Win32.SetWindowPos(handle, 0, position.X, position.Y - GripPixels, 0, 0, 21u);
				if (m == 583)
				{
					gripPointer = null;
					Options.X = position.X;
					Options.Y = position.Y;
					store.Save();
				}
			}
			else
			{
				stick.Feed(m, id, (double)p.X / scale, (double)p.Y / scale, Environment.TickCount64);
			}
		}
	}

	[DllImport("user32")]
	private static extern nint SetCursor(nint cursor);

	private nint Procedure(nint h, uint m, nuint w, nint l)
	{
		try
		{
			if (m == 32 && (stick.Active || gripPointer.HasValue))
			{
				SetCursor(0);
				return 1;
			}
			if ((m == 33 || m == 587) ? true : false)
			{
				return 3;
			}
			bool flag;
			switch (m)
			{
			case 576u:
				TouchMessages.Read(w, l, delegate(uint id, uint f, Win32.POINT p)
				{
					Point(((f & 2) != 0) ? 582u : (((f & 4) != 0) ? 583u : 581u), id, p);
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
				flag = Win32.GetPointerInfo((uint)((int)w & 0xFFFF), out var info);
				if (flag)
				{
					uint pointerType = info.pointerType;
					bool flag2 = pointerType - 2 <= 1;
					flag = flag2;
				}
				if (flag)
				{
					Point(m, info.pointerId, info.ptPixelLocation, (info.pointerFlags & 0x8000) != 0);
				}
				return 0;
			}
			if ((m == 533 || m == 588) ? true : false)
			{
				Cancel();
			}
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "MouseJoystick.Input");
			Cancel();
		}
		return Win32.DefWindowProc(h, m, w, l);
	}

	private void Draw(string? preview = null)
	{
		int num = (int)Math.Ceiling(stick.Size * scale);
		using Bitmap bitmap = new Bitmap(num, num + GripPixels, PixelFormat.Format32bppPArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			using (Pen pen = new Pen(Color.FromArgb(130, 35, 39, 47), (float)(14.0 * scale))
			{
				StartCap = LineCap.Round,
				EndCap = LineCap.Round
			})
			{
				graphics.DrawLine(pen, (float)num / 2f - (float)(9.0 * scale), (float)GripPixels / 2f, (float)num / 2f + (float)(9.0 * scale), (float)GripPixels / 2f);
			}
			using (Pen pen2 = new Pen(Color.FromArgb(210, 255, 255, 255), (float)(3.0 * scale))
			{
				StartCap = LineCap.Round,
				EndCap = LineCap.Round
			})
			{
				graphics.DrawLine(pen2, (float)num / 2f - (float)(8.0 * scale), (float)GripPixels / 2f, (float)num / 2f + (float)(8.0 * scale), (float)GripPixels / 2f);
			}
			graphics.TranslateTransform(0f, GripPixels);
			float num2 = (float)num / 2f;
			using SolidBrush brush = new SolidBrush(Color.FromArgb(100, 35, 39, 47));
			using Pen pen3 = new Pen(Color.FromArgb(130, 255, 255, 255), (float)scale);
			graphics.FillEllipse(brush, 1, 1, num - 2, num - 2);
			graphics.DrawEllipse(pen3, 1, 1, num - 2, num - 2);
			float num3 = num2 * 0.42f;
			float num4 = num2 + (float)stick.DX * num2 * 0.5f;
			float num5 = num2 + (float)stick.DY * num2 * 0.5f;
			using SolidBrush brush2 = new SolidBrush(Color.FromArgb(160, 255, 255, 255));
			graphics.FillEllipse(brush2, num4 - num3, num5 - num3, num3 * 2f, num3 * 2f);
			if (right)
			{
				using Pen pen4 = new Pen(Color.FromArgb(210, 255, 255, 255), (float)(2.0 * scale));
				graphics.DrawArc(pen4, 5, 5, num - 10, num - 10, -80, 160);
			}
		}
		if (preview != null)
		{
			bitmap.Save(preview, ImageFormat.Png);
		}
		nint dC = Win32.GetDC(0);
		nint num6 = Win32.CreateCompatibleDC(dC);
		nint hbitmap = bitmap.GetHbitmap(Color.FromArgb(0));
		nint o = Win32.SelectObject(num6, hbitmap);
		try
		{
			Win32.SIZE size = new Win32.SIZE(num, num + GripPixels);
			Win32.POINT dst = new Win32.POINT(position.X, position.Y - GripPixels);
			Win32.POINT origin = default(Win32.POINT);
			Win32.BLENDFUNCTION blend = new Win32.BLENDFUNCTION
			{
				SourceConstantAlpha = (byte)Math.Round(Options.Opacity * 255.0),
				AlphaFormat = 1
			};
			Win32.UpdateLayeredWindow(handle, dC, ref dst, ref size, num6, ref origin, 0u, ref blend, 2u);
		}
		finally
		{
			Win32.SelectObject(num6, o);
			Win32.DeleteObject(hbitmap);
			Win32.DeleteDC(num6);
			Win32.ReleaseDC(0, dC);
		}
	}

	internal async Task CheckGripAsync()
	{
		if (testOutput == null)
		{
			throw new InvalidOperationException("Grip check requires isolated input recorder");
		}
		Options.Enabled = true;
		Options.ShakeToClose = true;
		SetVisible(visible: true);
		Win32.POINT p = new Win32.POINT(position.X + (int)(stick.Size * scale / 2.0), position.Y + (int)(stick.Size * scale / 2.0));
		Point(582u, 301u, p);
		Point(581u, 301u, new Win32.POINT(p.X + 25, p.Y));
		await Task.Delay(40);
		Win32.POINT p2 = new Win32.POINT(position.X + (int)(stick.Size * scale / 2.0), position.Y - GripPixels / 2);
		int x = position.X;
		int y = position.Y;
		Point(582u, 302u, p2);
		if (timer.IsRunning || held || stick.Active)
		{
			throw new InvalidOperationException("拖动横条没有释放鼠标输入");
		}
		Win32.POINT p3 = new Win32.POINT(p2.X + 30, p2.Y + 20);
		Point(581u, 302u, p3);
		Point(583u, 302u, p3);
		if (position.X != x + 30 || position.Y != y + 20 || Options.X != position.X || Options.Y != position.Y)
		{
			throw new InvalidOperationException("拖动横条没有保存位置");
		}
		Draw(Path.Combine(AppContext.BaseDirectory, right ? "mouse-grip-right.png" : "mouse-grip-pointer.png"));
		p2 = new Win32.POINT(position.X + (int)(stick.Size * scale / 2.0), position.Y - GripPixels / 2);
		Options.ShakeToClose = false;
		Point(582u, 303u, p2);
		int[] array = new int[4] { 40, -40, 40, -40 };
		foreach (int num in array)
		{
			Point(581u, 303u, new Win32.POINT(p2.X + (int)((double)num * scale), p2.Y));
		}
		if (!shown || !Options.Enabled)
		{
			throw new InvalidOperationException("关闭晃动开关后仍触发关闭");
		}
		Point(583u, 303u, p2);
		Options.ShakeToClose = true;
		p2 = new Win32.POINT(position.X + (int)(stick.Size * scale / 2.0), position.Y - GripPixels / 2);
		Point(582u, 304u, p2);
		array = new int[4] { 40, -40, 40, -40 };
		foreach (int num2 in array)
		{
			Point(581u, 304u, new Win32.POINT(p2.X + (int)((double)num2 * scale), p2.Y));
		}
		if (shown || Options.Enabled || held || timer.IsRunning || gripPointer.HasValue)
		{
			throw new InvalidOperationException("晃动关闭后没有完整释放摇杆");
		}
	}

	internal async Task CheckAsync()
	{
		Win32.GetCursorPos(out var p);
		Win32.RECT rcWork = Win32.Monitor(p).rcWork;
		Options.X = rcWork.Left + rcWork.Width / 2;
		Options.Y = rcWork.Top + rcWork.Height / 2;
		Sync();
		SetVisible(visible: true);
		Win32.POINT center = new Win32.POINT(position.X + (int)(stick.Size * scale / 2.0), position.Y + (int)(stick.Size * scale / 2.0));
		Point(582u, 101u, center);
		Point(581u, 101u, new Win32.POINT(center.X + 25, center.Y));
		await Task.Delay(60);
		if (!timer.IsRunning || (right && !held))
		{
			throw new InvalidOperationException("Joystick motion/hold missing");
		}
		Point(582u, 102u, new Win32.POINT(center.X - 15, center.Y));
		if (timer.IsRunning || held)
		{
			throw new InvalidOperationException("Two-finger edit did not release input");
		}
		int x = position.X;
		Point(581u, 101u, new Win32.POINT(center.X + 65, center.Y));
		Point(581u, 102u, new Win32.POINT(center.X + 25, center.Y));
		if (position.X == x)
		{
			throw new InvalidOperationException("Joystick did not move independently");
		}
		SetVisible(visible: false);
		if (timer.IsRunning || held || stick.Active)
		{
			throw new InvalidOperationException("Hidden joystick retains input");
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			Cancel();
			store.Changed -= Sync;
			mousePass.Dispose();
			OverlayOrder.Remove(handle);
			Win32.DestroyWindow(handle);
			UnregisterClass(cls, Win32.GetModuleHandle(null));
		}
	}
}
