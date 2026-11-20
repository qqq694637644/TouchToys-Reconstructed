using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.Services;
using TouchAssistant.UI;
using Windows.UI;

namespace TouchAssistant.Native;

internal sealed class ShortcutArk : IDisposable
{
	private readonly SettingsStore store;

	private readonly SummonAnimation summon;

	private readonly Win32.WndProc callback;

	private readonly Dictionary<nint, ArkBubble?> windows = new Dictionary<nint, ArkBubble>();

	private readonly Dictionary<string, Bitmap> images = new Dictionary<string, Bitmap>();

	private readonly DispatcherQueue queue;

	private readonly DispatcherQueueTimer hold;

	private readonly DispatcherQueueTimer idle;

	private readonly DispatcherQueueTimer animation;

	private readonly DispatcherQueueTimer click;

	private ArkProfile profile;

	private readonly ShakeDetector shake = new ShakeDetector();

	private nint main;

	private nint pressed;

	private readonly Dictionary<nint, MousePassthrough> passes = new Dictionary<nint, MousePassthrough>();

	private readonly Dictionary<nint, (string Key, int X, int Y)> rendered = new Dictionary<nint, (string, int, int)>();

	private readonly Dictionary<ArkBubble, (string Key, double Width)> measured = new Dictionary<ArkBubble, (string, double)>();

	private readonly Dictionary<ArkBubble, double> bubbleProgress = new Dictionary<ArkBubble, double>();

	private ArkBubble? pendingBubble;

	private ArkBubble? heldBubble;

	private bool captureHeldFixture;

	private readonly HeldKeys heldKeys;

	private ArkProfile? pendingProfile;

	private int taps;

	private bool suppressed;

	private bool preview;

	private bool mousePressed;

	private double followX;

	private double followY;

	private double followVx;

	private double followVy;

	private Win32.POINT origin;

	private Win32.POINT initial;

	private Win32.POINT position;

	private readonly TwoFingerDrag twoFinger = new TwoFingerDrag();

	private uint pointer;

	private bool dragging;

	private bool consumed;

	private bool expanded;

	private bool disposed;

	private bool queued;

	private bool isIdle;

	private bool armed;

	private double opacity = 1.0;

	private double menuProgress;

	private double pressScale = 1.0;

	private double vx;

	private double vy;

	private bool momentum;

	private Win32.POINT lastPoint;

	private long lastMoved;

	private nint foregroundWindow;

	private string settingsStamp = "";

	private string profileTargets = "";

	private ArkProfile Gestures => profile;

	private ArkOptions Options => profile.Options;

	internal Win32.POINT TriggerOrigin { get; private set; }

	internal Win32.POINT Center => new Win32.POINT(position.X + (int)(Options.ArkSize * Win32.Scale(position) / 2.0), position.Y + (int)(Options.ArkSize * Win32.Scale(position) / 2.0));

	internal bool IsPresented => Win32.IsWindowVisible(main);

	public event Action<GestureAction>? Trigger;

	internal void PlaySummon()
	{
		summon.Start();
	}

	private void ReleaseHeld()
	{
		try
		{
			heldKeys.Release();
			heldBubble = null;
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Ark.ReleaseHeld");
			App.CurrentApp.Report("持续按键释放失败，请检查目标应用权限。");
		}
	}

