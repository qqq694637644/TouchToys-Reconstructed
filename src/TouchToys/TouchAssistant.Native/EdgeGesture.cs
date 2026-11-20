using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class EdgeGesture : IDisposable
{
	private delegate bool MonitorCallback(nint monitor, nint dc, ref Win32.RECT rect, nint data);

	private struct InputSource
	{
		public int Device;

		public int Origin;
	}

	private readonly Win32.WndProc procedure;

	private readonly SettingsStore store;

	private readonly Dictionary<nint, EdgeKind> windows = new Dictionary<nint, EdgeKind>();

	private readonly Dictionary<nint, nint> bottomInputs = new Dictionary<nint, nint>();

	private readonly HashSet<nint> visualWindows = new HashSet<nint>();

	private readonly List<MousePassthrough> mousePass = new List<MousePassthrough>();

	private readonly Dictionary<nint, (Win32.RECT Screen, int Width, int Height)> bars = new Dictionary<nint, (Win32.RECT, int, int)>();

	private readonly Dictionary<nint, (int Y, bool Light)> appearance = new Dictionary<nint, (int, bool)>();

	private readonly Dictionary<nint, (Win32.RECT Screen, double Scale)> topInputs = new Dictionary<nint, (Win32.RECT, double)>();

	private readonly DispatcherQueueTimer refreshBars;

	private readonly DispatcherQueueTimer topHint;

	private readonly DispatcherQueueTimer indicatorHide;

	private readonly DispatcherQueueTimer tapTimer;

	private readonly IndicatorTaps taps = new IndicatorTaps();

	private Win32.POINT tapOrigin;

	private long downTime;

	private double travel;

	private long hintStarted;

	private uint pointer;

	private Win32.POINT start;

	private long lastTime;

	private double lastDistance;

	private double velocity;

	private bool tracking;

	private bool revealing;

	private bool rebuilding;

	private bool horizontal;

	private bool globalTop;

	private bool indicatorVisible;

	private bool indicatorSuppressed;

	private bool bottomEnabled;

	private bool refreshing;

	private bool disposed;

	private bool lastGuard;

	private EdgeKind kind;

	private static readonly bool traceEnabled = Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--touch-smoke");

	internal bool IsRefreshActive => refreshBars.IsRunning;

	internal bool IsIndicatorPresented
	{
		get
		{
			if (!visualWindows.Any(Win32.IsWindowVisible))
			{
				return bottomInputs.Values.Any(Win32.IsWindowVisible);
			}
			return true;
		}
	}

	private byte TopAlpha => 1;

	public event Action<string, Win32.POINT>? IndicatorAction;

	public event Action<EdgeSwipe>? Swipe;

	[DllImport("user32")]
	private static extern bool EnumDisplayMonitors(nint dc, nint clip, MonitorCallback callback, nint data);

	[DllImport("user32")]
	private static extern bool SetLayeredWindowAttributes(nint hwnd, uint key, byte alpha, uint flags);

	[DllImport("user32")]
	private static extern bool GetLayeredWindowAttributes(nint hwnd, out uint key, out byte alpha, out uint flags);

	[DllImport("user32")]
	private static extern bool GetCurrentInputMessageSource(out InputSource source);

	[DllImport("gdi32")]
	private static extern nint GetStockObject(int index);

	[DllImport("user32")]
	private static extern nint WindowFromPoint(Win32.POINT point);

	private static nint WindowStyle(nint h, int index)
	{
		return WindowLong.Get(h, index);
	}

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint h, StringBuilder name, int size);

	private static string HitDescription(Win32.POINT point)
	{
		nint num = WindowFromPoint(point);
		StringBuilder stringBuilder = new StringBuilder(256);
		GetClassName(num, stringBuilder, 256);
		Win32.GetWindowThreadProcessId(num, out var process);
		return $"{num}/{process}/{stringBuilder}";
	}

	[DllImport("user32")]
	private static extern uint GetDoubleClickTime();

	public void SuppressIndicator(bool value)
	{
		if (indicatorSuppressed == value)
		{
			return;
		}
		indicatorSuppressed = value;
		if (value)
		{
			indicatorVisible = false;
			indicatorHide.Stop();
			refreshBars.Stop();
			tapTimer.Stop();
			taps.Reset();
			foreach (nint visualWindow in visualWindows)
			{
				Win32.ShowWindow(visualWindow, 0);
			}
			{
				foreach (nint value2 in bottomInputs.Values)
				{
					Win32.ShowWindow(value2, 0);
				}
				return;
			}
		}
		if (Enabled(EdgeKind.Bottom))
		{
			ShowBottomIndicator();
		}
	}

	public void SetGlobalTop(bool value)
	{
		globalTop = value;
		Update();
	}

	private static void Trace(string message)
	{
		if (traceEnabled)
		{
			File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "edge-touch-trace.txt"), $"{DateTime.Now:HH:mm:ss.fff} {message}\n");
		}
	}

	public EdgeGesture(SettingsStore settings)
	{
		store = settings;
		procedure = Procedure;
		Win32.WNDCLASS wc = new Win32.WNDCLASS
		{
			lpfnWndProc = procedure,
			hCursor = Win32.ArrowCursor,
			hInstance = Win32.GetModuleHandle(null),
			lpszClassName = "TouchAssistant.Edge",
			hbrBackground = GetStockObject(0)
		};
		Win32.RegisterClass(ref wc);
		refreshBars = DispatcherQueue.GetForCurrentThread().CreateTimer();
		refreshBars.Interval = TimeSpan.FromMilliseconds(300.0);
		refreshBars.Tick += delegate
		{
			RefreshBars();
		};
		topHint = DispatcherQueue.GetForCurrentThread().CreateTimer();
		topHint.Interval = TimeSpan.FromMilliseconds(40.0);
		indicatorHide = DispatcherQueue.GetForCurrentThread().CreateTimer();
		indicatorHide.Interval = TimeSpan.FromSeconds(3.0);
		indicatorHide.IsRepeating = false;
		indicatorHide.Tick += delegate
		{
			if (tracking)
			{
				indicatorHide.Start();
				return;
			}
			indicatorVisible = false;
			refreshBars.Stop();
			foreach (nint visualWindow in visualWindows)
			{
				Win32.ShowWindow(visualWindow, 0);
			}
			foreach (nint value in bottomInputs.Values)
			{
				Win32.ShowWindow(value, 0);
			}
		};
		tapTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
		tapTimer.IsRepeating = false;
		tapTimer.Interval = TimeSpan.FromMilliseconds(GetDoubleClickTime());
		tapTimer.Tick += delegate
		{
			string text = taps.Flush(Environment.TickCount64);
			if (text != null && Enabled(EdgeKind.Bottom) && !indicatorSuppressed)
			{
				this.IndicatorAction?.Invoke(text, tapOrigin);
			}
		};
		topHint.Tick += delegate
		{
			double num = (double)(Environment.TickCount64 - hintStarted) / 1000.0;
			byte alpha = ((num >= 1.0) ? TopAlpha : ((byte)Math.Min(255.0, (double)(int)TopAlpha + 160.0 * Math.Pow(Math.Sin(num * Math.PI * 2.0), 2.0))));
			foreach (var (hwnd, edgeKind2) in windows)
			{
				if (edgeKind2 == EdgeKind.TopRight)
				{
					SetLayeredWindowAttributes(hwnd, 0u, alpha, 2u);
				}
			}
			if (num >= 1.0)
			{
				topHint.Stop();
			}
		};
		lastGuard = store.Value.CloseGuardEnabled;
		Rebuild();
		store.Changed += Update;
		OverlayOrder.ForegroundChanged += ForegroundChanged;
		if (store.Value.TopEdgeEnabled && !store.Value.TopEdgeHintShown)
		{
			store.Value.TopEdgeHintShown = true;
			store.Save();
			hintStarted = Environment.TickCount64;
			topHint.Start();
		}
		if (Enabled(EdgeKind.Bottom))
		{
			ShowBottomIndicator();
		}
	}

	private bool Enabled(EdgeKind edge)
	{
		bool flag = ((edge == EdgeKind.Bottom) ? ApplicationBindings.Bottom(store.Value) : store.Value.EdgeGestures[(int)edge]).Enabled;
		if (flag)
		{
			bool flag2;
			if (edge == EdgeKind.Bottom)
			{
				flag2 = globalTop && store.Value.MultiTouch.EdgeEnabled && ApplicationBindings.Bottom(store.Value).Kind == ActionKind.BuiltIn && ApplicationBindings.Bottom(store.Value).Command == "Overview";
			}
			else
			{
				bool flag3 = ((edge == EdgeKind.Left || edge == EdgeKind.Right) ? true : false);
				flag2 = flag3 && store.Value.EdgeGestures[(int)edge].Kind != ActionKind.None;
			}
			flag = flag2;
		}
		return flag;
	}

	private void ForegroundChanged(nint handle)
	{
		Update();
		Win32.GetWindowThreadProcessId(handle, out var process);
		if (process != Environment.ProcessId && Enabled(EdgeKind.Bottom))
		{
			ShowBottomIndicator();
		}
	}

	public void ShowBottomIndicator()
	{
		if (indicatorSuppressed || !Enabled(EdgeKind.Bottom))
		{
			return;
		}
		indicatorVisible = true;
		RefreshBars();
		refreshBars.Start();
		foreach (nint visualWindow in visualWindows)
		{
			Win32.ShowWindow(visualWindow, 4);
		}
		foreach (nint value in bottomInputs.Values)
		{
			Win32.ShowWindow(value, 4);
		}
		indicatorHide.Stop();
		indicatorHide.Start();
	}

	private void Update()
	{
		if (tracking && !Enabled(kind))
		{
			Cancel();
		}
		bool flag = Enabled(EdgeKind.Bottom);
		if (flag && !bottomEnabled)
		{
			ShowBottomIndicator();
		}
		if (!flag)
		{
			indicatorVisible = false;
		}
		bottomEnabled = flag;
		if (Enabled(EdgeKind.Bottom) && indicatorVisible && !indicatorSuppressed)
		{
			if (!refreshBars.IsRunning)
			{
				RefreshBars();
				refreshBars.Start();
			}
		}
		else
		{
			refreshBars.Stop();
			indicatorHide.Stop();
			tapTimer.Stop();
			taps.Reset();
			foreach (nint visualWindow in visualWindows)
			{
				Win32.ShowWindow(visualWindow, 0);
			}
		}
		foreach (KeyValuePair<nint, EdgeKind> window in windows)
		{
			window.Deconstruct(out var key, out var value);
			nint num = key;
			EdgeKind edgeKind = value;
			bool flag2 = Enabled(edgeKind) && (edgeKind != EdgeKind.Bottom || indicatorVisible);
			Win32.ShowWindow(num, flag2 ? 4 : 0);
			Win32.GetWindowRect(num, out var r);
			Diagnostics.State($"EdgeWindow.{num}", $"Edge={edgeKind}; Visible={Win32.IsWindowVisible(num)}; Enabled={Enabled(edgeKind)}; Bounds={r.Left},{r.Top},{r.Width},{r.Height}");
		}
	}

	private async void RefreshBars()
	{
		if (tracking || disposed || !indicatorVisible || indicatorSuppressed || !Enabled(EdgeKind.Bottom))
		{
			return;
		}
		nint key;
		(Win32.RECT, int, int) value;
		foreach (KeyValuePair<nint, (Win32.RECT, int, int)> bar2 in bars)
		{
			bar2.Deconstruct(out key, out value);
			nint num = key;
			(Win32.RECT, int, int) tuple = value;
			int left = tuple.Item1.Left;
			Win32.RECT item = tuple.Item1;
			int num2 = left + (item.Width - tuple.Item2) / 2;
			int num3 = GestureGeometry.BarTop(tuple.Item1.Top, GestureBarAppearance.Bottom(tuple.Item1), tuple.Item3);
			if (bottomInputs.TryGetValue(num, out var value2))
			{
				int num4 = (int)(32.0 * Win32.Scale(new Win32.POINT(tuple.Item1.Left, tuple.Item1.Top)));
				int y = GestureGeometry.BottomInputTop(num3, tuple.Item3, num4, tuple.Item1.Top, tuple.Item1.Bottom);
				Win32.SetWindowPos(value2, -1, num2 - 12, y, tuple.Item2 + 24, num4, 16u);
			}
			(int, bool) value3;
			bool flag = appearance.TryGetValue(num, out value3);
			if (!flag || value3.Item1 != num3)
			{
				bool flag2 = !flag || value3.Item2;
				GestureBarAppearance.Draw(num, num2, num3, tuple.Item2, tuple.Item3, flag2);
				appearance[num] = (num3, flag2);
			}
		}
		if (refreshing)
		{
			return;
		}
		refreshing = true;
		try
		{
			KeyValuePair<nint, (Win32.RECT Screen, int Width, int Height)>[] array = bars.ToArray();
			foreach (KeyValuePair<nint, (Win32.RECT, int, int)> keyValuePair in array)
			{
				keyValuePair.Deconstruct(out key, out value);
				nint handle = key;
				(Win32.RECT Screen, int Width, int Height) bar = value;
				int left2 = bar.Screen.Left;
				Win32.RECT item = bar.Screen;
				int x = left2 + (item.Width - bar.Width) / 2;
				int y2 = appearance[handle].Y;
				(int, bool) value4;
				bool? previousLight = (appearance.TryGetValue(handle, out value4) ? new bool?(value4.Item2) : ((bool?)null));
				bool flag3 = await Task.Run(() => GestureBarAppearance.Light(x, y2, bar.Width, previousLight));
				if (disposed || !indicatorVisible || indicatorSuppressed || !bars.ContainsKey(handle) || !Enabled(EdgeKind.Bottom))
				{
					break;
				}
				if (appearance.TryGetValue(handle, out (int, bool) value5))
				{
					(y2, _) = value5;
					if (value5.Item2 != flag3)
					{
						GestureBarAppearance.Draw(handle, x, y2, bar.Width, bar.Height, flag3);
						appearance[handle] = (y2, flag3);
					}
				}
			}
		}
		catch (Exception error)
		{
			App.Log(error);
		}
		finally
		{
			refreshing = false;
		}
	}

	private void Rebuild()
	{
		if (rebuilding)
		{
			return;
		}
		rebuilding = true;
		try
		{
			Cancel();
			foreach (MousePassthrough item in mousePass)
			{
				item.Dispose();
			}
			mousePass.Clear();
			nint[] array = windows.Keys.Concat(visualWindows).ToArray();
			foreach (nint num in array)
			{
				OverlayOrder.Remove(num);
				Win32.DestroyWindow(num);
			}
			windows.Clear();
			visualWindows.Clear();
			bars.Clear();
			bottomInputs.Clear();
			appearance.Clear();
			topInputs.Clear();
			EnumDisplayMonitors(0, 0, delegate(nint monitor, nint dc, ref Win32.RECT rect, nint data)
			{
				double num2 = Win32.Scale(new Win32.POINT(rect.Left, rect.Top));
				Add(EdgeKind.Left, rect.Left, rect.Top, (int)Math.Ceiling(EdgeTolerance.Pixels(num2, rect.Width, rect.Height)), rect.Height);
				int num3 = (int)Math.Ceiling(EdgeTolerance.Pixels(num2, rect.Width, rect.Height));
				Add(EdgeKind.Right, rect.Right - num3, rect.Top, num3, rect.Height);
				int num4 = (int)(144.0 * num2);
				int num5 = Math.Max(4, (int)(5.0 * num2));
				int num6 = rect.Left + (rect.Width - num4) / 2;
				int num7 = GestureGeometry.BarTop(rect.Top, GestureBarAppearance.Bottom(rect), num5);
				nint num8 = Win32.CreateWindowEx(134742184u, "TouchAssistant.Edge", "全面屏指示器", 2147483648u, num6, num7, num4, num5, 0, 0, Win32.GetModuleHandle(null), 0);
				visualWindows.Add(num8);
				OverlayOrder.Register(num8);
				bars[num8] = (rect, num4, num5);
				bottomInputs[num8] = Add(EdgeKind.Bottom, num6 - 12, num7 - 12, num4 + 24, (int)(32.0 * num2));
				GestureBarAppearance.Draw(num8, num6, num7, num4, num5, light: true);
				return true;
			}, 0);
			Update();
		}
		finally
		{
			rebuilding = false;
		}
		nint Add(EdgeKind edge, int x, int y, int w, int h)
		{
			nint num2 = Win32.CreateWindowEx(134742152u, "TouchAssistant.Edge", "TouchToys Gesture Bar", 2147483648u, x, y, w, h, 0, 0, Win32.GetModuleHandle(null), 0);
			windows[num2] = edge;
			mousePass.Add(new MousePassthrough(num2, () => !tracking));
			SetLayeredWindowAttributes(num2, 0u, 1, 2u);
			Win32.SuppressFeedback(num2);
			TouchMessages.Register(num2);
			OverlayOrder.Register(num2);
			return num2;
		}
	}

	private void Cancel()
	{
		tapTimer?.Stop();
		taps.Reset();
		if (revealing)
		{
			this.Swipe?.Invoke(new EdgeSwipe(kind, EdgePhase.Cancel, start, lastDistance, 0.0));
		}
		tracking = (revealing = (horizontal = false));
	}

	internal void ForwardTopRight(uint message, nuint w, nint l)
	{
		if (message == 588)
		{
			if (tracking && kind == EdgeKind.TopRight)
			{
				Cancel();
			}
		}
		else
		{
			if (!Enabled(EdgeKind.TopRight) || !Win32.GetPointerInfo((uint)((int)w & 0xFFFF), out var info))
			{
				return;
			}
			nint key;
			EdgeKind value;
			foreach (KeyValuePair<nint, EdgeKind> window in windows)
			{
				window.Deconstruct(out key, out value);
				nint h = key;
				if (value == EdgeKind.TopRight && Win32.GetWindowRect(h, out var r) && info.ptPixelLocation.X >= r.Left && info.ptPixelLocation.X < r.Right && info.ptPixelLocation.Y >= r.Top && info.ptPixelLocation.Y < r.Bottom)
				{
					Procedure(h, message, w, l);
					return;
				}
			}
			if (!tracking || kind != EdgeKind.TopRight)
			{
				return;
			}
			foreach (KeyValuePair<nint, EdgeKind> window2 in windows)
			{
				window2.Deconstruct(out key, out value);
				nint h2 = key;
				if (value == EdgeKind.TopRight)
				{
					Procedure(h2, message, w, l);
					break;
				}
			}
		}
	}

	internal async Task CheckBottomIndicatorAsync()
	{
		ShowBottomIndicator();
		CheckHitAreas();
		if (visualWindows.Count == 0 || visualWindows.Any((nint h) => (((IntPtr)WindowStyle(h, -16)).ToInt64() & 0x10000000) == 0))
		{
			throw new InvalidOperationException("指示器没有显示。");
		}
		await Task.Delay(3300);
		if (visualWindows.Any((nint h) => (((IntPtr)WindowStyle(h, -16)).ToInt64() & 0x10000000) != 0) || bottomInputs.Values.Any((nint h) => (((IntPtr)WindowStyle(h, -16)).ToInt64() & 0x10000000) != 0))
		{
			throw new InvalidOperationException("指示器或触摸范围未在 3 秒后隐藏。");
		}
	}

	internal void CheckHitAreas()
	{
		foreach (var (h, h2) in bottomInputs)
		{
			Win32.GetWindowRect(h, out var r);
			Win32.GetWindowRect(h2, out var r2);
			if (r2.Top > r.Top || r2.Bottom < r.Bottom)
			{
				throw new InvalidOperationException("底部手势范围没有覆盖指示器。");
			}
		}
	}

	internal void CheckTopConfiguration()
	{
		foreach (var (num2, tuple2) in topInputs)
		{
			Win32.GetWindowRect(num2, out var r);
			if (r.Top == tuple2.Item1.Top + (int)Math.Round(24.0 * tuple2.Item2) && r.Height == Math.Max(8, (int)Math.Ceiling(12.0 * tuple2.Item2)))
			{
				int width = r.Width;
				var (rECT, _) = tuple2;
				if (width == Math.Max(1, rECT.Width / 3) && GetLayeredWindowAttributes(num2, out var _, out var alpha, out var _) && alpha == TopAlpha)
				{
					continue;
				}
			}
			throw new InvalidOperationException("Top strip dimensions or opacity did not update.");
		}
	}

	internal void ConfigureDiagnostic(EdgeKind edge, int inset, int height, byte alpha)
	{
		if (!Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--edge-diagnostics"))
		{
			throw new InvalidOperationException();
		}
		foreach (var (num2, edgeKind2) in windows)
		{
			if (edgeKind2 == edge)
			{
				Win32.GetWindowRect(num2, out var r);
				Win32.RECT rcMonitor = Win32.Monitor(new Win32.POINT(r.Left, r.Top)).rcMonitor;
				Win32.SetWindowPos(num2, -1, r.Left, rcMonitor.Top + inset, r.Width, height, 16u);
				SetLayeredWindowAttributes(num2, 0u, alpha, 2u);
			}
		}
	}

	internal Win32.POINT TestPoint(EdgeKind edge, Win32.POINT monitorPoint)
	{
		Win32.RECT rcMonitor = Win32.Monitor(monitorPoint).rcMonitor;
		nint key2;
		if (edge == EdgeKind.Bottom)
		{
			foreach (KeyValuePair<nint, nint> bottomInput in bottomInputs)
			{
				bottomInput.Deconstruct(out var key, out key2);
				Win32.GetWindowRect(key, out var r);
				if (r.Left >= rcMonitor.Left && r.Right <= rcMonitor.Right)
				{
					return new Win32.POINT((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2);
				}
			}
		}
		foreach (KeyValuePair<nint, EdgeKind> window in windows)
		{
			window.Deconstruct(out key2, out var value);
			nint num = key2;
			if (value == edge)
			{
				Win32.GetWindowRect(num, out var r2);
				if (r2.Left >= rcMonitor.Left && r2.Right <= rcMonitor.Right && r2.Top >= rcMonitor.Top && r2.Bottom <= rcMonitor.Bottom)
				{
					Win32.POINT pOINT = new Win32.POINT((r2.Left + r2.Right) / 2, (r2.Top + r2.Bottom) / 2);
					Trace($"Test {edge}: h={num},point={pOINT.X},{pOINT.Y},hit={HitDescription(pOINT)},style={WindowStyle(num, -20):X},normal={WindowStyle(num, -16):X},enabled={Enabled(edge)}");
					return pOINT;
				}
			}
		}
		throw new InvalidOperationException("找不到边缘手势测试入口。");
	}

	private nint Procedure(nint h, uint msg, nuint wp, nint lp)
	{
		if (traceEnabled && msg == 132)
		{
			nint num = Win32.DefWindowProc(h, msg, wp, lp);
			Trace($"HitTest {windows.GetValueOrDefault(h)} result={num} point={lp:X}");
			return num;
		}
		bool flag = traceEnabled;
		if (flag)
		{
			bool flag2 = msg >= 576 && msg <= 591;
			if (!flag2)
			{
				bool flag3 = ((msg - 161 <= 1 || msg - 513 <= 1) ? true : false);
				flag2 = flag3;
			}
			flag = flag2;
		}
		if (flag)
		{
			GetCurrentInputMessageSource(out var source);
			Trace($"Message {msg:X} edge={windows.GetValueOrDefault(h)} source={source.Device}/{source.Origin} wp={wp:X}");
		}
		switch (msg)
		{
		case 33u:
			return 3;
		case 32u:
			Win32.SetCursor(Win32.ArrowCursor);
			return 1;
		case 126u:
		case 736u:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			DispatcherQueue.GetForCurrentThread().TryEnqueue(Rebuild);
		}
		switch (msg)
		{
		case 576u:
			TouchMessages.Read(wp, lp, delegate(uint id2, uint flags, Win32.POINT p)
			{
				ProcessPoint(h, ((flags & 2) != 0) ? 582u : (((flags & 4) != 0) ? 583u : 581u), id2, p, 0u);
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
			uint id = (uint)((int)wp & 0xFFFF);
			flag = Win32.GetPointerInfo(id, out var info);
			if (flag)
			{
				uint pointerType = info.pointerType;
				bool flag2 = pointerType - 2 <= 1;
				flag = flag2;
			}
			if (flag)
			{
				ProcessPoint(h, msg, id, info.ptPixelLocation, info.pointerFlags);
				return 0;
			}
		}
		if (msg == 588 && tracking)
		{
			Trace($"CaptureLost {kind}");
			Diagnostics.Edge($"Side Cancel edge={kind} captureLost=true");
			Cancel();
		}
		return Win32.DefWindowProc(h, msg, wp, lp);
	}

	private void ProcessPoint(nint h, uint msg, uint id, Win32.POINT point, uint flags)
	{
		if (msg == 582 && !tracking && windows.TryGetValue(h, out kind) && Enabled(kind))
		{
			pointer = id;
			start = point;
			downTime = (lastTime = Environment.TickCount64);
			travel = 0.0;
			tapTimer.Stop();
			lastDistance = (velocity = 0.0);
			tracking = true;
			if (kind == EdgeKind.Bottom)
			{
				indicatorHide.Stop();
				ShowBottomIndicator();
			}
			revealing = (horizontal = false);
			Trace($"Down {kind} {start.X},{start.Y}");
			Diagnostics.Edge($"Side Down edge={kind} enabled={Enabled(kind)} dpi={Win32.Scale(point):F2}");
		}
		else
		{
			if (!tracking || id != pointer)
			{
				return;
			}
			if ((flags & 0x8000) != 0)
			{
				Cancel();
				return;
			}
			double num = Win32.Scale(start);
			double num2 = (double)(point.X - start.X) / num;
			double num3 = (double)(point.Y - start.Y) / num;
			travel = Math.Max(travel, Math.Max(Math.Abs(num2), Math.Abs(num3)));
			if (travel > 10.0)
			{
				tapTimer.Stop();
				taps.Reset();
			}
			double num4 = GestureGeometry.EdgeDistance((int)kind, num2, num3);
			if (!revealing && !horizontal && kind == EdgeKind.Bottom && Math.Abs(num2) > 14.0 && Math.Abs(num2) > Math.Abs(num3) * 1.3)
			{
				horizontal = true;
			}
			EdgeKind edgeKind = kind;
			bool flag = ((edgeKind == EdgeKind.Left || edgeKind == EdgeKind.Right) ? true : false);
			bool flag2 = (flag ? (num4 > Math.Abs(num3)) : (num4 > Math.Abs(num2)));
			if (!horizontal && !revealing && num4 > 6.0 && flag2)
			{
				revealing = true;
				Trace($"Begin {kind}");
				Diagnostics.Edge($"Side Begin edge={kind}");
				this.Swipe?.Invoke(new EdgeSwipe(kind, EdgePhase.Begin, start, 0.0, 0.0));
			}
			long tickCount = Environment.TickCount64;
			long num5 = tickCount - lastTime;
			if (num5 > 0 && (msg != 583 || Math.Abs(num4 - lastDistance) > 0.5 || num5 > 120))
			{
				velocity = (num4 - lastDistance) / (double)num5;
			}
			lastTime = tickCount;
			lastDistance = num4;
			if (revealing)
			{
				this.Swipe?.Invoke(new EdgeSwipe(kind, EdgePhase.Update, start, num4, velocity));
			}
			if (msg != 583)
			{
				return;
			}
			Trace($"Up {kind} distance={num4} revealing={revealing} velocity={velocity}");
			Diagnostics.Edge($"Side End edge={kind} distance={num4:F1} revealing={revealing}");
			if (horizontal && Math.Abs(num2) >= 54.0)
			{
				this.IndicatorAction?.Invoke((num2 > 0.0) ? "Right" : "Left", start);
			}
			else if (revealing)
			{
				this.Swipe?.Invoke(new EdgeSwipe(kind, EdgePhase.End, start, num4, velocity));
			}
			else if (kind == EdgeKind.Bottom && !horizontal && num3 >= 12.0 && num3 > Math.Abs(num2) * 1.3)
			{
				this.IndicatorAction?.Invoke("Down", start);
			}
			else if (kind == EdgeKind.Bottom && travel <= 10.0 && tickCount - downTime <= 500)
			{
				double num6 = Win32.Scale(start);
				if (taps.Pending && !taps.CanCombine(tickCount, (double)start.X / num6, (double)start.Y / num6))
				{
					taps.Reset();
					this.IndicatorAction?.Invoke("Tap", tapOrigin);
				}
				tapOrigin = start;
				string text = taps.Tap(tickCount, (double)start.X / num6, (double)start.Y / num6, (int)GetDoubleClickTime());
				if (text != null)
				{
					this.IndicatorAction?.Invoke(text, start);
				}
				else
				{
					tapTimer.Start();
				}
			}
			else
			{
				taps.Reset();
				tapTimer.Stop();
			}
			tracking = (revealing = (horizontal = false));
		}
	}

	public void Dispose()
	{
		disposed = true;
		OverlayOrder.ForegroundChanged -= ForegroundChanged;
		foreach (MousePassthrough item in mousePass)
		{
			item.Dispose();
		}
		mousePass.Clear();
		store.Changed -= Update;
		refreshBars.Stop();
		topHint.Stop();
		indicatorHide.Stop();
		tapTimer.Stop();
		Cancel();
		nint[] array = windows.Keys.Concat(visualWindows).ToArray();
		foreach (nint num in array)
		{
			OverlayOrder.Remove(num);
			Win32.DestroyWindow(num);
		}
		windows.Clear();
		visualWindows.Clear();
		bars.Clear();
		bottomInputs.Clear();
		appearance.Clear();
		topInputs.Clear();
	}
}
