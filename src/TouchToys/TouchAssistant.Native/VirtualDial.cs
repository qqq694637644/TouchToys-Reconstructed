using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.Services;
using TouchAssistant.UI;
using Windows.UI;

namespace TouchAssistant.Native;

internal sealed class VirtualDial : IDisposable
{
	private readonly SettingsStore store;

	private readonly SummonAnimation summon;

	private DialSettings? lastOptions;

	private readonly Win32.WndProc callback;

	private readonly nint handle;

	private readonly string windowClass = "TouchToys.VirtualDial." + Guid.NewGuid().ToString("N");

	private readonly MousePassthrough mousePass;

	private readonly DispatcherQueueTimer hold;

	private readonly DispatcherQueueTimer labelTimer;

	private readonly DispatcherQueueTimer animation;

	private readonly DispatcherQueueTimer idle;

	private readonly DispatcherQueueTimer click;

	private readonly DispatcherQueueTimer rotationOutput;

	private double pendingAngle;

	private double scrollRemainder;

	private readonly DialSound sound = new DialSound();

	private readonly ShakeDetector shake = new ShakeDetector();

	private CenterHint? hint;

	private Win32.POINT position;

	private Win32.POINT origin;

	private Win32.POINT initial;

	private Win32.POINT lastPoint;

	private bool visible;

	private bool suppressed;

	private bool pressed;

	private bool mousePressed;

	private bool center;

	private bool moving;

	private bool menu;

	private bool held;

	private bool isIdle;

	private bool momentum;

	private bool disposed;

	private bool closing;

	private bool showLabel = true;

	private uint pointer;

	private readonly Dictionary<uint, Win32.POINT> contacts = new Dictionary<uint, Win32.POINT>();

	private bool twoFinger;

	private int tool;

	private int taps;

	private bool outerTap;

	private double angle;

	private double remainder;

	private double vx;

	private double vy;

	private double fx;

	private double fy;

	private double pressScale = 1.0;

	private double opacity = 1.0;

	private long lastMoved;

	private long pulseStarted;

	private double pulseAmplitude;

	private double pulseScale = 1.0;

	private double outerScale = 1.0;

	private double menuFrom = 1.0;

	private long menuStarted;

	private int? savedX;

	private int? savedY;

	private string selectionHint = "";

	private DialSettings Options => ApplicationBindings.Dial(store.Value);

	private bool PointerVisible
	{
		get
		{
			if (pressed && !center && !twoFinger)
			{
				return !menu;
			}
			return false;
		}
	}

	internal bool IsVisible => visible;

	internal bool IsOpen
	{
		get
		{
			if (visible)
			{
				return !closing;
			}
			return false;
		}
	}

	internal int SelectedTool
	{
		get
		{
			return tool;
		}
		set
		{
			tool = Math.Clamp(value, 0, Math.Max(0, Options.Tools.Count - 1));
		}
	}

	internal bool IsPresented => Win32.IsWindowVisible(handle);

	internal Win32.POINT Center => new Win32.POINT(position.X + (int)(82.0 * Scale), position.Y + (int)(82.0 * Scale));

	private double CenterRadius => Math.Min(70.0, 32.0 * Win32.Scale(position) / Scale);

	private double Scale => Win32.Scale(position) * Options.Size / 164.0;

	private string ToolName
	{
		get
		{
			if (Options.Tools.Count != 0)
			{
				return Options.Tools[Math.Clamp(tool, 0, Options.Tools.Count - 1)].Name;
			}
			return "未设置工具";
		}
	}

	public event Action? VisibilityChanged;

	public event Action<GestureAction>? Trigger;

	internal void PlaySummon()
	{
		summon.Start();
	}

	private void ForegroundChanged(nint h)
	{
		if (!disposed && lastOptions != Options)
		{
			CancelInput();
			lastOptions = Options;
			SettingsChanged();
			Wake();
		}
	}

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool UnregisterClass(string name, nint module);

	private Win32.POINT Centroid()
	{
		long num = 0L;
		long num2 = 0L;
		foreach (Win32.POINT value in contacts.Values)
		{
			num += value.X;
			num2 += value.Y;
		}
		return new Win32.POINT((int)(num / contacts.Count), (int)(num2 / contacts.Count));
	}

	private void SetMenu(bool value)
	{
		if (menu != value)
		{
			menuFrom = outerScale;
			menuStarted = Environment.TickCount64;
			menu = value;
			if (value)
			{
				ShowSelectionHint();
			}
			else
			{
				hint?.Hide();
			}
			animation.Start();
		}
	}

	private void Pulse(double amount)
	{
		pulseAmplitude = amount;
		pulseStarted = Environment.TickCount64;
		animation.Start();
	}