	internal async Task CheckAsync()
	{
		bool enabled = Options.ArkEnabled;
		bool follow = Options.ArkFollow;
		try
		{
			Options.ArkEnabled = true;
			store.Save();
			await Task.Delay(80);
			ToggleMenu();
			await Task.Delay(300);
			if (windows.Count != profile.Buttons.Count + 1 || !expanded)
			{
				throw new InvalidOperationException("快捷方舟默认气泡创建失败。");
			}
			if (Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--capture-ui") && Win32.GetWindowRect(main, out var r))
			{
				using Bitmap bitmap = new Bitmap(r.Width, r.Height);
				using (Graphics graphics = Graphics.FromImage(bitmap))
				{
					graphics.CopyFromScreen(r.Left, r.Top, 0, 0, new Size(r.Width, r.Height));
				}
				bitmap.Save(Path.Combine(AppContext.BaseDirectory, "ark-style-v082.png"));
			}
			HashSet<nint> handles = new HashSet<nint>(windows.Keys);
			position.X += 16;
			SavePosition();
			await Task.Delay(80);
			if (!handles.SetEquals(windows.Keys))
			{
				throw new InvalidOperationException("移动保存重建了气泡窗口。");
			}
			ArkBubble bubble = profile.Buttons[0];
			pressed = windows.First<KeyValuePair<nint, ArkBubble>>((KeyValuePair<nint, ArkBubble> keyValuePair) => keyValuePair.Value == bubble).Key;
			Hold();
			pressed = 0;
			await Task.Delay(80);
			if (!bubble.Pinned)
			{
				throw new InvalidOperationException("长按未固定气泡。");
			}
			expanded = false;
			Wake();
			await Task.Delay(400);
			if (bubbleProgress[bubble] != 1.0)
			{
				throw new InvalidOperationException("关闭菜单后固定气泡消失。");
			}
			SetSuppressed(value: true);
			if (IsVisible(main))
			{
				throw new InvalidOperationException("面板未隐藏方舟。");
			}
			SetSuppressed(value: false);
			pressed = windows.First<KeyValuePair<nint, ArkBubble>>((KeyValuePair<nint, ArkBubble> keyValuePair) => keyValuePair.Value == bubble).Key;
			Hold();
			pressed = 0;
			for (int wait = 0; wait < 30; wait++)
			{
				if (!bubble.Pinned && bubbleProgress[bubble] == 0.0)
				{
					break;
				}
				await Task.Delay(100);
			}
			if (bubble.Pinned || bubbleProgress[bubble] != 0.0)
			{
				throw new InvalidOperationException($"取消固定后气泡未收起：Pinned={bubble.Pinned},Progress={bubbleProgress[bubble]},Expanded={expanded},Timer={animation.IsRunning}。");
			}
			Options.ArkFollow = false;
			expanded = false;
			ToggleMenu();
			await Task.Delay(80);
			if (profile.Buttons.Exists((ArkBubble b) => !b.FixedX.HasValue || !b.FixedY.HasValue))
			{
				throw new InvalidOperationException("固定气泡未记录屏幕坐标。");
			}
			List<ArkBubble> buttons = profile.Buttons;
			ArkBubble removed = buttons[buttons.Count - 1];
			profile.Buttons.Remove(removed);
			store.Save();
			await Task.Delay(80);
			if (windows.ContainsValue(removed))
			{
				throw new InvalidOperationException("删除气泡后仍保留窗口。");
			}
			profile.Buttons.Add(removed);
			store.Save();
			bool arkShakeToClose = Options.ArkShakeToClose;
			try
			{
				Options.ArkShakeToClose = true;
				Win32.POINT p = position;
				Down(main, p);
				double num = Win32.Scale(p);
				int[] array = new int[4] { 40, -40, 40, -40 };
				foreach (int num3 in array)
				{
					Move(new Win32.POINT(p.X + (int)((double)num3 * num), p.Y));
				}
				if (Options.ArkEnabled)
				{
					throw new InvalidOperationException("快捷方舟晃动未关闭。");
				}
			}
			finally
			{
				Options.ArkShakeToClose = arkShakeToClose;
			}
		}
		finally
		{
			expanded = false;
			menuProgress = 0.0;
			Options.ArkEnabled = enabled;
			Options.ArkFollow = follow;
			store.Save();
		}
	}

	public ShortcutArk(SettingsStore settings, HeldKeys? keys = null)
	{
		heldKeys = keys ?? new HeldKeys();
		summon = new SummonAnimation(DrawAll, compact: true);
		store = settings;
		profile = store.Value.ArkProfiles[0];
		callback = Procedure;
		queue = DispatcherQueue.GetForCurrentThread();
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = callback,
			hInstance = Win32.GetModuleHandle(null),
			hCursor = Win32.ArrowCursor,
			lpszClassName = "TouchAssistant.Ark"
		};
		Win32.RegisterClass(ref wc);
		hold = queue.CreateTimer();
		hold.IsRepeating = false;
		hold.Tick += delegate
		{
			Hold();
		};
		idle = queue.CreateTimer();
		idle.IsRepeating = false;
		idle.Tick += delegate
		{
			if (pressed == 0 && !expanded)
			{
				isIdle = true;
				animation?.Start();
			}
		};
		click = queue.CreateTimer();
		click.IsRepeating = false;
		click.Interval = TimeSpan.FromMilliseconds(Win32DoubleClick());
		click.Tick += delegate
		{
			FlushClick();
		};
		animation = queue.CreateTimer();
		animation.Interval = TimeSpan.FromMilliseconds(16.0);
		animation.Tick += delegate
		{
			Animate();
		};
		store.Changed += SettingsChanged;
		store.Replaced += SettingsReplaced;
		AccentTheme.Changed += DrawAll;
		OverlayOrder.ForegroundChanged += ForegroundChanged;
		Rebuild();
		ForegroundChanged(Win32.GetForegroundWindow());
		if (store.Value.ArkProfiles.Count > 1)
		{
			ResolveProfilesAsync();
		}
	}

	private async void ResolveProfilesAsync()
	{
		try
		{
			await InstalledApps.EnsureAliasesAsync();
			if (!disposed && !preview)
			{
				ChooseProfile();
				DrawAll();
			}
		}
		catch (Exception error)
		{
			App.Log(error);
		}
	}

	[DllImport("user32", EntryPoint = "GetDoubleClickTime")]
	private static extern uint Win32DoubleClick();

	[DllImport("user32", EntryPoint = "IsWindowVisible")]
	private static extern bool IsVisible(nint h);

	public void SetSuppressed(bool value)
	{
		if (suppressed != value)
		{
			suppressed = value;
			if (value)
			{
				preview = false;
				CancelInput();
			}
			DrawAll();
		}
	}

	public void Preview(ArkProfile selected)
	{
		ReleaseHeld();
		preview = true;
		profile = selected;
		expanded = true;
		Rebuild();
		Wake();
	}

	private void CancelInput(bool resetTouches = true)
	{
		if (resetTouches)
		{
			twoFinger.Reset();
		}
		ReleaseHeld();
		pressed = 0;
		hold.Stop();
		click.Stop();
		taps = 0;
		pendingBubble = null;
		momentum = false;
	}

	private void Animate()
	{
		if (momentum)
		{
			Win32.POINT pOINT = BoundedPosition();
			vx = (vx + (double)(pOINT.X - position.X) * 0.018) * 0.8;
			vy = (vy + (double)(pOINT.Y - position.Y) * 0.018) * 0.8;
			position.X += (int)Math.Round(vx * 16.0);
			position.Y += (int)Math.Round(vy * 16.0);
			if (Math.Abs(vx) + Math.Abs(vy) < 0.08)
			{
				ConstrainMain();
				momentum = false;
				SavePosition();
			}
		}
		followVx = (followVx + ((double)position.X - followX) * 0.24) * 0.64;
		followVy = (followVy + ((double)position.Y - followY) * 0.24) * 0.64;
		followX += followVx;
		followY += followVy;
		bool flag = Math.Abs((double)position.X - followX) + Math.Abs((double)position.Y - followY) + Math.Abs(followVx) + Math.Abs(followVy) < 0.5;
		if (flag)
		{
			followX = position.X;
			followY = position.Y;
			followVx = (followVy = 0.0);
		}
		double num = (isIdle ? Options.ArkIdleOpacity : Options.ArkOpacity);
		opacity = GestureGeometry.Ease(opacity, num, 0.2, 0.003);
		menuProgress = GestureGeometry.Ease(menuProgress, expanded ? 1 : 0, 0.25, 0.01);
		double num2 = ((pressed == main) ? 1.045 : 1.0);
		pressScale = GestureGeometry.Ease(pressScale, num2, 0.25, 0.001);
		bool flag2 = true;
		foreach (ArkBubble button in profile.Buttons)
		{
			double num3 = ((expanded || profile.AllPinned || button.Pinned) ? 1 : 0);
			double num4 = GestureGeometry.Ease(bubbleProgress.GetValueOrDefault(button), num3, (num3 == 0.0) ? 0.22 : 0.25, 0.01);
			bubbleProgress[button] = num4;
			flag2 = flag2 && num4 == num3;
		}
		DrawAll();
		if (!momentum && flag && flag2 && opacity == num && menuProgress == (double)(expanded ? 1 : 0) && pressScale == num2)
		{
			animation.Stop();
		}
	}

