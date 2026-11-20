using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.Services;
using WinRT;
using WinRT.TouchToysVtableClasses;
using Windows.ApplicationModel;

namespace TouchAssistant.Native;

[WinRTRuntimeClassName("Windows.Foundation.IClosable")]
[WinRTExposedType(typeof(TouchAssistant_Native_GlobalTouchGesturesWinRTTypeDetails))]
internal sealed class GlobalTouchGestures : IDisposable
{
	private struct Touch
	{
		public Win32.POINTER_INFO Info;

		public uint Flags;

		public uint Mask;

		public Win32.RECT Contact;

		public Win32.RECT ContactRaw;

		public uint Orientation;

		public uint Pressure;
	}

	private struct Message
	{
		public nint H;

		public uint Id;

		public nuint W;

		public nint L;

		public uint Time;

		public Win32.POINT Point;

		public uint Private;
	}

	private readonly record struct RelayFrame(DateTimeOffset At, string Stage, int Contacts, uint Flags, int Active, double Gap, double Api, int Error, int GC0, int GC2);

	private readonly SettingsStore store;

	private readonly DispatcherQueue ui;

	private readonly Thread thread;

	private readonly Win32.WndProc callback;

	private readonly Func<Touch[], bool> trackedSender;

	private readonly object gate = new object();

	private MultiTouchSettings pending = new MultiTouchSettings();

	private MultiTouchSettings options = new MultiTouchSettings();

	private readonly Dictionary<uint, Touch> active = new Dictionary<uint, Touch>();

	private readonly Touch[] frameBuffer = new Touch[256];

	private readonly Touch[][] frameCache = new Touch[257][];

	private readonly Touch[][] packetCache = new Touch[257][];

	private readonly Dictionary<uint, Touch> nextFrame = new Dictionary<uint, Touch>(256);

	private readonly HashSet<uint> recoveryContacts = new HashSet<uint>();

	private readonly RelayContactRecovery recoveryFrames = new RelayContactRecovery();

	private long recoveryStarted;

	private long sequenceNumber;

	private bool recovering;

	private bool customShapes;

	private int recoveryAttempt;

	private GCLatencyMode? previousLatency;

	private readonly MultiTouchRecognizer recognizer = new MultiTouchRecognizer();

	private readonly ShapeSequence shapes = new ShapeSequence();

	private readonly TapSequence tapSequence = new TapSequence();

	private double tapScale = 1.0;

	private volatile bool bottomBound;

	private readonly HashSet<uint> edgeContacts = new HashSet<uint>();

	private bool edgeReserved;

	private ManualResetEvent? heartbeatStop;

	private int tickQueued;

	private bool bottomStarted;

	private bool topStarted;

	private EdgeKind topKind;

	private EdgeSwipe? panelSwipe;

	private int panelFingers;

	private bool panelConsumed;

	private nint window;

	private bool registered;

	private bool suspended;

	private bool sequenceInvalid;

	private bool guardEdgeSequence;

	private bool desktopPaused;

	private volatile bool disposed;

	private string settingsStamp = "";

	private uint? lastFrame;

	private string forwardFailure = "";

	private readonly Queue<RelayFrame> recentFrames = new Queue<RelayFrame>();

	private long lastSend;

	private string injectionStage = "forward";

	private bool injectionAttempted;

	[ThreadStatic]
	private static int lastInjectionError;

	internal Action<string>? RelayDiagnostic;

	private long cooldown;

	private Win32.RECT monitor;

	private double scale = 1.0;

	private readonly HashSet<uint> physical = new HashSet<uint>();

	private Win32.POINT singleStart;

	private Win32.POINT singleLast;

	private long singleTime;

	private bool singleCandidate;

	private static string InjectionError
	{
		get
		{
			if (lastInjectionError != 0)
			{
				return $"错误 {lastInjectionError} {new Win32Exception(lastInjectionError).Message}";
			}
			return "系统返回失败但未提供错误码";
		}
	}

	internal bool IsActive => Volatile.Read(in registered);

	public string Status { get; private set; } = "全局触摸手势未启用";

	private static bool Packaged
	{
		get
		{
			try
			{
				return Package.Current != null;
			}
			catch
			{
				return false;
			}
		}
	}

	public event Action<EdgeSwipe>? PanelGesture;

	public event Action<EdgeSwipe>? TopGesture;

	public event Action<EdgeSwipe>? BottomSwipe;

	public event Action<GestureAction, Win32.POINT>? Trigger;

	public event Action<bool>? AvailabilityChanged;

	[DllImport("user32", SetLastError = true)]
	private static extern bool RegisterPointerInputTarget(nint h, uint type);

	[DllImport("user32", SetLastError = true)]
	private static extern bool UnregisterPointerInputTarget(nint h, uint type);

	[DllImport("user32", SetLastError = true)]
	private static extern bool InitializeTouchInjection(uint count, uint feedback);

	[DllImport("user32", SetLastError = true)]
	private static extern bool InjectTouchInput(uint count, [In] Touch[] touches);

	[DllImport("user32", SetLastError = true)]
	private static extern bool GetPointerFrameTouchInfo(uint id, ref uint count, [Out] Touch[]? touches);

	[DllImport("user32")]
	private static extern nuint SetTimer(nint h, nuint id, uint milliseconds, nint callback);

	[DllImport("user32")]
	private static extern bool KillTimer(nint h, nuint id);

	[DllImport("user32")]
	private static extern int GetMessage(out Message message, nint h, uint min, uint max);

	[DllImport("user32")]
	private static extern nint DispatchMessage(ref Message message);

	[DllImport("user32")]
	private static extern bool PostMessage(nint h, uint message, nuint w, nint l);

	[DllImport("user32")]
	private static extern void PostQuitMessage(int code);

	[DllImport("user32")]
	private static extern nint WindowFromPoint(Win32.POINT point);

	[DllImport("kernel32")]
	private static extern nint GetCurrentProcess();

	[DllImport("advapi32", SetLastError = true)]
	private static extern bool OpenProcessToken(nint process, uint access, out nint token);

	[DllImport("advapi32", SetLastError = true)]
	private static extern bool GetTokenInformation(nint token, int info, out int value, int size, out int length);

	[DllImport("kernel32")]
	private static extern bool CloseHandle(nint h);