	public VirtualDial(SettingsStore settings)
	{
		summon = new SummonAnimation(Draw);
		summon.Finished += CloseFinished;
		store = settings;
		callback = Procedure;
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = callback,
			hInstance = Win32.GetModuleHandle(null),
			hCursor = Win32.ArrowCursor,
			lpszClassName = windowClass
		};
		Win32.RegisterClass(ref wc);
		handle = Win32.CreateWindowEx(134742152u, wc.lpszClassName, "虚拟 Dial · 软件模式", 2147483648u, 0, 0, 1, 1, 0, 0, wc.hInstance, 0);
		Win32.SuppressFeedback(handle);
		OverlayOrder.Register(handle);
		mousePass = new MousePassthrough(handle, () => !Options.MouseEnabled);
		position = DefaultPosition();
		savedX = Options.X;
		savedY = Options.Y;
		if (savedX.HasValue && savedY.HasValue)
		{
			position = new Win32.POINT(savedX.Value, savedY.Value);
		}
		fx = position.X;
		fy = position.Y;
		DispatcherQueue forCurrentThread = DispatcherQueue.GetForCurrentThread();
		rotationOutput = forCurrentThread.CreateTimer();
		rotationOutput.Interval = TimeSpan.FromMilliseconds(16.0);
		rotationOutput.IsRepeating = false;
		rotationOutput.Tick += delegate
		{
			FlushRotation();
		};
		hold = forCurrentThread.CreateTimer();
		hold.IsRepeating = false;
		hold.Tick += delegate
		{
			if (pressed && center && !moving)
			{
				click?.Stop();
				taps = 0;
				SetMenu(value: true);
				held = true;
				Pulse(0.045);
				Draw();
			}
		};
		labelTimer = forCurrentThread.CreateTimer();
		labelTimer.Interval = TimeSpan.FromSeconds(3.0);
		labelTimer.IsRepeating = false;
		labelTimer.Tick += delegate
		{
			if (!pressed)
			{
				showLabel = false;
				Draw();
			}
		};
		idle = forCurrentThread.CreateTimer();
		idle.IsRepeating = false;
		idle.Tick += delegate
		{
			if (!pressed && !menu)
			{
				isIdle = true;
				animation?.Start();
			}
		};
		click = forCurrentThread.CreateTimer();
		click.IsRepeating = false;
		click.Interval = TimeSpan.FromMilliseconds(DoubleClickTime());
		click.Tick += delegate
		{
			FlushClick();
		};
		animation = forCurrentThread.CreateTimer();
		animation.Interval = TimeSpan.FromMilliseconds(16.0);
		animation.Tick += delegate
		{
			Animate();
		};
		store.Changed += SettingsChanged;
		AccentTheme.Changed += Draw;
		OverlayOrder.ForegroundChanged += ForegroundChanged;
	}

	[DllImport("user32", EntryPoint = "GetDoubleClickTime")]
	private static extern uint DoubleClickTime();

	private Win32.POINT DefaultPosition()
	{
		Win32.GetCursorPos(out var p);
		Win32.RECT rcWork = Win32.Monitor(p).rcWork;
		return new Win32.POINT(rcWork.Left + (rcWork.Width - (int)(Options.Size * Win32.Scale(p))) / 2, rcWork.Top + (rcWork.Height - (int)(Options.Size * Win32.Scale(p))) / 2);
	}

	private void SettingsChanged()
	{
		if (!disposed)
		{
			if (!Options.MouseEnabled && mousePressed)
			{
				CancelInput();
				Win32.ReleaseCapture();
			}
			tool = Math.Clamp(tool, 0, Math.Max(0, Options.Tools.Count - 1));
			if (savedX != Options.X || savedY != Options.Y)
			{
				savedX = Options.X;
				savedY = Options.Y;
				position = ((savedX.HasValue && savedY.HasValue) ? new Win32.POINT(savedX.Value, savedY.Value) : DefaultPosition());
				fx = position.X;
				fy = position.Y;
			}
			Draw();
		}
	}

	public void SetSuppressed(bool value)
	{
		if (suppressed != value)
		{
			suppressed = value;
			if (value)
			{
				closing = false;
				CancelInput();
				animation.Stop();
				hint?.Hide();
				Win32.ShowWindow(handle, 0);
			}
			else if (visible)
			{
				Wake();
				summon.Start();
				Draw();
			}
		}
	}

	private void CancelInput()
	{
		rotationOutput.Stop();
		pendingAngle = (scrollRemainder = 0.0);
		pressed = (mousePressed = (moving = (momentum = false)));
		contacts.Clear();
		twoFinger = false;
		hold.Stop();
		click.Stop();
		taps = 0;
		menu = false;
		outerScale = (menuFrom = 1.0);
		menuStarted = 0L;
	}

	internal void PositionAt(Win32.POINT point)
	{
		CancelInput();
		double num = Win32.Scale(point) * Options.Size / 164.0;
		Win32.RECT rcMonitor = Win32.Monitor(point).rcMonitor;
		(int, int) tuple = GestureGeometry.CenteredOverlay(point.X, point.Y, (int)(164.0 * num), (int)(164.0 * num), rcMonitor.Left, rcMonitor.Top, rcMonitor.Width, rcMonitor.Height);
		position = new Win32.POINT(tuple.Item1, tuple.Item2);
		fx = position.X;
		fy = position.Y;
		SavePosition();
	}

	public void Toggle()
	{
		if (!closing)
		{
			if (!visible)
			{
				visible = true;
				Wake();
				summon.Start();
				Draw();
				this.VisibilityChanged?.Invoke();
			}
			else
			{
				CancelInput();
				idle.Stop();
				labelTimer.Stop();
				hint?.Hide();
				closing = true;
				summon.StartReverse(450.0);
			}
		}
	}

	private void CloseFinished()
	{
		if (closing)
		{
			closing = false;
			visible = false;
			animation.Stop();
			Win32.ShowWindow(handle, 0);
			this.VisibilityChanged?.Invoke();
		}
	}

	private void Wake()
	{
		showLabel = true;
		isIdle = false;
		labelTimer.Stop();
		labelTimer.Start();
		idle.Stop();
		idle.Interval = TimeSpan.FromMilliseconds(Math.Max(1.0, Options.IdleSeconds * 1000.0));
		idle.Start();
		if (visible && !suppressed)
		{
			animation.Start();
		}
	}

	private Win32.POINT Bound()
	{
		Win32.RECT rcMonitor = Win32.Monitor(position).rcMonitor;
		int num = (int)Math.Ceiling(164.0 * Scale);
		return new Win32.POINT(Math.Clamp(position.X, rcMonitor.Left, Math.Max(rcMonitor.Left, rcMonitor.Right - num)), Math.Clamp(position.Y, rcMonitor.Top, Math.Max(rcMonitor.Top, rcMonitor.Bottom - num)));
	}

	private void SavePosition()
	{
		int? num = (Options.X = position.X);
		savedX = num;
		num = (Options.Y = position.Y);
		savedY = num;
		store.Save();
	}

	private void Animate()
	{
		if (momentum)
		{
			Win32.POINT pOINT = Bound();
			vx = (vx + ((double)pOINT.X - fx) * 0.018) * 0.8;
			vy = (vy + ((double)pOINT.Y - fy) * 0.018) * 0.8;
			fx += vx * 16.0;
			fy += vy * 16.0;
			position = new Win32.POINT((int)Math.Round(fx), (int)Math.Round(fy));
			if (Math.Abs(vx) + Math.Abs(vy) < 0.06)
			{
				position = Bound();
				fx = position.X;
				fy = position.Y;
				momentum = false;
				SavePosition();
			}
		}
		double num = (double)(Environment.TickCount64 - pulseStarted) / 260.0;
		pulseScale = ((num >= 1.0) ? 1.0 : (1.0 + pulseAmplitude * Math.Sin(Math.PI * num)));
		double num2 = (double)(Environment.TickCount64 - menuStarted) / 450.0;
		double num3 = (menu ? 1.5 : 1.0);
		outerScale = ((num2 >= 1.0) ? num3 : (num3 + (menuFrom - num3) * Math.Exp(-8.0 * num2) * Math.Cos(12.0 * num2)));
		double num4 = (pressed ? 1.035 : 1.0);
		double num5 = (isIdle ? Options.IdleOpacity : Options.Opacity);
		pressScale = GestureGeometry.Ease(pressScale, num4, 0.24, 0.001);
		opacity = GestureGeometry.Ease(opacity, num5, 0.2, 0.003);
		Draw();
		if (!momentum && num2 >= 1.0 && num >= 1.0 && pressScale == num4 && opacity == num5)
		{
			animation.Stop();
		}
	}

	private double Angle(Win32.POINT p)
	{
		return Math.Atan2((double)(p.Y - position.Y) - 82.0 * Scale, (double)(p.X - position.X) - 82.0 * Scale) * 180.0 / Math.PI;
	}

	private void Down(Win32.POINT p)
	{
		if (!pressed && !suppressed && visible)
		{
			Wake();
			momentum = false;
			vx = (vy = 0.0);
			pressed = true;
			held = (moving = false);
			origin = (lastPoint = p);
			initial = position;
			lastMoved = Environment.TickCount64;
			double num = (double)(p.X - position.X) / Scale - 82.0;
			double num2 = (double)(p.Y - position.Y) / Scale - 82.0;
			center = num * num + num2 * num2 < CenterRadius * CenterRadius;
			shake.Reset((double)p.X / Win32.Scale(p), (double)p.Y / Win32.Scale(p), lastMoved);
			angle = Angle(p);
			remainder = 0.0;
			pendingAngle = (scrollRemainder = 0.0);
			hold.Interval = TimeSpan.FromMilliseconds(Options.HoldSeconds * 1000.0);
			if (center)
			{
				hold.Start();
			}
			Draw();
		}
	}

	private void Move(Win32.POINT p)
	{
		if (!pressed)
		{
			return;
		}
		if (center || twoFinger)
		{
			if (!((double)Math.Max(Math.Abs(p.X - origin.X), Math.Abs(p.Y - origin.Y)) < 8.0 * Win32.Scale(p)) || moving)
			{
				moving = true;
				hold.Stop();
				click.Stop();
				taps = 0;
				long tickCount = Environment.TickCount64;
				double num = Math.Max(8L, tickCount - lastMoved);
				vx = 0.4 * vx + 0.6 * (double)(p.X - lastPoint.X) / num;
				vy = 0.4 * vy + 0.6 * (double)(p.Y - lastPoint.Y) / num;
				lastMoved = tickCount;
				lastPoint = p;
				fx = initial.X + p.X - origin.X;
				fy = initial.Y + p.Y - origin.Y;
				position = new Win32.POINT((int)fx, (int)fy);
				if (Options.ShakeToClose && shake.Add((double)p.X / Win32.Scale(p), (double)p.Y / Win32.Scale(p), tickCount))
				{
					Toggle();
				}
				else
				{
					Draw();
				}
			}
		}
		else
		{
			if (!moving && (double)Math.Max(Math.Abs(p.X - origin.X), Math.Abs(p.Y - origin.Y)) < 1.0 * Win32.Scale(p))
			{
				return;
			}
			double num2 = Angle(p);
			double num3 = (num2 - angle + 540.0) % 360.0 - 180.0;
			angle = num2;
			if (menu)
			{
				if (!(Math.Abs(num3) < 0.2))
				{
					moving = true;
					hold.Stop();
					int num4 = GestureGeometry.DialIndex(num2 + 180.0, Options.Tools.Count);
					if (tool != num4)
					{
						tool = num4;
						ShowSelectionHint();
					}
					Wake();
					Draw();
				}
				return;
			}
			bool flag = Options.Tools.Count > 0;
			if (flag)
			{
				string preset = Options.Tools[tool].Preset;
				bool flag2 = ((preset == "Scroll" || preset == "Volume") ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				hold.Stop();
				moving = true;
				pendingAngle += num3;
				if (!rotationOutput.IsRunning)
				{
					rotationOutput.Start();
				}
				Wake();
				Draw();
				return;
			}
			remainder += num3 * Options.Sensitivity;
			Draw();
			if (Math.Abs(remainder) < 15.0)
			{
				return;
			}
			hold.Stop();
			moving = true;
			int num5 = Math.Clamp((int)(remainder / 15.0), -8, 8);
			remainder -= num5 * 15;
			try
			{
				for (int i = 0; i < Math.Min(8, Math.Abs(num5)); i++)
				{
					Rotate(Math.Sign(num5));
				}
			}
			catch (Exception ex)
			{
				Diagnostics.Error(ex, "Dial.Rotate");
				App.CurrentApp.Report(ex.Message);
			}
			Wake();
			Draw();
		}
	}

	private void Rotate(int direction)
	{
		if (Options.Tools.Count == 0)
		{
			return;
		}
		DialTool dialTool = Options.Tools[tool];
		GestureAction gestureAction = ((direction > 0) ? dialTool.Clockwise : dialTool.CounterClockwise);
		if (gestureAction.Enabled)
		{
			Diagnostics.State("Dial", "旋转工具类型=" + dialTool.Preset);
			switch (dialTool.Preset)
			{
			case "Volume":
				SystemControl.SendKeys(new int[1] { (direction > 0) ? 175 : 174 });
				break;
			case "Scroll":
				KeyboardInput.Wheel(-direction * 120);
				break;
			case "Zoom":
				SystemControl.SendKeys(new int[2]
				{
					17,
					(direction > 0) ? 187 : 189
				});
				break;
			case "Undo":
				SystemControl.SendKeys(new int[2]
				{
					17,
					(direction > 0) ? 89 : 90
				});
				break;
			default:
				this.Trigger?.Invoke((direction > 0) ? dialTool.Clockwise : dialTool.CounterClockwise);
				break;
			}
			if (Options.RotationSound)
			{
				sound.Tick();
			}
			ShowBehaviorHint(gestureAction, dialTool.RotationHint(direction, ActionNames.Describe(gestureAction)), dialTool.Preset);
		}
	}

	private void FlushRotation()
	{
		rotationOutput.Stop();
		double num = pendingAngle;
		pendingAngle = 0.0;
		if (num == 0.0 || Options.Tools.Count == 0)
		{
			return;
		}
		DialTool dialTool = Options.Tools[tool];
		GestureAction gestureAction = ((num > 0.0) ? dialTool.Clockwise : dialTool.CounterClockwise);
		if (!gestureAction.Enabled)
		{
			return;
		}
		try
		{
			if (dialTool.Preset == "Volume")
			{
				AudioEndpoint.Adjust(DialMotion.VolumeDelta(num, Options.Sensitivity));
			}
			else
			{
				if (!(dialTool.Preset == "Scroll"))
				{
					return;
				}
				scrollRemainder += DialMotion.WheelDelta(num, Options.Sensitivity);
				int num2 = (int)scrollRemainder;
				scrollRemainder -= num2;
				if (num2 == 0)
				{
					return;
				}
				KeyboardInput.Wheel(num2);
			}
			if (Options.RotationSound)
			{
				sound.Tick();
			}
			ShowBehaviorHint(gestureAction, dialTool.RotationHint(Math.Sign(num), ActionNames.Describe(gestureAction)), dialTool.Preset);
		}
		catch (Exception ex)
		{
			Diagnostics.Error(ex, "Dial.Continuous");
			App.CurrentApp.Report("旋钮操作暂时不可用：" + ex.Message);
		}
	}

	internal void CheckSelectionHint()
	{
		SetMenu(value: true);
		if (selectionHint != Options.Tools[tool].Name)
		{
			throw new InvalidOperationException("工具选择未显示提示");
		}
		SetMenu(value: false);
	}

	private void ShowSelectionHint()
	{
		if (Options.CenterToolHint && Options.Tools.Count != 0)
		{
			DialTool dialTool = Options.Tools[tool];
			selectionHint = dialTool.Name;
			if (hint == null)
			{
				hint = new CenterHint();
			}
			hint.Show(new GestureAction
			{
				Label = dialTool.Name,
				Icon = ""
			}, Center);
		}
	}

	private void ShowBehaviorHint(GestureAction action, string fallback, string preset = "")
	{
		if (Options.CenterToolHint)
		{
			if (hint == null)
			{
				hint = new CenterHint();
			}
			string text = "";
			string label = ((action.Label.Length > 0) ? Localization.T(action.Label) : (string.IsNullOrWhiteSpace(fallback) ? ActionNames.Describe(action) : Localization.T(fallback)));
			hint.Show(new GestureAction
			{
				Label = label,
				Icon = ((action.Icon.Length > 0) ? action.Icon : text),
				IconPath = action.IconPath,
				IconFont = action.IconFont,
				TextColor = action.TextColor,
				TextOpacity = action.TextOpacity
			}, Center, 1.5, custom: true, rectangle: true);
		}
	}

	private void FlushClick()
	{
		click.Stop();
		int num = taps;
		taps = 0;
		if (num <= 0 || !visible || suppressed)
		{
			return;
		}
		if (num > 1)
		{
			SetMenu(!menu);
			Pulse(menu ? 0.045 : (-0.05));
			Wake();
			Draw();
		}
		else if (!outerTap && Options.Tools.Count > 0)
		{
			GestureAction tap = Options.Tools[tool].Tap;
			if (tap != null && tap.Enabled && tap.Kind != ActionKind.None)
			{
				this.Trigger?.Invoke(tap);
				ShowBehaviorHint(tap, ActionNames.Describe(tap));
			}
		}
	}

	private void Up()
	{
		if (!pressed)
		{
			return;
		}
		FlushRotation();
		pressed = false;
		hold.Stop();
		if (moving && center)
		{
			if (Environment.TickCount64 - lastMoved > 100)
			{
				vx = (vy = 0.0);
			}
			vx = Math.Clamp(vx, -1.2, 1.2);
			vy = Math.Clamp(vy, -1.2, 1.2);
			momentum = true;
		}
		else if (!moving && !held)
		{
			if (menu)
			{
				click.Stop();
				taps = 0;
				SetMenu(value: false);
				hint?.Hide();
				Pulse(-0.05);
			}
			else
			{
				if (taps > 0 && outerTap == center)
				{
					click.Stop();
					taps = 0;
				}
				outerTap = !center;
				taps++;
				click.Stop();
				if (taps > 1)
				{
					FlushClick();
				}
				else
				{
					click.Start();
				}
			}
		}
		moving = false;
		Wake();
		Draw();
	}

	private void TouchInput(uint message, uint id, Win32.POINT point, uint flags)
	{
		if ((flags & 0x8000) != 0)
		{
			CancelInput();
		}
		else if (message == 582)
		{
			contacts[id] = point;
			if (contacts.Count > 2)
			{
				CancelInput();
			}
			else if (contacts.Count == 1)
			{
				pointer = id;
				Down(point);
			}
			else if (contacts.Count == 2)
			{
				twoFinger = true;
				center = true;
				held = true;
				moving = true;
				hold.Stop();
				click.Stop();
				taps = 0;
				origin = (lastPoint = Centroid());
				initial = position;
				vx = (vy = 0.0);
			}
		}
		else
		{
			if (!contacts.ContainsKey(id))
			{
				return;
			}
			contacts[id] = point;
			if (message == 581)
			{
				if (twoFinger && contacts.Count >= 2)
				{
					Move(Centroid());
				}
				else if (!twoFinger && id == pointer)
				{
					Move(point);
				}
				return;
			}
			contacts.Remove(id);
			if (contacts.Count == 0)
			{
				Up();
				twoFinger = false;
			}
			else if (twoFinger)
			{
				vx = (vy = 0.0);
			}
		}
	}

	private nint Procedure(nint h, uint message, nuint w, nint l)
	{
		bool flag;
		switch (message)
		{
		case 33u:
			return 3;
		case 32u:
			Win32.SetCursor(Win32.ArrowCursor);
			return 1;
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
			uint id = (uint)((int)w & 0xFFFF);
			flag = Win32.GetPointerInfo(id, out var info);
			if (flag)
			{
				uint pointerType = info.pointerType;
				bool flag2 = pointerType - 2 <= 1;
				flag = flag2;
			}
			if (flag)
			{
				TouchInput(message, id, info.ptPixelLocation, info.pointerFlags);
				return 0;
			}
		}
		if (message - 512 <= 2)
		{
			if (!Options.MouseEnabled)
			{
				return 0;
			}
			if (((ulong)Win32.GetMessageExtraInfo() & 0xFFFFFF00uL) == 4283520768u)
			{
				return 0;
			}
			Win32.GetCursorPos(out var p);
			if (message == 513 && !pressed)
			{
				mousePressed = true;
				Down(p);
				Win32.SetCapture(h);
			}
			else if (message == 512 && mousePressed)
			{
				Move(p);
			}
			else if (message == 514 && mousePressed)
			{
				mousePressed = false;
				Up();
				Win32.ReleaseCapture();
			}
			return 0;
		}
		if (message == 588)
		{
			CancelInput();
		}
		if (message == 533 && mousePressed)
		{
			CancelInput();
		}
		return Win32.DefWindowProc(h, message, w, l);
	}

	private void Draw()
	{
		if (disposed || !visible || suppressed)
		{
			return;
		}
		double scale = Scale;
		double num = Math.Max(94.0, Math.Ceiling(78.0 * outerScale + 16.0));
		int num2 = Math.Max(1, (int)Math.Ceiling(2.0 * num * scale));
		Win32.POINT dst = new Win32.POINT(position.X - (int)((num - 82.0) * scale), position.Y - (int)((num - 82.0) * scale));
		using Bitmap bitmap = new Bitmap(num2, num2, PixelFormat.Format32bppPArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.ScaleTransform((float)scale, (float)scale);
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.TranslateTransform((float)num, (float)num);
			graphics.ScaleTransform((float)(pressScale * pulseScale), (float)(pressScale * pulseScale));
			graphics.TranslateTransform(-82f, -82f);
			using SolidBrush brush = new SolidBrush(ColorTranslator.FromHtml(Options.OuterColor));
			(double, double) tuple = summon.Sample(CenterRadius / 78.0);
			float num3 = (float)(78.0 * outerScale * tuple.Item2);
			if (num3 > 0.1f)
			{
				graphics.FillEllipse(brush, 82f - num3, 82f - num3, 2f * num3, 2f * num3);
			}
			using SolidBrush brush2 = new SolidBrush(ColorTranslator.FromHtml(Options.InnerColor));
			float num4 = (float)(78.0 * tuple.Item1);
			graphics.FillEllipse(brush2, 82f - num4, 82f - num4, 2f * num4, 2f * num4);
			if (moving && center && Options.MoveOutline)
			{
				Windows.UI.Color color = AccentTheme.Color;
				using Pen pen = new Pen(System.Drawing.Color.FromArgb(color.R, color.G, color.B), 4f);
				graphics.DrawEllipse(pen, -4, -4, 172, 172);
			}
			if (PointerVisible)
			{
				double num5 = (angle + 180.0) * Math.PI / 180.0;
				using Pen pen2 = new Pen(System.Drawing.Color.White, 1.8f)
				{
					StartCap = LineCap.Round,
					EndCap = LineCap.Round
				};
				graphics.DrawLine(pen2, 82f + (float)Math.Cos(num5) * 61f, 82f + (float)Math.Sin(num5) * 61f, 82f + (float)Math.Cos(num5) * 73f, 82f + (float)Math.Sin(num5) * 73f);
			}
			if (menu && Options.Tools.Count > 0)
			{
				Windows.UI.Color color2 = AccentTheme.Color;
				using Pen pen3 = new Pen(System.Drawing.Color.FromArgb(color2.R, color2.G, color2.B), 3f);
				float num6 = 360f / (float)Options.Tools.Count;
				float num7 = (float)(76.5 * outerScale);
				graphics.DrawArc(pen3, 82f - num7, 82f - num7, 2f * num7, 2f * num7, (float)(angle + 180.0 - (double)(num6 / 2f)), Math.Max(0.5f, num6 - 2f));
			}
			double num8 = Math.Clamp((summon.Progress - 0.95) / 0.05, 0.0, 1.0);
			if ((showLabel || menu) && num8 > 0.0)
			{
				using Font font = IconFontResolver.CreateTextFont("Microsoft YaHei UI", (float)(12.0 * Win32.Scale(position) / Scale), GraphicsUnit.Pixel, "Microsoft YaHei", "DengXian", "Segoe UI");
				using StringFormat format = new StringFormat
				{
					Alignment = StringAlignment.Center,
					LineAlignment = StringAlignment.Center,
					Trimming = StringTrimming.EllipsisCharacter
				};
				using SolidBrush brush3 = new SolidBrush(System.Drawing.Color.FromArgb((int)Math.Round(255.0 * num8), 255, 255, 255));
				float num9 = (float)((1.0 - num8) * 10.0);
				graphics.DrawString(ToolName, font, brush3, new RectangleF(86f - num4, 86f - num4 + num9, 2f * num4 - 8f, 2f * num4 - 8f), format);
			}
		}
		nint dC = Win32.GetDC(0);
		nint num10 = Win32.CreateCompatibleDC(dC);
		nint hbitmap = bitmap.GetHbitmap(System.Drawing.Color.FromArgb(0));
		nint o = Win32.SelectObject(num10, hbitmap);
		try
		{
			Win32.SIZE size = new Win32.SIZE(num2, num2);
			Win32.POINT pOINT = default(Win32.POINT);
			Win32.BLENDFUNCTION blend = new Win32.BLENDFUNCTION
			{
				SourceConstantAlpha = (byte)(255.0 * Math.Clamp(opacity, 0.0, 1.0)),
				AlphaFormat = 1
			};
			Win32.UpdateLayeredWindow(handle, dC, ref dst, ref size, num10, ref pOINT, 0u, ref blend, 2u);
			Win32.ShowWindow(handle, 4);
		}
		finally
		{
			Win32.SelectObject(num10, o);
			Win32.DeleteObject(hbitmap);
			Win32.DeleteDC(num10);
			Win32.ReleaseDC(0, dC);
		}
	}

	internal async Task CheckAsync()
	{
		Toggle();
		Down(P(82, 82));
		for (int i = 0; i < 25; i++)
		{
			if (menu)
			{
				break;
			}
			await Task.Delay(100);
		}
		if (!menu)
		{
			throw new InvalidOperationException("Dial 长按工具菜单未展开。");
		}
		Up();
		await Task.Delay(500);
		if (Math.Abs(outerScale - 1.5) > 0.005)
		{
			throw new InvalidOperationException("Dial 选择模式未放大外圈至 1.5 倍。");
		}
		Down(P(150, 82));
		if (PointerVisible)
		{
			throw new InvalidOperationException("选择模式仍显示旋转指针。");
		}
		Move(P(82, 150));
		Up();
		if (tool != 0)
		{
			throw new InvalidOperationException("Dial 角度选择工具失败。");
		}
		Down(P(82, 82));
		Up();
		if (menu)
		{
			throw new InvalidOperationException("Dial 点击未确认工具。");
		}
		await Task.Delay(500);
		if (Math.Abs(outerScale - 1.0) > 0.005)
		{
			throw new InvalidOperationException("Dial 确认后外圈未还原。");
		}
		Down(P(82, 82));
		Up();
		Down(P(82, 82));
		Up();
		if (!menu)
		{
			throw new InvalidOperationException("Dial 双击未进入选择模式。");
		}
		Down(P(82, 82));
		Up();
		Down(P(150, 82));
		Up();
		Down(P(150, 82));
		Up();
		if (!menu)
		{
			throw new InvalidOperationException("Dial 外圈双击未进入选择模式。");
		}
		Win32.POINT pOINT = position;
		Down(P(82, 82));
		Move(P(102, 102));
		Up();
		if (!menu || (position.X == pOINT.X && position.Y == pOINT.Y))
		{
			throw new InvalidOperationException("Dial 选择模式拖动失败。");
		}
		await Task.Delay(500);
		Down(P(180, 82));
		Up();
		if (menu)
		{
			throw new InvalidOperationException("Dial 外圈单击未确认。");
		}
		await Task.Delay(500);
		Win32.POINT pOINT2 = position;
		Down(P(82, 82));
		Move(P(102, 102));
		Up();
		if (position.X == pOINT2.X && position.Y == pOINT2.Y)
		{
			throw new InvalidOperationException("Dial 中心拖动失败。");
		}
		await Task.Delay(500);
		Win32.POINT pOINT3 = position;
		Win32.POINT point = P(40, 82);
		Win32.POINT point2 = P(124, 82);
		TouchInput(582u, 901u, point, 0u);
		TouchInput(582u, 902u, point2, 0u);
		TouchInput(581u, 901u, new Win32.POINT(point.X + 30, point.Y + 20), 0u);
		TouchInput(581u, 902u, new Win32.POINT(point2.X + 30, point2.Y + 20), 0u);
		if (position.X != pOINT3.X + 30 || position.Y != pOINT3.Y + 20)
		{
			throw new InvalidOperationException("Dial 双指重心拖动失败。");
		}
		TouchInput(583u, 901u, new Win32.POINT(point.X + 30, point.Y + 20), 0u);
		TouchInput(583u, 902u, new Win32.POINT(point2.X + 30, point2.Y + 20), 0u);
		if (menu || taps != 0)
		{
			throw new InvalidOperationException("Dial 双指拖动误触点击。");
		}
		await Task.Delay(500);
		bool shakeToClose = Options.ShakeToClose;
		try
		{
			Options.ShakeToClose = true;
			Win32.POINT p = P(82, 82);
			double num = Win32.Scale(p);
			Down(p);
			int[] array = new int[4] { 40, -40, 40, -40 };
			foreach (int num2 in array)
			{
				Move(new Win32.POINT(p.X + (int)((double)num2 * num), p.Y));
			}
			if (visible)
			{
				throw new InvalidOperationException("Dial 晃动未关闭。");
			}
		}
		finally
		{
			Options.ShakeToClose = shakeToClose;
			if (visible)
			{
				Toggle();
			}
		}
		Win32.POINT P(int x, int y)
		{
			return new Win32.POINT(position.X + (int)((double)x * Scale), position.Y + (int)((double)y * Scale));
		}
	}

	internal void CheckToolBindings()
	{
		Action<GestureAction> trigger = this.Trigger;
		List<DialTool> tools = Options.Tools;
		int num = tool;
		try
		{
			DialTool dialTool = new DialTool
			{
				Name = "甲",
				Tap = new GestureAction
				{
					Kind = ActionKind.Keys,
					Keys = new List<int> { 65 }
				},
				Clockwise = new GestureAction
				{
					Label = "顺时针",
					Icon = "\ue7a7"
				},
				CounterClockwise = new GestureAction
				{
					Label = "逆时针"
				}
			};
			DialTool dialTool2 = new DialTool
			{
				Name = "乙",
				Tap = new GestureAction
				{
					Kind = ActionKind.Keys,
					Keys = new List<int> { 66 }
				}
			};
			Options.Tools = new List<DialTool> { dialTool, dialTool2 };
			List<GestureAction> list = new List<GestureAction>();
			this.Trigger = list.Add;
			visible = true;
			suppressed = false;
			menu = false;
			outerTap = false;
			tool = 0;
			taps = 1;
			FlushClick();
			tool = 1;
			taps = 1;
			FlushClick();
			tool = 0;
			Rotate(1);
			Rotate(-1);
			if (list.Count != 4 || list[0] != dialTool.Tap || list[1] != dialTool2.Tap || list[2] != dialTool.Clockwise || list[3] != dialTool.CounterClockwise)
			{
				throw new InvalidOperationException("Dial 没有按当前工具分发三个独立行为。");
			}
		}
		finally
		{
			Options.Tools = tools;
			tool = num;
			this.Trigger = trigger;
			visible = false;
			hint?.Hide();
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			CancelInput();
			rotationOutput.Stop();
			sound.Dispose();
			mousePass.Dispose();
			summon.Dispose();
			store.Changed -= SettingsChanged;
			OverlayOrder.ForegroundChanged -= ForegroundChanged;
			hold.Stop();
			labelTimer.Stop();
			animation.Stop();
			idle.Stop();
			click.Stop();
			hint?.Dispose();
			AccentTheme.Changed -= Draw;
			OverlayOrder.Remove(handle);
			Win32.DestroyWindow(handle);
			UnregisterClass(windowClass, Win32.GetModuleHandle(null));
		}
	}
}