	internal void PositionAt(Win32.POINT point)
	{
		CancelInput();
		dragging = (armed = false);
		int num = (int)(Options.ArkSize * Win32.Scale(point));
		Win32.RECT rcMonitor = Win32.Monitor(point).rcMonitor;
		(int, int) tuple = GestureGeometry.CenteredOverlay(point.X, point.Y, num, num, rcMonitor.Left, rcMonitor.Top, rcMonitor.Width, rcMonitor.Height);
		position = new Win32.POINT(tuple.Item1, tuple.Item2);
		followX = position.X;
		followY = position.Y;
		followVx = (followVy = 0.0);
		SavePosition();
		Wake();
		DrawAll();
	}

	private void SavePosition()
	{
		Options.ArkX = position.X;
		Options.ArkY = position.Y;
		settingsStamp = Stamp();
		store.Save();
	}

	public void ToggleMenu()
	{
		expanded = !expanded;
		InitializeFixed();
		Wake();
	}

	private void InitializeFixed()
	{
		if (profile.Follow ?? true)
		{
			return;
		}
		bool flag = false;
		double num = Win32.Scale(position);
		foreach (ArkBubble button in profile.Buttons)
		{
			if (!button.FixedX.HasValue || !button.FixedY.HasValue)
			{
				button.FixedX = position.X + (int)(button.X * num);
				button.FixedY = position.Y + (int)(button.Y * num);
				flag = true;
			}
		}
		if (flag)
		{
			settingsStamp = Stamp();
			store.Save();
		}
	}

	public void EnterMove()
	{
		armed = pressed == 0;
		dragging = pressed == main;
		Wake();
	}

	private void Run(GestureAction action, ArkBubble? source = null)
	{
		if (action.Enabled)
		{
			Win32.POINT p = ((source == null) ? position : BubblePosition(source));
			TriggerOrigin = new Win32.POINT(p.X + (int)(((source == null) ? Options.ArkSize : BubbleWidth(source)) * Win32.Scale(p) / 2.0), p.Y + (int)(((source == null) ? Options.ArkSize : 48.0) * Win32.Scale(p) / 2.0));
			if (action.Kind == ActionKind.BuiltIn && action.Command == "ArkMenu")
			{
				ToggleMenu();
			}
			else if (action.Kind == ActionKind.BuiltIn && action.Command == "ArkMove")
			{
				EnterMove();
			}
			else
			{
				this.Trigger?.Invoke(action);
			}
		}
	}

	private void ForegroundChanged(nint h)
	{
		Win32.GetWindowThreadProcessId(h, out var process);
		if (h != foregroundWindow && heldBubble != null)
		{
			ReleaseHeld();
		}
		if (process != Environment.ProcessId)
		{
			preview = false;
			foregroundWindow = h;
			ChooseProfile();
			DrawAll();
		}
	}

	private void ChooseProfile()
	{
		HashSet<string> targets = ((store.Value.ArkProfiles.Count > 1) ? WindowCatalog.TargetsForWindow(foregroundWindow) : new HashSet<string>());
		ArkProfile arkProfile = store.Value.ArkProfiles.Find((ArkProfile p) => p.Target.Length > 0 && WindowCatalog.MatchesTarget(p.Target, targets)) ?? store.Value.ArkProfiles[0];
		if (arkProfile != profile)
		{
			CancelInput();
			profile = arkProfile;
			expanded = false;
			Rebuild();
		}
	}

	private string Stamp()
	{
		return JsonSerializer.Serialize(new
		{
			Options.ArkEnabled,
			Options.ArkSize,
			Options.ArkOpacity,
			Options.ArkIdleSeconds,
			Options.ArkIdleOpacity,
			Options.ArkHoldSeconds,
			Options.ArkMouseEnabled,
			Options.ArkMoveOutline,
			Options.ArkFollow,
			Options.ArkX,
			Options.ArkY,
			store.Value.ArkProfiles
		});
	}

	private void SettingsReplaced()
	{
		preview = false;
		CancelInput();
		ChooseProfile();
		Rebuild();
	}

	internal void CheckImportedProfile()
	{
		if (preview || profile != store.Value.ArkProfiles[0])
		{
			throw new InvalidOperationException("导入后方舟仍引用旧预览配置。");
		}
	}

	private void SettingsChanged()
	{
		string text = string.Join("|", store.Value.ArkProfiles.Select((ArkProfile p) => p.Target));
		if (text != profileTargets)
		{
			profileTargets = text;
			preview = false;
			ResolveProfilesAsync();
		}
		if (queued || disposed || Stamp() == settingsStamp)
		{
			return;
		}
		queued = true;
		queue.TryEnqueue(delegate
		{
			queued = false;
			if (!disposed)
			{
				if (!preview)
				{
					ChooseProfile();
				}
				Rebuild();
			}
		});
	}