	private Touch[] HeartbeatPacket()
	{
		Touch[][] array = packetCache;
		int count = active.Count;
		Touch[] array2 = array[count] ?? (array[count] = new Touch[active.Count]);
		int num = 0;
		foreach (Touch value in active.Values)
		{
			Touch current = value;
			current.Info.pointerFlags = 131078u;
			array2[num++] = current;
		}
		return array2;
	}

	private bool SendTracked(Touch[] touches)
	{
		if (touches.Length == 0)
		{
			return true;
		}
		long timestamp = Stopwatch.GetTimestamp();
		double num = ((lastSend == 0L) ? 0.0 : Stopwatch.GetElapsedTime(lastSend, timestamp).TotalMilliseconds);
		injectionAttempted = true;
		bool ok = Send(touches);
		if (!ok && RelayRecovery.CanReset(active.Count, num, lastInjectionError, touches.All((Touch t) => t.Info.pointerFlags == 65542)))
		{
			int original = lastInjectionError;
			if (InitializeTouchInjection(256u, 3u))
			{
				ok = Send(touches);
				injectionStage = (ok ? "idle-reset-recovered" : "idle-reset");
			}
			else
			{
				lastInjectionError = Marshal.GetLastWin32Error();
			}
			ui.TryEnqueue(delegate
			{
				Diagnostics.Event("TouchRelay.IdleReset", $"First DOWN after idle; original={original}; recovered={ok}");
			});
		}
		double totalMilliseconds = Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
		lastSend = Stopwatch.GetTimestamp();
		uint num2 = 0u;
		for (int num3 = 0; num3 < touches.Length; num3++)
		{
			Touch touch = touches[num3];
			num2 |= touch.Info.pointerFlags;
		}
		recentFrames.Enqueue(new RelayFrame(DateTimeOffset.Now, ok ? injectionStage : (injectionStage + "-FAILED"), touches.Length, num2, active.Count, num, totalMilliseconds, (!ok) ? lastInjectionError : 0, GC.CollectionCount(0), GC.CollectionCount(2)));
		while (recentFrames.Count > 96)
		{
			recentFrames.Dequeue();
		}
		return ok;
	}

	public GlobalTouchGestures(SettingsStore settings)
	{
		store = settings;
		ui = DispatcherQueue.GetForCurrentThread();
		callback = Procedure;
		trackedSender = SendTracked;
		pending = Snapshot();
		bottomBound = ApplicationBindings.Bottom(store.Value).Enabled && ApplicationBindings.Bottom(store.Value).Kind != ActionKind.None;
		settingsStamp = SettingsStamp();
		thread = new Thread(Run)
		{
			IsBackground = true,
			Priority = ThreadPriority.AboveNormal,
			Name = "TouchAssistant.TouchRelay"
		};
		thread.Start();
		store.Changed += Changed;
		OverlayOrder.ForegroundChanged += ForegroundBindings;
		SystemEvents.SessionSwitch += SessionChanged;
		SystemEvents.PowerModeChanged += PowerChanged;
		SystemEvents.DisplaySettingsChanged += DisplayChanged;
		DesktopVisibility.Changed += InputDesktopChanged;
	}

	private MultiTouchSettings Snapshot()
	{
		MultiTouchSettings? multiTouchSettings = JsonSerializer.Deserialize<MultiTouchSettings>(JsonSerializer.Serialize(ApplicationBindings.Gestures(store.Value)));
		multiTouchSettings.Enabled = store.Value.MultiTouch.Enabled;
		multiTouchSettings.EdgeEnabled = store.Value.MultiTouch.EdgeEnabled;
		return multiTouchSettings;
	}

	private void ForegroundBindings(nint h)
	{
		Changed();
	}

	private string SettingsStamp()
	{
		return JsonSerializer.Serialize(new
		{
			Gestures = ApplicationBindings.Gestures(store.Value),
			Enabled = store.Value.MultiTouch.Enabled,
			EdgeEnabled = store.Value.MultiTouch.EdgeEnabled,
			Bottom = ApplicationBindings.Bottom(store.Value)
		});
	}

	private void Changed()
	{
		bottomBound = ApplicationBindings.Bottom(store.Value).Enabled && ApplicationBindings.Bottom(store.Value).Kind != ActionKind.None;
		string text = SettingsStamp();
		if (!(text == settingsStamp))
		{
			settingsStamp = text;
			lock (gate)
			{
				pending = Snapshot();
			}
			if (window != 0)
			{
				PostMessage(window, 32769u, 0u, 0);
			}
		}
	}

	private void DisplayChanged(object? sender, EventArgs e)
	{
		if (window != 0)
		{
			PostMessage(window, 32772u, 0u, 0);
		}
	}

	private void InputDesktopChanged()
	{
		if (window != 0)
		{
			PostMessage(window, 32774u, 0u, 0);
		}
	}

	private void RefreshInputDesktop()
	{
		bool flag = DesktopVisibility.Locked || !InputDesktopState.CanInteract();
		if (flag != desktopPaused)
		{
			desktopPaused = flag;
			Configure();
		}
	}

	private void SessionChanged(object sender, SessionSwitchEventArgs e)
	{
		SessionSwitchReason reason = e.Reason;
		if ((uint)(reason - 6) <= 1u)
		{
			PostMessage(window, 32770u, 1u, 0);
			return;
		}
		reason = e.Reason;
		if ((reason == SessionSwitchReason.SessionLogon || reason == SessionSwitchReason.SessionUnlock) ? true : false)
		{
			PostMessage(window, 32770u, 0u, 0);
		}
	}

	private void PowerChanged(object sender, PowerModeChangedEventArgs e)
	{
		if (e.Mode == PowerModes.Suspend)
		{
			PostMessage(window, 32770u, 1u, 0);
		}
		else if (e.Mode == PowerModes.Resume)
		{
			PostMessage(window, 32770u, 0u, 0);
		}
	}

	private void Run()
	{
		try
		{
			Win32.WNDCLASS wc = new Win32.WNDCLASS
			{
				lpfnWndProc = callback,
				hInstance = Win32.GetModuleHandle(null),
				lpszClassName = "TouchAssistant.GlobalTouchRelay"
			};
			Win32.RegisterClass(ref wc);
			window = Win32.CreateWindowEx(134217728u, wc.lpszClassName, "Touch relay", 0u, 0, 0, 0, 0, -3, 0, wc.hInstance, 0);
			if (window == 0)
			{
				throw new InvalidOperationException("无法创建触摸消息窗口");
			}
			if (!disposed)
			{
				SetTimer(window, 10u, 100u, 0);
				Configure();
				Message message;
				while (GetMessage(out message, 0, 0u, 0u) > 0)
				{
					DispatchMessage(ref message);
				}
			}
		}
		catch (Exception ex)
		{
			Fail(ex.Message);
		}
		finally
		{
			Stop();
			if (window != 0)
			{
				KillTimer(window, 10u);
				Win32.DestroyWindow(window);
			}
			window = 0;
		}
	}

