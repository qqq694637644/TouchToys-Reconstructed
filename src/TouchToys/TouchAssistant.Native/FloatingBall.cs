using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.UI;
using Windows.UI;

namespace TouchAssistant.Native;

internal sealed class FloatingBall : IDisposable
{
	private readonly SettingsStore store;

	private readonly SummonAnimation summon;

	private readonly Win32.WndProc wndProc;

	private readonly DispatcherQueueTimer hold;

	private readonly DispatcherQueueTimer tap;

	private readonly DispatcherQueueTimer idle;

	private readonly DispatcherQueueTimer animation;

	private readonly DispatcherQueueTimer displayChange;

	private Win32.RECT lastArea;

	private nint lastMonitor;

	private double lastScale;

	private TaskCompletionSource? visibilityCompletion;

	private OutsideTouchShield? wheelShield;

	private WheelBackdrop? wheelBackdrop;

	private CenterHint? centerHint;

	private readonly ShakeDetector shake = new ShakeDetector();

	private nint hwnd;

	private readonly MousePassthrough mousePassthrough;

	private Win32.POINT center;

	private Win32.POINT start;

	private Win32.POINT last;

	private Win32.POINT directionOrigin;

	private Win32.POINT? wheelAnchor;

	private double scale = 1.0;

	private double vx;

	private double vy;

	private long lastTime;

	private readonly TwoFingerDrag twoFinger = new TwoFingerDrag();

	private uint pointerId;

	private bool pressed;

	private bool moving;

	private bool wheel;

	private bool isIdle;

	private bool hidden;

	private bool armedMove;

	private bool mousePointer;

	private bool gestureConsumed;

	private bool heldWheel;

	private readonly string[] customIconPaths = new string[8];

	private readonly Bitmap?[] customIcons = new Bitmap[8];

	private int selected = -1;

	private int taps;

	private int grabOffsetX;

	private int grabOffsetY;

	private double visibility = 1.0;

	private double visibilityTarget = 1.0;

	private double fadeOffset;

	private double wheelProgress;

	private double pressScale = 1.0;

	private double ballOpacity = 1.0;

	private string? lastVisual;

	private string? settingsStamp;

	private const int Extent = 168;

	internal bool IsPresented => Win32.IsWindowVisible(hwnd);

	public Win32.POINT Center => center;

	public event Action<int>? Trigger;

	internal void PlaySummon()
	{
		summon.Start();
	}