	private void RemoveWindow(nint h)
	{
		rendered.Remove(h);
		if (passes.Remove(h, out MousePassthrough value))
		{
			value.Dispose();
		}
		OverlayOrder.Remove(h);
		Win32.DestroyWindow(h);
		windows.Remove(h);
	}

	private void ClearWindows()
	{
		foreach (nint item in new List<nint>(windows.Keys))
		{
			RemoveWindow(item);
		}
	}

	private void Rebuild()
	{
		if (disposed)
		{
			return;
		}
		ReleaseHeld();
		rendered.Clear();
		measured.Clear();
		if (!Options.ArkEnabled && !preview)
		{
			CancelInput();
			ClearWindows();
			main = 0;
			foreach (Bitmap value in images.Values)
			{
				value.Dispose();
			}
			images.Clear();
			bubbleProgress.Clear();
			idle.Stop();
			animation.Stop();
			settingsStamp = Stamp();
			return;
		}
		Win32.RECT rcWork = Win32.Monitor(default(Win32.POINT)).rcWork;
		double num = Win32.Scale(new Win32.POINT(rcWork.Left, rcWork.Top));
		Win32.POINT pOINT = new Win32.POINT(Options.ArkX ?? (rcWork.Left + (int)(200.0 * num)), Options.ArkY ?? (rcWork.Top + (int)(300.0 * num)));
		if (main == 0 || (!dragging && !momentum))
		{
			position = pOINT;
			followX = position.X;
			followY = position.Y;
		}
		if (main == 0)
		{
			main = Add(null);
		}
		foreach (KeyValuePair<nint, ArkBubble> item in new List<KeyValuePair<nint, ArkBubble>>(windows))
		{
			if (item.Value != null && !profile.Buttons.Contains(item.Value))
			{
				if (pressed == item.Key)
				{
					CancelInput();
				}
				RemoveWindow(item.Key);
				bubbleProgress.Remove(item.Value);
			}
		}
		foreach (ArkBubble button in profile.Buttons)
		{
			if (!windows.ContainsValue(button))
			{
				Add(button);
			}
			if (string.IsNullOrEmpty(button.Action.IconPath) || !File.Exists(button.Action.IconPath) || images.ContainsKey(button.Action.IconPath))
			{
				continue;
			}
			try
			{
				using Image image = Image.FromFile(button.Action.IconPath);
				if (image.Width <= 512 && image.Height <= 512)
				{
					images.Add(button.Action.IconPath, new Bitmap(image));
				}
			}
			catch
			{
			}
		}
		foreach (string key in new List<string>(images.Keys))
		{
			if (!profile.Buttons.Exists((ArkBubble b) => b.Action.IconPath == key))
			{
				images[key].Dispose();
				images.Remove(key);
			}
		}
		ConstrainMain();
		InitializeFixed();
		settingsStamp = Stamp();
		Wake();
		DrawAll();
		nint Add(ArkBubble? bubble)
		{
			nint num2 = Win32.CreateWindowEx(134742152u, "TouchAssistant.Ark", "TouchToys 快捷方舟", 2147483648u, 0, 0, 1, 1, 0, 0, Win32.GetModuleHandle(null), 0);
			windows.Add(num2, bubble);
			Win32.SuppressFeedback(num2);
			passes.Add(num2, new MousePassthrough(num2, () => !Options.ArkMouseEnabled && pressed == 0));
			OverlayOrder.Register(num2);
			return num2;
		}
	}

	private void Hold()
	{
		if (pressed != 0 && !dragging)
		{
			consumed = true;
			ArkBubble value;
			if (pressed == main)
			{
				profile.AllPinned = true;
				InitializeFixed();
				store.Save();
				Wake();
			}
			else if (windows.TryGetValue(pressed, out value) && value != null)
			{
				value.Pinned = !value.Pinned;
				store.Save();
				Wake();
			}
		}
	}

	private void Wake()
	{
		isIdle = false;
		idle.Stop();
		idle.Interval = TimeSpan.FromMilliseconds(Math.Max(1.0, Options.ArkIdleSeconds * 1000.0));
		idle.Start();
		animation.Start();
	}

	private Win32.POINT BoundedPosition()
	{
		Win32.RECT rcMonitor = Win32.Monitor(position).rcMonitor;
		int num = (int)Math.Ceiling(Options.ArkSize * Win32.Scale(position));
		return new Win32.POINT(Math.Clamp(position.X, rcMonitor.Left, Math.Max(rcMonitor.Left, rcMonitor.Right - num)), Math.Clamp(position.Y, rcMonitor.Top, Math.Max(rcMonitor.Top, rcMonitor.Bottom - num)));
	}

	private void ConstrainMain()
	{
		position = BoundedPosition();
	}

	private Win32.POINT BubblePosition(ArkBubble bubble)
	{
		double num = Win32.Scale(position);
		if (!(profile.Follow ?? true))
		{
			return new Win32.POINT(bubble.FixedX ?? (position.X + (int)(bubble.X * num)), bubble.FixedY ?? (position.Y + (int)(bubble.Y * num)));
		}
		return new Win32.POINT((int)followX + (int)(bubble.X * num), (int)followY + (int)(bubble.Y * num));
	}