	private static bool HasAccess()
	{
		if (!OpenProcessToken(GetCurrentProcess(), 8u, out var token))
		{
			return false;
		}
		try
		{
			int value;
			int length;
			return GetTokenInformation(token, 26, out value, 4, out length) && value != 0;
		}
		finally
		{
			CloseHandle(token);
		}
	}

	private void Configure()
	{
		CancelBottom();
		MultiTouchSettings multiTouchSettings;
		lock (gate)
		{
			multiTouchSettings = pending;
		}
		options = multiTouchSettings;
		customShapes = options.Enabled && options.Bindings.Any((MultiTouchBinding b) => b.Gesture == "Custom" && b.Action.Enabled && b.Action.Kind != ActionKind.None);
		recognizer.Reset();
		shapes.Reset();
		tapSequence.Reset();
		sequenceInvalid = physical.Count > 0;
		desktopPaused = DesktopVisibility.Locked || !InputDesktopState.CanInteract();
		if (disposed || suspended || desktopPaused || (!options.Enabled && !options.EdgeEnabled))
		{
			recovering = false;
			KillTimer(window, 9u);
			Stop();
		}
		else
		{
			if (recovering || registered)
			{
				return;
			}
			if (!HasAccess())
			{
				Fail(Packaged ? "全局触摸手势当前不可用。" : "全局手势需要签名安装的 UIAccess 版本；管理员权限不能替代 UIAccess。");
				return;
			}
			if (!InitializeTouchInjection(256u, 3u) || !RegisterPointerInputTarget(window, 2u))
			{
				int lastWin32Error = Marshal.GetLastWin32Error();
				Fail(Localization.T("全局触摸注册失败（{0}），请检查是否被其他触摸程序占用。", lastWin32Error), lastWin32Error != 5 && lastWin32Error != 87);
				return;
			}
			registered = true;
			GCLatencyMode valueOrDefault = previousLatency.GetValueOrDefault();
			if (!previousLatency.HasValue)
			{
				valueOrDefault = GCSettings.LatencyMode;
				previousLatency = valueOrDefault;
			}
			GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
			ManualResetEvent stopSignal = new ManualResetEvent(initialState: false);
			heartbeatStop = stopSignal;
			Thread obj = new Thread((ThreadStart)delegate
			{
				try
				{
					while (!stopSignal.WaitOne(25))
					{
						if (!disposed && IsActive && Interlocked.Exchange(ref tickQueued, 1) == 0 && !PostMessage(window, 32773u, 0u, 0))
						{
							Interlocked.Exchange(ref tickQueued, 0);
						}
					}
				}
				finally
				{
					stopSignal.Dispose();
				}
			});
			obj.IsBackground = true;
			obj.Priority = ThreadPriority.AboveNormal;
			obj.Name = "TouchAssistant.TouchHeartbeat";
			obj.Start();
			Diagnostics.Event("TouchRelay", "已启用；专用唤醒线程续报 25ms（不经过线程池），UIAccess=True");
			Status = Localization.T("全局触摸手势已启用");
			ui.TryEnqueue(delegate
			{
				if (!disposed)
				{
					this.AvailabilityChanged?.Invoke(obj: true);
				}
			});
		}
	}

	private void Stop()
	{
		CancelBottom();
		GCLatencyMode? gCLatencyMode = previousLatency;
		if (gCLatencyMode.HasValue)
		{
			GCLatencyMode valueOrDefault = gCLatencyMode.GetValueOrDefault();
			if (GCSettings.LatencyMode == GCLatencyMode.SustainedLowLatency)
			{
				GCSettings.LatencyMode = valueOrDefault;
			}
			previousLatency = null;
		}
		heartbeatStop?.Set();
		heartbeatStop = null;
		Interlocked.Exchange(ref tickQueued, 0);
		bool num = registered;
		if (registered)
		{
			bool value = UnregisterPointerInputTarget(window, 2u);
			Diagnostics.Event("TouchRelay.Stop", $"Unregister={value}; Suspended={suspended}; DesktopPaused={desktopPaused}; Recovering={recovering}");
			registered = false;
		}
		if (active.Count > 0 && !desktopPaused && InputDesktopState.CanInteract())
		{
			Touch[] touches = active.Values.Select(delegate(Touch t)
			{
				t.Info.pointerFlags = 294912u;
				return t;
			}).ToArray();
			injectionStage = "cancel";
			SendTracked(touches);
		}
		active.Clear();
		physical.Clear();
		edgeContacts.Clear();
		edgeReserved = false;
		tapSequence.Reset();
		lastFrame = null;
		recognizer.Reset();
		shapes.Reset();
		singleCandidate = false;
		if (num)
		{
			ui.TryEnqueue(delegate
			{
				if (!disposed)
				{
					this.AvailabilityChanged?.Invoke(obj: false);
				}
			});
		}
		if (num || suspended || desktopPaused)
		{
			Status = Localization.T(desktopPaused ? "系统权限确认期间已暂停输入接管" : (suspended ? "锁屏或休眠期间已暂停全局手势" : "全局触摸手势未启用"));
		}
	}