	public FloatingBall(SettingsStore settings)
	{
		summon = new SummonAnimation(Render, compact: true);
		store = settings;
		wndProc = WindowProc;
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = wndProc,
			hInstance = Win32.GetModuleHandle(null),
			hCursor = Win32.ArrowCursor,
			lpszClassName = "TouchAssistant.Ball"
		};
		Win32.RegisterClass(ref wc);
		hwnd = Win32.CreateWindowEx(134742152u, wc.lpszClassName, "TouchToys", 2147483648u, 0, 0, 1, 1, 0, 0, wc.hInstance, 0);
		Win32.SuppressFeedback(hwnd);
		OverlayOrder.Register(hwnd);
		mousePassthrough = new MousePassthrough(hwnd, () => !store.Value.MouseEnabled && !pressed);
		AccentTheme.Changed += AccentChanged;
		DispatcherQueue forCurrentThread = DispatcherQueue.GetForCurrentThread();
		hold = forCurrentThread.CreateTimer();
		hold.IsRepeating = false;
		hold.Tick += delegate
		{
			if (pressed && !wheel && !gestureConsumed)
			{
				gestureConsumed = true;
				taps = 0;
				tap?.Stop();
				this.Trigger?.Invoke(12);
				heldWheel = wheel;
				Render();
			}
		};
		tap = forCurrentThread.CreateTimer();
		tap.IsRepeating = false;
		tap.Tick += delegate
		{
			int num = taps;
			taps = 0;
			if (num > 0)
			{
				this.Trigger?.Invoke(7 + Math.Min(num, 3));
			}
		};
		idle = forCurrentThread.CreateTimer();
		idle.IsRepeating = false;
		idle.Tick += delegate
		{
			if (!pressed && !wheel)
			{
				isIdle = true;
				animation?.Start();
			}
		};
		animation = forCurrentThread.CreateTimer();
		animation.Interval = TimeSpan.FromMilliseconds(16.0);
		animation.Tick += Animate;
		displayChange = forCurrentThread.CreateTimer();
		displayChange.IsRepeating = false;
		displayChange.Interval = TimeSpan.FromMilliseconds(180.0);
		displayChange.Tick += delegate
		{
			RemapDisplay();
		};
		int? ballX = store.Value.BallX;
		Win32.POINT pOINT;
		if (ballX.HasValue)
		{
			int valueOrDefault = ballX.GetValueOrDefault();
			ballX = store.Value.BallY;
			if (ballX.HasValue)
			{
				int valueOrDefault2 = ballX.GetValueOrDefault();
				pOINT = new Win32.POINT(valueOrDefault, valueOrDefault2);
				goto IL_02e2;
			}
		}
		pOINT = DefaultPosition();
		goto IL_02e2;
		IL_02e2:
		center = pOINT;
		Constrain();
		ballOpacity = store.Value.Opacity;
		store.Changed += OnSettings;
		OnSettings();
		Render();
		Win32.ShowWindow(hwnd, 4);
		Wake();
	}

	private Win32.POINT DefaultPosition()
	{
		Win32.POINT p = new Win32.POINT(0, 0);
		Win32.RECT rcMonitor = Win32.Monitor(p).rcMonitor;
		_ = Win32.Scale(p) * store.Value.BallSize / 58.0;
		return new Win32.POINT((rcMonitor.Left + rcMonitor.Right) / 2, (rcMonitor.Top + rcMonitor.Bottom) / 2);
	}

	internal void PositionAt(Win32.POINT point)
	{
		Cancel();
		tap.Stop();
		taps = 0;
		center = point;
		Constrain();
		SavePosition();
		lastVisual = null;
		Wake();
		Render();
	}

	public void ResetPosition()
	{
		center = DefaultPosition();
		Constrain();
		SavePosition();
		Render();
	}

	public void ShowWheel()
	{
		FitWheel();
		wheel = true;
		selected = -1;
		Wake();
		animation.Start();
		Render();
		wheelShield?.Dispose();
		wheelShield = new OutsideTouchShield(hwnd, CloseWheel);
	}

	public void ToggleWheel()
	{
		if (wheel)
		{
			CloseWheel();
		}
		else
		{
			ShowWheel();
		}
	}

	private void CloseWheel()
	{
		if (wheel)
		{
			wheel = false;
			pressed = false;
			selected = -1;
			wheelShield?.Dispose();
			wheelShield = null;
			RestoreWheelAnchor();
			animation.Start();
			Render();
		}
	}

	public void EnterMoveMode()
	{
		moving = pressed;
		armedMove = !pressed;
		wheel = false;
		Wake();
		animation.Start();
		Render();
	}

	public void SetVisible(bool visible)
	{
		visible &= store.Value.BallEnabled;
		bool flag = visible && hidden;
		visibilityCompletion?.TrySetCanceled();
		visibilityCompletion = null;
		visibilityTarget = (visible ? 1 : 0);
		hidden = !visible;
		wheel = false;
		RestoreWheelAnchor();
		selected = -1;
		pressed = false;
		moving = false;
		armedMove = false;
		hold.Stop();
		idle.Stop();
		tap.Stop();
		taps = 0;
		wheelShield?.Dispose();
		wheelShield = null;
		if (visible)
		{
			Win32.ShowWindow(hwnd, 4);
			Wake();
			if (flag)
			{
				summon.Start();
			}
		}
		animation.Start();
	}

	internal void HideForCapture()
	{
		SetVisible(visible: false);
		visibility = 0.0;
		Win32.ShowWindow(hwnd, 0);
	}

	public Task HideAsync()
	{
		SetVisible(visible: false);
		visibilityCompletion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		return visibilityCompletion.Task;
	}

	private void OnSettings()
	{
		string text = $"{store.Value.BallSize}:{store.Value.Opacity}:{store.Value.IdleOpacity}:{store.Value.IdleSeconds}:{store.Value.HoldSeconds}:{store.Value.MouseEnabled}:{store.Value.MoveOutline}:{store.Value.WheelMask}:{store.Value.WheelMaskClock}";
		text += string.Join("|", store.Value.Gestures.ConvertAll((GestureAction x) => $"{x.Icon}:{x.IconPath}:{x.IconFont}:{x.Label}:{x.TextColor}:{x.TextOpacity}:{x.AppearanceDestination}"));
		if (text == settingsStamp)
		{
			return;
		}
		settingsStamp = text;
		for (int num = 0; num < 8; num++)
		{
			string iconPath = store.Value.Gestures[num].IconPath;
			if (customIconPaths[num] == iconPath)
			{
				continue;
			}
			customIconPaths[num] = iconPath;
			customIcons[num]?.Dispose();
			customIcons[num] = null;
			try
			{
				string iconPath2 = store.Value.Gestures[num].IconPath;
				if (!File.Exists(iconPath2))
				{
					continue;
				}
				using Image image = Image.FromFile(iconPath2);
				if (image.Width <= 512 && image.Height <= 512)
				{
					customIcons[num] = new Bitmap(image);
				}
			}
			catch
			{
			}
		}
		if (Math.Abs(scale - Win32.Scale(center) * store.Value.BallSize / 58.0) > 0.001)
		{
			Constrain();
		}
		lastVisual = null;
		Wake();
		Render();
	}

	private void Wake()
	{
		isIdle = false;
		animation.Start();
		idle.Stop();
		if (!hidden)
		{
			idle.Interval = TimeSpan.FromMilliseconds(Math.Max(1.0, store.Value.IdleSeconds * 1000.0));
			idle.Start();
		}
	}

	private void SavePosition()
	{
		if (!wheel && !pressed)
		{
			Constrain();
		}
		if (store.Value.BallX != center.X || store.Value.BallY != center.Y)
		{
			store.Value.BallX = center.X;
			store.Value.BallY = center.Y;
			store.Save();
		}
	}

	private void Constrain()
	{
		scale = Win32.Scale(center) * store.Value.BallSize / 58.0;
		Win32.RECT rcMonitor = Win32.Monitor(center).rcMonitor;
		ref int x = ref center.X;
		ref int y = ref center.Y;
		(x, y) = GestureGeometry.Constrain(center.X, center.Y, rcMonitor.Left, rcMonitor.Top, rcMonitor.Width, rcMonitor.Height, scale);
		if (!displayChange.IsRunning)
		{
			lastArea = rcMonitor;
			lastScale = scale;
			lastMonitor = Win32.MonitorFromPoint(center, 2u);
		}
	}

	private void RemapDisplay()
	{
		displayChange.Stop();
		Cancel();
		animation.Stop();
		vx = (vy = 0.0);
		Win32.MONITORINFO info = new Win32.MONITORINFO
		{
			cbSize = Marshal.SizeOf<Win32.MONITORINFO>()
		};
		Win32.RECT rECT = (Win32.GetMonitorInfo(lastMonitor, ref info) ? info.rcMonitor : Win32.Monitor(new Win32.POINT((lastArea.Left + lastArea.Right) / 2, (lastArea.Top + lastArea.Bottom) / 2)).rcMonitor);
		double num = Win32.Scale(new Win32.POINT(rECT.Left + rECT.Width / 2, rECT.Top + rECT.Height / 2)) * store.Value.BallSize / 58.0;
		ref int x = ref center.X;
		ref int y = ref center.Y;
		(int, int) tuple = GestureGeometry.Remap(center.X, center.Y, lastArea.Left, lastArea.Top, lastArea.Width, lastArea.Height, lastScale, rECT.Left, rECT.Top, rECT.Width, rECT.Height, num);
		x = tuple.Item1;
		y = tuple.Item2;
		lastArea = rECT;
		lastScale = num;
		lastMonitor = Win32.MonitorFromPoint(center, 2u);
		Constrain();
		SavePosition();
		lastVisual = null;
		Render();
		if (visibility != visibilityTarget)
		{
			animation.Start();
		}
	}

	private void FitWheel()
	{
		Win32.POINT valueOrDefault = wheelAnchor.GetValueOrDefault();
		if (!wheelAnchor.HasValue)
		{
			valueOrDefault = center;
			wheelAnchor = valueOrDefault;
		}
		Win32.RECT rcMonitor = Win32.Monitor(center).rcMonitor;
		ref int x = ref center.X;
		ref int y = ref center.Y;
		(x, y) = GestureGeometry.Constrain(center.X, center.Y, rcMonitor.Left, rcMonitor.Top, rcMonitor.Width, rcMonitor.Height, scale);
	}

	private void RestoreWheelAnchor()
	{
		Win32.POINT? pOINT = wheelAnchor;
		if (pOINT.HasValue)
		{
			Win32.POINT valueOrDefault = pOINT.GetValueOrDefault();
			center = valueOrDefault;
			wheelAnchor = null;
		}
	}

	private void Select(int next)
	{
		if (selected != next)
		{
			selected = next;
		}
	}

	private double Distance(Win32.POINT a, Win32.POINT b)
	{
		return Math.Sqrt(Math.Pow(a.X - b.X, 2.0) + Math.Pow(a.Y - b.Y, 2.0)) / scale;
	}

	private int Direction(Win32.POINT p)
	{
		return GestureGeometry.Direction(p.X - directionOrigin.X, p.Y - directionOrigin.Y);
	}

	private void Down(Win32.POINT p)
	{
		if (hidden || pressed)
		{
			return;
		}
		if (wheel && Distance(p, center) > 150.0)
		{
			CloseWheel();
		}
		else if (wheel || !(Distance(p, center) > 35.0))
		{
			shake.Reset((double)p.X / Win32.Scale(p), (double)p.Y / Win32.Scale(p), Environment.TickCount64);
			Wake();
			tap.Stop();
			pressed = true;
			start = (last = p);
			directionOrigin = center;
			vx = (vy = 0.0);
			lastTime = Stopwatch.GetTimestamp();
			grabOffsetX = p.X - center.X;
			grabOffsetY = p.Y - center.Y;
			moving = armedMove;
			armedMove = false;
			gestureConsumed = moving;
			heldWheel = false;
			if (wheel && Distance(p, center) > 43.0)
			{
				Select(Direction(p));
			}
			hold.Interval = TimeSpan.FromMilliseconds(Math.Max(1.0, store.Value.HoldSeconds * 1000.0));
			if (!wheel && !moving)
			{
				hold.Start();
			}
			Render();
		}
	}

	private void Move(Win32.POINT p)
	{
		if (!pressed)
		{
			return;
		}
		if (moving)
		{
			if (store.Value.BallShakeToClose && shake.Add((double)p.X / Win32.Scale(p), (double)p.Y / Win32.Scale(p), Environment.TickCount64))
			{
				Cancel();
				store.Value.BallEnabled = false;
				store.Save();
				return;
			}
			double num = Math.Max(0.008, (double)(Stopwatch.GetTimestamp() - lastTime) / (double)Stopwatch.Frequency);
			int num2 = p.X - last.X;
			int num3 = p.Y - last.Y;
			vx = 0.45 * vx + 0.55 * (double)num2 / num;
			vy = 0.45 * vy + 0.55 * (double)num3 / num;
			center.X = p.X - grabOffsetX;
			center.Y = p.Y - grabOffsetY;
			Win32.RECT rcMonitor = Win32.Monitor(p).rcMonitor;
			scale = Win32.Scale(p) * store.Value.BallSize / 58.0;
			center.X = Math.Clamp(center.X, rcMonitor.Left - (int)(20.0 * scale), rcMonitor.Right + (int)(20.0 * scale));
			center.Y = Math.Clamp(center.Y, rcMonitor.Top - (int)(20.0 * scale), rcMonitor.Bottom + (int)(20.0 * scale));
			last = p;
			lastTime = Stopwatch.GetTimestamp();
		}
		else if (!wheel && !gestureConsumed && Distance(p, start) > 12.0)
		{
			hold.Stop();
			tap.Stop();
			taps = 0;
			gestureConsumed = true;
			this.Trigger?.Invoke(11);
			if (moving)
			{
				Move(p);
				return;
			}
		}
		else if (wheel)
		{
			hold.Stop();
			tap.Stop();
			taps = 0;
			if (!wheel)
			{
				FitWheel();
				wheel = true;
				animation.Start();
			}
			if (GestureGeometry.CancelWheel(Distance(p, directionOrigin), store.Value.WheelCancelOutside, store.Value.WheelCancelRadius))
			{
				CloseWheel();
				return;
			}
			Select((Distance(p, directionOrigin) > 48.0) ? Direction(p) : (-1));
		}
		Render();
	}

	private void Up(Win32.POINT p)
	{
		if (!pressed)
		{
			return;
		}
		pressed = false;
		hold.Stop();
		if (moving)
		{
			moving = false;
			if ((double)(Stopwatch.GetTimestamp() - lastTime) / (double)Stopwatch.Frequency > 0.1)
			{
				vx = (vy = 0.0);
			}
			vx = Math.Clamp(vx, -1500.0 * scale, 1500.0 * scale);
			vy = Math.Clamp(vy, -1500.0 * scale, 1500.0 * scale);
			animation.Start();
		}
		else if (wheel)
		{
			int num = selected;
			wheel = false;
			wheelShield?.Dispose();
			wheelShield = null;
			RestoreWheelAnchor();
			selected = -1;
			animation.Start();
			Render();
			if (num >= 0)
			{
				this.Trigger?.Invoke(num);
			}
		}
		else if (!gestureConsumed)
		{
			taps++;
			if (taps == 3)
			{
				int num2 = taps;
				taps = 0;
				tap.Stop();
				this.Trigger?.Invoke(7 + num2);
			}
			else
			{
				tap.Interval = TimeSpan.FromMilliseconds(Win32.GetDoubleClickTime());
				tap.Start();
			}
		}
		Wake();
		Render();
	}

	private void Cancel(bool resetTouches = true)
	{
		if (resetTouches)
		{
			twoFinger.Reset();
		}
		tap.Stop();
		taps = 0;
		pressed = (moving = (wheel = (armedMove = false)));
		wheelProgress = 0.0;
		wheelShield?.Dispose();
		wheelShield = null;
		RestoreWheelAnchor();
		selected = -1;
		vx = (vy = 0.0);
		hold.Stop();
		Render();
	}

	internal async Task CheckGestureTransitionsAsync()
	{
		Win32.POINT original = center;
		double oldHold = store.Value.HoldSeconds;
		try
		{
			SetVisible(visible: true);
			store.Value.HoldSeconds = 0.1;
			ToggleWheel();
			if (!wheel)
			{
				throw new InvalidOperationException("切换轮盘未打开。");
			}
			ToggleWheel();
			if (wheel)
			{
				throw new InvalidOperationException("再次切换轮盘未关闭。");
			}
			Down(center);
			await Task.Delay(180);
			if (!wheel || moving)
			{
				throw new InvalidOperationException("长按未展开轮盘，或误进入拖动。");
			}
			Up(center);
			if (wheel)
			{
				throw new InvalidOperationException("长按松手后轮盘未关闭。");
			}
			Cancel();
			Down(center);
			Move(new Win32.POINT(center.X + (int)(20.0 * scale), center.Y));
			if (!moving || wheel)
			{
				throw new InvalidOperationException("直接拖动未进入移动模式。");
			}
			Up(center);
			Cancel();
			string oldIcon = store.Value.Gestures[0].Icon;
			string oldLabel = store.Value.Gestures[0].Label;
			try
			{
				store.Value.Gestures[0].Icon = "\ue90c";
				store.Value.Gestures[0].Label = "测试";
				OnSettings();
				ShowWheel();
				await Task.Delay(120);
				wheel = false;
				animation.Start();
				await Task.Delay(250);
			}
			finally
			{
				store.Value.Gestures[0].Icon = oldIcon;
				store.Value.Gestures[0].Label = oldLabel;
			}
			bool ballShakeToClose = store.Value.BallShakeToClose;
			bool ballEnabled = store.Value.BallEnabled;
			try
			{
				store.Value.BallShakeToClose = true;
				Win32.POINT p = center;
				EnterMoveMode();
				Down(p);
				double num = Win32.Scale(p);
				int[] array = new int[4] { 40, -40, 40, -40 };
				foreach (int num2 in array)
				{
					Move(new Win32.POINT(p.X + (int)((double)num2 * num), p.Y));
				}
				if (store.Value.BallEnabled)
				{
					throw new InvalidOperationException("触控圆点晃动未关闭。");
				}
			}
			finally
			{
				store.Value.BallShakeToClose = ballShakeToClose;
				store.Value.BallEnabled = ballEnabled;
				store.Save();
				SetVisible(ballEnabled);
			}
		}
		finally
		{
			Cancel();
			center = original;
			store.Value.HoldSeconds = oldHold;
			Constrain();
			SavePosition();
			OnSettings();
		}
	}

	private void Animate(DispatcherQueueTimer sender, object args)
	{
		bool flag = !pressed && Math.Abs(vx) + Math.Abs(vy) > 15.0;
		if (flag)
		{
			center.X += (int)(vx * 0.016);
			center.Y += (int)(vy * 0.016);
			vx *= 0.72;
			vy *= 0.72;
		}
		bool flag2 = false;
		if (!pressed && !wheel && !hidden)
		{
			Win32.RECT rcMonitor = Win32.Monitor(center).rcMonitor;
			(int, int) tuple = GestureGeometry.Constrain(center.X, center.Y, rcMonitor.Left, rcMonitor.Top, rcMonitor.Width, rcMonitor.Height, scale);
			int num = tuple.Item1 - center.X;
			int num2 = tuple.Item2 - center.Y;
			flag2 = num != 0 || num2 != 0;
			if (flag2)
			{
				vx = (vy = 0.0);
				center.X = GestureGeometry.SpringStep(center.X, tuple.Item1);
				center.Y = GestureGeometry.SpringStep(center.Y, tuple.Item2);
			}
		}
		double num3 = (double)(wheel ? 1 : 0) - wheelProgress;
		wheelProgress = ((Math.Abs(num3) < 0.015) ? ((double)(wheel ? 1 : 0)) : (wheelProgress + num3 * 0.27));
		double num4 = visibilityTarget - visibility;
		visibility = ((Math.Abs(num4) < 0.025) ? visibilityTarget : (visibility + num4 * 0.4816));
		fadeOffset = (1.0 - visibility) * 22.0 * scale;
		double num5 = (pressed ? 1.08 : 1.0);
		pressScale = GestureGeometry.Ease(pressScale, num5, 0.24, 0.001);
		double num6 = (isIdle ? store.Value.IdleOpacity : store.Value.Opacity);
		ballOpacity = GestureGeometry.Ease(ballOpacity, num6, 0.13, 0.002);
		Render();
		if (hidden && visibility == 0.0)
		{
			animation.Stop();
			vx = (vy = 0.0);
			pressScale = 1.0;
			wheelProgress = 0.0;
			Win32.ShowWindow(hwnd, 0);
			visibilityCompletion?.TrySetResult();
			visibilityCompletion = null;
		}
		else if (!flag && !flag2 && visibility == visibilityTarget && wheelProgress == (double)(wheel ? 1 : 0) && pressScale == num5 && ballOpacity == num6)
		{
			animation.Stop();
			vx = (vy = 0.0);
			if (hidden)
			{
				Win32.ShowWindow(hwnd, 0);
			}
			else
			{
				SavePosition();
			}
			visibilityCompletion?.TrySetResult();
			visibilityCompletion = null;
		}
	}

	private nint WindowProc(nint h, uint msg, nuint wp, nint lp)
	{
		bool flag;
		switch (msg)
		{
		case 32u:
			Win32.SetCursor(Win32.ArrowCursor);
			return 1;
		case 33u:
			return 3;
		case 26u:
		case 126u:
		case 736u:
			if (displayChange != null)
			{
				displayChange.Stop();
				displayChange.Start();
			}
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
			uint num = (uint)((int)wp & 0xFFFF);
			flag = Win32.GetPointerInfo(num, out var info);
			if (flag)
			{
				uint pointerType = info.pointerType;
				bool flag2 = pointerType - 2 <= 1;
				flag = flag2;
			}
			if (flag)
			{
				if ((info.pointerFlags & 0x8000) != 0)
				{
					twoFinger.Reset();
					Cancel();
					return 0;
				}
				if (twoFinger.Feed(msg, num, info.ptPixelLocation.X, info.ptPixelLocation.Y, delegate
				{
					Cancel(resetTouches: false);
					CloseWheel();
					Wake();
				}, delegate(int x, int y)
				{
					center.X += x;
					center.Y += y;
					Constrain();
					Render();
				}, delegate
				{
					SavePosition();
				}))
				{
					return 0;
				}
				if (msg == 582 && !pressed)
				{
					pointerId = num;
					mousePointer = false;
					Down(info.ptPixelLocation);
				}
				else if (num == pointerId)
				{
					if ((info.pointerFlags & 0x8000) != 0)
					{
						Cancel();
					}
					else
					{
						switch (msg)
						{
						case 581u:
							Move(info.ptPixelLocation);
							break;
						case 583u:
							Up(info.ptPixelLocation);
							break;
						}
					}
				}
				return 0;
			}
		}
		if (msg - 512 <= 2)
		{
			if (((ulong)Win32.GetMessageExtraInfo() & 0xFFFFFF00uL) == 4283520768u || !store.Value.MouseEnabled)
			{
				return 0;
			}
			Win32.GetCursorPos(out var p);
			if (msg == 513)
			{
				mousePointer = true;
				Down(p);
				if (pressed)
				{
					Win32.SetCapture(h);
				}
			}
			else if (mousePointer && msg == 512)
			{
				Move(p);
			}
			else if (mousePointer && msg == 514)
			{
				Up(p);
				Win32.ReleaseCapture();
			}
			return 0;
		}
		if ((msg == 533 || msg == 588) && pressed)
		{
			Cancel();
		}
		return Win32.DefWindowProc(h, msg, wp, lp);
	}

	private void Render()
	{
		if (hwnd == 0)
		{
			return;
		}
		if (store.Value.WheelMask && wheelProgress > 0.0 && !hidden)
		{
			if (wheelBackdrop == null)
			{
				wheelBackdrop = new WheelBackdrop();
			}
			wheelBackdrop.Update(hwnd, center, wheelProgress, store.Value.WheelMaskClock);
		}
		else
		{
			wheelBackdrop?.Dispose();
			wheelBackdrop = null;
		}
		if (wheel && selected >= 0 && store.Value.WheelCenterHint && !hidden)
		{
			if (centerHint == null)
			{
				centerHint = new CenterHint();
			}
			GestureAction gestureAction = store.Value.Gestures[selected];
			centerHint.Show(gestureAction, center, 0.0, (gestureAction.AppearanceDestination & 2) != 0);
		}
		else
		{
			centerHint?.Hide();
		}
		int num = (int)Math.Ceiling(336.0 * scale);
		byte b = (byte)Math.Clamp(visibility * ((wheelProgress > 0.0) ? 1.0 : ballOpacity) * 255.0, 0.0, 255.0);
		int num2 = ((wheelProgress > 0.0) ? ((int)Math.Clamp(ballOpacity * 255.0, 0.0, 255.0)) : 255);
		string text = $"{num}:{wheelProgress:F3}:{selected}:{moving || armedMove}:{b}:{num2}:{pressScale:F3}:{summon.Progress:F3}";
		if (text == lastVisual)
		{
			Win32.SetWindowPos(hwnd, -1, center.X - num / 2, center.Y - num / 2 + (int)fadeOffset, num, num, 16u);
			return;
		}
		lastVisual = text;
		using Bitmap bitmap = new Bitmap(num, num, PixelFormat.Format32bppPArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.ScaleTransform((float)scale, (float)scale);
			graphics.TranslateTransform(168f, 168f);
			if (wheelProgress > 0.0)
			{
				GraphicsState gstate = graphics.Save();
				float num3 = (float)(0.62 + 0.38 * wheelProgress);
				graphics.ScaleTransform(num3, num3);
				WheelDrawing.Draw(graphics, store.Value.Gestures, customIcons, selected, wheelProgress);
				graphics.Restore(gstate);
			}
			graphics.ScaleTransform((float)pressScale, (float)pressScale);
			using SolidBrush brush = new SolidBrush(System.Drawing.Color.FromArgb(num2, 42, 47, 56));
			using (new GraphicsPath(FillMode.Alternate))
			{
				(double, double) tuple = summon.Sample(0.8620689655172413);
				float num4 = (float)(29.0 * tuple.Item2);
				if (num4 > 0.1f && (!store.Value.WheelMask || !(wheelProgress > 0.0)))
				{
					graphics.FillEllipse(brush, 0f - num4, 0f - num4, num4 * 2f, num4 * 2f);
				}
				using SolidBrush brush2 = new SolidBrush(System.Drawing.Color.FromArgb(num2, 205, 207, 211));
				float num5 = (float)(29.0 * tuple.Item1);
				graphics.FillEllipse(brush2, 0f - num5, 0f - num5, num5 * 2f, num5 * 2f);
				if (store.Value.MoveOutline && (moving || armedMove))
				{
					Windows.UI.Color color = AccentTheme.Color;
					using Pen pen = new Pen(System.Drawing.Color.FromArgb(255, color.R, color.G, color.B), 4f);
					graphics.DrawEllipse(pen, -33, -33, 66, 66);
				}
			}
		}
		nint dC = Win32.GetDC(0);
		nint num6 = Win32.CreateCompatibleDC(dC);
		nint hbitmap = bitmap.GetHbitmap(System.Drawing.Color.FromArgb(0));
		nint o = Win32.SelectObject(num6, hbitmap);
		try
		{
			Win32.POINT dst = new Win32.POINT(center.X - num / 2, center.Y - num / 2 + (int)fadeOffset);
			Win32.SIZE size = new Win32.SIZE(num, num);
			Win32.POINT origin = default(Win32.POINT);
			Win32.BLENDFUNCTION blend = new Win32.BLENDFUNCTION
			{
				SourceConstantAlpha = b,
				AlphaFormat = 1
			};
			Win32.UpdateLayeredWindow(hwnd, dC, ref dst, ref size, num6, ref origin, 0u, ref blend, 2u);
		}
		finally
		{
			Win32.SelectObject(num6, o);
			Win32.DeleteObject(hbitmap);
			Win32.DeleteDC(num6);
			Win32.ReleaseDC(0, dC);
		}
	}

	private void AccentChanged()
	{
		lastVisual = null;
		Render();
	}

	internal async Task CheckMaskAsync()
	{
		bool enabled = store.Value.WheelMask;
		try
		{
			store.Value.WheelMask = true;
			OnSettings();
			SetVisible(visible: true);
			ShowWheel();
			await Task.Delay(300);
			WheelBackdrop? obj = wheelBackdrop;
			if (obj == null || !obj.IsPresented)
			{
				throw new InvalidOperationException("环形菜单遮罩未显示。");
			}
			store.Value.WheelMask = false;
			OnSettings();
			if (wheelBackdrop?.IsPresented ?? false)
			{
				throw new InvalidOperationException("遮罩关闭开关未生效。");
			}
			store.Value.WheelMask = true;
			OnSettings();
			CloseWheel();
			for (int wait = 0; wait < 40; wait++)
			{
				if (!(wheelBackdrop?.IsPresented ?? false))
				{
					break;
				}
				await Task.Delay(50);
			}
			if (wheelBackdrop?.IsPresented ?? false)
			{
				throw new InvalidOperationException($"菜单关闭后遮罩未收起。wheel={wheel}; progress={wheelProgress}; running={animation.IsRunning}");
			}
		}
		finally
		{
			store.Value.WheelMask = enabled;
			CloseWheel();
			OnSettings();
		}
	}

	public void Dispose()
	{
		wheelBackdrop?.Dispose();
		summon.Dispose();
		centerHint?.Dispose();
		AccentTheme.Changed -= AccentChanged;
		wheelShield?.Dispose();
		store.Changed -= OnSettings;
		mousePassthrough.Dispose();
		Bitmap[] array = customIcons;
		for (int i = 0; i < array.Length; i++)
		{
			array[i]?.Dispose();
		}
		displayChange.Stop();
		visibilityCompletion?.TrySetCanceled();
		hold.Stop();
		tap.Stop();
		idle.Stop();
		animation.Stop();
		OverlayOrder.Remove(hwnd);
		Win32.DestroyWindow(hwnd);
		hwnd = 0;
	}
}