	private Win32.POINT SnapBubble(ArkBubble moving, Win32.POINT proposed)
	{
		double num = Win32.Scale(proposed);
		double w = BubbleWidth(moving) * num;
		double h = 48.0 * num;
		double bestX = 8.0 * num + 1.0;
		double bestY = 8.0 * num + 1.0;
		double offsetX = 0.0;
		double offsetY = 0.0;
		foreach (ArkBubble button in profile.Buttons)
		{
			if (button != moving)
			{
				Win32.POINT p = BubblePosition(button);
				double num2 = Win32.Scale(p);
				Align(p.X, p.Y, BubbleWidth(button) * num2, 48.0 * num2);
			}
		}
		Win32.RECT rcWork = Win32.Monitor(proposed).rcWork;
		Align(rcWork.Left, rcWork.Top, rcWork.Width, rcWork.Height);
		return new Win32.POINT(proposed.X + (int)Math.Round(offsetX), proposed.Y + (int)Math.Round(offsetY));
		void Align(double x, double y, double width, double height)
		{
			double[] array = new double[3]
			{
				x - (double)proposed.X,
				x + (width - w) / 2.0 - (double)proposed.X,
				x + width - w - (double)proposed.X
			};
			double[] array2 = new double[3]
			{
				y - (double)proposed.Y,
				y + (height - h) / 2.0 - (double)proposed.Y,
				y + height - h - (double)proposed.Y
			};
			double[] array3 = array;
			foreach (double num3 in array3)
			{
				if (Math.Abs(num3) < bestX)
				{
					bestX = Math.Abs(num3);
					offsetX = num3;
				}
			}
			array3 = array2;
			foreach (double num4 in array3)
			{
				if (Math.Abs(num4) < bestY)
				{
					bestY = Math.Abs(num4);
					offsetY = num4;
				}
			}
		}
	}

	private void Down(nint h, Win32.POINT p)
	{
		if (pressed == 0)
		{
			momentum = false;
			vx = (vy = 0.0);
			lastPoint = p;
			lastMoved = Environment.TickCount64;
			pressed = h;
			shake.Reset((double)p.X / Win32.Scale(p), (double)p.Y / Win32.Scale(p), Environment.TickCount64);
			origin = p;
			ArkBubble arkBubble = windows[h];
			initial = ((arkBubble != null) ? BubblePosition(arkBubble) : position);
			dragging = armed && h == main;
			armed = false;
			consumed = false;
			Wake();
			hold.Interval = TimeSpan.FromMilliseconds((h == main) ? Math.Max(1.0, Options.ArkHoldSeconds * 1000.0) : 500.0);
			hold.Start();
		}
	}

	private void Move(Win32.POINT p)
	{
		if (pressed == 0)
		{
			return;
		}
		int num = p.X - origin.X;
		int num2 = p.Y - origin.Y;
		double num3 = Win32.Scale(position);
		if (!dragging && (double)Math.Max(Math.Abs(num), Math.Abs(num2)) < 8.0 * num3)
		{
			return;
		}
		if (!dragging && pressed == main && !consumed && (!Gestures.Drag.Enabled || Gestures.Drag.Kind != ActionKind.BuiltIn || !(Gestures.Drag.Command == "ArkMove")))
		{
			consumed = true;
			hold.Stop();
			Run(Gestures.Drag);
		}
		else
		{
			if (consumed && !dragging)
			{
				return;
			}
			dragging = true;
			consumed = true;
			hold.Stop();
			click.Stop();
			taps = 0;
			if (pressed == main)
			{
				if (Options.ArkShakeToClose && shake.Add((double)p.X / Win32.Scale(p), (double)p.Y / Win32.Scale(p), Environment.TickCount64))
				{
					CancelInput();
					Options.ArkEnabled = false;
					store.Save();
					return;
				}
				long tickCount = Environment.TickCount64;
				double num4 = Math.Max(8L, tickCount - lastMoved);
				vx = 0.4 * vx + 0.6 * (double)(p.X - lastPoint.X) / num4;
				vy = 0.4 * vy + 0.6 * (double)(p.Y - lastPoint.Y) / num4;
				lastPoint = p;
				lastMoved = tickCount;
				position = new Win32.POINT(initial.X + num, initial.Y + num2);
				animation.Start();
			}
			else
			{
				ArkBubble arkBubble = windows[pressed];
				if (arkBubble != null)
				{
					Win32.POINT pOINT = SnapBubble(arkBubble, new Win32.POINT(initial.X + num, initial.Y + num2));
					if (profile.Follow ?? true)
					{
						arkBubble.X = ((double)pOINT.X - followX) / num3;
						arkBubble.Y = ((double)pOINT.Y - followY) / num3;
					}
					else
					{
						arkBubble.FixedX = pOINT.X;
						arkBubble.FixedY = pOINT.Y;
					}
				}
			}
			DrawAll();
		}
	}

	private void FlushClick()
	{
		click.Stop();
		int num = taps;
		taps = 0;
		ArkBubble arkBubble = pendingBubble;
		pendingBubble = null;
		if (num == 0 || pendingProfile != profile || (suppressed && !preview))
		{
			return;
		}
		if (arkBubble == null)
		{
			if (profile.AllPinned)
			{
				ReleaseHeld();
				profile.AllPinned = false;
				expanded = false;
				store.Save();
				Wake();
			}
			else
			{
				Run((num >= 3) ? Gestures.TripleTap : ((num == 2) ? Gestures.DoubleTap : Gestures.Tap));
			}
		}
		else
		{
			if (!profile.Buttons.Contains(arkBubble) || (profile.Target.Length > 0 && !WindowCatalog.MatchesTarget(profile.Target, Win32.GetForegroundWindow())))
			{
				return;
			}
			if (num >= 2 && (arkBubble.Pinned || profile.AllPinned))
			{
				if (heldBubble == arkBubble)
				{
					ReleaseHeld();
					Wake();
					return;
				}
				ReleaseHeld();
				if (!arkBubble.Action.Enabled)
				{
					return;
				}
				try
				{
					if (arkBubble.Action.Kind != ActionKind.Keys)
					{
						throw new InvalidOperationException("此功能不支持持续按住，请绑定键盘按键。");
					}
					heldKeys.Press(arkBubble.Action.Keys);
					heldBubble = arkBubble;
				}
				catch (Exception ex)
				{
					Diagnostics.Error(ex, "Ark.Hold");
					App.CurrentApp.Report(ex.Message);
				}
				Wake();
			}
			else
			{
				if (heldBubble != null)
				{
					ReleaseHeld();
				}
				expanded = false;
				Wake();
				Run(arkBubble.Action, arkBubble);
			}
		}
	}