	private void Fail(string reason, bool retry = false)
	{
		if (DesktopVisibility.Locked || !InputDesktopState.CanInteract())
		{
			desktopPaused = true;
			recovering = false;
			KillTimer(window, 9u);
			Stop();
			return;
		}
		string text = string.Join("\n", recentFrames.Select((RelayFrame f) => $"{f.At:O} stage={f.Stage} contacts={f.Contacts} flags={f.Flags:X} active={f.Active} gapMs={f.Gap:F2} apiMs={f.Api:F2} error={f.Error} gc0={f.GC0} gc2={f.GC2}"));
		string text2 = $"Enabled={options.Enabled}, Edges={options.EdgeEnabled}, physical={physical.Count}, injected={active.Count}, UIAccess={HasAccess()}, DPI={scale:F2}";
		if (retry)
		{
			recoveryContacts.Clear();
			recoveryFrames.Clear();
			recoveryStarted = Environment.TickCount64;
			foreach (uint item in physical)
			{
				recoveryContacts.Add(item);
			}
		}
		Stop();
		Status = Localization.T(reason);
		Diagnostics.Relay(reason + "\n" + text2 + "\n" + text);
		App.Log(new InvalidOperationException("GlobalTouch: " + reason));
		if (retry && !disposed && !suspended)
		{
			recovering = true;
			recoveryAttempt = Math.Min(recoveryAttempt + 1, 6);
			uint milliseconds = RelayRecovery.RetryDelay(recoveryAttempt);
			Status = Localization.T(reason) + Localization.T(" 松手后将自动重新开启。");
			SetTimer(window, 9u, milliseconds, 0);
			if (recoveryAttempt != 1)
			{
				return;
			}
			ui.TryEnqueue(delegate
			{
				if (!disposed)
				{
					App.CurrentApp.Report("触摸接管已暂停，松手后自动恢复。");
				}
			});
			return;
		}
		recovering = false;
		KillTimer(window, 9u);
		ui.TryEnqueue(delegate
		{
			if (!disposed)
			{
				store.Value.MultiTouch.Enabled = false;
				store.Value.MultiTouch.EdgeEnabled = false;
				store.Save();
				App.CurrentApp.Report(Localization.T(reason) + Localization.T(" 可在基础设置 → 诊断与反馈导出日志。"));
			}
		});
	}

	private static bool Send(Touch[] touches)
	{
		lastInjectionError = 0;
		if (touches.Length == 0)
		{
			return true;
		}
		for (int i = 0; i < 4; i++)
		{
			if (InjectTouchInput((uint)touches.Length, touches))
			{
				return true;
			}
			lastInjectionError = Marshal.GetLastWin32Error();
			if (lastInjectionError != 21)
			{
				return false;
			}
			Thread.Sleep(1);
		}
		return false;
	}

	private bool Forward(uint id, out Touch[] frame)
	{
		forwardFailure = "";
		frame = Array.Empty<Touch>();
		if (!Win32.GetPointerInfo(id, out var info))
		{
			forwardFailure = $"GetPointerInfo，错误 {Marshal.GetLastWin32Error()}";
			return false;
		}
		if (lastFrame == info.frameId)
		{
			return true;
		}
		uint count = 256u;
		if (!GetPointerFrameTouchInfo(id, ref count, frameBuffer))
		{
			forwardFailure = $"GetPointerFrameTouchInfo，错误 {Marshal.GetLastWin32Error()}";
			return false;
		}
		if (count == 0 || count > 256)
		{
			forwardFailure = $"无效帧触点数 {count}";
			return false;
		}
		Touch[][] array = frameCache;
		uint num = count;
		frame = array[num] ?? (array[num] = new Touch[count]);
		Array.Copy(frameBuffer, frame, (int)count);
		RelayDiagnostic?.Invoke(string.Join(";", frame.Select((Touch t) => $"{t.Info.pointerId}:{t.Info.pointerFlags:X}")));
		injectionAttempted = false;
		injectionStage = "forward";
		if (!ForwardFrame(active, FilterEdgeFrame(frame), trackedSender, nextFrame, packetCache))
		{
			forwardFailure = (injectionAttempted ? $"InjectTouchInput，{InjectionError}，触点数 {frame.Length}" : "触点 ID 映射容量已用尽");
			return false;
		}
		lastFrame = info.frameId;
		return true;
	}

	private Touch[] FilterEdgeFrame(Touch[] frame)
	{
		if (!edgeReserved && physical.Count == 0 && active.Count == 0 && options.EdgeEnabled && bottomBound)
		{
			Touch[] array = frame.Where((Touch t) => (t.Info.pointerFlags & 0x10000) != 0).ToArray();
			if (array.Length == 1)
			{
				Win32.POINT ptPixelLocation = array[0].Info.ptPixelLocation;
				Win32.RECT rcMonitor = Win32.Monitor(ptPixelLocation).rcMonitor;
				edgeReserved = (double)(rcMonitor.Bottom - ptPixelLocation.Y) <= 8.0 * Win32.Scale(ptPixelLocation);
			}
		}
		Touch[] array2;
		if (edgeReserved)
		{
			array2 = frame;
			for (int num = 0; num < array2.Length; num++)
			{
				Touch touch = array2[num];
				if ((touch.Info.pointerFlags & 0x10000) != 0)
				{
					edgeContacts.Add(touch.Info.pointerId);
				}
			}
		}
		if (!edgeReserved)
		{
			return frame;
		}
		array2 = frame;
		for (int num = 0; num < array2.Length; num++)
		{
			Touch touch2 = array2[num];
			if ((touch2.Info.pointerFlags & 0x48000) != 0)
			{
				edgeContacts.Remove(touch2.Info.pointerId);
			}
		}
		if (edgeContacts.Count == 0)
		{
			edgeReserved = false;
		}
		return Array.Empty<Touch>();
	}

	private void DispatchMatch(MultiTouchRecognizer.Match? result, double dpi)
	{
		if (!result.HasValue)
		{
			return;
		}
		MultiTouchRecognizer.Match match = result.GetValueOrDefault();
		MultiTouchBinding binding = options.Bindings.FirstOrDefault((MultiTouchBinding b) => b.Action.Enabled && b.Fingers == match.Fingers && b.Gesture == match.Gesture);
		if (binding == null || binding.Action.Kind == ActionKind.None)
		{
			return;
		}
		Win32.POINT location = new Win32.POINT((int)Math.Round(match.X * dpi), (int)Math.Round(match.Y * dpi));
		ui.TryEnqueue(delegate
		{
			if (!disposed && store.Value.MultiTouch.Enabled)
			{
				this.Trigger?.Invoke(binding.Action, location);
			}
		});
	}

