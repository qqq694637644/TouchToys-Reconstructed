using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class VirtualTouchpad : IDisposable
{
	private readonly MousePassthrough mousePass;

	private readonly SettingsStore store;

	private readonly Win32.WndProc callback;

	private readonly nint handle;

	private readonly string cls;

	private readonly IdleOpacity fade;

	private readonly Action<PadEvent> dispatch;

	private readonly TouchpadGesture gesture;

	private readonly Dictionary<uint, int> buttons = new Dictionary<uint, int>();

	private Win32.POINT position;

	private Win32.POINT handleStart;

	private Win32.POINT positionStart;

	private uint? handlePointer;

	private bool shown;

	private double wheelRemainder;

	private string stamp = "";

	private Bitmap? surface;

	private nint surfaceBitmap;

	private int surfaceWidth;

	private int surfaceHeight;

	private double surfaceScale;

	private string surfaceStamp = "";

	private readonly DispatcherQueue queue;

	private readonly DispatcherQueueTimer gestureTimer;

	private readonly TouchpadMiddleGesture middle;

	private uint? middlePointer;

	private double middleSteps;

	private string middleDirection = "";

	private double moveX;

	private double moveY;

	private double horizontalWheel;

	private const int ButtonHeight = 64;

	private readonly ShakeDetector shake = new ShakeDetector();

	private readonly HashSet<uint> touches = new HashSet<uint>();

	private readonly TouchpadCursor? cursorOverlay;

	private bool resizing;

	private bool cursorSaved;

	private Win32.POINT cursorBeforeTouch;

	private double initialWidth;

	private double initialHeight;

	private bool disposed;

	private TouchpadLayout Layout => new TouchpadLayout(Options.Width, Options.Height, Options.EffectiveButtonPlacement);

	private double ButtonTop => Layout.LeftButton.Top;

	private VirtualTouchpadSettings Options => store.Value.VirtualTouchpad;

	private double Scale => Win32.Scale(position);

	private int Width => (int)Math.Round(Options.Width * Scale);

	private int Height => (int)Math.Round(Options.Height * Scale);

	private string SurfaceStamp => $"{Width}x{Height}@{Scale:0.###}:{Options.Width:0.##}:{Options.Height:0.##}:{Options.EffectiveButtonPlacement}";

	public event Action<GestureAction>? Trigger;

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool UnregisterClass(string name, nint module);

	private bool Bound(int count, string kind)
	{
		return Options.Gestures.Any((MultiTouchBinding b) => b.Fingers == count && b.Gesture == kind && b.Action.Enabled && b.Action.Kind != ActionKind.None);
	}

	private bool MiddleBound(string kind)
	{
		if (Options.MiddleActions.TryGetValue(kind, out GestureAction value) && value.Enabled)
		{
			return value.Kind != ActionKind.None;
		}
		return false;
	}

	private void Run(GestureAction action)
	{
		if (!action.Enabled || action.Kind == ActionKind.None)
		{
			return;
		}
		queue.TryEnqueue(delegate
		{
			if (!disposed && shown && !DesktopVisibility.Locked)
			{
				this.Trigger?.Invoke(action);
			}
		});
	}

	private void RunGesture(int count, string kind)
	{
		MultiTouchBinding multiTouchBinding = Options.Gestures.FirstOrDefault((MultiTouchBinding b) => b.Fingers == count && b.Gesture == kind);
		if (multiTouchBinding != null)
		{
			Run(multiTouchBinding.Action);
		}
	}

	private void MiddleEvent(string kind, double distance)
	{
		if (!Options.MiddleActions.TryGetValue(kind, out GestureAction value) || !value.Enabled || value.Kind == ActionKind.None)
		{
			return;
		}
		if ((kind == "Up" || kind == "Down") ? true : false)
		{
			int num = ((kind == "Up") ? 516 : 517);
			if (value.Kind == ActionKind.Keys && value.Keys.Count == 1 && value.Keys[0] == num)
			{
				dispatch(new PadEvent("Wheel", 0.0, distance * (double)((kind == "Up") ? 3 : (-3))));
				return;
			}
			if (middleDirection != kind)
			{
				middleDirection = kind;
				middleSteps = 0.0;
			}
			middleSteps += distance;
			while (middleSteps >= 18.0)
			{
				middleSteps -= 18.0;
				Run(value);
			}
		}
		else
		{
			Run(value);
		}
	}

	private void UpdateGestureTimer()
	{
		if (gesture.NeedsTick || middle.NeedsTick)
		{
			if (!gestureTimer.IsRunning)
			{
				gestureTimer.Start();
			}
		}
		else
		{
			gestureTimer.Stop();
		}
	}

	public VirtualTouchpad(SettingsStore store, Action<PadEvent>? testOutput = null)
	{
		this.store = store;
		fade = new IdleOpacity(() => Options.Opacity, () => Options.IdleOpacity, delegate
		{
			Draw();
		});
		Win32.GetCursorPos(out var p);
		Win32.RECT rcWork = Win32.Monitor(p).rcWork;
		position = new Win32.POINT(Options.X ?? (rcWork.Right - (int)(Options.Width * Win32.Scale(p)) - 24), Options.Y ?? (rcWork.Bottom - (int)(Options.Height * Win32.Scale(p)) - 24));
		callback = Procedure;
		cls = "TouchToys.Touchpad." + Guid.NewGuid().ToString("N");
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = callback,
			hInstance = Win32.GetModuleHandle(null),
			hCursor = Win32.ArrowCursor,
			lpszClassName = cls
		};
		Win32.RegisterClass(ref wc);
		handle = Win32.CreateWindowEx(134742152u, cls, "TT触摸板", 2147483648u, position.X, position.Y, Width, Height, 0, 0, wc.hInstance, 0);
		Win32.SuppressFeedback(handle);
		TouchMessages.Register(handle);
		OverlayOrder.Register(handle, -5);
		mousePass = new MousePassthrough(handle, () => shown, () => shown && touches.Count > 0, suppressFirstTouch: true);
		dispatch = testOutput ?? new Action<PadEvent>(Output);
		if (testOutput == null)
		{
			cursorOverlay = new TouchpadCursor();
		}
		queue = DispatcherQueue.GetForCurrentThread();
		gesture = new TouchpadGesture(dispatch, Bound, RunGesture);
		middle = new TouchpadMiddleGesture(MiddleBound, MiddleEvent, () => Options.GestureHoldSeconds * 1000.0);
		gestureTimer = queue.CreateTimer();
		gestureTimer.Interval = TimeSpan.FromMilliseconds(16.0);
		gestureTimer.Tick += delegate
		{
			long tickCount = Environment.TickCount64;
			gesture.Tick(tickCount, Options.GestureHoldSeconds * 1000.0);
			middle.Tick(tickCount);
			UpdateGestureTimer();
		};
		store.Changed += Sync;
		Sync();
	}

	private void Output(PadEvent e)
	{
		if (e.Kind == "Move")
		{
			moveX += e.X * Scale;
			moveY += e.Y * Scale;
			int num = (int)moveX;
			int num2 = (int)moveY;
			moveX -= num;
			moveY -= num2;
			KeyboardInput.Move(num, num2);
		}
		else if (e.Kind == "Wheel")
		{
			wheelRemainder += e.Y;
			horizontalWheel += e.X;
			int num3 = (int)wheelRemainder;
			int num4 = (int)horizontalWheel;
			if (num3 != 0)
			{
				KeyboardInput.Wheel(num3);
				wheelRemainder -= num3;
			}
			if (num4 != 0)
			{
				KeyboardInput.Wheel(num4, horizontal: true);
				horizontalWheel -= num4;
			}
		}
		else if (e.Kind == "Click")
		{
			KeyboardInput.Tap(new int[1] { e.Button });
		}
		else
		{
			KeyboardInput.Held(new int[1] { e.Button }, e.Kind == "Down");
		}
	}

	public void SetVisible(bool visible)
	{
		if (shown != visible)
		{
			shown = visible;
			if (!visible)
			{
				Cancel();
				fade.Pause();
			}
			else
			{
				fade.Active(held: false);
			}
			Win32.ShowWindow(handle, visible ? 4 : 0);
			if (visible)
			{
				OverlayOrder.Raise(handle);
			}
		}
	}

	private void Sync()
	{
		if (disposed)
		{
			return;
		}
		string text = $"{Options.Width}:{Options.Height}:{Options.Opacity}:{Options.IdleOpacity}:{Options.X}:{Options.Y}:{Options.EffectiveButtonPlacement}";
		if (!(text == stamp))
		{
			stamp = text;
			Cancel();
			position = new Win32.POINT(Options.X ?? position.X, Options.Y ?? position.Y);
			Constrain();
			fade.Active(held: false);
			if (!shown)
			{
				fade.Pause();
			}
			Draw();
		}
	}

	private void Constrain()
	{
		Win32.RECT rcWork = Win32.Monitor(position).rcWork;
		position = new Win32.POINT(Math.Clamp(position.X, rcWork.Left, Math.Max(rcWork.Left, rcWork.Right - Width)), Math.Clamp(position.Y, rcWork.Top, Math.Max(rcWork.Top, rcWork.Bottom - Height)));
		Options.X = position.X;
		Options.Y = position.Y;
	}

	private void Cancel()
	{
		gestureTimer.Stop();
		middle.Reset();
		middlePointer = null;
		middleSteps = 0.0;
		middleDirection = "";
		gesture.Reset();
		moveX = (moveY = (horizontalWheel = 0.0));
		foreach (int value in buttons.Values)
		{
			dispatch(new PadEvent("Up", 0.0, 0.0, value));
		}
		buttons.Clear();
		handlePointer = null;
		resizing = false;
		shake.Reset(0.0, 0.0, 0L);
		touches.Clear();
		cursorOverlay?.SetActive(active: false);
		RestoreCursorAfterTouch();
		wheelRemainder = 0.0;
	}

	private void Point(uint message, uint id, Win32.POINT point, bool cancel = false)
	{
		if (cancel || !shown || DesktopVisibility.Locked)
		{
			Cancel();
		}
		else
		{
			if (handlePointer.HasValue && handlePointer != id)
			{
				return;
			}
			switch (message)
			{
			case 582u:
				if (touches.Count == 0)
				{
					MousePassthrough.RestoreMousePosition();
					SaveCursorBeforeTouch();
					EnsureCursorOutsidePad();
					cursorOverlay?.SetActive(active: true);
				}
				touches.Add(id);
				fade.Active(held: true);
				break;
			case 583u:
				touches.Remove(id);
				if (touches.Count == 0)
				{
					fade.Active(held: false);
					RestoreCursorAfterTouch();
					cursorOverlay?.Linger(1200);
				}
				break;
			}
			if (message == 582)
			{
				double num = (double)(point.X - position.X) / Scale;
				double num2 = (double)(point.Y - position.Y) / Scale;
				if ((num2 < 30.0 || (num >= Options.Width - 28.0 && num2 >= Options.Height - 28.0)) && !handlePointer.HasValue)
				{
					Cancel();
					touches.Add(id);
					SaveCursorBeforeTouch();
					EnsureCursorOutsidePad();
					cursorOverlay?.SetActive(active: true);
					fade.Active(held: true);
					handlePointer = id;
					handleStart = point;
					positionStart = position;
					resizing = num2 >= 30.0;
					initialWidth = Options.Width;
					initialHeight = Options.Height;
					shake.Reset((double)point.X / Scale, (double)point.Y / Scale, Environment.TickCount64);
					return;
				}
				int num3 = Layout.Hit(num, num2);
				switch (num3)
				{
				case 515:
					if (!middlePointer.HasValue)
					{
						middlePointer = id;
						middleSteps = 0.0;
						middleDirection = "";
						middle.Down(num, num2, Environment.TickCount64);
						UpdateGestureTimer();
					}
					return;
				default:
				{
					if (buttons.TryGetValue(id, out var value))
					{
						if (value == num3)
						{
							return;
						}
						dispatch(new PadEvent("Up", 0.0, 0.0, value));
					}
					buttons[id] = num3;
					dispatch(new PadEvent("Down", 0.0, 0.0, num3));
					return;
				}
				case 0:
					break;
				}
			}
			int value2;
			if (handlePointer == id)
			{
				if (resizing)
				{
					Options.Width = Math.Clamp(initialWidth + (double)(point.X - handleStart.X) / Scale, 180.0, 900.0);
					Options.Height = Math.Clamp(initialHeight + (double)(point.Y - handleStart.Y) / Scale, 140.0, 700.0);
					Constrain();
					Draw();
					if (message == 583)
					{
						handlePointer = null;
						resizing = false;
						store.Save();
					}
				}
				else if (Options.ShakeToClose && message == 581 && shake.Add((double)point.X / Scale, (double)point.Y / Scale, Environment.TickCount64))
				{
					Options.Enabled = false;
					SetVisible(visible: false);
					store.Save();
				}
				else
				{
					position = new Win32.POINT(positionStart.X + point.X - handleStart.X, positionStart.Y + point.Y - handleStart.Y);
					Constrain();
					Win32.SetWindowPos(handle, 0, position.X, position.Y, 0, 0, 21u);
					if (message == 583)
					{
						handlePointer = null;
						store.Save();
					}
				}
			}
			else if (middlePointer == id)
			{
				double x = (double)(point.X - position.X) / Scale;
				double y = (double)(point.Y - position.Y) / Scale;
				if (message == 583)
				{
					middle.Up(x, y, Environment.TickCount64);
					middlePointer = null;
				}
				else
				{
					middle.Move(x, y);
				}
				UpdateGestureTimer();
			}
			else if (buttons.TryGetValue(id, out value2))
			{
				if (message == 583)
				{
					dispatch(new PadEvent("Up", 0.0, 0.0, value2));
					buttons.Remove(id);
				}
			}
			else
			{
				gesture.Feed(message, id, (double)point.X / Scale, (double)point.Y / Scale, Environment.TickCount64);
				UpdateGestureTimer();
			}
		}
	}

	[DllImport("user32")]
	private static extern nint SetCursor(nint cursor);

	[DllImport("user32")]
	private static extern bool SetCursorPos(int x, int y);

	private void SaveCursorBeforeTouch()
	{
		if (!cursorSaved)
		{
			cursorSaved = Win32.GetCursorPos(out cursorBeforeTouch);
		}
	}

	private void EnsureCursorOutsidePad()
	{
		if (Win32.GetCursorPos(out var p) && InsidePad(p))
		{
			Win32.POINT pOINT = OutsidePad(p);
			SetCursorPos(pOINT.X, pOINT.Y);
		}
		SetCursor(Win32.ArrowCursor);
	}

	private bool InsidePad(Win32.POINT p)
	{
		if (p.X >= position.X && p.X < position.X + Width && p.Y >= position.Y)
		{
			return p.Y < position.Y + Height;
		}
		return false;
	}

	private Win32.POINT OutsidePad(Win32.POINT preferred)
	{
		if (!InsidePad(preferred))
		{
			return preferred;
		}
		Win32.RECT area = Win32.Monitor(position).rcWork;
		Win32.POINT[] array = new Win32.POINT[4]
		{
			Clamp(position.X - 24, position.Y - 24),
			Clamp(position.X + Width + 24, position.Y - 24),
			Clamp(position.X + Width + 24, position.Y + Height + 24),
			Clamp(position.X - 24, position.Y + Height + 24)
		};
		foreach (Win32.POINT pOINT in array)
		{
			if (!InsidePad(pOINT))
			{
				return pOINT;
			}
		}
		return preferred;
		Win32.POINT Clamp(int x, int y)
		{
			return new Win32.POINT(Math.Clamp(x, area.Left, area.Right - 1), Math.Clamp(y, area.Top, area.Bottom - 1));
		}
	}

	private void RestoreCursorAfterTouch()
	{
		if (!cursorSaved)
		{
			return;
		}
		cursorSaved = false;
		if (!DesktopVisibility.Locked && InputDesktopState.CanInteract())
		{
			if (Win32.GetCursorPos(out var p))
			{
				Win32.POINT pOINT = OutsidePad(InsidePad(p) ? cursorBeforeTouch : p);
				SetCursorPos(pOINT.X, pOINT.Y);
			}
			SetCursor(Win32.ArrowCursor);
		}
	}

	private nint Procedure(nint h, uint message, nuint w, nint l)
	{
		try
		{
			if ((message == 33 || message == 587) ? true : false)
			{
				return 3;
			}
			bool flag;
			switch (message)
			{
			case 576u:
				TouchMessages.Read(w, l, delegate(uint id, uint flags, Win32.POINT p)
				{
					Point(((flags & 2) != 0) ? 582u : (((flags & 4) != 0) ? 583u : 581u), id, p);
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
					Point(message, info.pointerId, info.ptPixelLocation, (info.pointerFlags & 0x8000) != 0);
				}
				return 0;
			}
			if ((message == 533 || message == 588) ? true : false)
			{
				Cancel();
			}
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Touchpad.Input");
			try
			{
				Cancel();
			}
			catch (Exception error2)
			{
				Diagnostics.Error(error2, "Touchpad.Release");
			}
		}
		return Win32.DefWindowProc(h, message, w, l);
	}

	private void Draw()
	{
		if (surface == null || surfaceWidth != Width || surfaceHeight != Height || Math.Abs(surfaceScale - Scale) > 0.001 || surfaceStamp != SurfaceStamp)
		{
			RenderSurface();
		}
		PresentSurface();
	}

	private void RenderSurface()
	{
		ReleaseSurface();
		surfaceWidth = Width;
		surfaceHeight = Height;
		surfaceScale = Scale;
		surfaceStamp = SurfaceStamp;
		surface = new Bitmap(surfaceWidth, surfaceHeight, PixelFormat.Format32bppPArgb);
		using Graphics graphics = Graphics.FromImage(surface);
		graphics.SmoothingMode = SmoothingMode.AntiAlias;
		graphics.ScaleTransform((float)Scale, (float)Scale);
		float num = (float)Options.Width;
		float num2 = (float)Options.Height;
		using GraphicsPath graphicsPath = new GraphicsPath();
		graphicsPath.AddArc(1, 1, 24, 24, 180f, 90f);
		graphicsPath.AddArc(num - 25f, 1f, 24f, 24f, 270f, 90f);
		graphicsPath.AddArc(num - 25f, num2 - 25f, 24f, 24f, 0f, 90f);
		graphicsPath.AddArc(1f, num2 - 25f, 24f, 24f, 90f, 90f);
		graphicsPath.CloseFigure();
		using SolidBrush brush = new SolidBrush(Color.FromArgb(35, 39, 47));
		graphics.FillPath(brush, graphicsPath);
		using Pen pen = new Pen(Color.FromArgb(130, 255, 255, 255), 1f);
		graphics.DrawPath(pen, graphicsPath);
		TouchpadLayout layout = Layout;
		string effectiveButtonPlacement = Options.EffectiveButtonPlacement;
		if ((effectiveButtonPlacement == "Left" || effectiveButtonPlacement == "Right") ? true : false)
		{
			float num3 = ((Options.EffectiveButtonPlacement == "Left") ? 96f : (num - 96f));
			graphics.DrawLine(pen, num3, 30f, num3, num2 - 1f);
			graphics.DrawLine(pen, layout.LeftButton.Left + 1f, layout.RightButton.Top, layout.LeftButton.Right, layout.RightButton.Top);
			graphics.DrawLine(pen, layout.MiddleButton.Left, 30f, layout.MiddleButton.Left, num2 - 1f);
		}
		else
		{
			float top = layout.LeftButton.Top;
			graphics.DrawLine(pen, 1f, top, num - 1f, top);
			graphics.DrawLine(pen, layout.MiddleButton.Left, top, layout.MiddleButton.Left, top + 64f);
			graphics.DrawLine(pen, layout.MiddleButton.Right, top, layout.MiddleButton.Right, top + 64f);
			if (Options.EffectiveButtonPlacement == "Top")
			{
				graphics.DrawLine(pen, 1f, top + 64f, num - 1f, top + 64f);
			}
		}
		float num4 = layout.MiddleButton.Left + layout.MiddleButton.Width / 2f;
		float num5 = layout.MiddleButton.Top + layout.MiddleButton.Height / 2f;
		for (int i = -4; i <= 4; i += 4)
		{
			graphics.DrawLine(pen, num4 - 5f, num5 + (float)i, num4 + 5f, num5 + (float)i);
		}
		using Pen pen2 = new Pen(Color.FromArgb(200, 255, 255, 255), 4f)
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round
		};
		graphics.DrawLine(pen2, num / 2f - 24f, 15f, num / 2f + 24f, 15f);
		graphics.DrawLine(pen, num - 20f, num2 - 6f, num - 6f, num2 - 20f);
		graphics.DrawLine(pen, num - 13f, num2 - 6f, num - 6f, num2 - 13f);
		surfaceBitmap = surface.GetHbitmap(Color.FromArgb(0));
	}

	private void PresentSurface()
	{
		if (surfaceBitmap == 0)
		{
			return;
		}
		nint dC = Win32.GetDC(0);
		nint num = Win32.CreateCompatibleDC(dC);
		nint o = Win32.SelectObject(num, surfaceBitmap);
		try
		{
			Win32.SIZE size = new Win32.SIZE(surfaceWidth, surfaceHeight);
			Win32.POINT origin = default(Win32.POINT);
			Win32.BLENDFUNCTION blend = new Win32.BLENDFUNCTION
			{
				SourceConstantAlpha = (byte)Math.Round(fade.Value * 255.0),
				AlphaFormat = 1
			};
			Win32.UpdateLayeredWindow(handle, dC, ref position, ref size, num, ref origin, 0u, ref blend, 2u);
		}
		finally
		{
			Win32.SelectObject(num, o);
			Win32.DeleteDC(num);
			Win32.ReleaseDC(0, dC);
		}
	}

	private void ReleaseSurface()
	{
		if (surfaceBitmap != 0)
		{
			Win32.DeleteObject(surfaceBitmap);
			surfaceBitmap = 0;
		}
		surface?.Dispose();
		surface = null;
	}

	internal void CheckNative()
	{
		SetVisible(visible: true);
		Win32.POINT pOINT = position;
		Point(582u, 100u, new Win32.POINT(position.X + Width / 2, position.Y + (int)(15.0 * Scale)));
		Point(581u, 100u, new Win32.POINT(pOINT.X + Width / 2 - 20, pOINT.Y + (int)(15.0 * Scale) - 20));
		Point(583u, 100u, new Win32.POINT(pOINT.X + Width / 2 - 20, pOINT.Y + (int)(15.0 * Scale) - 20));
		if (position.X == pOINT.X && position.Y == pOINT.Y)
		{
			throw new InvalidOperationException("触摸板手柄不能移动");
		}
		Point(582u, 101u, new Win32.POINT(position.X + Width / 4, position.Y + Height - 10));
		SetVisible(visible: false);
	}

	internal void CheckV0823()
	{
		SetVisible(visible: true);
		double width = Options.Width;
		double height = Options.Height;
		Win32.POINT point = new Win32.POINT(position.X + Width - 8, position.Y + Height - 8);
		Point(582u, 200u, point);
		Point(581u, 200u, new Win32.POINT(point.X + 30, point.Y + 30));
		Point(583u, 200u, new Win32.POINT(point.X + 30, point.Y + 30));
		if (Options.Width <= width || Options.Height <= height)
		{
			throw new InvalidOperationException("触摸板无法拖动缩放");
		}
		Options.ShakeToClose = true;
		Options.Enabled = true;
		Win32.POINT point2 = new Win32.POINT(position.X + Width / 2, position.Y + (int)(15.0 * Scale));
		Point(582u, 201u, point2);
		int[] array = new int[4] { 40, -40, 40, -40 };
		foreach (int num in array)
		{
			Point(581u, 201u, new Win32.POINT(point2.X + (int)((double)num * Scale), point2.Y));
		}
		if (shown || Options.Enabled)
		{
			throw new InvalidOperationException("触摸板晃动未关闭");
		}
	}

	[DllImport("user32")]
	private static extern nint WindowFromPoint(Win32.POINT p);

	internal async Task CheckTopButtonsAsync()
	{
		int left = 0;
		int right = 0;
		Win32.WndProc proc = delegate(nint h, uint m, nuint w, nint l)
		{
			switch (m)
			{
			case 513u:
				left++;
				return 0;
			case 516u:
				right++;
				return 0;
			default:
				return Win32.DefWindowProc(h, m, w, l);
			}
		};
		string name = "TT.ClickProbe." + Guid.NewGuid().ToString("N");
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = proc,
			hInstance = Win32.GetModuleHandle(null),
			lpszClassName = name
		};
		Win32.RegisterClass(ref wc);
		nint fixture = Win32.CreateWindowEx(134217864u, name, "TT click test", 2147483648u, position.X, position.Y, Width, Height, 0, 0, wc.hInstance, 0);
		Win32.GetCursorPos(out var previous);
		try
		{
			Options.ButtonsOnTop = true;
			Options.Opacity = 0.45;
			Sync();
			Win32.ShowWindow(fixture, 4);
			SetVisible(visible: true);
			OverlayOrder.Raise(handle);
			for (int i = 0; i < 2; i++)
			{
				Win32.POINT point = new Win32.POINT(position.X + Width * ((i == 0) ? 1 : 3) / 4, position.Y + (int)(55.0 * Scale));
				SetCursorPos(point.X, point.Y);
				await Task.Delay(60);
				nint num = WindowFromPoint(point);
				if (num != handle && num != fixture)
				{
					throw new InvalidOperationException("测试区域被其它窗口覆盖，未发送点击。");
				}
				Point(582u, (uint)(500 + i), point);
				await KeyboardInput.DrainAsync();
				await Task.Delay(50);
				Point(583u, (uint)(500 + i), point);
				await KeyboardInput.DrainAsync();
				await Task.Delay(50);
			}
			if (left != 1 || right != 1)
			{
				throw new InvalidOperationException($"顶部按键未传递到测试窗口: left={left}, right={right}");
			}
		}
		finally
		{
			SetVisible(visible: false);
			SetCursorPos(previous.X, previous.Y);
			Win32.DestroyWindow(fixture);
			UnregisterClass(name, Win32.GetModuleHandle(null));
			GC.KeepAlive(proc);
		}
	}

	internal void CheckSideButtons()
	{
		string[] array = new string[2] { "Left", "Right" };
		foreach (string buttonPlacement in array)
		{
			Options.ButtonPlacement = buttonPlacement;
			Sync();
			SetVisible(visible: true);
			RectangleF[] array2 = new RectangleF[2] { Layout.LeftButton, Layout.RightButton };
			for (int j = 0; j < array2.Length; j++)
			{
				RectangleF rectangleF = array2[j];
				Win32.POINT point = new Win32.POINT(position.X + (int)((double)(rectangleF.Left + rectangleF.Width / 2f) * Scale), position.Y + (int)((double)(rectangleF.Top + rectangleF.Height / 2f) * Scale));
				Point(582u, 701u, point);
				Point(583u, 701u, point);
			}
			SetVisible(visible: false);
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			Cancel();
			fade.Dispose();
			mousePass.Dispose();
			cursorOverlay?.Dispose();
			ReleaseSurface();
			store.Changed -= Sync;
			OverlayOrder.Remove(handle);
			Win32.DestroyWindow(handle);
			UnregisterClass(cls, Win32.GetModuleHandle(null));
		}
	}
}