	internal async Task CheckPinnedDoubleClickAsync()
	{
		Options.ArkEnabled = true;
		ArkBubble bubble = profile.Buttons[0];
		bubble.Pinned = true;
		store.Save();
		await Task.Delay(120);
		SetSuppressed(value: false);
		Click(bubble);
		if (heldKeys.Active)
		{
			throw new InvalidOperationException("单击错误地锁住按键。");
		}
		Click(bubble);
		captureHeldFixture = Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--v0814-smoke") && Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--capture-ui");
		rendered.Clear();
		DrawAll();
		if (!heldKeys.Active || heldBubble != bubble)
		{
			throw new InvalidOperationException("固定气泡双击未持续按住。");
		}
		Click(bubble);
		Click(bubble);
		if (heldKeys.Active)
		{
			throw new InvalidOperationException("再次双击未释放按键。");
		}
		Click(bubble);
		Click(bubble);
		SetSuppressed(value: true);
		if (heldKeys.Active)
		{
			throw new InvalidOperationException("隐藏气泡未释放按键。");
		}
		SetSuppressed(value: false);
		Click(bubble);
		Click(bubble);
		CancelInput();
		if (heldKeys.Active)
		{
			throw new InvalidOperationException("切换配置清理未释放按键。");
		}
		SetSuppressed(value: true);
	}

	internal void CheckAllPinned()
	{
		bool[] first = profile.Buttons.Select((ArkBubble b) => b.Pinned).ToArray();
		pressed = main;
		dragging = false;
		Hold();
		pressed = 0;
		if (!profile.AllPinned)
		{
			throw new InvalidOperationException("长按没有永久展开气泡");
		}
		pendingBubble = null;
		pendingProfile = profile;
		taps = 1;
		FlushClick();
		if (profile.AllPinned)
		{
			throw new InvalidOperationException("点击没有取消永久展开");
		}
		if (!first.SequenceEqual(profile.Buttons.Select((ArkBubble b) => b.Pinned)))
		{
			throw new InvalidOperationException("批量展开改变了独立固定状态");
		}
	}

	private void Click(ArkBubble? bubble)
	{
		if (taps > 0 && (pendingBubble != bubble || pendingProfile != profile))
		{
			FlushClick();
		}
		pendingBubble = bubble;
		pendingProfile = profile;
		taps++;
		if (bubble != null && ((!bubble.Pinned && !profile.AllPinned) || taps >= 2))
		{
			FlushClick();
			return;
		}
		if (bubble == null && (taps >= 3 || (Gestures.DoubleTap.Kind == ActionKind.None && Gestures.TripleTap.Kind == ActionKind.None)))
		{
			FlushClick();
			return;
		}
		click.Stop();
		click.Start();
	}

	private void Up()
	{
		if (pressed == 0)
		{
			return;
		}
		nint num = pressed;
		pressed = 0;
		hold.Stop();
		ArkBubble value;
		if (dragging)
		{
			if (num == main && Environment.TickCount64 - lastMoved < 100)
			{
				vx = Math.Clamp(vx, -1.2, 1.2);
				vy = Math.Clamp(vy, -1.2, 1.2);
				momentum = Math.Abs(vx) + Math.Abs(vy) > 0.15;
			}
			if (num == main)
			{
				Win32.POINT pOINT = BoundedPosition();
				momentum |= pOINT.X != position.X || pOINT.Y != position.Y;
			}
			if (!momentum)
			{
				SavePosition();
			}
		}
		else if (!consumed && windows.TryGetValue(num, out value))
		{
			Click(value);
		}
		dragging = false;
		Wake();
	}

	private nint Procedure(nint h, uint m, nuint w, nint l)
	{
		bool flag;
		switch (m)
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
			uint num = (uint)((int)w & 0xFFFF);
			flag = Win32.GetPointerInfo(num, out var info);
			if (flag)
			{
				uint pointerType = info.pointerType;
				bool flag2 = pointerType - 2 <= 1;
				flag = flag2;
			}
			if (flag)
			{
				if (h == main)
				{
					if ((info.pointerFlags & 0x8000) != 0)
					{
						twoFinger.Reset();
						CancelInput();
						return 0;
					}
					if (twoFinger.Feed(m, num, info.ptPixelLocation.X, info.ptPixelLocation.Y, delegate
					{
						CancelInput(resetTouches: false);
						Wake();
					}, delegate(int x, int y)
					{
						position.X += x;
						position.Y += y;
						ConstrainMain();
						animation.Start();
						DrawAll();
					}, delegate
					{
						SavePosition();
					}))
					{
						return 0;
					}
				}
				if (m == 582)
				{
					pointer = num;
					Down(h, info.ptPixelLocation);
				}
				else if (num == pointer)
				{
					if ((info.pointerFlags & 0x8000) != 0)
					{
						pressed = 0;
						hold.Stop();
					}
					else if (m == 581)
					{
						Move(info.ptPixelLocation);
					}
					else
					{
						Up();
					}
				}
				return 0;
			}
		}
		if (m - 512 <= 2)
		{
			if (!Options.ArkMouseEnabled || ((ulong)Win32.GetMessageExtraInfo() & 0xFFFFFF00uL) == 4283520768u)
			{
				return 0;
			}
			Win32.GetCursorPos(out var p);
			if (m == 513 && pressed == 0)
			{
				mousePressed = true;
				Down(h, p);
				Win32.SetCapture(h);
			}
			else if (m == 512 && mousePressed)
			{
				Move(p);
			}
			else if (m == 514 && mousePressed)
			{
				mousePressed = false;
				Up();
				Win32.ReleaseCapture();
			}
			return 0;
		}
		if (m == 588 || m == 533)
		{
			pressed = 0;
			mousePressed = false;
			hold.Stop();
		}
		if ((m == 126 || m == 736) ? true : false)
		{
			SettingsChanged();
		}
		return Win32.DefWindowProc(h, m, w, l);
	}

	private static GraphicsPath Rounded(float w, float h, float radius)
	{
		GraphicsPath graphicsPath = new GraphicsPath();
		float num = radius * 2f;
		graphicsPath.AddArc(1f, 1f, num, num, 180f, 90f);
		graphicsPath.AddArc(w - num - 1f, 1f, num, num, 270f, 90f);
		graphicsPath.AddArc(w - num - 1f, h - num - 1f, num, num, 0f, 90f);
		graphicsPath.AddArc(1f, h - num - 1f, num, num, 90f, 90f);
		graphicsPath.CloseFigure();
		return graphicsPath;
	}

	private void DrawAll()
	{
		if (disposed)
		{
			return;
		}
		foreach (var (h, arkBubble2) in windows)
		{
			if ((!Options.ArkEnabled && !preview) || (suppressed && !preview) || (arkBubble2 != null && bubbleProgress.GetValueOrDefault(arkBubble2) == 0.0))
			{
				Win32.ShowWindow(h, 0);
				continue;
			}
			Draw(h, arkBubble2);
			if (!IsVisible(h))
			{
				Win32.ShowWindow(h, 4);
			}
		}
	}

	private void Draw(nint h, ArkBubble? bubble)
	{
		Win32.POINT p = ((bubble == null) ? position : BubblePosition(bubble));
		double num = Win32.Scale(p);
		int num2 = (int)Math.Ceiling(((bubble == null) ? (Options.ArkSize * pressScale) : (BubbleWidth(bubble) * ((pressed == h) ? 1.04 : 1.0))) * num);
		int num3 = (int)Math.Ceiling(((bubble == null) ? (Options.ArkSize * pressScale) : (48.0 * ((pressed == h) ? 1.04 : 1.0))) * num);
		Win32.RECT rcMonitor = Win32.Monitor(p).rcMonitor;
		if (bubble != null)
		{
			p.X = Math.Clamp(p.X, rcMonitor.Left, Math.Max(rcMonitor.Left, rcMonitor.Right - num2));
			p.Y = Math.Clamp(p.Y, rcMonitor.Top, Math.Max(rcMonitor.Top, rcMonitor.Bottom - num3));
		}
		int num4 = (int)Math.Ceiling(8.0 * num);
		Win32.POINT pOINT = new Win32.POINT(p.X - num4, p.Y - num4);
		double num5 = ((bubble == null) ? 1.0 : bubbleProgress.GetValueOrDefault(bubble));
		if (bubble != null && pressed != h)
		{
			double num6 = 1.0 - Math.Pow(1.0 - num5, 2.0);
			if (expanded || bubble.Pinned)
			{
				p.X = (int)((double)position.X + (double)(p.X - position.X) * num6);
				p.Y = (int)((double)position.Y + (double)(p.Y - position.Y) * num6);
			}
		}
		pOINT = new Win32.POINT(p.X - num4, p.Y - num4);
		string text = $"{num2}:{num3}:{num5}:{opacity}:{profile.Opacity}:{profile.BubbleOpacity}:{dragging && h == pressed}:{bubble?.Pinned}:{profile.AllPinned}:{bubble == heldBubble && heldBubble != null}:{AccentTheme.Color}:{((bubble == null) ? summon.Progress : 1.0):F3}";
		if (rendered.TryGetValue(h, out (string, int, int) value) && value.Item1 == text)
		{
			if (value.Item2 != p.X || value.Item3 != p.Y)
			{
				Win32.SetWindowPos(h, 0, pOINT.X, pOINT.Y, 0, 0, 21u);
			}
			rendered[h] = (text, p.X, p.Y);
			return;
		}
		rendered[h] = (text, p.X, p.Y);
		using Bitmap bitmap = new Bitmap(num2 + 2 * num4, num3 + 2 * num4, PixelFormat.Format32bppPArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.SmoothingMode = SmoothingMode.AntiAlias;
			graphics.TranslateTransform(num4, num4);
			using GraphicsPath path = Rounded(num2, num3, (float)((double)Math.Min(num2, num3) * ((bubble == null) ? 0.36 : 0.25)));
			Windows.UI.Color color = AccentTheme.Color;
			bool flag = bubble != null && bubble == heldBubble;
			using SolidBrush brush = new SolidBrush(flag ? System.Drawing.Color.FromArgb(color.R, color.G, color.B) : ColorTranslator.FromHtml((bubble == null) ? profile.Color : profile.BubbleColor));
			if (bubble != null)
			{
				graphics.FillPath(brush, path);
			}
			else
			{
				(double, double) tuple = summon.Sample(0.8620689655172413);
				if (tuple.Item2 > 0.01)
				{
					GraphicsState gstate = graphics.Save();
					graphics.TranslateTransform((float)num2 / 2f, (float)num3 / 2f);
					graphics.ScaleTransform((float)tuple.Item2, (float)tuple.Item2);
					graphics.TranslateTransform((float)(-num2) / 2f, (float)(-num3) / 2f);
					graphics.FillPath(brush, path);
					graphics.Restore(gstate);
				}
			}
			if (dragging && h == pressed && (bubble != null || Options.ArkMoveOutline))
			{
				Windows.UI.Color color2 = AccentTheme.Color;
				using Pen pen = new Pen(System.Drawing.Color.FromArgb(color2.R, color2.G, color2.B), (float)(4.0 * num));
				float num7 = (float)(4.0 * num);
				graphics.TranslateTransform(0f - num7, 0f - num7);
				using GraphicsPath path2 = Rounded((float)num2 + 2f * num7, (float)num3 + 2f * num7, (float)((double)Math.Min(num2, num3) * ((bubble == null) ? 0.36 : 0.25)) + num7);
				graphics.DrawPath(pen, path2);
				graphics.TranslateTransform(num7, num7);
			}
			if (bubble == null)
			{
				float num8 = (float)summon.Sample(0.8620689655172413).Inner;
				float num9 = (1f - num8) / 2f;
				if ((double)num8 > 0.03)
				{
					graphics.TranslateTransform((float)num2 * num9, (float)num3 * num9);
					using GraphicsPath path3 = Rounded((float)num2 * num8, (float)num3 * num8, (float)num2 * num8 * 0.36f);
					using SolidBrush brush2 = new SolidBrush(System.Drawing.Color.FromArgb(220, 225, 225, 230));
					graphics.FillPath(brush2, path3);
					graphics.ResetTransform();
				}
			}
			else
			{
				GestureAction action = bubble.Action;
				using SolidBrush brush3 = new SolidBrush(flag ? System.Drawing.Color.White : System.Drawing.Color.FromArgb((int)(255.0 * action.TextOpacity), ColorTranslator.FromHtml(action.TextColor)));
				float num10 = 12f * (float)num;
				if (images.TryGetValue(action.IconPath, out Bitmap value2))
				{
					graphics.DrawImage(value2, 10f * (float)num, 12f * (float)num, 24f * (float)num, 24f * (float)num);
					num10 = 42f * (float)num;
				}
				else if (action.Icon.Length > 0)
				{
					using Font font = new Font(action.IconFont, 20f * (float)num, GraphicsUnit.Pixel);
					graphics.DrawString(action.Icon, font, Brushes.White, new PointF(10f * (float)num, 11f * (float)num));
					num10 = 42f * (float)num;
				}
				using Font font2 = new Font("Microsoft YaHei UI", 12f * (float)num, GraphicsUnit.Pixel);
				using StringFormat format = new StringFormat
				{
					Alignment = StringAlignment.Center,
					LineAlignment = StringAlignment.Center,
					Trimming = StringTrimming.EllipsisCharacter
				};
				graphics.DrawString(Caption(action), font2, brush3, new RectangleF(num10, 0f, (float)num2 - num10 - (float)((bubble.Pinned || profile.AllPinned) ? 24 : 10) * (float)num, num3), format);
				if (bubble.Pinned || profile.AllPinned)
				{
					Windows.UI.Color color3 = AccentTheme.Color;
					using SolidBrush brush4 = new SolidBrush(System.Drawing.Color.FromArgb(color3.R, color3.G, color3.B));
					graphics.FillEllipse(brush4, (float)num2 - 17f * (float)num, (float)(num3 / 2) - 3f * (float)num, 6f * (float)num, 6f * (float)num);
				}
			}
		}
		if (captureHeldFixture && bubble != null && bubble == heldBubble)
		{
			bitmap.Save(Path.Combine(AppContext.BaseDirectory, "bubble-held-v0814.png"));
			captureHeldFixture = false;
		}
		nint dC = Win32.GetDC(0);
		nint num11 = Win32.CreateCompatibleDC(dC);
		nint hbitmap = bitmap.GetHbitmap(System.Drawing.Color.FromArgb(0));
		nint o = Win32.SelectObject(num11, hbitmap);
		try
		{
			Win32.SIZE size = new Win32.SIZE(bitmap.Width, bitmap.Height);
			Win32.POINT pOINT2 = default(Win32.POINT);
			Win32.BLENDFUNCTION blend = new Win32.BLENDFUNCTION
			{
				SourceConstantAlpha = (byte)(255.0 * ((bubble == null) ? (opacity * profile.Opacity) : (num5 * profile.BubbleOpacity))),
				AlphaFormat = 1
			};
			Win32.UpdateLayeredWindow(h, dC, ref pOINT, ref size, num11, ref pOINT2, 0u, ref blend, 2u);
		}
		finally
		{
			Win32.SelectObject(num11, o);
			Win32.DeleteObject(hbitmap);
			Win32.DeleteDC(num11);
			Win32.ReleaseDC(0, dC);
		}
	}

	private static string Caption(GestureAction a)
	{
		if (a.Label.Length <= 0)
		{
			return ActionNames.Describe(a);
		}
		return a.Label;
	}

	private double BubbleWidth(ArkBubble bubble)
	{
		string text = $"{Caption(bubble.Action)}:{bubble.Action.Icon}:{bubble.Action.IconPath}:{bubble.Pinned || profile.AllPinned}";
		if (measured.TryGetValue(bubble, out (string, double) value) && value.Item1 == text)
		{
			return value.Item2;
		}
		using Bitmap image = new Bitmap(1, 1);
		using Graphics graphics = Graphics.FromImage(image);
		using Font font = new Font("Microsoft YaHei UI", 12f, GraphicsUnit.Pixel);
		double num = Math.Clamp(graphics.MeasureString(Caption(bubble.Action), font).Width + 28f + (float)((bubble.Action.Icon.Length > 0 || bubble.Action.IconPath.Length > 0) ? 30 : 0) + (float)((bubble.Pinned || profile.AllPinned) ? 14 : 0), 48.0, Math.Max(48.0, (double)Win32.Monitor(position).rcWork.Width / Win32.Scale(position) - 24.0));
		measured[bubble] = (text, num);
		return num;
	}

	public void Dispose()
	{
		ReleaseHeld();
		summon.Dispose();
		disposed = true;
		store.Changed -= SettingsChanged;
		store.Replaced -= SettingsReplaced;
		AccentTheme.Changed -= DrawAll;
		OverlayOrder.ForegroundChanged -= ForegroundChanged;
		hold.Stop();
		idle.Stop();
		animation.Stop();
		click.Stop();
		ClearWindows();
		foreach (Bitmap value in images.Values)
		{
			value.Dispose();
		}
		images.Clear();
	}
}