	private static bool ForwardFrame(Dictionary<uint, Touch> state, Touch[] frame, Func<Touch[], bool> send, Dictionary<uint, Touch>? scratch = null, Touch[][]? packets = null)
	{
		Dictionary<uint, Touch> dictionary = scratch ?? new Dictionary<uint, Touch>(256);
		dictionary.Clear();
		foreach (KeyValuePair<uint, Touch> item in state)
		{
			Touch value = item.Value;
			value.Info.pointerFlags = 131078u;
			dictionary.Add(item.Key, value);
		}
		Touch[] array = frame;
		for (int i = 0; i < array.Length; i++)
		{
			Win32.POINTER_INFO info = array[i].Info;
			bool flag = (info.pointerFlags & 0x10000) != 0;
			bool flag2 = (info.pointerFlags & 0x48000) != 0;
			if (!dictionary.TryGetValue(info.pointerId, out var value2) && !flag)
			{
				continue;
			}
			uint num = value2.Info.pointerId;
			if (flag && dictionary.ContainsKey(info.pointerId))
			{
				flag = false;
			}
			if (flag)
			{
				num = 0u;
				for (uint num2 = 1u; num2 <= 256; num2++)
				{
					bool flag3 = false;
					foreach (Touch value4 in dictionary.Values)
					{
						if (value4.Info.pointerId == num2)
						{
							flag3 = true;
							break;
						}
					}
					if (!flag3)
					{
						num = num2;
						break;
					}
				}
				if (num == 0)
				{
					return false;
				}
			}
			Win32.POINT ptPixelLocation = (flag2 ? value2.Info.ptPixelLocation : info.ptPixelLocation);
			Touch value3 = new Touch
			{
				Info = new Win32.POINTER_INFO
				{
					pointerType = 2u,
					pointerId = num,
					ptPixelLocation = ptPixelLocation,
					pointerFlags = (flag2 ? ((uint)(0x40000 | (((info.pointerFlags & 0x8000) != 0) ? 32768 : 0))) : (flag ? 65542u : 131078u))
				},
				Mask = 1u,
				Contact = new Win32.RECT
				{
					Left = ptPixelLocation.X - 2,
					Right = ptPixelLocation.X + 2,
					Top = ptPixelLocation.Y - 2,
					Bottom = ptPixelLocation.Y + 2
				}
			};
			if (flag2)
			{
				value3.Contact = value2.Contact;
			}
			dictionary[info.pointerId] = value3;
		}
		object obj;
		if (packets != null)
		{
			int i = dictionary.Count;
			obj = packets[i] ?? (packets[i] = new Touch[dictionary.Count]);
		}
		else
		{
			obj = new Touch[dictionary.Count];
		}
		Touch[] array2 = (Touch[])obj;
		int num3 = 0;
		foreach (Touch value5 in dictionary.Values)
		{
			array2[num3++] = value5;
		}
		if (!send(array2))
		{
			return false;
		}
		state.Clear();
		foreach (KeyValuePair<uint, Touch> item2 in dictionary)
		{
			if ((item2.Value.Info.pointerFlags & 0x40000) == 0)
			{
				state.Add(item2.Key, item2.Value);
			}
		}
		return true;
	}

	internal async Task CheckAccessGuardAsync()
	{
		if (HasAccess())
		{
			return;
		}
		store.Value.MultiTouch.Enabled = true;
		store.Save();
		for (int i = 0; i < 30; i++)
		{
			if (!store.Value.MultiTouch.Enabled)
			{
				break;
			}
			await Task.Delay(100);
		}
		if (!store.Value.MultiTouch.Enabled && Status.Contains("UIAccess"))
		{
			return;
		}
		throw new InvalidOperationException("UIAccess 不可用时未明确停用全局手势。");
	}

	internal static void CheckRelay()
	{
		if (Marshal.SizeOf<Win32.POINTER_INFO>() != 96 || Marshal.SizeOf<Touch>() != 144)
		{
			throw new InvalidOperationException("触摸结构布局错误。");
		}
		Dictionary<uint, Touch> dictionary = new Dictionary<uint, Touch>();
		Touch[] sent = Array.Empty<Touch>();
		if (!ForwardFrame(dictionary, new Touch[2]
		{
			Point(800u, 65542u, 50),
			Point(900u, 65542u, 80)
		}, SendFake) || dictionary.Count != 2 || sent[0].Info.pointerId == sent[1].Info.pointerId || sent[0].Mask != 1 || sent[0].Pressure != 0 || sent[0].Orientation != 0)
		{
			throw new InvalidOperationException("触摸帧映射失败。");
		}
		if (!ForwardFrame(dictionary, new Touch[1] { Point(800u, 65542u, 55) }, SendFake) || dictionary.Count != 2 || sent.Any((Touch t) => (t.Info.pointerFlags & 0x10000) != 0))
		{
			throw new InvalidOperationException("活动触点重复 DOWN 未转成 UPDATE。");
		}
		if (!ForwardFrame(dictionary, new Touch[1] { Point(800u, 131078u, 60) }, SendFake) || sent.Length != 2 || sent.Any((Touch t) => t.Info.pointerFlags != 131078))
		{
			throw new InvalidOperationException("触摸整帧续报失败。");
		}
		if (ForwardFrame(dictionary, new Touch[1] { Point(800u, 131078u, 70) }, (Touch[] _) => false) || dictionary[800u].Info.ptPixelLocation.X != 60)
		{
			throw new InvalidOperationException("失败注入提交了状态。");
		}
		if (!ForwardFrame(dictionary, new Touch[1] { Point(800u, 262144u, 999) }, SendFake) || dictionary.ContainsKey(800u) || sent.First((Touch t) => (t.Info.pointerFlags & 0x40000) != 0).Info.ptPixelLocation.X != 60)
		{
			throw new InvalidOperationException("结束帧未保持已注入坐标。");
		}
		if (!ForwardFrame(dictionary, new Touch[1] { Point(900u, 294912u, 80) }, SendFake) || dictionary.Count != 0 || sent[0].Info.pointerFlags != 294912)
		{
			throw new InvalidOperationException("取消触点失败。");
		}
		if (!ForwardFrame(dictionary, new Touch[1] { Point(123u, 131078u, 10) }, SendFake) || dictionary.Count != 0 || sent.Length != 0)
		{
			throw new InvalidOperationException("未收到 DOWN 的触点被补造。");
		}
		static Touch Point(uint id, uint flags, int x)
		{
			return new Touch
			{
				Info = new Win32.POINTER_INFO
				{
					pointerType = 2u,
					pointerId = id,
					pointerFlags = flags,
					ptPixelLocation = new Win32.POINT(x, 100)
				},
				Mask = 7u,
				Contact = new Win32.RECT
				{
					Left = x - 3,
					Right = x + 3,
					Top = 97,
					Bottom = 103
				},
				Pressure = 400u,
				Orientation = 90u
			};
		}
		bool SendFake(Touch[] frame)
		{
			sent = frame;
			return true;
		}
	}

	internal static long CheckRelayAllocation()
	{
		Dictionary<uint, Touch> state = new Dictionary<uint, Touch>(256);
		Dictionary<uint, Touch> scratch = new Dictionary<uint, Touch>(256);
		Touch[][] packets = new Touch[257][];
		Touch[] array = new Touch[1]
		{
			new Touch
			{
				Info = new Win32.POINTER_INFO
				{
					pointerType = 2u,
					pointerId = 42u,
					pointerFlags = 65542u,
					ptPixelLocation = new Win32.POINT(100, 100)
				}
			}
		};
		Func<Touch[], bool> send = (Touch[] _) => true;
		ForwardFrame(state, array, send, scratch, packets);
		array[0].Info.pointerFlags = 131078u;
		for (int num = 0; num < 1000; num++)
		{
			ForwardFrame(state, array, send, scratch, packets);
		}
		long allocatedBytesForCurrentThread = GC.GetAllocatedBytesForCurrentThread();
		for (int num2 = 0; num2 < 10000; num2++)
		{
			array[0].Info.ptPixelLocation.X = num2;
			ForwardFrame(state, array, send, scratch, packets);
		}
		long num3 = GC.GetAllocatedBytesForCurrentThread() - allocatedBytesForCurrentThread;
		if (num3 > 1024)
		{
			throw new InvalidOperationException($"转发映射循环仍分配 {num3} 字节。");
		}
		if (RelayRecovery.RetryDelay(1) != 1500 || RelayRecovery.RetryDelay(2) != 3000 || RelayRecovery.RetryDelay(20) != 30000)
		{
			throw new InvalidOperationException("自动恢复退避异常。");
		}
		return num3;
	}

	private void Recognize(Touch[] frame)
	{
		for (int i = 0; i < 2; i++)
		{
			for (int j = 0; j < frame.Length; j++)
			{
				Touch touch = frame[j];
				if ((touch.Info.pointerFlags & 0x10000) != 0 != (i == 0))
				{
					continue;
				}
				Win32.POINTER_INFO info = touch.Info;
				Win32.POINT ptPixelLocation = info.ptPixelLocation;
				bool flag = (info.pointerFlags & 0x10000) != 0 && !physical.Contains(info.pointerId);
				bool flag2 = (info.pointerFlags & 0x40000) != 0;
				bool flag3 = (info.pointerFlags & 0x8000) != 0;
				long tickCount = Environment.TickCount64;
				if (flag)
				{
					if (physical.Count == 0)
					{
						sequenceNumber++;
						monitor = Win32.Monitor(ptPixelLocation).rcMonitor;
						scale = Win32.Scale(ptPixelLocation);
						sequenceInvalid = false;
						guardEdgeSequence = false;
						singleStart = (singleLast = ptPixelLocation);
						singleTime = tickCount;
						singleCandidate = true;
						bottomStarted = false;
						panelConsumed = false;
					}
					physical.Add(info.pointerId);
					if (physical.Count > 1)
					{
						singleCandidate = false;
					}
					Win32.RECT rcMonitor = Win32.Monitor(ptPixelLocation).rcMonitor;
					if (rcMonitor.Left != monitor.Left || rcMonitor.Top != monitor.Top)
					{
						sequenceInvalid = true;
					}
					nint hwnd = OriginWindow(ptPixelLocation);
					Win32.GetWindowThreadProcessId(hwnd, out var process);
					Diagnostics.Edge($"Start seq={sequenceNumber} contacts={physical.Count} topDip={(double)(ptPixelLocation.Y - monitor.Top) / scale:F1} leftDip={(double)(ptPixelLocation.X - monitor.Left) / scale:F1} rightDip={(double)(monitor.Right - ptPixelLocation.X) / scale:F1} dpi={scale:F2} ownWindow={process == Environment.ProcessId} guard={CaptionTouchGuard.IsInputWindow(hwnd)} edges={options.EdgeEnabled}");
					if (process == Environment.ProcessId)
					{
						if (CloseGuardEdgePolicy.CanStart(options.EdgeEnabled, physical.Count == 1, CaptionTouchGuard.IsInputWindow(hwnd), ptPixelLocation.X, ptPixelLocation.Y, monitor.Left, monitor.Top, monitor.Right, scale))
						{
							guardEdgeSequence = true;
						}
						else
						{
							sequenceInvalid = true;
						}
					}
				}
				if (physical.Count > 0 && (ptPixelLocation.X < monitor.Left || ptPixelLocation.X >= monitor.Right || ptPixelLocation.Y < monitor.Top || ptPixelLocation.Y >= monitor.Bottom))
				{
					sequenceInvalid = true;
				}
				if (physical.Count == 1)
				{
					singleLast = ptPixelLocation;
				}
				MultiTouchBinding custom = (customShapes ? shapes.Feed(info.pointerId, (double)ptPixelLocation.X / scale, (double)ptPixelLocation.Y / scale, flag, flag2, flag3, tickCount, options) : null);
				MultiTouchRecognizer.Match? match = (options.Enabled ? recognizer.Feed(info.pointerId, (double)ptPixelLocation.X / scale, (double)ptPixelLocation.Y / scale, flag, flag2, flag3, tickCount, options.MaxSpacing, options.HoldSeconds * 1000.0, options.MatchStrictness) : ((MultiTouchRecognizer.Match?)null));
				if (flag3)
				{
					sequenceInvalid = true;
				}
				if (flag2 || flag3)
				{
					physical.Remove(info.pointerId);
					Diagnostics.Edge($"Finish seq={sequenceNumber} cancel={flag3} excluded={sequenceInvalid} single={singleCandidate} topStarted={topStarted} remaining={physical.Count} dyDip={(double)(singleLast.Y - singleStart.Y) / scale:F1} dxDip={(double)(singleLast.X - singleStart.X) / scale:F1}");
				}
				EdgeSwipe? edgeSwipe;
				if (custom != null)
				{
					edgeSwipe = panelSwipe;
					if (edgeSwipe.HasValue)
					{
						EdgeSwipe valueOrDefault = edgeSwipe.GetValueOrDefault();
						EmitPanel(valueOrDefault with
						{
							Phase = EdgePhase.Cancel
						});
						panelSwipe = null;
						panelConsumed = false;
					}
				}
				if (options.Enabled && !guardEdgeSequence && !sequenceInvalid && !panelConsumed)
				{
					MultiTouchRecognizer.Drag? drag = recognizer.CurrentDrag(options.MatchStrictness);
					if (drag.HasValue)
					{
						MultiTouchRecognizer.Drag drag2 = drag.GetValueOrDefault();
						if (options.Bindings.Any((MultiTouchBinding b) => b.Action.Enabled && b.Fingers == drag2.Fingers && b.Gesture == drag2.Gesture && b.Action.Kind == ActionKind.BuiltIn && b.Action.Command == "ControlCenter"))
						{
							EdgeKind kind = drag2.Gesture switch
							{
								"Up" => EdgeKind.Bottom, 
								"Down" => EdgeKind.TopRight, 
								"Left" => EdgeKind.Right, 
								_ => EdgeKind.Left, 
							};
							panelFingers = drag2.Fingers;
							panelConsumed = true;
							panelSwipe = new EdgeSwipe(kind, EdgePhase.Begin, new Win32.POINT((int)(drag2.X * scale), (int)(drag2.Y * scale)), 0.0, 0.0);
							EmitPanel(panelSwipe.Value);
						}
					}
				}
				edgeSwipe = panelSwipe;
				if (edgeSwipe.HasValue)
				{
					EdgeSwipe edgeSwipe2 = edgeSwipe.GetValueOrDefault();
					bool flag4 = sequenceInvalid || recognizer.Invalid || flag3 || (flag && physical.Count != panelFingers);
					if (!flag4 && physical.Count > 0)
					{
						string direction = edgeSwipe2.Kind switch
						{
							EdgeKind.Bottom => "Up", 
							EdgeKind.TopRight => "Down", 
							EdgeKind.Right => "Left", 
							_ => "Right", 
						};
						edgeSwipe2 = edgeSwipe2 with
						{
							Phase = EdgePhase.Update,
							Distance = recognizer.DragDistance(direction)
						};
						panelSwipe = edgeSwipe2;
						EmitPanel(edgeSwipe2);
					}
					if (flag4 || physical.Count == 0)
					{
						EmitPanel(edgeSwipe2 with
						{
							Phase = (flag4 ? EdgePhase.Cancel : EdgePhase.End)
						});
						panelSwipe = null;
					}
				}
				if (options.Enabled && !guardEdgeSequence && !sequenceInvalid && !bottomStarted && !topStarted && !panelConsumed)
				{
					if (custom != null && tickCount >= cooldown)
					{
						tapSequence.Reset();
						cooldown = tickCount + 650;
						Win32.POINT location = new Win32.POINT((int)Math.Round(shapes.EndCenter.X * scale), (int)Math.Round(shapes.EndCenter.Y * scale));
						ui.TryEnqueue(delegate
						{
							if (!disposed && store.Value.MultiTouch.Enabled)
							{
								this.Trigger?.Invoke(custom.Action, location);
							}
						});
					}
					else if (match.HasValue)
					{
						MultiTouchRecognizer.Match match2 = match.GetValueOrDefault();
						DispatchMatch(tapSequence.Displaced(match2, tickCount), tapScale);
						tapScale = scale;
						int maxTaps = (options.Bindings.Any((MultiTouchBinding b) => b.Action.Enabled && b.Fingers == match2.Fingers && b.Gesture == "TripleTap" && b.Action.Kind != ActionKind.None) ? 3 : ((!options.Bindings.Any((MultiTouchBinding b) => b.Action.Enabled && b.Fingers == match2.Fingers && b.Gesture == "DoubleTap" && b.Action.Kind != ActionKind.None)) ? 1 : 2));
						DispatchMatch(tapSequence.Feed(match2, tickCount, maxTaps), scale);
					}
				}
				else if (guardEdgeSequence || sequenceInvalid || panelConsumed || bottomStarted || topStarted)
				{
					tapSequence.Reset();
				}
				double num = (double)(singleStart.Y - singleLast.Y) / scale;
				if (options.EdgeEnabled && singleCandidate && !sequenceInvalid && !flag3 && (double)(monitor.Bottom - singleStart.Y) <= 8.0 * scale && num >= 12.0 && num > 1.5 * (double)Math.Abs(singleLast.X - singleStart.X) / scale && !bottomStarted)
				{
					bottomStarted = true;
					EmitBottom(EdgePhase.Begin, 0.0);
				}
				if (bottomStarted)
				{
					EmitBottom(EdgePhase.Update, Math.Max(0.0, num));
				}
				if (bottomStarted && (physical.Count == 0 || !singleCandidate || sequenceInvalid || flag3))
				{
					EmitBottom((!singleCandidate || sequenceInvalid || flag3) ? EdgePhase.Cancel : EdgePhase.End, num);
					bottomStarted = false;
				}
				double num2 = (double)(singleLast.Y - singleStart.Y) / scale;
				if (options.EdgeEnabled && singleCandidate && !sequenceInvalid && !flag3 && (double)(singleStart.Y - monitor.Top) <= EdgeTolerance.Pixels(scale, monitor.Width, monitor.Height) && (singleStart.X < monitor.Left + monitor.Width / 3 || singleStart.X >= monitor.Right - monitor.Width / 3) && num2 >= 12.0 && num2 > 1.5 * (double)Math.Abs(singleLast.X - singleStart.X) / scale && !topStarted)
				{
					topKind = ((singleStart.X >= monitor.Left + monitor.Width / 3) ? EdgeKind.TopRight : EdgeKind.TopLeft);
					topStarted = true;
					EmitTop(EdgePhase.Begin, 0.0);
				}
				if (topStarted)
				{
					EmitTop(EdgePhase.Update, Math.Max(0.0, num2));
				}
				if (topStarted && (physical.Count == 0 || !singleCandidate || sequenceInvalid || flag3))
				{
					EmitTop((!singleCandidate || sequenceInvalid || flag3) ? EdgePhase.Cancel : EdgePhase.End, num2);
					topStarted = false;
				}
			}
		}
	}

	private nint OriginWindow(Win32.POINT point)
	{
		return WindowFromPoint(point);
	}

	private void EmitBottom(EdgePhase phase, double distance)
	{
		EdgeSwipe swipe = new EdgeSwipe(EdgeKind.Bottom, phase, singleStart, distance, 0.0);
		ui.TryEnqueue(delegate
		{
			if (!disposed)
			{
				this.BottomSwipe?.Invoke(swipe);
			}
		});
	}

	private void EmitPanel(EdgeSwipe swipe)
	{
		ui.TryEnqueue(delegate
		{
			if (!disposed && (swipe.Phase == EdgePhase.Cancel || store.Value.MultiTouch.Enabled))
			{
				this.PanelGesture?.Invoke(swipe);
			}
		});
	}

	private void EmitTop(EdgePhase phase, double distance)
	{
		if (phase != EdgePhase.Update)
		{
			Diagnostics.Edge($"Top seq={sequenceNumber} edge={topKind} phase={phase} distance={distance:F1}");
		}
		EdgeSwipe swipe = new EdgeSwipe(topKind, phase, singleStart, distance, 0.0);
		ui.TryEnqueue(delegate
		{
			if (!disposed && (phase == EdgePhase.Cancel || store.Value.MultiTouch.EdgeEnabled))
			{
				this.TopGesture?.Invoke(swipe);
			}
		});
	}

	private void CancelBottom()
	{
		EdgeSwipe? edgeSwipe = panelSwipe;
		if (edgeSwipe.HasValue)
		{
			EdgeSwipe valueOrDefault = edgeSwipe.GetValueOrDefault();
			EmitPanel(valueOrDefault with
			{
				Phase = EdgePhase.Cancel
			});
			panelSwipe = null;
		}
		if (topStarted)
		{
			EmitTop(EdgePhase.Cancel, 0.0);
		}
		topStarted = false;
		if (bottomStarted)
		{
			EmitBottom(EdgePhase.Cancel, 0.0);
		}
		bottomStarted = false;
	}

	private nint Procedure(nint h, uint message, nuint w, nint l)
	{
		try
		{
			if (message == 32774 || (message == 275 && w == 10))
			{
				RefreshInputDesktop();
				return 0;
			}
			bool flag = message == 32773;
			if (!flag)
			{
				bool flag2 = ((message - 581 <= 2 || message == 588) ? true : false);
				flag = flag2;
			}
			if (flag && (DesktopVisibility.Locked || !InputDesktopState.CanInteract()))
			{
				desktopPaused = true;
				recovering = false;
				KillTimer(window, 9u);
				Stop();
				return 0;
			}
			if (message == 275 && w == 9)
			{
				KillTimer(window, 9u);
				if (!recovering || disposed || suspended || desktopPaused)
				{
					return 0;
				}
				lock (gate)
				{
					options = pending;
				}
				if (!options.Enabled && !options.EdgeEnabled)
				{
					recovering = false;
					Stop();
					return 0;
				}
				bool flag3 = false;
				foreach (uint recoveryContact in recoveryContacts)
				{
					Win32.POINTER_INFO info;
					bool pointerInfo = Win32.GetPointerInfo(recoveryContact, out info);
					bool flag4 = pointerInfo && (info.pointerFlags & 4) != 0 && (info.pointerFlags & 0x48000) == 0 && recoveryFrames.Wait(recoveryContact, info.frameId, Environment.TickCount64);
					flag3 = flag3 || flag4;
					Diagnostics.Edge($"Recovery contact={recoveryContact} found={pointerInfo} flags={info.pointerFlags:X} frame={info.frameId} wait={flag4} elapsedMs={Environment.TickCount64 - recoveryStarted}");
				}
				if (flag3)
				{
					SetTimer(window, 9u, 500u, 0);
					return 0;
				}
				Diagnostics.Event("TouchRelay.Recovery", $"重新注册；等待 {Environment.TickCount64 - recoveryStarted}ms；旧触点后续非 DOWN 帧不会转发为新按下");
				recovering = false;
				Configure();
				if (registered)
				{
					ui.TryEnqueue(delegate
					{
						Diagnostics.Event("TouchRelay.Recovery", "触摸接管已自动恢复");
					});
				}
				return 0;
			}
			switch (message)
			{
			case 32769u:
				Configure();
				return 0;
			case 32770u:
				suspended = w != 0;
				Configure();
				return 0;
			case 32772u:
				Stop();
				Configure();
				return 0;
			case 32771u:
				Stop();
				PostQuitMessage(0);
				return 0;
			case 32773u:
				Interlocked.Exchange(ref tickQueued, 0);
				if (!registered)
				{
					return 0;
				}
				DispatchMatch(tapSequence.Flush(Environment.TickCount64), tapScale);
				if (lastSend != 0L && Stopwatch.GetElapsedTime(lastSend).TotalMilliseconds < 30.0)
				{
					return 0;
				}
				injectionStage = "heartbeat";
				if (!SendTracked(HeartbeatPacket()))
				{
					Fail(Localization.T("触摸续报失败（InjectTouchInput，{0}），已暂停接管。", InjectionError), retry: true);
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
			if (flag && registered)
			{
				if (!Forward((uint)((int)w & 0xFFFF), out Touch[] frame))
				{
					Touch[] array = frame;
					for (int num = 0; num < array.Length; num++)
					{
						Touch touch = array[num];
						if ((touch.Info.pointerFlags & 0x48000) != 0)
						{
							physical.Remove(touch.Info.pointerId);
						}
					}
					Fail(Localization.T("触摸转发失败（{0}），已暂停接管。", forwardFailure), retry: true);
					return 0;
				}
				if (frame.Length != 0 && active.Count == 0)
				{
					recoveryAttempt = 0;
				}
				Recognize(frame);
				return 0;
			}
			if (message == 588 && registered)
			{
				Fail("触摸接管丢失，已暂停全局手势。", retry: true);
				return 0;
			}
		}
		catch (Exception ex)
		{
			Fail(Localization.T("触摸处理异常：") + ex.Message);
		}
		return Win32.DefWindowProc(h, message, w, l);
	}

	public void Dispose()
	{
		disposed = true;
		store.Changed -= Changed;
		OverlayOrder.ForegroundChanged -= ForegroundBindings;
		DesktopVisibility.Changed -= InputDesktopChanged;
		SystemEvents.SessionSwitch -= SessionChanged;
		SystemEvents.PowerModeChanged -= PowerChanged;
		SystemEvents.DisplaySettingsChanged -= DisplayChanged;
		if (window != 0)
		{
			PostMessage(window, 32771u, 0u, 0);
		}
		thread.Join(2000);
	}
}
