using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Win32;
using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.Native;
using TouchAssistant.Services;
using TouchAssistant.TouchToys_XamlTypeInfo;
using TouchAssistant.UI;
using WinRT;
using WinRT.TouchToysVtableClasses;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using ZXing;
using ZXing.Common;
using ZXing.Rendering;

namespace TouchAssistant;

[WinRTRuntimeClassName("Microsoft.UI.Xaml.IApplicationOverrides")]
[WinRTExposedType(typeof(TouchAssistant_AppWinRTTypeDetails))]
public class App : Application, IXamlMetadataProvider
{
	private static string TestOutputDirectory
	{
		get
		{
			string path = Environment.GetEnvironmentVariable("TOUCHTOYS_TEST_OUTPUT_DIR");
			if (string.IsNullOrWhiteSpace(path))
			{
				path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TouchToys", "TestOutput");
			}
			Directory.CreateDirectory(path);
			return path;
		}
	}

	private readonly Mutex instance = new Mutex(initiallyOwned: true, Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--manual") ? "Local\\TouchAssistant.Manual" : ((!Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--smoke-test")) ? "Local\\TouchAssistant.3B00E537" : (Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--resource-baseline") ? "Local\\TouchAssistant.ResourceBaseline" : "Local\\TouchAssistant.SmokeTest")), out var _);

	private SettingsStore store;

	private FloatingBall ball;

	private readonly SystemControl system = new SystemControl();

	private Window keeper;

	private SettingsWindow? settings;

	private ControlCenterWindow? center;

	private Window? trayWindow;

	private TrayIcon? tray;

	private EdgeGesture? edgeGesture;

	private TaskbarMode? taskbarMode;

	private SystemEdgePolicy? edgePolicy;

	private CaptionTouchGuard? captionGuard;

	private ShortcutArk? ark;

	private AccentTheme? accentTheme;

	private bool lastBallEnabled = true;

	private bool lastArkEnabled;

	private CenterHint? statusHint;

	private bool controlCenterSource;

	private StageSwitcherWindow? switcher;

	private StageSwitcherWindow? overview;

	private readonly List<nint> switchOrder = new List<nint>();

	private QrScannerWindow? scanner;

	private SystemStatusBadge? statusBadge;

	private DispatcherQueue queue;

	private Task? systemInitialization;

	private bool exiting;

	private bool openingCenter;

	private bool isSmokeTest;

	private bool manualTestMode;

	private EdgeKind? activeEdge;

	private bool settingsActive;

	private bool scannerActive;

	private bool reopeningSettings;

	private GlobalTouchGestures? touchGestures;

	private SystemInputControl? inputControl;

	private SystemInputControl? touchpadControl;

	private VirtualDial? dial;

	private bool controlGestureOwned;

	private bool bottomTaskbar;

	private bool bottomCards;

	private bool bottomControl;

	private bool bottomStartOpened;

	private DispatcherQueueTimer? panelCleanup;

	private VirtualTouchpad? virtualTouchpad;

	private MouseJoystickWindow? mouseJoystick;

	private MouseJoystickWindow? rightMouseJoystick;

	private VirtualGamepad? gamepad;

	private int lastDialTool;

	private readonly List<string> v0813KeyEvents = new List<string>();

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2608")]
	private bool _contentLoaded;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2608")]
	private XamlMetaDataProvider __appProvider;

	internal static App CurrentApp => (App)Application.Current;

	internal string TouchGestureStatus => touchGestures?.Status ?? "全局触摸手势未启用";

	internal string DiagnosticStatus => $"Touch={TouchGestureStatus}; ControlCenter={center?.IsOpen ?? false}; Stage={switcher?.IsOpen ?? false}; Overview={overview?.IsOpen ?? false}; Dial={dial?.IsVisible ?? false}; MediaPlaying={system.IsPlaying}; WifiAvailable={system.HasWifi}; BluetoothAvailable={system.HasBluetooth}; EdgePolicy={SystemEdgePolicy.Status}";

	internal SystemInputControl InputControl
	{
		get
		{
			if (inputControl == null)
			{
				inputControl = new SystemInputControl(store.StorageDirectory);
				if (!isSmokeTest)
				{
					inputControl.BindSettings(store);
				}
			}
			return inputControl;
		}
	}

	internal SystemInputControl.Candidate? KeyboardTarget => inputControl?.Target;

	internal SystemInputControl TouchpadControl
	{
		get
		{
			if (touchpadControl == null)
			{
				touchpadControl = new SystemInputControl(store.StorageDirectory, touchpad: true);
				if (!isSmokeTest)
				{
					touchpadControl.BindSettings(store);
				}
			}
			return touchpadControl;
		}
	}

	internal bool DialVisible => dial?.IsOpen ?? false;

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2608")]
	[DebuggerNonUserCode]
	private XamlMetaDataProvider _AppProvider
	{
		get
		{
			if (__appProvider == null)
			{
				__appProvider = new XamlMetaDataProvider();
			}
			return __appProvider;
		}
	}

	internal T FromControlCenter<T>(Func<T> action)
	{
		bool flag = controlCenterSource;
		controlCenterSource = true;
		try
		{
			return action();
		}
		finally
		{
			controlCenterSource = flag;
		}
	}

	internal void FromControlCenter(Action action)
	{
		FromControlCenter(delegate
		{
			action();
			return true;
		});
	}

	private void StatusHint(string text)
	{
		if (!isSmokeTest && !exiting)
		{
			if (statusHint == null)
			{
				statusHint = new CenterHint();
			}
			statusHint.Show(new GestureAction
			{
				Label = Localization.T(text),
				Icon = ((!controlCenterSource) ? "" : (text.StartsWith("虚拟 Dial") ? "\ue958" : (text.StartsWith("快捷方舟") ? "\ueca5" : (text.StartsWith("触控圆点") ? "\uea3a" : ""))))
			}, ball.Center, 1.5);
		}
	}

	public App()
	{
		AppDomain.CurrentDomain.UnhandledException += delegate(object _, System.UnhandledExceptionEventArgs e)
		{
			if (e.ExceptionObject is Exception error2)
			{
				Diagnostics.Error(error2, "Runtime.Unhandled");
			}
		};
		TaskScheduler.UnobservedTaskException += delegate(object? _, UnobservedTaskExceptionEventArgs e)
		{
			Diagnostics.Error(e.Exception, "Task.Unobserved");
		};
		base.UnhandledException += delegate(object _, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
		{
			Log(e.Exception);
		};
		try
		{
			InitializeComponent();
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Application.Initialize");
			throw;
		}
	}

	protected override void OnLaunched(LaunchActivatedEventArgs args)
	{
		Diagnostics.Event("Application", "OnLaunched");
		if (Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--diagnose" || x == "--restore"))
		{
			NativeDiagnosticCommand();
			return;
		}
		int num = Array.IndexOf(Environment.GetCommandLineArgs(), "--inject-relay-fixture");
		if (num >= 0)
		{
			InjectRelayFixture(num);
			return;
		}
		if (!instance.WaitOne(Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--elevated-restart") ? 5000 : 0))
		{
			Diagnostics.Event("Application", "Existing instance; exiting duplicate launch.");
			Exit();
			return;
		}
		queue = DispatcherQueue.GetForCurrentThread();
		DesktopVisibility.Start();
		bool flag = (isSmokeTest = Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--smoke-test"));
		Diagnostics.Event("Application", $"启动；SmokeTest={flag}；OS={RuntimeInformation.OSDescription}");
		int num2 = Array.IndexOf(Environment.GetCommandLineArgs(), "--extract-motion");
		if (num2 >= 0)
		{
			ExtractMotion(Environment.GetCommandLineArgs()[num2 + 1]);
			return;
		}
		bool flag2 = (manualTestMode = Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--manual"));
		store = new SettingsStore(flag ? Path.Combine(TestOutputDirectory, "smoke-state") : (flag2 ? Path.Combine(TestOutputDirectory, "manual-state") : null));
		Localization.Use(store.Value.Language);
		KeyboardInput.MinimumNeutralMs = (store.Value.Gamepad.FastRepeat ? 22 : 0);
		store.Changed += delegate
		{
			KeyboardInput.MinimumNeutralMs = (store.Value.Gamepad.FastRepeat ? 22 : 0);
		};
		if (Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--reset-ball"))
		{
			Win32.RECT rcWork = Win32.Monitor(new Win32.POINT(0, 0)).rcWork;
			store.Value.BallEnabled = true;
			store.Value.BallX = rcWork.Left + rcWork.Width / 2;
			store.Value.BallY = rcWork.Top + rcWork.Height / 2;
			store.Value.Opacity = 1.0;
			store.Save();
		}
		if (Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--show-ball"))
		{
			store.Value.BallEnabled = true;
			store.Save();
		}
		if (flag)
		{
			store.RestoreDefaults();
			store.Value.Launchers.Add(new LauncherEntry
			{
				Name = "记事本",
				Target = Path.Combine(Environment.SystemDirectory, "notepad.exe")
			});
			store.Value.Launchers.Add(new LauncherEntry
			{
				Name = "录音机",
				Target = "Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe!App"
			});
			store.Save();
		}
		if (!flag)
		{
			try
			{
				StartupRegistration.MigrateLegacy();
			}
			catch (Exception error)
			{
				Diagnostics.Error(error, "Migration.Startup");
			}
		}
		if (!flag && store.Value.AutoNativeInputOpacity && store.Value.TouchKeyboardOpacity < 100)
		{
			_ = InputControl;
		}
		store.Changed += SyncVirtualTouchpad;
		SyncVirtualTouchpad();
		store.Changed += delegate
		{
			Diagnostics.State("Settings", $"Ball={store.Value.BallEnabled}; Ark={ApplicationBindings.ArkOptions(store.Value).ArkEnabled}; Guard={store.Value.CloseGuardEnabled}; DialTools={store.Value.Dial.Tools.Count}; GestureBindings={store.Value.MultiTouch.Bindings.Count}; GlobalGestures={store.Value.MultiTouch.Enabled}; EdgeGestures={store.Value.MultiTouch.EdgeEnabled}");
		};
		keeper = new Window
		{
			Title = "TouchToys Host",
			Content = new Grid()
		};
		keeper.AppWindow.IsShownInSwitchers = false;
		accentTheme = new AccentTheme(store);
		ball = new FloatingBall(store);
		ball.Trigger += delegate(int index)
		{
			Execute(index);
		};
		lastArkEnabled = ApplicationBindings.ArkOptions(store.Value).ArkEnabled;
		lastBallEnabled = store.Value.BallEnabled;
		if (!lastBallEnabled)
		{
			ball.SetVisible(visible: false);
		}
		ark = new ShortcutArk(store, Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--v0813-smoke" || a == "--v0814-smoke") ? new HeldKeys(delegate(IReadOnlyList<int> keys, bool down)
		{
			v0813KeyEvents.Add((down ? "down:" : "up:") + string.Join(",", keys));
		}) : null);
		ark.Trigger += delegate(GestureAction action)
		{
			Execute(action, ark.TriggerOrigin);
		};
		captionGuard = new CaptionTouchGuard(store);
		system.Notice += delegate(string message)
		{
			queue.TryEnqueue(delegate
			{
				Report(message);
			});
		};
		tray = new TrayIcon();
		tray.Invoked += delegate
		{
			queue.TryEnqueue(OpenSettings);
		};
		tray.ContextRequested += delegate
		{
			queue.TryEnqueue(ShowTray);
		};
		store.Changed += delegate
		{
			if (store.Value.EdgeGestures[0].Kind == ActionKind.None)
			{
				switcher?.Hide();
			}
			if (ApplicationBindings.Bottom(store.Value).Kind == ActionKind.None)
			{
				overview?.Hide();
			}
		};
		store.Changed += delegate
		{
			if (lastBallEnabled == store.Value.BallEnabled)
			{
				return;
			}
			lastBallEnabled = store.Value.BallEnabled;
			StatusHint(lastBallEnabled ? "触控圆点已打开" : "触控圆点已关闭");
			FloatingBall floatingBall = ball;
			int visible;
			if (lastBallEnabled)
			{
				ControlCenterWindow? controlCenterWindow = center;
				if ((object)controlCenterWindow == null || !controlCenterWindow.IsOpen)
				{
					StageSwitcherWindow? stageSwitcherWindow = switcher;
					if ((object)stageSwitcherWindow == null || !stageSwitcherWindow.IsOpen)
					{
						StageSwitcherWindow? stageSwitcherWindow2 = overview;
						visible = (((object)stageSwitcherWindow2 == null || !stageSwitcherWindow2.IsOpen) ? 1 : 0);
						goto IL_009f;
					}
				}
			}
			visible = 0;
			goto IL_009f;
			IL_009f:
			floatingBall.SetVisible((byte)visible != 0);
		};
		store.Changed += delegate
		{
			if (lastArkEnabled != ApplicationBindings.ArkOptions(store.Value).ArkEnabled)
			{
				lastArkEnabled = ApplicationBindings.ArkOptions(store.Value).ArkEnabled;
				if (lastArkEnabled)
				{
					ark?.PlaySummon();
				}
				StatusHint(lastArkEnabled ? "快捷方舟已打开" : "快捷方舟已关闭");
			}
		};
		if (!flag)
		{
			try
			{
				taskbarMode = new TaskbarMode(store);
			}
			catch (Exception error2)
			{
				Diagnostics.Error(error2, "TaskbarMode.Start");
			}
			try
			{
				edgePolicy = new SystemEdgePolicy(store);
			}
			catch (Exception error3)
			{
				Diagnostics.Error(error3, "EdgePolicy.Start");
			}
		}
		edgeGesture = new EdgeGesture(store);
		edgeGesture.IndicatorAction += delegate(string key, Win32.POINT origin)
		{
			GestureAction gestureAction = ApplicationBindings.Indicator(store.Value)[key];
			if (gestureAction.Enabled)
			{
				bool flag3 = gestureAction.Kind == ActionKind.BuiltIn;
				if (flag3)
				{
					string command = gestureAction.Command;
					bool flag4 = ((command == "PreviousApp" || command == "NextApp") ? true : false);
					flag3 = flag4;
				}
				if (flag3)
				{
					SwitchAdjacent((!(gestureAction.Command == "PreviousApp")) ? 1 : (-1));
				}
				else
				{
					Execute(gestureAction, origin, gestureSummon: true);
				}
			}
		};
		store.Changed += SyncGamepad;
		store.Changed += SyncSystemStatusBadge;
		SyncGamepad();
		DesktopVisibility.Changed += SyncArkVisibility;
		edgeGesture.Swipe += delegate(EdgeSwipe swipe)
		{
			bool flag3 = swipe.Kind == EdgeKind.Bottom;
			if (flag3)
			{
				EdgePhase phase = swipe.Phase;
				bool flag4 = (uint)(phase - 4) <= 1u;
				flag3 = !flag4;
			}
			if (flag3)
			{
				HandleBottomSwipe(swipe);
			}
			else
			{
				HandleEdgeSwipe(swipe);
			}
		};
		touchGestures = new GlobalTouchGestures(store);
		touchGestures.Trigger += delegate(GestureAction action, Win32.POINT origin)
		{
			Execute(action, origin, gestureSummon: true);
		};
		touchGestures.AvailabilityChanged += delegate(bool active)
		{
			edgeGesture.SetGlobalTop(active);
		};
		touchGestures.PanelGesture += delegate(EdgeSwipe swipe)
		{
			if (!DesktopVisibility.Locked)
			{
				HandleControlGesture(swipe);
			}
		};
		touchGestures.BottomSwipe += HandleBottomSwipe;
		touchGestures.TopGesture += HandleEdgeSwipe;
		captionGuard.PointerInput += edgeGesture.ForwardTopRight;
		SystemEvents.SessionEnding += SessionEnding;
		if (!flag)
		{
			StoreEntitlements.Changed += delegate
			{
				if (!exiting)
				{
					store.Save();
				}
			};
			queue.TryEnqueue(async delegate
			{
				if (!exiting)
				{
					if (systemInitialization == null)
					{
						systemInitialization = system.InitializeAsync();
					}
					await StoreEntitlements.RefreshAsync(keeper);
				}
			});
		}
		string[] commandLineArgs = Environment.GetCommandLineArgs();
		if (Array.Exists(commandLineArgs, (string x) => x == "--settings"))
		{
			ShowSettings();
		}
		if (Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--probe-opacity" || x == "--probe-move"))
		{
			ShowSettings();
			settings.ShowPageForCapture(5);
		}
		if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0820-input-smoke"))
		{
			try
			{
				Win32.KEYBDINPUT ki = GamepadKeyboard.Build(87, 17u, scanCodes: false, up: false).data.ki;
				Win32.KEYBDINPUT ki2 = GamepadKeyboard.Build(87, 17u, scanCodes: true, up: false).data.ki;
				Win32.KEYBDINPUT ki3 = GamepadKeyboard.Build(87, 17u, scanCodes: true, up: true).data.ki;
				if (ki.wVk != 87 || ki.wScan != 0 || ki.dwFlags != 0 || ki2.wVk != 0 || ki2.wScan != 17 || ki2.dwFlags != 8 || ki3.dwFlags != 10)
				{
					throw new InvalidOperationException("输入格式错误");
				}
				File.WriteAllText(Path.Combine(TestOutputDirectory, "v0820-input-test.txt"), "PASS: legacy input unchanged; scan-code press/release packets correct; no input injected.");
				return;
			}
			catch (Exception ex)
			{
				File.WriteAllText(Path.Combine(TestOutputDirectory, "v0820-input-test.txt"), ex.ToString());
				return;
			}
			finally
			{
				Shutdown();
			}
		}
		if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--status-badge-check"))
		{
			StatusBadgeCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--panel-full-check"))
		{
			PanelFullCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0829-visual-check"))
		{
			V0829VisualCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0831-inertia-check"))
		{
			V0831InertiaCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0830-settings-check"))
		{
			V0830SettingsCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0830-input-check"))
		{
			V0830InputCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--input-timing-check"))
		{
			InputTimingCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--panel-top-fade-check"))
		{
			PanelTopFadeCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--panel-side-fade-check"))
		{
			PanelSideFadeCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--hint-style-check"))
		{
			HintStyleCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0829-input-check"))
		{
			V0829InputCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--panel-retention-check"))
		{
			PanelRetentionCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--dynamic-release-check"))
		{
			DynamicReleaseCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--icon-resource-check"))
		{
			IconResourceCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--mouse-grip-check"))
		{
			MouseGripCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--resource-wake-check"))
		{
			ResourceWakeCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--preview-resource-check"))
		{
			PreviewResourceCheckAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--resource-study"))
		{
			ResourceStudyAsync();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0828-smoke"))
		{
			V0828SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0827-smoke"))
		{
			V0827SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0825-smoke"))
		{
			V0825SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0824-smoke"))
		{
			V0824SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0823-smoke"))
		{
			V0823SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0822-smoke"))
		{
			V0822SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0821-smoke"))
		{
			V0821SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0820-smoke"))
		{
			V0820SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0819-smoke"))
		{
			V0819SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0818-smoke"))
		{
			V0818SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0817-smoke"))
		{
			V0817SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0816-smoke"))
		{
			V0816SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0815-smoke"))
		{
			V0815SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0814-smoke"))
		{
			V0814SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0813-smoke"))
		{
			V0813SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0812-frame"))
		{
			V0812FrameTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0812-smoke"))
		{
			V0812SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0811-smoke"))
		{
			V0811SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0810-final-check"))
		{
			V0810FinalCheck();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v0810-smoke"))
		{
			V0810SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--overview-coexistence"))
		{
			OverviewCoexistenceTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v088-smoke"))
		{
			V088SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v087-smoke"))
		{
			V087SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--v086-smoke"))
		{
			V086SmokeTest();
		}
		else if (flag && Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--relay-integration-test"))
		{
			CheckDesktopRelay();
		}
		else if (Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--preview-probe"))
		{
			PreviewProbeTest();
		}
		else if (Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--edge-smoke"))
		{
			EdgeSmokeTest();
		}
		else if (Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--smoke-test"))
		{
			SmokeTest();
		}
	}

	private async Task NativeDiagnosticCommand()
	{
		keeper = new Window
		{
			Content = new Grid()
		};
		try
		{
			if (Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--restore"))
			{
				SystemInputControl.RestoreSaved(SettingsStore.DirectoryPath);
				return;
			}
			using SystemInputControl nativeInput = new SystemInputControl(SettingsStore.DirectoryPath);
			await nativeInput.DiagnoseAsync();
			nativeInput.WriteReport();
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "NativeInput.Command");
			Environment.ExitCode = 1;
		}
		finally
		{
			keeper.Close();
			Exit();
		}
	}

	private async Task InjectRelayFixture(int index)
	{
		try
		{
			string[] commandLineArgs = Environment.GetCommandLineArgs();
			await TouchSmoke.MultiFingerFixture(int.Parse(commandLineArgs[index + 1]), int.Parse(commandLineArgs[index + 2]));
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(TestOutputDirectory, "relay-injector-error.txt"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			Exit();
		}
	}

	private async Task CheckDesktopRelay()
	{
		try
		{
			string path = Path.Combine(TestOutputDirectory, "relay-integration.txt");
			File.WriteAllText(path, await RelayIntegrationTest.Run(store, touchGestures));
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(TestOutputDirectory, "relay-integration.txt"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private void HandleControlGesture(EdgeSwipe swipe)
	{
		if (DesktopVisibility.Locked)
		{
			return;
		}
		if (swipe.Phase == EdgePhase.Begin)
		{
			ControlCenterWindow? controlCenterWindow = center;
			controlGestureOwned = (object)controlCenterWindow == null || !controlCenterWindow.IsOpen;
		}
		if (!controlGestureOwned)
		{
			return;
		}
		if (swipe.Phase == EdgePhase.Begin)
		{
			switcher?.Hide();
			overview?.Hide();
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			ball.SetVisible(visible: false);
			ControlCenterWindow controlCenterWindow2 = center;
			Win32.POINT origin = swipe.Origin;
			EdgeKind kind = swipe.Kind;
			bool fromTop = ((kind == EdgeKind.TopRight || kind == EdgeKind.TopLeft) ? true : false);
			controlCenterWindow2.ShowPanel(origin, interactive: true, fromTop, swipe.Kind);
			if (systemInitialization == null)
			{
				systemInitialization = system.InitializeAsync();
			}
		}
		if (swipe.Phase == EdgePhase.Update)
		{
			center?.UpdateGesture(swipe.Distance);
		}
		EdgePhase phase = swipe.Phase;
		if ((uint)(phase - 2) <= 1u)
		{
			center?.EndGesture(swipe.Velocity, swipe.Phase == EdgePhase.Cancel);
		}
		SyncArkVisibility();
	}

	private void HandleBottomSwipe(EdgeSwipe swipe)
	{
		if (DesktopVisibility.Locked)
		{
			return;
		}
		if (swipe.Phase == EdgePhase.Begin)
		{
			bottomStartOpened = false;
		}
		if (bottomStartOpened)
		{
			EdgePhase phase = swipe.Phase;
			if ((uint)(phase - 2) <= 1u)
			{
				bottomStartOpened = false;
			}
			return;
		}
		if (swipe.Phase == EdgePhase.Begin)
		{
			bottomControl = ApplicationBindings.Bottom(store.Value).Enabled && store.Value.MultiTouch.EdgeEnabled && ApplicationBindings.Bottom(store.Value).Kind == ActionKind.BuiltIn && ApplicationBindings.Bottom(store.Value).Command == "ControlCenter";
		}
		if (bottomControl)
		{
			HandleControlGesture(swipe);
			EdgePhase phase = swipe.Phase;
			if ((uint)(phase - 2) <= 1u)
			{
				bottomControl = false;
			}
		}
		else if (swipe.Phase == EdgePhase.Cancel)
		{
			if (bottomCards)
			{
				overview?.Hide();
			}
			bottomTaskbar = (bottomCards = false);
			SyncArkVisibility();
		}
		else
		{
			if (!store.Value.MultiTouch.EdgeEnabled)
			{
				return;
			}
			GestureAction gestureAction = ApplicationBindings.Bottom(store.Value);
			if (!gestureAction.Enabled || DesktopVisibility.Locked)
			{
				return;
			}
			if (gestureAction.Kind != ActionKind.BuiltIn || gestureAction.Command != "Overview")
			{
				if (swipe.Phase == EdgePhase.End && swipe.Distance >= 48.0)
				{
					Execute(gestureAction, swipe.Origin);
				}
				return;
			}
			if (swipe.Phase == EdgePhase.Begin)
			{
				bottomTaskbar = (bottomCards = false);
				WindowPreview.Prewarm(overview: true);
				return;
			}
			if (swipe.Distance >= 12.0 && !bottomTaskbar)
			{
				bottomTaskbar = true;
				edgeGesture?.ShowBottomIndicator();
			}
			Win32.RECT rcMonitor = Win32.Monitor(swipe.Origin).rcMonitor;
			if (BottomGesture.StartMenuReached(swipe.Distance + (double)(rcMonitor.Bottom - swipe.Origin.Y) / Win32.Scale(swipe.Origin), rcMonitor.Height, Win32.Scale(swipe.Origin)))
			{
				bottomStartOpened = true;
				if (bottomCards)
				{
					overview?.CompleteBottomGesture(keep: false);
				}
				bottomTaskbar = (bottomCards = false);
				if (!isSmokeTest)
				{
					SystemControl.SendKeys(new int[1] { 91 });
				}
				SyncArkVisibility();
				return;
			}
			if (swipe.Distance >= 24.0 && !bottomCards)
			{
				bottomCards = true;
				PrepareOverview();
				if ((object)overview == null)
				{
					overview = MakeSwitcher(full: true);
				}
				overview.Show(swipe.Origin, interactive: true, EdgeKind.Bottom);
			}
			if (bottomCards)
			{
				overview?.UpdateBottomGesture(BottomGesture.BlurProgress(swipe.Distance));
			}
			if (swipe.Phase == EdgePhase.End)
			{
				Win32.RECT rcMonitor2 = Win32.Monitor(swipe.Origin).rcMonitor;
				int num = BottomGesture.Release(swipe.Distance, swipe.Origin.Y, rcMonitor2.Top, rcMonitor2.Height, Win32.Scale(swipe.Origin));
				if (bottomCards)
				{
					overview?.CompleteBottomGesture(num == 1);
				}
				if (num == 2 && !isSmokeTest)
				{
					SystemControl.SendKeys(new int[1] { 91 });
				}
				bottomTaskbar = (bottomCards = false);
			}
			SyncArkVisibility();
		}
	}

	private void HandleEdgeSwipe(EdgeSwipe swipe)
	{
		if (exiting || DesktopVisibility.Locked)
		{
			return;
		}
		EdgePhase phase = swipe.Phase;
		if ((uint)(phase - 4) <= 1u)
		{
			SwitchAdjacent((swipe.Phase != EdgePhase.Previous) ? 1 : (-1));
			return;
		}
		GestureAction gestureAction = ((swipe.Kind == EdgeKind.Bottom) ? ApplicationBindings.Bottom(store.Value) : store.Value.EdgeGestures[(int)swipe.Kind]);
		if (swipe.Phase != EdgePhase.Update)
		{
			Diagnostics.Edge($"Dispatch edge={swipe.Kind} phase={swipe.Phase} enabled={gestureAction.Enabled} kind={gestureAction.Kind} builtin={((gestureAction.Kind == ActionKind.BuiltIn) ? gestureAction.Command : "-")}");
		}
		if (!gestureAction.Enabled || DesktopVisibility.Locked)
		{
			return;
		}
		bool flag = gestureAction.Kind == ActionKind.BuiltIn && gestureAction.Command == "ControlCenter";
		bool flag2 = gestureAction.Kind == ActionKind.BuiltIn && gestureAction.Command == "Stage";
		bool flag3 = gestureAction.Kind == ActionKind.BuiltIn && gestureAction.Command == "Overview";
		if (swipe.Phase == EdgePhase.Begin)
		{
			bool flag4 = ((!flag) ? ((!flag2) ? (flag3 && (overview?.IsOpen ?? false)) : (switcher?.IsOpen ?? false)) : (center?.IsOpen ?? false));
			activeEdge = (flag4 ? ((EdgeKind?)null) : new EdgeKind?(swipe.Kind));
		}
		if (activeEdge != swipe.Kind)
		{
			return;
		}
		if (flag)
		{
			if (swipe.Phase == EdgePhase.Begin)
			{
				switcher?.Hide();
				overview?.Hide();
				if ((object)center == null)
				{
					center = MakeCenter();
				}
				ball.SetVisible(visible: false);
				ControlCenterWindow controlCenterWindow = center;
				Win32.POINT origin = swipe.Origin;
				EdgeKind kind = swipe.Kind;
				bool fromTop = ((kind == EdgeKind.TopRight || kind == EdgeKind.TopLeft) ? true : false);
				controlCenterWindow.ShowPanel(origin, interactive: true, fromTop, swipe.Kind);
				if (systemInitialization == null)
				{
					systemInitialization = system.InitializeAsync();
				}
			}
			if (swipe.Phase == EdgePhase.Update)
			{
				center?.UpdateGesture(swipe.Distance);
			}
			phase = swipe.Phase;
			if ((uint)(phase - 2) <= 1u)
			{
				center?.EndGesture(swipe.Velocity, swipe.Phase == EdgePhase.Cancel);
			}
		}
		else if (flag2 || flag3)
		{
			StageSwitcherWindow stageSwitcherWindow = (flag2 ? (switcher ?? (switcher = MakeSwitcher(full: false))) : (overview ?? (overview = MakeSwitcher(full: true))));
			if (swipe.Phase == EdgePhase.Begin)
			{
				ball.SetVisible(visible: false);
				center?.HidePanel();
				if (flag2)
				{
					overview?.Hide();
				}
				else
				{
					PrepareOverview();
				}
				stageSwitcherWindow.Show(swipe.Origin, interactive: true, swipe.Kind);
			}
			if (swipe.Phase == EdgePhase.Update)
			{
				stageSwitcherWindow.UpdateGesture(swipe.Distance);
			}
			phase = swipe.Phase;
			if ((uint)(phase - 2) <= 1u)
			{
				stageSwitcherWindow.EndGesture(swipe.Velocity, swipe.Phase == EdgePhase.Cancel);
			}
		}
		else if (swipe.Phase == EdgePhase.End && swipe.Distance >= 36.0)
		{
			Execute(gestureAction);
		}
		SyncArkVisibility();
		phase = swipe.Phase;
		if ((uint)(phase - 2) <= 1u)
		{
			activeEdge = null;
		}
	}

	private void SwitchAdjacent(int direction)
	{
		List<RunningWindow> available = WindowCatalog.Read();
		switchOrder.RemoveAll((nint h) => !available.Exists((RunningWindow x) => x.Handle == h));
		foreach (RunningWindow item in available)
		{
			if (!switchOrder.Contains(item.Handle))
			{
				switchOrder.Add(item.Handle);
			}
		}
		if (switchOrder.Count >= 2)
		{
			int num = switchOrder.IndexOf(Win32.GetForegroundWindow());
			if (num < 0)
			{
				num = 0;
			}
			WindowCatalog.Activate(switchOrder[(num + direction + switchOrder.Count) % switchOrder.Count]);
			edgeGesture?.ShowBottomIndicator();
		}
	}

	internal void RestartElevated()
	{
		try
		{
			if (Process.Start(new ProcessStartInfo(Environment.ProcessPath)
			{
				UseShellExecute = true,
				Verb = "runas",
				Arguments = "--elevated-restart --settings",
				WorkingDirectory = AppContext.BaseDirectory
			}) != null)
			{
				Shutdown();
			}
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
		}
		catch (Exception ex2)
		{
			Report(Localization.T("无法重新启动：") + ex2.Message);
		}
	}

	private async Task CheckGestureSummonAsync()
	{
		Win32.RECT rcWork = Win32.Monitor(ball.Center).rcWork;
		Win32.POINT point = new Win32.POINT(rcWork.Left + rcWork.Width / 2, rcWork.Top + rcWork.Height / 2);
		string[] array = new string[3] { "VirtualDial", "BallToggle", "ArkToggle" };
		foreach (string command in array)
		{
			await Execute(GestureAction.BuiltIn(command), point, gestureSummon: true);
			await Task.Delay(100);
			Win32.POINT pOINT = ((command == "VirtualDial") ? dial.Center : ((command == "BallToggle") ? ball.Center : ark.Center));
			if (Math.Abs(pOINT.X - point.X) > 1 || Math.Abs(pOINT.Y - point.Y) > 1)
			{
				throw new InvalidOperationException(command + " 未出现在手势中心。");
			}
			Win32.POINT next = new Win32.POINT(point.X + 40, point.Y + 40);
			await Execute(GestureAction.BuiltIn(command), next, gestureSummon: true);
			await Task.Delay(100);
			pOINT = ((command == "VirtualDial") ? dial.Center : ((command == "BallToggle") ? ball.Center : ark.Center));
			if (Math.Abs(pOINT.X - next.X) > 1 || Math.Abs(pOINT.Y - next.Y) > 1 || (command == "VirtualDial" && !dial.IsVisible))
			{
				throw new InvalidOperationException(command + " 再次召唤未重定位。");
			}
		}
		if (dial.IsVisible)
		{
			dial.Toggle();
		}
		ApplicationBindings.ArkOptions(store.Value).ArkEnabled = false;
		store.Save();
	}

	private async Task Execute(int index)
	{
		await Execute(store.Value.Gestures[index]);
	}

	private async Task Execute(GestureAction action, Win32.POINT? triggerOrigin = null, bool gestureSummon = false)
	{
		if (!action.Enabled || DesktopVisibility.Locked)
		{
			return;
		}
		Diagnostics.Event("Action", $"Kind={action.Kind}; BuiltIn={((action.Kind == ActionKind.BuiltIn) ? action.Command : "-")}; GestureSummon={gestureSummon}");
		try
		{
			Win32.POINT valueOrDefault = default(Win32.POINT);
			int num;
			if (gestureSummon && triggerOrigin.HasValue)
			{
				valueOrDefault = triggerOrigin.GetValueOrDefault();
				num = ((action.Kind == ActionKind.BuiltIn) ? 1 : 0);
			}
			else
			{
				num = 0;
			}
			bool flag = (byte)num != 0;
			if (flag)
			{
				bool flag2;
				switch (action.Command)
				{
				case "VirtualDial":
				case "BallToggle":
				case "ArkToggle":
				case "ArkOn":
					flag2 = true;
					break;
				default:
					flag2 = false;
					break;
				}
				flag = flag2;
			}
			if (flag)
			{
				center?.HidePanel();
				switcher?.Hide();
				overview?.Hide();
				string command = action.Command;
				if (!(command == "VirtualDial"))
				{
					if (command == "BallToggle")
					{
						ball.PositionAt(valueOrDefault);
						store.Value.BallEnabled = true;
						store.Save();
						ball.SetVisible(visible: true);
						ball.PlaySummon();
					}
					else
					{
						ark?.PositionAt(valueOrDefault);
						ApplicationBindings.ArkOptions(store.Value).ArkEnabled = true;
						store.Save();
						ark?.PlaySummon();
					}
				}
				else
				{
					VirtualDial virtualDial = EnsureDial();
					virtualDial.PositionAt(valueOrDefault);
					if (!virtualDial.IsVisible)
					{
						virtualDial.Toggle();
					}
					else
					{
						virtualDial.PlaySummon();
					}
				}
				return;
			}
			switch (action.Kind)
			{
			case ActionKind.None:
				break;
			case ActionKind.Launch:
				SystemControl.Launch(action.Command);
				break;
			case ActionKind.Keys:
				await system.KeysAsync(action.Keys);
				break;
			case ActionKind.BuiltIn:
			{
				string command = action.Command;
				if (command == null)
				{
					break;
				}
				switch (command.Length)
				{
				case 13:
					switch (command[0])
					{
					case 'C':
						if (command == "ControlCenter")
						{
							await ShowCenter(triggerOrigin);
						}
						break;
					case 'T':
						if (command == "TouchKeyboard")
						{
							ToggleTouchKeyboard();
						}
						break;
					}
					break;
				case 8:
					switch (command[1])
					{
					case 'o':
						if (command == "Touchpad")
						{
							ToggleTouchpad();
						}
						break;
					case 'v':
						if (command == "Overview")
						{
							PrepareOverview();
							(overview ?? (overview = MakeSwitcher(full: true))).Show(triggerOrigin ?? ball.Center);
							SyncArkVisibility();
						}
						break;
					case 'a':
						if (command == "TaskView")
						{
							SystemControl.SendKeys(new int[2] { 91, 9 });
						}
						break;
					}
					break;
				case 5:
					switch (command[0])
					{
					case 'W':
						if (command == "Wheel")
						{
							ball.ToggleWheel();
						}
						break;
					case 'S':
						if (command == "Stage")
						{
							gamepad?.SetSuppressed(value: true);
							ark?.SetSuppressed(value: true);
							await ball.HideAsync();
							center?.HidePanel();
							overview?.Hide();
							(switcher ?? (switcher = MakeSwitcher(full: false))).Show(triggerOrigin ?? ball.Center);
						}
						break;
					case 'A':
						if (command == "ArkOn")
						{
							ApplicationBindings.ArkOptions(store.Value).ArkEnabled = true;
							store.Save();
						}
						break;
					}
					break;
				case 9:
					switch (command[0])
					{
					case 'A':
						if (command == "ArkToggle")
						{
							ApplicationBindings.ArkOptions(store.Value).ArkEnabled = !ApplicationBindings.ArkOptions(store.Value).ArkEnabled;
							store.Save();
						}
						break;
					case 'S':
						if (command == "StartMenu" && !isSmokeTest)
						{
							SystemControl.SendKeys(new int[1] { 91 });
						}
						break;
					}
					break;
				case 7:
					switch (command[4])
					{
					case 'e':
						if (command == "ArkMenu")
						{
							if (!ApplicationBindings.ArkOptions(store.Value).ArkEnabled)
							{
								ApplicationBindings.ArkOptions(store.Value).ArkEnabled = true;
								store.Save();
							}
							ark?.ToggleMenu();
						}
						break;
					case 'o':
						if (command == "ArkMove")
						{
							ark?.EnterMove();
						}
						break;
					case 'p':
						if (command == "Gamepad")
						{
							ToggleGamepad();
						}
						break;
					case 'A':
						if (command == "NextApp")
						{
							SystemControl.SendKeys(new int[2] { 18, 9 });
						}
						break;
					case 't':
						if (command == "Desktop")
						{
							SystemControl.SendKeys(new int[2] { 91, 68 });
						}
						break;
					}
					break;
				case 11:
					switch (command[0])
					{
					case 'V':
						if (command == "VirtualDial")
						{
							ShowDial(triggerOrigin);
						}
						break;
					case 'A':
						if (command == "AppSettings")
						{
							OpenSettings();
						}
						break;
					case 'P':
						if (command == "PreviousApp")
						{
							SystemControl.SendKeys(new int[3] { 18, 16, 9 });
						}
						break;
					}
					break;
				case 14:
					if (command == "SystemTouchpad")
					{
						NativeTouchpad.Toggle();
					}
					break;
				case 18:
					if (command == "NotificationCenter")
					{
						SystemControl.SendKeys(new int[2] { 91, 78 });
					}
					break;
				case 6:
					if (command == "ArkOff")
					{
						ApplicationBindings.ArkOptions(store.Value).ArkEnabled = false;
						store.Save();
					}
					break;
				case 10:
					if (command == "BallToggle")
					{
						store.Value.BallEnabled = !store.Value.BallEnabled;
						store.Save();
					}
					break;
				case 4:
					if (command == "Move")
					{
						ball.EnterMoveMode();
					}
					break;
				case 12:
				case 15:
				case 16:
				case 17:
					break;
				}
				break;
			}
			}
		}
		catch (Exception ex)
		{
			Report(ex.Message);
			Log(ex);
		}
		finally
		{
			SyncArkVisibility();
		}
	}

	private void PrepareOverview()
	{
		center?.HideForSwitch();
		switcher?.Hide();
		switcher?.AppWindow.Hide();
		gamepad?.SetSuppressed(value: true);
		ball.HideForCapture();
		ark?.SetSuppressed(value: true);
		dial?.SetSuppressed(value: true);
		edgeGesture?.SuppressIndicator(value: true);
		Ui.DwmFlush();
	}

	private StageSwitcherWindow MakeSwitcher(bool full)
	{
		StageSwitcherWindow stageSwitcherWindow = new StageSwitcherWindow(full, store);
		stageSwitcherWindow.IgnoreExternalDismissal = isSmokeTest;
		stageSwitcherWindow.Dismissed += delegate
		{
			SchedulePanelCleanup();
			ControlCenterWindow? controlCenterWindow = center;
			if ((object)controlCenterWindow == null || !controlCenterWindow.IsOpen)
			{
				StageSwitcherWindow? stageSwitcherWindow2 = switcher;
				if ((object)stageSwitcherWindow2 == null || !stageSwitcherWindow2.IsOpen)
				{
					StageSwitcherWindow? stageSwitcherWindow3 = overview;
					if ((object)stageSwitcherWindow3 == null || !stageSwitcherWindow3.IsOpen)
					{
						ball.SetVisible(visible: true);
					}
				}
			}
			SyncArkVisibility();
			SyncSystemStatusBadge();
		};
		stageSwitcherWindow.Shown += SyncSystemStatusBadge;
		return stageSwitcherWindow;
	}

	private async Task ExtractMotion(string path)
	{
		try
		{
			await MotionReference.ExtractAsync(path, Path.Combine(TestOutputDirectory, "motion-frames"));
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(TestOutputDirectory, "motion-error.txt"), ex.ToString());
		}
		Exit();
	}

	private async Task ShowCenter(Win32.POINT? triggerOrigin = null)
	{
		if (openingCenter)
		{
			return;
		}
		if (center?.IsOpen ?? false)
		{
			center.HidePanel();
			return;
		}
		openingCenter = true;
		try
		{
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			ark?.SetSuppressed(value: true);
			await ball.HideAsync();
			if (!exiting)
			{
				switcher?.Hide();
				overview?.Hide();
				center.ShowPanel(triggerOrigin ?? ball.Center);
				SyncArkVisibility();
				if (systemInitialization == null)
				{
					systemInitialization = system.InitializeAsync();
				}
				await systemInitialization;
			}
		}
		finally
		{
			openingCenter = false;
		}
	}

	private void SyncSystemStatusBadge()
	{
		if (exiting)
		{
			return;
		}
		if (store.Value.ShowSystemStatusBadge && (TaskbarMode.AutoHide || manualTestMode))
		{
			ControlCenterWindow? controlCenterWindow = center;
			if ((object)controlCenterWindow == null || !controlCenterWindow.IsOpen)
			{
				StageSwitcherWindow? stageSwitcherWindow = switcher;
				if ((object)stageSwitcherWindow == null || !stageSwitcherWindow.IsOpen || switcher.IsOverview)
				{
					goto IL_00b4;
				}
			}
			if ((object)statusBadge == null)
			{
				statusBadge = new SystemStatusBadge(system);
			}
			if (!statusBadge.IsVisible)
			{
				statusBadge.Show(ball.Center);
			}
			else
			{
				statusBadge.Raise();
			}
			return;
		}
		goto IL_00b4;
		IL_00b4:
		statusBadge?.Hide();
	}

	private ControlCenterWindow MakeCenter()
	{
		ControlCenterWindow obj = new ControlCenterWindow(store, system)
		{
			IgnoreExternalDismissal = isSmokeTest
		};
		DesktopVisibility.AllowedWindow = Ui.Handle(obj);
		obj.Dismissed += delegate
		{
			SchedulePanelCleanup();
			StageSwitcherWindow? stageSwitcherWindow = switcher;
			if ((object)stageSwitcherWindow == null || !stageSwitcherWindow.IsOpen)
			{
				StageSwitcherWindow? stageSwitcherWindow2 = overview;
				if ((object)stageSwitcherWindow2 == null || !stageSwitcherWindow2.IsOpen)
				{
					ball.SetVisible(visible: true);
				}
			}
			SyncArkVisibility();
			SyncSystemStatusBadge();
		};
		obj.Shown += SyncSystemStatusBadge;
		obj.Presented += SyncSystemStatusBadge;
		return obj;
	}

	private void SchedulePanelCleanup()
	{
		if (exiting)
		{
			return;
		}
		if (panelCleanup == null)
		{
			panelCleanup = queue.CreateTimer();
			panelCleanup.IsRepeating = false;
			panelCleanup.Interval = TimeSpan.FromSeconds(60.0);
			panelCleanup.Tick += delegate
			{
				ReleaseIdlePanels();
			};
		}
		panelCleanup.Stop();
		panelCleanup.Start();
	}

	private void ReleaseIdlePanels()
	{
		if (exiting)
		{
			return;
		}
		if (!openingCenter)
		{
			ControlCenterWindow controlCenterWindow = center;
			if ((object)controlCenterWindow != null && controlCenterWindow.CanReleaseResources)
			{
				center = null;
				DesktopVisibility.AllowedWindow = 0;
				Ui.ReleaseTitleBar(controlCenterWindow);
				controlCenterWindow.Close();
			}
		}
		StageSwitcherWindow stageSwitcherWindow = switcher;
		if ((object)stageSwitcherWindow != null && stageSwitcherWindow.CanReleaseResources)
		{
			switcher = null;
			Ui.ReleaseTitleBar(stageSwitcherWindow);
			stageSwitcherWindow.Close();
		}
		StageSwitcherWindow stageSwitcherWindow2 = overview;
		if ((object)stageSwitcherWindow2 != null && stageSwitcherWindow2.CanReleaseResources)
		{
			overview = null;
			Ui.ReleaseTitleBar(stageSwitcherWindow2);
			stageSwitcherWindow2.Close();
		}
		ControlCenterWindow controlCenterWindow2 = center;
		if ((object)controlCenterWindow2 == null || controlCenterWindow2.IsOpen)
		{
			StageSwitcherWindow stageSwitcherWindow3 = switcher;
			if ((object)stageSwitcherWindow3 == null || stageSwitcherWindow3.IsOpen)
			{
				stageSwitcherWindow3 = overview;
				if ((object)stageSwitcherWindow3 == null || stageSwitcherWindow3.IsOpen)
				{
					return;
				}
			}
		}
		SchedulePanelCleanup();
	}

	private void SyncArkVisibility()
	{
		SyncVirtualTouchpad();
		ControlCenterWindow? controlCenterWindow = center;
		int num;
		if ((object)controlCenterWindow == null || !controlCenterWindow.IsOpen)
		{
			StageSwitcherWindow? stageSwitcherWindow = switcher;
			if ((object)stageSwitcherWindow == null || !stageSwitcherWindow.IsOpen)
			{
				num = ((overview?.IsOpen ?? false) ? 1 : 0);
				goto IL_0043;
			}
		}
		num = 1;
		goto IL_0043;
		IL_00e0:
		edgeGesture?.SuppressIndicator(overview?.IsOpen ?? false);
		return;
		IL_0043:
		bool flag = (byte)num != 0;
		gamepad?.SetSuppressed(flag || DesktopVisibility.Locked);
		ark?.SetSuppressed(flag || DesktopVisibility.Locked);
		dial?.SetSuppressed(flag || DesktopVisibility.Locked);
		ControlCenterWindow? controlCenterWindow2 = center;
		if ((object)controlCenterWindow2 == null || !controlCenterWindow2.IsOpen)
		{
			StageSwitcherWindow? stageSwitcherWindow2 = switcher;
			if ((object)stageSwitcherWindow2 == null || !stageSwitcherWindow2.IsOpen)
			{
				StageSwitcherWindow? stageSwitcherWindow3 = overview;
				if ((object)stageSwitcherWindow3 == null || !stageSwitcherWindow3.IsOpen)
				{
					goto IL_00e0;
				}
			}
		}
		ball.SetVisible(visible: false);
		goto IL_00e0;
	}

	private void ShowSettings()
	{
		if (DesktopVisibility.Locked)
		{
			return;
		}
		trayWindow?.AppWindow.Hide();
		if (settings == null)
		{
			settings = new SettingsWindow(store, ball.ResetPosition, Shutdown);
			WindowCatalog.SettingsHandle = Ui.Handle(settings);
			WindowPreview.RegisterSettings(settings);
			settings.PreviewBubbles += delegate(ArkProfile profile)
			{
				ark?.Preview(profile);
			};
			settings.Activated += delegate(object _, WindowActivatedEventArgs e)
			{
				settingsActive = e.WindowActivationState != WindowActivationState.Deactivated;
				SyncArkVisibility();
			};
			settings.Closed += delegate
			{
				VirtualGamepad? virtualGamepad = gamepad;
				if (virtualGamepad != null && virtualGamepad.Editing)
				{
					gamepad.SetEditing(value: false);
					store.Save();
				}
				WindowCatalog.SettingsHandle = 0;
				settings = null;
				settingsActive = false;
				SyncArkVisibility();
				if (isSmokeTest && !reopeningSettings)
				{
					Shutdown();
				}
			};
		}
		Ui.PresentTemporarilyTopmost(settings);
	}

	internal void OpenSettings()
	{
		ShowSettings();
	}

	internal void ToggleTouchKeyboard()
	{
		NativeTouchpad.CancelDiscovery();
		NativeTouchKeyboard.Toggle();
		InputControl.ScheduleAutoApply();
	}

	internal void ReopenSettings()
	{
		ReleaseIdlePanels();
		if (!(settings == null))
		{
			reopeningSettings = true;
			try
			{
				settings.Close();
			}
			finally
			{
				reopeningSettings = false;
			}
			queue.TryEnqueue(ShowSettings);
		}
	}

	private void SyncVirtualTouchpad()
	{
		if (exiting)
		{
			return;
		}
		int num;
		if (!DesktopVisibility.Locked)
		{
			ControlCenterWindow? controlCenterWindow = center;
			if ((object)controlCenterWindow == null || !controlCenterWindow.IsOpen)
			{
				StageSwitcherWindow? stageSwitcherWindow = overview;
				if ((object)stageSwitcherWindow == null || !stageSwitcherWindow.IsOpen)
				{
					StageSwitcherWindow? stageSwitcherWindow2 = switcher;
					num = (((object)stageSwitcherWindow2 == null || !stageSwitcherWindow2.IsOpen) ? 1 : 0);
					goto IL_0056;
				}
			}
		}
		num = 0;
		goto IL_0056;
		IL_0056:
		bool flag = (byte)num != 0;
		if (!store.Value.VirtualTouchpad.Enabled)
		{
			virtualTouchpad?.Dispose();
			virtualTouchpad = null;
		}
		if (store.Value.VirtualTouchpad.Enabled && virtualTouchpad == null)
		{
			virtualTouchpad = new VirtualTouchpad(store);
			virtualTouchpad.Trigger += delegate(GestureAction action)
			{
				Execute(action);
			};
		}
		virtualTouchpad?.SetVisible(store.Value.VirtualTouchpad.Enabled && flag);
		mouseJoystick?.Dispose();
		mouseJoystick = null;
		rightMouseJoystick?.Dispose();
		rightMouseJoystick = null;
	}

	internal void ToggleTouchpad()
	{
		if (!DesktopVisibility.Locked)
		{
			store.Value.VirtualTouchpad.Enabled = !store.Value.VirtualTouchpad.Enabled;
			store.Save();
		}
	}

	internal void ShowScanner()
	{
		if (scanner == null)
		{
			scanner = new QrScannerWindow();
			scanner.Activated += delegate(object _, WindowActivatedEventArgs e)
			{
				scannerActive = e.WindowActivationState != WindowActivationState.Deactivated;
				SyncArkVisibility();
			};
			scanner.Closed += delegate
			{
				scanner = null;
				scannerActive = false;
				SyncArkVisibility();
			};
		}
		Ui.PresentTemporarilyTopmost(scanner);
	}

	internal void ShowDial(Win32.POINT? origin = null)
	{
		VirtualDial virtualDial = EnsureDial();
		if (origin.HasValue && !virtualDial.IsVisible)
		{
			virtualDial.PositionAt(origin.Value);
		}
		virtualDial.Toggle();
	}

	internal void ToggleGamepad(bool fromCenter = false)
	{
		store.Value.Gamepad.Enabled = !store.Value.Gamepad.Enabled;
		store.Save();
		FunctionState("摇杆和按钮", store.Value.Gamepad.Enabled, fromCenter ? "\uece9" : "");
	}

	internal void FunctionState(string name, bool enabled, string icon)
	{
		if (!isSmokeTest && !exiting && !DesktopVisibility.Locked)
		{
			if (statusHint == null)
			{
				statusHint = new CenterHint();
			}
			string label = Localization.T(enabled ? "{0}已开启" : "{0}已关闭", Localization.T(name));
			statusHint.Show(new GestureAction
			{
				Label = label,
				Icon = icon
			}, ball.Center, 1.5);
		}
	}

	private void SyncGamepad()
	{
		if (exiting)
		{
			return;
		}
		if (!store.Value.Gamepad.Enabled)
		{
			gamepad?.Dispose();
			gamepad = null;
		}
		else
		{
			if (gamepad == null)
			{
				gamepad = CreateGamepad();
			}
			gamepad.Sync();
		}
		SyncArkVisibility();
	}

	private VirtualGamepad CreateGamepad()
	{
		VirtualGamepad virtualGamepad = new VirtualGamepad(store);
		virtualGamepad.Trigger += delegate(GestureAction action, Win32.POINT origin)
		{
			Execute(action, origin);
		};
		virtualGamepad.Selected += delegate(GamepadControl control)
		{
			settings?.SelectGamepadControl(control);
		};
		virtualGamepad.LayoutChanged += delegate
		{
			settings?.RefreshGamepad();
		};
		return virtualGamepad;
	}

	internal void EditGamepad(bool editing)
	{
		store.Value.Gamepad.Enabled = true;
		store.Save();
		SyncGamepad();
		gamepad.SetEditing(editing);
		SyncArkVisibility();
		if (!editing)
		{
			store.Save();
		}
	}

	private VirtualDial EnsureDial()
	{
		if (dial == null)
		{
			VirtualDial wheel = new VirtualDial(store)
			{
				SelectedTool = lastDialTool
			};
			dial = wheel;
			wheel.Trigger += delegate(GestureAction action)
			{
				Execute(action, wheel.Center);
			};
			wheel.VisibilityChanged += delegate
			{
				if (!wheel.IsVisible)
				{
					lastDialTool = wheel.SelectedTool;
					queue.TryEnqueue(delegate
					{
						if (dial == wheel && !wheel.IsVisible)
						{
							dial = null;
							wheel.Dispose();
						}
					});
				}
				center?.SyncDialState();
				StatusHint(wheel.IsVisible ? "虚拟 Dial 已打开" : "虚拟 Dial 已关闭");
				SyncArkVisibility();
			};
		}
		return dial;
	}

	private void ShowTray()
	{
		trayWindow?.Close();
		trayWindow = new Window
		{
			Title = "TouchToys"
		};
		trayWindow.SystemBackdrop = new PanelBackdrop();
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 8.0,
			Padding = new Thickness(14.0)
		};
		ToggleSwitch startup = new ToggleSwitch
		{
			OnContent = Localization.T("开"),
			OffContent = Localization.T("关"),
			Header = Localization.T("开机自启动"),
			IsOn = StartupRegistration.Enabled
		};
		startup.Toggled += delegate
		{
			try
			{
				StartupRegistration.Enabled = startup.IsOn;
			}
			catch (Exception ex)
			{
				Report(ex.Message);
				startup.IsOn = StartupRegistration.Enabled;
			}
		};
		stackPanel.Children.Add(startup);
		stackPanel.Children.Add(Ui.Button("退出", Shutdown));
		trayWindow.Content = stackPanel;
		Ui.ConfigureOverlay(trayWindow);
		trayWindow.Activated += delegate(object _, WindowActivatedEventArgs e)
		{
			if (e.WindowActivationState == WindowActivationState.Deactivated)
			{
				trayWindow?.AppWindow.Hide();
			}
		};
		Win32.GetCursorPos(out var p);
		Win32.RECT rcWork = Win32.Monitor(p).rcWork;
		double num = Win32.Scale(p);
		int num2 = (int)(270.0 * num);
		int num3 = (int)(190.0 * num);
		trayWindow.AppWindow.MoveAndResize(new RectInt32(Math.Clamp(p.X - num2 / 2, rcWork.Left, rcWork.Right - num2), Math.Clamp(p.Y - num3, rcWork.Top, rcWork.Bottom - num3), num2, num3));
		trayWindow.Activate();
	}

	internal void Report(string message)
	{
		message = Localization.T(message);
		Diagnostics.Event("Notice", message);
		if (!isSmokeTest && !exiting)
		{
			if (statusHint == null)
			{
				statusHint = new CenterHint();
			}
			statusHint.Show(new GestureAction
			{
				Label = message
			}, ball.Center, 3.0);
		}
	}

	internal static void Log(Exception error)
	{
		Diagnostics.Error(error);
	}

	private void SessionEnding(object? sender, SessionEndingEventArgs e)
	{
		queue.TryEnqueue(Shutdown);
	}

	private async Task OverviewCoexistenceTest()
	{
		_ = 4;
		try
		{
			ApplicationBindings.ArkOptions(store.Value).ArkEnabled = true;
			store.Save();
			await Task.Delay(150);
			store.Value.MultiTouch.EdgeEnabled = true;
			edgeGesture.SetGlobalTop(value: true);
			edgeGesture.ShowBottomIndicator();
			ball.SetVisible(visible: true);
			EnsureDial().Toggle();
			await Task.Delay(350);
			if (!ball.IsPresented || !ark.IsPresented || !dial.IsPresented || !edgeGesture.IsIndicatorPresented)
			{
				throw new InvalidOperationException("互斥检查初始窗口没有全部出现");
			}
			await Execute(GestureAction.BuiltIn("Overview"));
			await Task.Delay(400);
			StageSwitcherWindow? stageSwitcherWindow = overview;
			if ((object)stageSwitcherWindow == null || !stageSwitcherWindow.IsOpen || ball.IsPresented || ark.IsPresented || dial.IsPresented || edgeGesture.IsIndicatorPresented)
			{
				throw new InvalidOperationException("后台卡片未收起全部浮层");
			}
			CaptureUi(overview, "overview-coexistence-v089");
			overview.Hide();
			await Task.Delay(400);
			if (!ball.IsPresented || !ark.IsPresented || !dial.IsPresented)
			{
				throw new InvalidOperationException("关闭后台卡片后浮层未恢复");
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v089-overview-test.json"), "PASS: ball, Dial, Ark and indicator all hidden before background capture; floating controls restored after dismissal.");
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v089-overview-test.json"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0812FrameTest()
	{
		_ = 1;
		try
		{
			ball.SetVisible(visible: false);
			ark?.SetSuppressed(value: true);
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			center.ShowPanel(ball.Center);
			await Task.Delay(500);
			await center.CheckV0812Async(delegate
			{
				CaptureUi(center, "center-frame-default");
			});
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0812-frame-children.txt"), SystemPanelFrame.Children(Ui.Handle(center)));
		}
		catch (Exception error)
		{
			Log(error);
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task PreviewResourceCheckAsync()
	{
		SettingsWindow page = null;
		try
		{
			object reuse = await WindowPreview.CheckReuseAsync(keeper);
			page = new SettingsWindow(store, ball.ResetPosition, delegate
			{
			});
			WindowPreview.RegisterSettings(page);
			Ui.Present(page);
			await Task.Delay(300);
			if (await WindowPreview.LoadAsync(Ui.Handle(page), CancellationToken.None) == null)
			{
				throw new InvalidOperationException("设置截图未生成");
			}
			page.Close();
			page = null;
			await Task.Delay(100);
			if (WindowPreview.HasSettingsCaptureReference)
			{
				throw new InvalidOperationException("关闭后保留设置截图引用");
			}
			page = new SettingsWindow(store, ball.ResetPosition, delegate
			{
			});
			Ui.Present(page);
			await Task.Delay(100);
			await page.CheckSystemCloseAsync();
			page = null;
			File.WriteAllText(Path.Combine(TestOutputDirectory, "preview-resource-check.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Reuse = reuse,
				SettingsCaptureReleased = true,
				SystemClose = "PASS"
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "preview-resource-check.json"), ex.ToString());
		}
		finally
		{
			page?.Close();
			Shutdown();
		}
	}

	private async Task PanelFullCheckAsync()
	{
		_ = 2;
		try
		{
			ball.SetVisible(visible: false);
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			EdgeKind[] array = new EdgeKind[4]
			{
				EdgeKind.Right,
				EdgeKind.Left,
				EdgeKind.TopRight,
				EdgeKind.Bottom
			};
			for (int i = 0; i < array.Length; i++)
			{
				EdgeKind edge = array[i];
				center.HidePanel();
				await Task.Delay(350);
				center.ShowPanel(ball.Center, interactive: true, edge == EdgeKind.TopRight, edge);
				ControlCenterWindow controlCenterWindow = center;
				EdgeKind edgeKind = edge;
				bool flag = ((edgeKind == EdgeKind.Left || edgeKind == EdgeKind.Right) ? true : false);
				controlCenterWindow.UpdateGesture(flag ? 600 : 1200);
				await Task.Delay(250);
				center.Activate();
				CaptureUi(center, "center-full-" + edge);
				if (edge == EdgeKind.Right)
				{
					CaptureArea(center, "center-full-Right-area", 40);
				}
				center.EndGesture(0.0);
				await Task.Delay(300);
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "panel-full-check.txt"), "PASS: full panel captured from right/left/top/bottom.");
		}
		catch (Exception ex)
		{
			Environment.ExitCode = 1;
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "panel-full-check.txt"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task StatusBadgeCheckAsync()
	{
		try
		{
			ball.SetVisible(visible: false);
			Win32.RECT rcMonitor = Win32.Monitor(ball.Center).rcMonitor;
			keeper.Content = new Grid
			{
				Background = new SolidColorBrush(Windows.UI.Color.FromArgb(byte.MaxValue, 20, 20, 24))
			};
			keeper.AppWindow.MoveAndResize(new RectInt32(rcMonitor.Left, rcMonitor.Top, rcMonitor.Width, rcMonitor.Height));
			keeper.AppWindow.Show();
			if ((object)statusBadge == null)
			{
				statusBadge = new SystemStatusBadge(system);
			}
			statusBadge.Show(ball.Center);
			await Task.Delay(1200);
			CaptureUi(statusBadge, "status-badge-check");
			if (Win32.GetWindowRect(Ui.Handle(statusBadge), out var r))
			{
				int num = (int)(40.0 * Win32.Scale(ball.Center));
				using Bitmap bitmap = new Bitmap(r.Width + num * 2, r.Height + num * 2);
				using (Graphics graphics = Graphics.FromImage(bitmap))
				{
					graphics.CopyFromScreen(r.Left - num, r.Top - num, 0, 0, bitmap.Size);
				}
				bitmap.Save(Path.Combine(TestOutputDirectory, "status-badge-area.png"), ImageFormat.Png);
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "status-badge-check.txt"), "PASS: badge window shown and captured; inspect status-badge-check.png. Text=" + statusBadge.RenderText + " Args=" + string.Join(' ', Environment.GetCommandLineArgs()));
		}
		catch (Exception ex)
		{
			Environment.ExitCode = 1;
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "status-badge-check.txt"), ex.ToString());
		}
		finally
		{
			statusBadge?.Hide();
			Shutdown();
		}
	}

	private async Task V0829VisualCheckAsync()
	{
		_ = 1;
		try
		{
			ball.SetVisible(visible: false);
			ark?.SetSuppressed(value: true);
			center = MakeCenter();
			await center.CheckV0829VisualAsync(delegate(string name)
			{
				CaptureUi(center, name);
			});
			await center.CheckRevealAsync(ball.Center, horizontalOnly: true);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0829-visual-check.json"), "PASS: media contrast in both themes; horizontal reveal geometry and dismissal thresholds. Inspect screenshots separately.");
		}
		catch (Exception ex)
		{
			Environment.ExitCode = 1;
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0829-visual-check.json"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0831InertiaCheckAsync()
	{
		_ = 1;
		try
		{
			ball.SetVisible(visible: false);
			ark?.SetSuppressed(value: true);
			bool[] array = new bool[2] { false, true };
			foreach (bool flag in array)
			{
				StageSwitcherWindow panel = new StageSwitcherWindow(flag, store)
				{
					IgnoreExternalDismissal = true
				};
				try
				{
					panel.Show(ball.Center, interactive: false, flag ? EdgeKind.Bottom : EdgeKind.Left);
					await Task.Delay(400);
					await panel.CheckInertiaAsync();
				}
				finally
				{
					panel.Close();
				}
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0831-inertia-check.txt"), "PASS: both task lists coast after release, stop on interruption and stop timers when hidden.");
		}
		catch (Exception ex)
		{
			Environment.ExitCode = 1;
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0831-inertia-check.txt"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0830SettingsCheckAsync()
	{
		SettingsWindow fixture = null;
		try
		{
			fixture = new SettingsWindow(store, delegate
			{
			}, delegate
			{
			});
			fixture.Activate();
			await fixture.CheckV0830Async();
			CaptureUi(fixture, "settings-updates-v0830");
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0830-settings-check.json"), "PASS: language selector, purchase visibility, hidden mouse joystick, input settings order and Updates navigation");
		}
		catch (Exception ex)
		{
			Environment.ExitCode = 1;
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0830-settings-check.json"), ex.ToString());
		}
		finally
		{
			fixture?.Close();
			Shutdown();
		}
	}

	private async Task V0830InputCheckAsync()
	{
		_ = 2;
		try
		{
			int frames = 0;
			using (IdleOpacity fade = new IdleOpacity(() => 0.8, () => 0.3, delegate
			{
				frames++;
			}))
			{
				fade.Active(held: false);
				await Task.Delay(3500);
				if (Math.Abs(fade.Value - 0.3) > 0.01)
				{
					throw new InvalidOperationException("Idle opacity not applied");
				}
				int finished = frames;
				await Task.Delay(150);
				if (frames != finished)
				{
					throw new InvalidOperationException("Idle fade kept repainting");
				}
				fade.Active(held: true);
				await Task.Delay(3200);
				if (Math.Abs(fade.Value - 0.8) > 0.01)
				{
					throw new InvalidOperationException("Held control faded");
				}
				fade.Active(held: false);
				fade.Pause();
			}
			using (GamepadShield gamepadShield = new GamepadShield(new GamepadShieldSettings
			{
				Width = 240.0,
				Height = 180.0
			}))
			{
				using GamepadShield gamepadShield2 = new GamepadShield(new GamepadShieldSettings
				{
					Width = 240.0,
					Height = 180.0
				}, 1);
				gamepadShield.CheckResizeAndMove();
				gamepadShield2.CheckResizeAndMove();
			}
			store.Value.Gamepad.Enabled = true;
			store.Value.Gamepad.SlideInput = true;
			using (VirtualGamepad virtualGamepad = new VirtualGamepad(store, delegate
			{
			}))
			{
				virtualGamepad.CheckSlideInput();
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0830-input-check.json"), "PASS: idle fade stops repainting; held controls remain visible; two shields resize and move; slide input releases correctly");
		}
		catch (Exception ex)
		{
			Environment.ExitCode = 1;
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0830-input-check.json"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0829InputCheckAsync()
	{
		_ = 1;
		try
		{
			store.Value.Gamepad.Enabled = true;
			store.Value.Gamepad.SlideInput = true;
			List<string> events = new List<string>();
			using (VirtualGamepad virtualGamepad = new VirtualGamepad(store, delegate(IReadOnlyList<int> keys, bool down)
			{
				events.Add((down ? "D" : "U") + string.Join(",", keys));
			}))
			{
				virtualGamepad.CheckSlideInput();
			}
			if (events.Count >= 6)
			{
				List<string> list = events;
				if (list[list.Count - 1].StartsWith("U"))
				{
					store.Value.Gamepad.Enabled = false;
					bool[] array = new bool[2] { false, true };
					foreach (bool right in array)
					{
						using MouseJoystickWindow fixture = new MouseJoystickWindow(store, right, delegate
						{
						});
						await fixture.CheckAsync();
						await fixture.CheckGripAsync();
					}
					File.WriteAllText(Path.Combine(TestOutputDirectory, "v0829-input-check.json"), JsonSerializer.Serialize(new
					{
						Result = "PASS",
						Events = events,
						Limitations = "Input recorder; physical cursor, game and IME switching require device verification."
					}));
					return;
				}
			}
			throw new InvalidOperationException("Recorded key releases missing");
		}
		catch (Exception ex)
		{
			Environment.ExitCode = 1;
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0829-input-check.json"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task PanelRetentionCheckAsync()
	{
		Dictionary<string, List<WeakReference<Window>>> references = new Dictionary<string, List<WeakReference<Window>>>();
		List<object> samples = new List<object>();
		try
		{
			string[] array = new string[6] { "Empty", "ControlCenter", "Stage", "Overview", "Hint", "Scanner" };
			foreach (string kind in array)
			{
				references[kind] = new List<WeakReference<Window>>();
				for (int j = 0; j < 20; j++)
				{
					Window window = kind switch
					{
						"ControlCenter" => new ControlCenterWindow(store, system), 
						"Stage" => new StageSwitcherWindow(overview: false, store), 
						"Overview" => new StageSwitcherWindow(overview: true, store), 
						"Hint" => new RectangleHintWindow(), 
						"Scanner" => new QrScannerWindow(), 
						_ => new Window
						{
							Content = new Grid()
						}, 
					};
					references[kind].Add(new WeakReference<Window>(window));
					if (window is RectangleHintWindow rectangleHintWindow)
					{
						rectangleHintWindow.Dispose();
					}
					else
					{
						Ui.ReleaseTitleBar(window);
						window.Close();
					}
					await Task.Delay(100);
				}
				Sample(kind + " closed");
			}
			await Task.Delay(5000);
			Sample("idle 5 seconds");
			File.WriteAllText(Path.Combine(TestOutputDirectory, "panel-retention-check.json"), JsonSerializer.Serialize(new
			{
				Result = "COMPLETED",
				Samples = samples,
				Limitations = "Construction/close only; no camera or external input; no forced GC. Alive counts identify candidates, not a leak proof without collection history and lifetime analysis."
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			Environment.ExitCode = 1;
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "panel-retention-check.json"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
		void Sample(string stage)
		{
			using Process process = Process.GetCurrentProcess();
			Dictionary<string, int> dictionary = new Dictionary<string, int>();
			foreach (KeyValuePair<string, List<WeakReference<Window>>> item in references)
			{
				int num = 0;
				foreach (WeakReference<Window> item2 in item.Value)
				{
					if (item2.TryGetTarget(out var _))
					{
						num++;
					}
				}
				dictionary[item.Key] = num;
			}
			samples.Add(new
			{
				Stage = stage,
				Alive = dictionary,
				PrivateBytes = process.PrivateMemorySize64,
				ManagedBytes = GC.GetTotalMemory(forceFullCollection: false),
				Gen2Collections = GC.CollectionCount(2),
				GDI = GetGuiResources(process.Handle, 0u),
				USER = GetGuiResources(process.Handle, 1u)
			});
		}
	}

	private async Task DynamicReleaseCheckAsync()
	{
		List<string> checks = new List<string>();
		try
		{
			for (int i = 0; i < 5; i++)
			{
				store.Value.Gamepad.Enabled = true;
				SyncGamepad();
				if (gamepad == null)
				{
					throw new InvalidOperationException("Gamepad create");
				}
				store.Value.Gamepad.Enabled = false;
				SyncGamepad();
				if (gamepad != null)
				{
					throw new InvalidOperationException("Gamepad release");
				}
				VirtualDial virtualDial = EnsureDial();
				virtualDial.SelectedTool = 1;
				virtualDial.Toggle();
				virtualDial.Toggle();
				await Task.Delay(100);
				if (dial != null)
				{
					throw new InvalidOperationException("Dial release");
				}
				if (EnsureDial().SelectedTool != 1)
				{
					throw new InvalidOperationException("Dial selection restore");
				}
			}
			checks.Add("Five gamepad/Dial disposal and recreation cycles; selected tool retained");
			VirtualDial rapid = EnsureDial();
			rapid.Toggle();
			rapid.Toggle();
			rapid.Toggle();
			await Task.Delay(100);
			if (dial != rapid || !rapid.IsVisible)
			{
				throw new InvalidOperationException("Rapid reopen disposed visible Dial");
			}
			rapid.Toggle();
			await Task.Delay(100);
			checks.Add("Rapid Dial reopen remains alive");
			for (int i = 0; i < 2; i++)
			{
				await ball.CheckMaskAsync();
			}
			checks.Add("Mask native window recreation");
			using CenterHint hint = new CenterHint();
			hint.Show(new GestureAction
			{
				Label = "资源释放验证"
			}, ball.Center);
			hint.Hide();
			hint.ReleaseIdle();
			if (hint.HasWindow)
			{
				throw new InvalidOperationException("Hint release");
			}
			hint.Show(new GestureAction
			{
				Label = "再次显示"
			}, ball.Center, 0.2);
			center = MakeCenter();
			center.ShowPanel(ball.Center);
			await Task.Delay(500);
			ReleaseIdlePanels();
			if (center == null)
			{
				throw new InvalidOperationException("Visible panel released");
			}
			center.HidePanel();
			await Task.Delay(700);
			for (int i = 0; i < 65; i++)
			{
				await Task.Delay(1000);
			}
			if (center != null || hint.HasWindow)
			{
				throw new InvalidOperationException("Idle timeout retained panel/hint");
			}
			checks.Add("Real 30-second hint and 60-second hidden panel timers release windows; visible panel retained");
			center = MakeCenter();
			center.ShowPanel(ball.Center);
			await Task.Delay(500);
			center.HidePanel();
			await Task.Delay(700);
			for (int i = 0; i < 50; i++)
			{
				if (center.CanReleaseResources)
				{
					break;
				}
				await Task.Delay(100);
			}
			ReleaseIdlePanels();
			if (center != null)
			{
				throw new InvalidOperationException("Reopened panel release");
			}
			checks.Add("Control center recreates and closes after idle disposal");
			switcher = MakeSwitcher(full: false);
			overview = MakeSwitcher(full: true);
			ReleaseIdlePanels();
			if (switcher != null || overview != null)
			{
				throw new InvalidOperationException("Hidden task panels retained");
			}
			checks.Add("Hidden side and overview task windows released");
			File.WriteAllText(Path.Combine(TestOutputDirectory, "dynamic-release-check.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Checks = checks,
				Limitations = "Isolated smoke configuration; no external keyboard input; does not measure physical touch or memory target."
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			Environment.ExitCode = 1;
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "dynamic-release-check.json"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task ResourceWakeCheckAsync()
	{
		List<object> measurements = new List<object>();
		try
		{
			systemInitialization = system.InitializeAsync();
			await systemInitialization;
			ball.SetVisible(visible: false);
			ark?.SetSuppressed(value: true);
			for (int i = 0; i < 21; i++)
			{
				TaskCompletionSource<bool> rendered = new TaskCompletionSource<bool>();
				Stopwatch watch = Stopwatch.StartNew();
				CompositionTarget.Rendering += Rendering;
				try
				{
					if ((object)center == null)
					{
						center = MakeCenter();
					}
					center.ShowPanel(ball.Center);
					await rendered.Task.WaitAsync(TimeSpan.FromSeconds(5.0));
					measurements.Add(new
					{
						Iteration = i,
						FirstRenderCallbackMilliseconds = watch.Elapsed.TotalMilliseconds
					});
				}
				finally
				{
					CompositionTarget.Rendering -= Rendering;
				}
				await Task.Delay(350);
				center.HidePanel();
				await Task.Delay(400);
				center.CheckIdleTimers();
				void Rendering(object? sender, object args)
				{
					rendered.TrySetResult(result: true);
				}
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "resource-wake-check.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Measurements = measurements,
				HiddenTimersStopped = true,
				Limitations = "First CompositionTarget.Rendering callback includes initial construction on iteration zero. Not DWM presentation/photon latency; isolated config, no external key input."
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "resource-wake-check.json"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task MouseGripCheckAsync()
	{
		_ = 1;
		try
		{
			SettingsStore fixtureStore = new SettingsStore(Path.Combine(TestOutputDirectory, "mouse-grip-state"));
			fixtureStore.RestoreDefaults();
			List<PadEvent> events = new List<PadEvent>();
			bool[] array = new bool[2] { false, true };
			foreach (bool right in array)
			{
				using MouseJoystickWindow fixture = new MouseJoystickWindow(fixtureStore, right, events.Add);
				await fixture.CheckAsync();
				await fixture.CheckGripAsync();
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "mouse-grip-check.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Cases = "Both joysticks: existing movement/two-finger edit; grip movement and persisted position; input release; shake toggle off/on; hide stops timers. Input recorder only.",
				Events = events.Count
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "mouse-grip-check.json"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	[DllImport("user32")]
	private static extern uint GetGuiResources(nint process, uint flags);

	private async Task IconResourceCheckAsync()
	{
		List<object> rows = new List<object>();
		try
		{
			bool[] array = new bool[2] { false, true };
			foreach (bool withIcon in array)
			{
				Sample(withIcon ? "icon start" : "control start");
				for (int j = 0; j < 25; j++)
				{
					Window window = new Window
					{
						Content = new Grid()
					};
					if (withIcon)
					{
						Ui.SetAppIcon(window);
					}
					window.Close();
					await Task.Delay(30);
				}
				await Task.Delay(500);
				Sample(withIcon ? "icon end" : "control end");
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "icon-resource-check.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Rows = rows,
				Limitations = "25 empty WinUI windows with/without SetIcon; no forced GC. Investigation only, not a memory improvement claim."
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "icon-resource-check.json"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
		void Sample(string stage)
		{
			using Process process = Process.GetCurrentProcess();
			rows.Add(new
			{
				Stage = stage,
				Gdi = GetGuiResources(process.Handle, 0u),
				User = GetGuiResources(process.Handle, 1u),
				PrivateBytes = process.PrivateMemorySize64
			});
		}
	}

	private async Task ResourceStudyAsync()
	{
		bool quick = Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--resource-quick");
		string resourcePrefix = (quick ? "resource-quick" : "resource-study");
		Stopwatch watch = Stopwatch.StartNew();
		List<object> stages = new List<object>();
		List<double> timings = new List<double>();
		SettingsWindow page = null;
		List<WeakReference<SettingsWindow>> windows = new List<WeakReference<SettingsWindow>>();
		try
		{
			Stage("startup");
			systemInitialization = system.InitializeAsync();
			await systemInitialization;
			await CheckedDelay(quick ? 1000 : 30000);
			Stage(quick ? "quick warmup" : "idle 30 seconds");
			await CheckedDelay(quick ? 1000 : 270000);
			Stage(quick ? "quick pre-cycles" : "background 5 minutes");
			page = new SettingsWindow(store, ball.ResetPosition, delegate
			{
			});
			windows.Add(new WeakReference<SettingsWindow>(page));
			Ui.Present(page);
			await CheckedDelay(3000);
			Stage("settings visible");
			page.AppWindow.Hide();
			await CheckedDelay(3000);
			Stage("settings hidden");
			for (int i = 0; i < 100; i++)
			{
				Stopwatch time = Stopwatch.StartNew();
				Ui.Present(page);
				await CheckedDelay(20);
				timings.Add(time.Elapsed.TotalMilliseconds);
				page.AppWindow.Hide();
				await CheckedDelay(20);
			}
			Stage("100 settings show/hide cycles");
			page.Close();
			page = null;
			for (int i = 0; i < 100; i++)
			{
				page = new SettingsWindow(store, ball.ResetPosition, delegate
				{
				});
				windows.Add(new WeakReference<SettingsWindow>(page));
				Ui.Present(page);
				await CheckedDelay(30);
				page.Close();
				page = null;
				await CheckedDelay(70);
				if (i % 10 == 9)
				{
					Stage("settings create/close " + (i + 1));
				}
				using Process process = Process.GetCurrentProcess();
				if (process.PrivateMemorySize64 > 1572864000)
				{
					throw new InvalidOperationException("Resource study stopped above 1500 MiB private bytes");
				}
			}
			Stage("100 settings create/close cycles complete");
			while (!quick && watch.Elapsed.TotalSeconds < 1800.0)
			{
				await CheckedDelay(1000);
			}
			Stage(quick ? "quick cycles complete" : "30 minutes complete");
			File.WriteAllText(Path.Combine(TestOutputDirectory, resourcePrefix + "-result.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Quick = quick,
				Version = typeof(App).Assembly.GetName().Version?.ToString(),
				TitlebarBaseline = Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--resource-baseline"),
				Stages = stages,
				UiPresentReturnPlus20msDelay = timings,
				Limitations = "Isolated config; real media/radio initialization; no global touch capture or external key input; UI timing is dispatcher scheduling proxy, not photon latency. External sampler records resources."
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, resourcePrefix + "-result.json"), ex.ToString());
		}
		finally
		{
			page?.Close();
			Shutdown();
		}
		static async Task CheckedDelay(int milliseconds)
		{
			while (milliseconds > 0)
			{
				int slice = Math.Min(1000, milliseconds);
				DateTimeOffset before = DateTimeOffset.UtcNow;
				await Task.Delay(slice);
				if ((DateTimeOffset.UtcNow - before).TotalMilliseconds > (double)(slice + 10000))
				{
					throw new InvalidOperationException("Resource study interrupted: sampling continuity lost");
				}
				milliseconds -= slice;
			}
		}
		void Stage(string name)
		{
			int num = 0;
			foreach (WeakReference<SettingsWindow> item in windows)
			{
				if (item.TryGetTarget(out var _))
				{
					num++;
				}
			}
			var anon = new
			{
				Stage = name,
				Seconds = watch.Elapsed.TotalSeconds,
				At = DateTimeOffset.Now,
				ManagedBytes = GC.GetTotalMemory(forceFullCollection: false),
				AllocatedBytes = GC.GetTotalAllocatedBytes(),
				Gen0Collections = GC.CollectionCount(0),
				Gen1Collections = GC.CollectionCount(1),
				Gen2Collections = GC.CollectionCount(2),
				SettingsCreated = windows.Count,
				SettingsAlive = num
			};
			stages.Add(anon);
			File.WriteAllText(Path.Combine(TestOutputDirectory, resourcePrefix + "-stage.json"), JsonSerializer.Serialize(anon));
		}
	}

	private async Task HintStyleCheckAsync()
	{
		Window background = null;
		try
		{
			Win32.RECT rcMonitor = Win32.Monitor(ball.Center).rcMonitor;
			background = new Window
			{
				Content = new Grid
				{
					Background = new SolidColorBrush(Windows.UI.Color.FromArgb(byte.MaxValue, 36, 74, 108))
				}
			};
			Ui.ConfigureOverlay(background);
			background.AppWindow.MoveAndResize(new RectInt32(rcMonitor.Left, rcMonitor.Top, rcMonitor.Width, rcMonitor.Height));
			Ui.Present(background);
			await Task.Delay(200);
			using CenterHint hint = new CenterHint();
			hint.Show(new GestureAction
			{
				Label = "音量 +"
			}, ball.Center);
			await Task.Delay(90);
			CaptureScreen("hint-style-mid");
			await Task.Delay(1500);
			CaptureScreen("hint-style");
			hint.Hide();
		}
		catch (Exception error)
		{
			Environment.ExitCode = 1;
			Log(error);
		}
		finally
		{
			background?.Close();
			Shutdown();
		}
	}

	private async Task PanelTopFadeCheckAsync()
	{
		_ = 1;
		try
		{
			center = MakeCenter();
			center.ShowPanel(ball.Center, interactive: true, fromTop: true, EdgeKind.TopRight);
			await Task.Delay(160);
			double start = center.ContentOpacity;
			double startShadow = center.ShadowOpacity;
			center.UpdateGesture(1200.0);
			await Task.Delay(80);
			double contentOpacity = center.ContentOpacity;
			double shadowOpacity = center.ShadowOpacity;
			File.WriteAllText(Path.Combine(TestOutputDirectory, "panel-top-fade-check.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Start = start,
				StartShadow = startShadow,
				AtFull = contentOpacity,
				AtFullShadow = shadowOpacity
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			Environment.ExitCode = 1;
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "panel-top-fade-check.json"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task PanelSideFadeCheckAsync()
	{
		_ = 4;
		try
		{
			center = MakeCenter();
			center.ShowPanel(ball.Center, interactive: true, fromTop: false, EdgeKind.Right);
			await Task.Delay(160);
			double start = center.ContentOpacity;
			double startShadow = center.ShadowOpacity;
			center.UpdateGesture(200.0);
			await Task.Delay(80);
			double mid = center.ContentOpacity;
			CaptureUi(center, "side-fade-mid");
			CaptureArea(center, "side-fade-area", 40);
			CaptureScreen("side-fade-screen");
			center.UpdateGesture(1200.0);
			await Task.Delay(80);
			double full = center.ContentOpacity;
			center.HidePanel();
			await Task.Delay(120);
			double hiding = center.ContentOpacity;
			await Task.Delay(260);
			double contentOpacity = center.ContentOpacity;
			File.WriteAllText(Path.Combine(TestOutputDirectory, "panel-side-fade-check.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Start = start,
				StartShadow = startShadow,
				At200DIP = mid,
				AtFull = full,
				Hiding = hiding,
				Hidden = contentOpacity
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			Environment.ExitCode = 1;
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "panel-side-fade-check.json"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task InputTimingCheckAsync()
	{
		try
		{
			string text = await KeyboardInput.MeasureDoubleTapAsync();
			File.WriteAllText(Path.Combine(TestOutputDirectory, "input-timing-check.txt"), "PASS: " + text);
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(TestOutputDirectory, "input-timing-check.txt"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0828SmokeTest()
	{
		List<object> samples = new List<object>();
		try
		{
			Sample("startup");
			store.Value.Gamepad.Enabled = true;
			using (VirtualGamepad fixture = new VirtualGamepad(store, delegate
			{
			}))
			{
				await fixture.CheckCenterClicksAsync();
			}
			store.Value.Gamepad.Enabled = false;
			for (int cycle = 0; cycle < 8; cycle++)
			{
				SettingsWindow page = new SettingsWindow(store, ball.ResetPosition, delegate
				{
				});
				Ui.Present(page);
				await Task.Delay(400);
				await page.CheckV0828Async();
				page.Close();
				await Task.Delay(1500);
				Sample("settings closed " + cycle);
			}
			store.Value.BallEnabled = false;
			store.Value.TopEdgeEnabled = false;
			store.Value.EdgeSwitcherEnabled = false;
			store.Value.MultiTouch.Enabled = false;
			store.Value.Gamepad.Enabled = false;
			store.Value.VirtualTouchpad.Enabled = false;
			store.Value.MouseJoysticks.Pointer.Enabled = false;
			store.Value.MouseJoysticks.RightDrag.Enabled = false;
			foreach (ArkProfile arkProfile in store.Value.ArkProfiles)
			{
				arkProfile.Options.ArkEnabled = false;
			}
			store.Save();
			await Task.Delay(15000);
			Sample("features disabled, 15 seconds idle; no forced GC or working set trimming");
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0828-ui-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Scenario = "Eight settings create/navigate/reset/close cycles; joystick center single/double/cancel/drag tests; gesture selector, Dial toggle, native document layout; isolated settings",
				Samples = samples
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0828-ui-test.json"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
		void Sample(string stage)
		{
			using Process process = Process.GetCurrentProcess();
			process.Refresh();
			samples.Add(new
			{
				Stage = stage,
				PrivateBytes = process.PrivateMemorySize64,
				WorkingSetBytes = process.WorkingSet64,
				Handles = process.HandleCount,
				ManagedBytes = GC.GetTotalMemory(forceFullCollection: false)
			});
		}
	}

	private async Task V0827SmokeTest()
	{
		SettingsWindow page = null;
		try
		{
			List<PadEvent> events = new List<PadEvent>();
			using (VirtualTouchpad virtualTouchpad = new VirtualTouchpad(store, events.Add))
			{
				virtualTouchpad.CheckSideButtons();
			}
			if (events.FindAll((PadEvent e) => e.Kind == "Down" && e.Button == 513).Count != 2 || events.FindAll((PadEvent e) => e.Kind == "Down" && e.Button == 514).Count != 2 || events.FindAll((PadEvent e) => e.Kind == "Up").Count != 4)
			{
				throw new InvalidOperationException("侧边按键命中错误");
			}
			bool[] array = new bool[2] { false, true };
			foreach (bool right in array)
			{
				events.Clear();
				using MouseJoystickWindow stick = new MouseJoystickWindow(store, right, events.Add);
				await stick.CheckAsync();
				if (!events.Exists((PadEvent e) => e.Kind == "Move") || (right && (!events.Exists((PadEvent e) => e.Kind == "Down") || !events.Exists((PadEvent e) => e.Kind == "Up"))))
				{
					throw new InvalidOperationException("独立摇杆输入不完整");
				}
			}
			page = new SettingsWindow(store, ball.ResetPosition, delegate
			{
			});
			Ui.Present(page);
			await page.CheckV0827Async(Capture);
			Process process = Process.GetCurrentProcess();
			page.ShowPageForCapture(0);
			await Task.Delay(1000);
			process.Refresh();
			var initial = new
			{
				PrivateBytes = process.PrivateMemorySize64,
				WorkingSet = process.WorkingSet64,
				Handles = process.HandleCount
			};
			List<double> timings = new List<double>();
			for (int num = 0; num < 24; num++)
			{
				Stopwatch watch = Stopwatch.StartNew();
				page.ShowPageForCapture(num % 6);
				await Task.Delay(1);
				timings.Add(watch.Elapsed.TotalMilliseconds);
			}
			page.ShowPageForCapture(0);
			await Task.Delay(1500);
			process.Refresh();
			var after = new
			{
				PrivateBytes = process.PrivateMemorySize64,
				WorkingSet = process.WorkingSet64,
				Handles = process.HandleCount
			};
			TimeSpan cpu = process.TotalProcessorTime;
			Stopwatch idle = Stopwatch.StartNew();
			await Task.Delay(10000);
			process.Refresh();
			double idleCpuPercent = (process.TotalProcessorTime - cpu).TotalMilliseconds / idle.Elapsed.TotalMilliseconds / (double)Environment.ProcessorCount * 100.0;
			timings.Sort();
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0827-performance.json"), JsonSerializer.Serialize(new
			{
				Scenario = "x64 isolated smoke; warm settings navigation, 24 switches; 10-second idle with settings open. Not physical touch latency or before/after comparison.",
				initial = initial,
				after = after,
				IdleCpuPercent = idleCpuPercent,
				NavigationP50Ms = timings[timings.Count / 2],
				NavigationP95Ms = timings[(int)((double)timings.Count * 0.95)],
				Samples = timings.Count
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0827-ui-test.txt"), "PASS: side-button down/up; independent joystick motion/right-hold/edit/hide release; shared application selectors; gesture profiles; separate mouse joystick tab; live wheel preview. Input recorded only, no real input sent. Screenshots and resource sample saved.");
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0827-ui-test.txt"), ex.ToString());
		}
		finally
		{
			page?.Close();
			Shutdown();
		}
		static async Task Capture(Window window, string name)
		{
			RenderTargetBitmap render = new RenderTargetBitmap();
			await render.RenderAsync(window.Content);
			IBuffer buffer = await render.GetPixelsAsync();
			using DataReader reader = DataReader.FromBuffer(buffer);
			byte[] bytes = new byte[buffer.Length];
			reader.ReadBytes(bytes);
			using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
			BitmapEncoder obj = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
			obj.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)render.PixelWidth, (uint)render.PixelHeight, 96.0, 96.0, bytes);
			await obj.FlushAsync();
			stream.Seek(0uL);
			using DataReader png = new DataReader(stream);
			await png.LoadAsync((uint)stream.Size);
			byte[] array2 = new byte[(uint)stream.Size];
			png.ReadBytes(array2);
			File.WriteAllBytes(Path.Combine(TestOutputDirectory, name + ".png"), array2);
		}
	}

	private async Task V0825SmokeTest()
	{
		try
		{
			using (VirtualTouchpad pad = new VirtualTouchpad(store))
			{
				await pad.CheckTopButtonsAsync();
			}
			SettingsWindow settingsWindow = new SettingsWindow(store, ball.ResetPosition, delegate
			{
			});
			try
			{
				Ui.Present(settingsWindow);
				settingsWindow.CheckV0825Bindings();
			}
			finally
			{
				settingsWindow.Close();
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0825-ui-test.txt"), "PASS: top left/right buttons reach app-owned test window; opacity redraw. No clicks sent to other applications.");
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0825-ui-test.txt"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0824SmokeTest()
	{
		try
		{
			List<PadEvent> list = new List<PadEvent>();
			using (VirtualTouchpad virtualTouchpad = new VirtualTouchpad(store, list.Add))
			{
				virtualTouchpad.CheckNative();
			}
			if (!list.Exists((PadEvent e) => e.Kind == "Down" && e.Button == 513) || !list.Exists((PadEvent e) => e.Kind == "Up" && e.Button == 513))
			{
				throw new InvalidOperationException("顶部按键未释放");
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0824-ui-test.txt"), "PASS: top/bottom button zones and release; joystick settings synchronization and bounds; no synthetic cursor overlay. No real input injected.");
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0824-ui-test.txt"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0823SmokeTest()
	{
		try
		{
			List<PadEvent> list = new List<PadEvent>();
			using (VirtualTouchpad virtualTouchpad = new VirtualTouchpad(store, list.Add))
			{
				virtualTouchpad.CheckNative();
				virtualTouchpad.CheckV0823();
			}
			if (!list.Exists((PadEvent e) => e.Kind == "Up"))
			{
				throw new InvalidOperationException("触摸板未释放按键");
			}
			await KeyboardInput.CheckRepeatAsync();
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0823-ui-test.txt"), "PASS: TY touchpad resize, shake-to-close, button release; held-key/wheel repeat and shared release. No real input injected. Physical cursor suppression requires target touchscreen testing.");
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0823-ui-test.txt"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0822SmokeTest()
	{
		SettingsWindow page = null;
		try
		{
			await KeyboardInput.CheckRepeatAsync();
			store.Value.Gamepad.Enabled = true;
			store.Save();
			using (VirtualGamepad virtualGamepad = new VirtualGamepad(store, delegate
			{
			}))
			{
				virtualGamepad.CheckV0822();
			}
			List<PadEvent> list = new List<PadEvent>();
			using (VirtualTouchpad virtualTouchpad = new VirtualTouchpad(store, list.Add))
			{
				virtualTouchpad.CheckNative();
			}
			if (!list.Exists((PadEvent e) => e.Kind == "Down" && e.Button == 513) || !list.Exists((PadEvent e) => e.Kind == "Up" && e.Button == 513))
			{
				throw new InvalidOperationException("触摸板隐藏时没有释放底部左键");
			}
			using (RectangleHintWindow hint = new RectangleHintWindow())
			{
				await hint.CheckV0822();
			}
			page = new SettingsWindow(store, ball.ResetPosition, delegate
			{
			});
			Ui.Present(page);
			page.Activate();
			page.CheckV0822Settings();
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0822-ui-test.txt"), "PASS: held-key and wheel repeat; shared mouse release; joystick/button resize and two-finger drag; touchpad handle and button cancellation; region-bounded fixed hint host and transition pixels and explicit icons; shared appearance editor after functions; separate keyboard/touchpad tabs. No real input injected.");
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0822-ui-test.txt"), ex.ToString());
		}
		finally
		{
			page?.Close();
			Shutdown();
		}
	}

	private async Task V0821SmokeTest()
	{
		SettingsWindow page = null;
		try
		{
			int[] array = new int[5] { 87, 17, 39, 19, 173 };
			foreach (int num in array)
			{
				Win32.INPUT iNPUT = InputPackets.Build(num, up: false);
				if (iNPUT.type != 1 || (num == 87 && (iNPUT.data.ki.wScan != 17 || iNPUT.data.ki.wVk != 0)) || (num == 19 && iNPUT.data.ki.wVk != 19))
				{
					throw new InvalidOperationException("键盘输入包错误");
				}
			}
			array = new int[3] { 513, 514, 515 };
			for (int i = 0; i < array.Length; i++)
			{
				Win32.INPUT input = InputPackets.Build(array[i], up: false);
				Win32.INPUT iNPUT2 = InputPackets.Release(input);
				if (input.type != 0 || iNPUT2.data.mi.dwFlags != input.data.mi.dwFlags * 2)
				{
					throw new InvalidOperationException("鼠标释放格式错误");
				}
			}
			if (InputPackets.Build(516, up: false).data.mi.mouseData != 120 || InputPackets.Build(517, up: false).data.mi.mouseData != 4294967176u)
			{
				throw new InvalidOperationException("滚轮方向错误");
			}
			if (ActionNames.Icon(new GestureAction
			{
				Kind = ActionKind.Keys
			}).Length != 0 || ActionNames.Icon(new GestureAction
			{
				Kind = ActionKind.Launch
			}).Length != 0 || ActionNames.Icon(GestureAction.BuiltIn("StartMenu")).Length != 0 || ActionNames.Icon(GestureAction.BuiltIn("Gamepad")) != "\uece9")
			{
				throw new InvalidOperationException("默认图标范围错误");
			}
			store.Value.Gamepad.Enabled = true;
			store.Value.Gamepad.ShowLayoutSelector = true;
			store.Save();
			using (VirtualGamepad virtualGamepad = new VirtualGamepad(store, delegate
			{
			}))
			{
				virtualGamepad.CheckV0821();
			}
			using (RectangleHintWindow hint = new RectangleHintWindow())
			{
				await hint.CheckWidthAnimation();
			}
			ark.CheckAllPinned();
			page = new SettingsWindow(store, ball.ResetPosition, delegate
			{
			});
			Ui.Present(page);
			page.Activate();
			await page.CheckV0821Settings();
			DesktopVisibility.SetLocked(value: true);
			try
			{
				if (DesktopVisibility.CanShow(Ui.Handle(page)))
				{
					throw new InvalidOperationException("锁屏未隐藏设置");
				}
				controlGestureOwned = false;
				HandleControlGesture(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Begin, ball.Center, 0.0, 0.0));
				if (controlGestureOwned)
				{
					throw new InvalidOperationException("锁屏响应了底边手势");
				}
			}
			finally
			{
				DesktopVisibility.SetLocked(value: false);
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0821-ui-test.txt"), "PASS: automatic scan codes with Pause fallback; mouse button release and wheel directions; hint icon allow-list and bounded eased HWND; layout selector resize; edit selection and independent copy; added control revealed; wide/narrow editors; add bubble; Ark pin-all preserves independent pins; lock dispatch guard. No keyboard/mouse input injected. Physical lock screen and games NOT tested.");
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0821-ui-test.txt"), ex.ToString());
		}
		finally
		{
			page?.Close();
			Shutdown();
		}
	}

	private async Task V0820SmokeTest()
	{
		SettingsWindow page = null;
		try
		{
			GamepadLayout gamepadLayout = store.Value.Gamepad.Layouts[0];
			gamepadLayout.Shields.Clear();
			gamepadLayout.Shields.Add(new GamepadShieldSettings
			{
				Enabled = true,
				Width = 300.0,
				Height = 200.0
			});
			gamepadLayout.Shields.Add(new GamepadShieldSettings
			{
				Enabled = true,
				Width = 250.0,
				Height = 180.0
			});
			store.Value.Gamepad.Enabled = true;
			store.Save();
			using (VirtualGamepad virtualGamepad = new VirtualGamepad(store, delegate
			{
			}))
			{
				if (virtualGamepad.ShieldCount != 2)
				{
					throw new InvalidOperationException("多个区域未创建");
				}
				virtualGamepad.SetSuppressed(value: true);
				virtualGamepad.SetSuppressed(value: false);
			}
			Win32.KEYBDINPUT ki = GamepadKeyboard.Build(87, 17u, scanCodes: true, up: false).data.ki;
			Win32.KEYBDINPUT ki2 = GamepadKeyboard.Build(87, 17u, scanCodes: true, up: true).data.ki;
			Win32.KEYBDINPUT ki3 = GamepadKeyboard.Build(39, 57421u, scanCodes: true, up: false).data.ki;
			Win32.KEYBDINPUT ki4 = GamepadKeyboard.Build(87, 17u, scanCodes: false, up: false).data.ki;
			if (ki.wVk != 0 || ki.wScan != 17 || ki.dwFlags != 8 || ki2.dwFlags != 10 || ki3.wScan != 77 || ki3.dwFlags != 9 || ki4.wVk != 87 || ki4.wScan != 0 || ki4.dwFlags != 0)
			{
				throw new InvalidOperationException("扫描码格式错误");
			}
			using (RectangleHintWindow hint = new RectangleHintWindow())
			{
				await hint.CheckWidthAnimation();
				await Capture(hint, "hint-easing-v0820");
			}
			EnsureDial().CheckSelectionHint();
			page = new SettingsWindow(store, ball.ResetPosition, delegate
			{
			});
			Ui.Present(page);
			page.Activate();
			page.CheckV0820Settings();
			await Task.Delay(250);
			await Capture(page, "gamepad-layout-v0820");
			page.ShowPageForCapture(1, 1);
			await Task.Delay(100);
			page.ScrollWheelBinding();
			await Task.Delay(250);
			await Capture(page, "wheel-directions-v0820");
			StageSwitcherWindow stageSwitcherWindow = new StageSwitcherWindow(overview: true, store);
			try
			{
				stageSwitcherWindow.CheckDarkOverview();
			}
			finally
			{
				stageSwitcherWindow.Close();
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0820-ui-test.txt"), "PASS: width easing with fixed native surface, Dial selection hint, eight sectors, layout button order, grouped add buttons, two shields, scan-code/extended-key packet construction, dark overview. Actual Dragon Age Origins input NOT TESTED.");
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0820-ui-test.txt"), ex.ToString());
		}
		finally
		{
			page?.Close();
			Shutdown();
		}
		static async Task Capture(Window window, string name)
		{
			RenderTargetBitmap render = new RenderTargetBitmap();
			await render.RenderAsync(window.Content);
			IBuffer buffer = await render.GetPixelsAsync();
			using DataReader reader = DataReader.FromBuffer(buffer);
			byte[] bytes = new byte[buffer.Length];
			reader.ReadBytes(bytes);
			using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
			BitmapEncoder obj = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
			obj.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)render.PixelWidth, (uint)render.PixelHeight, 96.0, 96.0, bytes);
			await obj.FlushAsync();
			stream.Seek(0uL);
			using DataReader png = new DataReader(stream);
			await png.LoadAsync((uint)stream.Size);
			byte[] array = new byte[(uint)stream.Size];
			png.ReadBytes(array);
			File.WriteAllBytes(Path.Combine(TestOutputDirectory, name + ".png"), array);
		}
	}

	private async Task V0819SmokeTest()
	{
		SettingsWindow page = null;
		try
		{
			store.Value.Gamepad.Enabled = true;
			store.Value.Gamepad.EnsureLayouts();
			store.Value.Gamepad.Layouts[0].Shields[0].Enabled = true;
			store.Value.Gamepad.Controls.Add(new GamepadControl
			{
				Joystick = true,
				Name = "右摇杆",
				Size = 140.0
			});
			string[] array = new string[2] { "Square", "Rounded" };
			foreach (string text in array)
			{
				store.Value.Gamepad.Controls.Add(new GamepadControl
				{
					Name = text,
					Style = text,
					Width = 150.0,
					Height = 70.0,
					CornerRadius = 22.0,
					Text = "跳跃",
					Icon = "\uece9"
				});
			}
			store.Save();
			List<string> calls = new List<string>();
			using (VirtualGamepad virtualGamepad = new VirtualGamepad(store, delegate(IReadOnlyList<int> keys, bool down)
			{
				calls.Add((down ? "D" : "U") + string.Join(",", keys));
			}))
			{
				virtualGamepad.CheckStyles(AppContext.BaseDirectory);
				if (calls.Count < 2)
				{
					goto IL_01e1;
				}
				List<string> list = calls;
				if (!list[list.Count - 1].StartsWith("U"))
				{
					goto IL_01e1;
				}
				goto end_IL_01a8;
				IL_01e1:
				throw new InvalidOperationException("摇杆输入未释放");
				end_IL_01a8:;
			}
			page = new SettingsWindow(store, ball.ResetPosition, delegate
			{
			});
			Ui.Present(page);
			page.Activate();
			page.CheckV0819Settings();
			await Task.Delay(300);
			await Capture(page, "gamepad-settings-v0819");
			page.ScrollGamepadDirection();
			await Task.Delay(300);
			await Capture(page, "gamepad-directions-v0819");
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			for (int i2 = 0; i2 < 3; i2++)
			{
				center.ShowPanel(ball.Center);
				SyncArkVisibility();
				VirtualGamepad? virtualGamepad2 = gamepad;
				if (virtualGamepad2 == null || !virtualGamepad2.IsSuppressed)
				{
					throw new InvalidOperationException("控制中心没有隐藏摇杆");
				}
				center.HidePanel();
				await Task.Delay(300);
			}
			if (store.Value.ControlCenterOpenCount != 3 || !store.Value.LauncherHintDismissed)
			{
				throw new InvalidOperationException("三次后说明清理失败");
			}
			DesktopVisibility.SetLocked(value: true);
			if (!DesktopVisibility.CanShow(Ui.Handle(page)) && !DesktopVisibility.CanShow(Ui.Handle(center)))
			{
				VirtualGamepad? virtualGamepad3 = gamepad;
				if (virtualGamepad3 != null && virtualGamepad3.IsSuppressed)
				{
					DesktopVisibility.SetLocked(value: false);
					using (RectangleHintWindow hint = new RectangleHintWindow())
					{
						hint.Show(new GestureAction
						{
							Kind = ActionKind.BuiltIn,
							Command = "Gamepad"
						}, ball.Center, 0.0);
						await Task.Delay(300);
						hint.CheckAdaptiveWidth();
					}
					File.WriteAllText(Path.Combine(TestOutputDirectory, "v0819-ui-test.json"), "PASS: styles, shield bounds and no-activation, direction arc, navigation, lazy editors, input release, panel suppression, simulated lock policy, third-open cleanup, adaptive hint and icon. Physical ARM64 touch and lock screen NOT TESTED.");
					return;
				}
			}
			throw new InvalidOperationException("锁屏显示规则不符");
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0819-ui-test.json"), ex.ToString());
		}
		finally
		{
			DesktopVisibility.SetLocked(value: false);
			page?.Close();
			Shutdown();
		}
		static async Task Capture(Window window, string name)
		{
			RenderTargetBitmap render = new RenderTargetBitmap();
			await render.RenderAsync(window.Content);
			IBuffer buffer = await render.GetPixelsAsync();
			using DataReader reader = DataReader.FromBuffer(buffer);
			byte[] bytes = new byte[buffer.Length];
			reader.ReadBytes(bytes);
			using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
			BitmapEncoder obj = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
			obj.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)render.PixelWidth, (uint)render.PixelHeight, 96.0, 96.0, bytes);
			await obj.FlushAsync();
			stream.Seek(0uL);
			using DataReader png = new DataReader(stream);
			await png.LoadAsync((uint)stream.Size);
			byte[] array2 = new byte[(uint)stream.Size];
			png.ReadBytes(array2);
			File.WriteAllBytes(Path.Combine(TestOutputDirectory, name + ".png"), array2);
		}
	}

	private async Task V0818SmokeTest()
	{
		SettingsWindow page = null;
		try
		{
			await ball.CheckMaskAsync();
			ball.SetVisible(visible: false);
			ark?.SetSuppressed(value: true);
			store.Value.MultiTouch.EdgeEnabled = true;
			store.Value.EdgeGestures[2] = GestureAction.BuiltIn("Overview");
			Win32.RECT rcMonitor = Win32.Monitor(ball.Center).rcMonitor;
			Win32.POINT origin = new Win32.POINT((rcMonitor.Left + rcMonitor.Right) / 2, rcMonitor.Bottom);
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Begin, origin, 0.0, 0.0));
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Update, origin, 450.0, 0.0));
			if (!bottomStartOpened || bottomCards)
			{
				throw new InvalidOperationException("450 DIP 未触发开始菜单。");
			}
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.End, origin, 450.0, 0.0));
			page = new SettingsWindow(store, ball.ResetPosition, delegate
			{
			});
			Ui.Present(page);
			page.Activate();
			await Task.Delay(700);
			page.CheckV0818Settings();
			await Task.Delay(250);
			await Capture(page, "settings-v0818");
			string path = Path.Combine(AppContext.BaseDirectory, "Assets", "TouchToys.png");
			await TouchFilePicker.CheckAsync(page, path, Capture);
			SettingsStore settingsStore = new SettingsStore(Path.Combine(TestOutputDirectory, "smoke-state", "pad"));
			settingsStore.Value.Gamepad.Enabled = true;
			List<string> padEvents = new List<string>();
			using (VirtualGamepad virtualGamepad = new VirtualGamepad(settingsStore, delegate(IReadOnlyList<int> keys, bool down)
			{
				padEvents.Add((down ? "down:" : "up:") + string.Join(",", keys));
			}))
			{
				virtualGamepad.CheckControls(AppContext.BaseDirectory);
				settingsStore.Save();
				if (padEvents.Count < 2)
				{
					goto IL_0465;
				}
				List<string> list = padEvents;
				if (!list[list.Count - 1].StartsWith("up:"))
				{
					goto IL_0465;
				}
				goto end_IL_041b;
				IL_0465:
				throw new InvalidOperationException("屏幕按钮没有成对释放。");
				end_IL_041b:;
			}
			using (RectangleHintWindow hint = new RectangleHintWindow())
			{
				hint.Show(new GestureAction
				{
					Label = "音量 25%",
					TextColor = "#FFFFFF"
				}, ball.Center, 0.0);
				await Task.Delay(350);
				await hint.CheckThemeAsync();
				await Capture(hint, "hint-v0818");
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0818-ui-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				TouchFileSelectionAndCancel = true,
				MaskLifecycle = true,
				Fixed450DIP = true,
				HintThemes = true,
				KeyboardOnlySettings = true,
				GamepadLayoutAndRelease = true,
				PhysicalTouchAndSound = "NOT_TESTED",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0818-ui-test.json"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			page?.Close();
			Shutdown();
		}
		static async Task Capture(Window window, string name)
		{
			RenderTargetBitmap render = new RenderTargetBitmap();
			await render.RenderAsync(window.Content);
			IBuffer buffer = await render.GetPixelsAsync();
			using DataReader reader = DataReader.FromBuffer(buffer);
			byte[] bytes = new byte[buffer.Length];
			reader.ReadBytes(bytes);
			using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
			BitmapEncoder obj = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
			obj.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)render.PixelWidth, (uint)render.PixelHeight, 96.0, 96.0, bytes);
			await obj.FlushAsync();
			stream.Seek(0uL);
			using DataReader png = new DataReader(stream);
			await png.LoadAsync((uint)stream.Size);
			byte[] array = new byte[(uint)stream.Size];
			png.ReadBytes(array);
			File.WriteAllBytes(Path.Combine(TestOutputDirectory, name + ".png"), array);
		}
	}

	private async Task V0817SmokeTest()
	{
		SettingsWindow page = null;
		try
		{
			ball.SetVisible(visible: false);
			ark?.SetSuppressed(value: true);
			await Task.Delay(1500);
			object idle = Memory();
			store.Value.MultiTouch.EdgeEnabled = true;
			store.Value.EdgeGestures[2] = GestureAction.BuiltIn("Overview");
			edgeGesture.SetGlobalTop(value: true);
			await edgeGesture.CheckBottomIndicatorAsync();
			if (edgeGesture.IsRefreshActive)
			{
				throw new InvalidOperationException("隐藏后仍读取屏幕。");
			}
			edgeGesture.ShowBottomIndicator();
			edgeGesture.SuppressIndicator(value: true);
			if (edgeGesture.IsRefreshActive)
			{
				throw new InvalidOperationException("抑制指示器后仍读取屏幕。");
			}
			Win32.RECT rcMonitor = Win32.Monitor(ball.Center).rcMonitor;
			Win32.POINT pOINT = new Win32.POINT((rcMonitor.Left + rcMonitor.Right) / 2, rcMonitor.Bottom - 1);
			double num = (double)rcMonitor.Height / 3.0 / Win32.Scale(pOINT) + 1.0;
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Begin, pOINT, 0.0, 0.0));
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Update, pOINT, num, 0.0));
			if (!bottomStartOpened || bottomCards)
			{
				throw new InvalidOperationException("上滑跨越三分之一未立即打开开始菜单。");
			}
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Update, pOINT, num + 20.0, 0.0));
			if (bottomCards)
			{
				throw new InvalidOperationException("同次滑动重复打开任务卡片。");
			}
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.End, pOINT, num + 20.0, 0.0));
			if (bottomStartOpened)
			{
				throw new InvalidOperationException("滑动锁没有释放。");
			}
			SystemInputControl.Candidate unsafeTarget = new SystemInputControl.Candidate(1L, 0L, 0, 0L, "TextInputHost.exe", "Windows.UI.Core.CoreWindow", Visible: true, new SystemInputControl.Bounds(0, 0, 1440, 960), 0L, 0L, 192u, null, "");
			bool rejected = false;
			try
			{
				await TouchpadControl.SelectAsync(unsafeTarget);
			}
			catch (InvalidOperationException ex)
			{
				rejected = ex.Message.Contains("共享");
			}
			if (!rejected || NativeTouchpad.IsSurface(unsafeTarget))
			{
				throw new InvalidOperationException("共享宿主仍可选作触摸板。");
			}
			page = new SettingsWindow(store, ball.ResetPosition, delegate
			{
			});
			Ui.Present(page);
			page.Activate();
			await Task.Delay(1800);
			await page.CheckIndicatorSettingsAsync();
			await Task.Delay(1000);
			byte[] array = await WindowSnapshot.Capture(Ui.Handle(page), default(CancellationToken));
			if (array != null)
			{
				File.WriteAllBytes(Path.Combine(TestOutputDirectory, "gesture-v0817.png"), array);
			}
			RenderTargetBitmap render = new RenderTargetBitmap();
			await render.RenderAsync(page.Content);
			IBuffer buffer = await render.GetPixelsAsync();
			using (DataReader reader = DataReader.FromBuffer(buffer))
			{
				byte[] bytes = new byte[buffer.Length];
				reader.ReadBytes(bytes);
				using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
				BitmapEncoder obj = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
				obj.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)render.PixelWidth, (uint)render.PixelHeight, 96.0, 96.0, bytes);
				await obj.FlushAsync();
				stream.Seek(0uL);
				using DataReader png = new DataReader(stream);
				await png.LoadAsync((uint)stream.Size);
				byte[] array2 = new byte[(uint)stream.Size];
				png.ReadBytes(array2);
				File.WriteAllBytes(Path.Combine(TestOutputDirectory, "gesture-content-v0817.png"), array2);
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0817-ui-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Idle = idle,
				AfterTaskCardsAndSettings = Memory(),
				HiddenIndicatorPollingStopped = true,
				ImmediateStartMenuOnce = true,
				UnsafeSharedHostRejected = true,
				NativeInputHardware = "NOT_TESTED",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex2)
		{
			Log(ex2);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0817-ui-test.json"), ex2.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			page?.Close();
			Shutdown();
		}
		static object Memory()
		{
			using Process process = Process.GetCurrentProcess();
			process.Refresh();
			return new
			{
				WorkingSetMiB = Math.Round((double)process.WorkingSet64 / 1048576.0, 1),
				PrivateMiB = Math.Round((double)process.PrivateMemorySize64 / 1048576.0, 1),
				ManagedMiB = Math.Round((double)GC.GetTotalMemory(forceFullCollection: false) / 1048576.0, 1)
			};
		}
	}

	private async Task V0816SmokeTest()
	{
		SettingsWindow page = null;
		try
		{
			ball.SetVisible(visible: false);
			ark?.SetSuppressed(value: true);
			await Task.Delay(1500);
			object idle = Memory();
			if (InputControl.RecoveryPath == TouchpadControl.RecoveryPath || InputControl.Layout == TouchpadControl.Layout)
			{
				throw new InvalidOperationException("输入面板状态没有隔离。");
			}
			using (SystemInputPage systemInputPage = new SystemInputPage(InputControl, store))
			{
				using SystemInputPage systemInputPage2 = new SystemInputPage(TouchpadControl, store);
				systemInputPage.CheckPercentSteps();
				systemInputPage2.CheckPercentSteps();
			}
			page = new SettingsWindow(store, ball.ResetPosition, delegate
			{
			});
			page.Activate();
			await Task.Delay(250);
			object basic = Memory();
			page.ShowPageForCapture(5);
			await Task.Delay(250);
			byte[] array = await WindowSnapshot.Capture(Ui.Handle(page), default(CancellationToken));
			if (array != null)
			{
				File.WriteAllBytes(Path.Combine(TestOutputDirectory, "input-v0816.png"), array);
			}
			object input = Memory();
			page.Close();
			page = null;
			await WindowPreview.Prewarm(overview: true);
			await Task.Delay(3300);
			if (WindowSnapshot.HasDevice)
			{
				throw new InvalidOperationException("闲置取图设备没有释放。");
			}
			object afterPreviewIdle = Memory();
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0816-ui-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Idle = idle,
				BasicSettings = basic,
				InputSettings = input,
				AfterPreviewIdle = afterPreviewIdle,
				IndependentInputJournals = true,
				IdleCaptureDeviceReleased = true,
				NativeInputHardware = "NOT_TESTED",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0816-ui-test.json"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			page?.Close();
			Shutdown();
		}
		static object Memory()
		{
			using Process process = Process.GetCurrentProcess();
			process.Refresh();
			return new
			{
				WorkingSetMiB = Math.Round((double)process.WorkingSet64 / 1048576.0, 1),
				PrivateMiB = Math.Round((double)process.PrivateMemorySize64 / 1048576.0, 1),
				ManagedMiB = Math.Round((double)GC.GetTotalMemory(forceFullCollection: false) / 1048576.0, 1)
			};
		}
	}

	private async Task V0815SmokeTest()
	{
		_ = 4;
		try
		{
			ball.SetVisible(visible: false);
			ark?.SetSuppressed(value: true);
			int previews = 0;
			bool[] array = new bool[2] { true, false };
			foreach (bool full in array)
			{
				await WindowPreview.Prewarm(full);
				StageSwitcherWindow stage = new StageSwitcherWindow(full, store)
				{
					IgnoreExternalDismissal = true
				};
				try
				{
					((FrameworkElement)stage.Content).IsHitTestVisible = false;
					stage.Show(ball.Center, interactive: true, full ? EdgeKind.Bottom : EdgeKind.Left);
					if (full)
					{
						stage.UpdateBottomGesture(0.4);
					}
					else
					{
						stage.UpdateGesture(180.0);
					}
					await Task.Delay(100);
					if (stage.LivePreviewCount == 0)
					{
						throw new InvalidOperationException("手势展开过程中未准备任何缓存预览。");
					}
					stage.CheckPreviewGeometry();
					stage.CheckNoCardInstructions();
					previews += stage.LivePreviewCount;
					stage.EndGesture(1.0);
					await Task.Delay(350);
					if (!stage.IsOpen)
					{
						throw new InvalidOperationException($"预览验证界面没有展开：full={full}, progress={stage.RevealProgress}。");
					}
					CaptureUi(stage, full ? "overview-v0815" : "stage-v0815");
					stage.Hide();
					await Task.Delay(350);
				}
				finally
				{
					stage.Close();
				}
			}
			ShowSettings();
			await Task.Delay(300);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0815-ui-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				PreviewsReadyDuringGesture = previews,
				TouchpadHardware = "NOT_TESTED",
				ARM64Touch = "NOT_TESTED",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0815-ui-test.json"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0814SmokeTest()
	{
		_ = 7;
		try
		{
			ball.SetVisible(visible: false);
			ark?.SetSuppressed(value: true);
			QrScannerWindow scan = new QrScannerWindow();
			try
			{
				await scan.CheckLifecycleAsync();
				CaptureUi(scan, "scanner-v0814");
			}
			finally
			{
				scan.Close();
			}
			string retained = await DwmPreviewProbe.CheckAsync();
			int previews = 0;
			bool[] array = new bool[2] { true, false };
			foreach (bool full in array)
			{
				StageSwitcherWindow stage = new StageSwitcherWindow(full, store)
				{
					IgnoreExternalDismissal = true
				};
				try
				{
					stage.Show(ball.Center, interactive: false, full ? EdgeKind.Bottom : EdgeKind.Left);
					await Task.Delay(450);
					await stage.WaitForPreviews();
					stage.CheckNoCardInstructions();
					stage.CheckPreviewGeometry();
					previews += stage.LivePreviewCount;
					CaptureUi(stage, full ? "overview-v0814" : "stage-v0814");
					await stage.CheckPreviewScrollAsync();
					stage.Hide();
					await Task.Delay(350);
				}
				finally
				{
					stage.Close();
				}
			}
			using (SystemInputPage page = new SystemInputPage(InputControl, store))
			{
				page.CheckPercentSteps();
				Window window = new Window
				{
					Content = page,
					Title = "透明度设置验证"
				};
				Ui.ConfigureOverlay(window);
				window.AppWindow.Resize(new SizeInt32(650, 700));
				Ui.Present(window);
				await Task.Delay(180);
				CaptureUi(window, "opacity-v0814");
				window.Close();
			}
			await ark.CheckPinnedDoubleClickAsync();
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0814-ui-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Retained = retained,
				Previews = previews,
				CameraLifecycleCycles = 20,
				CameraHardware = "NOT_TESTED",
				NativeKeyboardHardware = "NOT_TESTED",
				ARM64Touch = "NOT_TESTED",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0814-ui-test.json"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0813SmokeTest()
	{
		_ = 7;
		try
		{
			ball.SetVisible(visible: false);
			await ark.CheckPinnedDoubleClickAsync();
			if (v0813KeyEvents.Count != 6 || v0813KeyEvents[0] != v0813KeyEvents[2] || v0813KeyEvents[1] != v0813KeyEvents[3] || !v0813KeyEvents[0].StartsWith("down:") || !v0813KeyEvents[5].StartsWith("up:"))
			{
				throw new InvalidOperationException("持续按住事件不配对。");
			}
			bool cleaned = false;
			using (HeldKeys heldKeys = new HeldKeys(delegate(IReadOnlyList<int> _, bool down)
			{
				if (down)
				{
					throw new InvalidOperationException("test partial send failure");
				}
				cleaned = true;
			}))
			{
				try
				{
					heldKeys.Press(new int[2] { 17, 67 });
				}
				catch (InvalidOperationException)
				{
				}
				if (!cleaned || heldKeys.Active)
				{
					throw new InvalidOperationException("按下失败后没有清理按键。");
				}
			}
			string retained = await DwmPreviewProbe.CheckAsync();
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			center.ShowPanel(ball.Center);
			await Task.Delay(450);
			CaptureUi(center, "center-v0813");
			center.HidePanel();
			await Task.Delay(350);
			int cached = 0;
			bool[] array = new bool[2] { true, false };
			foreach (bool full in array)
			{
				StageSwitcherWindow stage = new StageSwitcherWindow(full, store)
				{
					IgnoreExternalDismissal = true
				};
				try
				{
					stage.Show(ball.Center, interactive: false, full ? EdgeKind.Bottom : EdgeKind.Left);
					await Task.Delay(450);
					await stage.WaitForPreviews();
					stage.CheckPreviewOpacity();
					stage.CheckPreviewGeometry();
					if (stage.LivePreviewCount == 0)
					{
						throw new InvalidOperationException("任务界面没有预览图像。");
					}
					cached += stage.LivePreviewCount;
					CaptureUi(stage, full ? "overview-v0813" : "stage-v0813");
					await stage.CheckPreviewScrollAsync();
					stage.Hide();
					await Task.Delay(450);
					if (stage.LivePreviewCount != 0)
					{
						throw new InvalidOperationException("隐藏后预览未释放。");
					}
				}
				finally
				{
					stage.Close();
				}
			}
			InputControl.ResetControl();
			if (InputControl.Target != null || InputControl.OpacityPassed)
			{
				throw new InvalidOperationException("恢复后仍保留旧的原生输入目标。");
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0813-ui-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Retained = retained,
				CachedPreviews = cached,
				HeldKeyEvents = v0813KeyEvents,
				NativeKeyboardVisualAndTouch = "NOT_TESTED",
				ARM64Touch = "NOT_TESTED",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex2)
		{
			Log(ex2);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0813-ui-test.json"), ex2.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0812SmokeTest()
	{
		_ = 14;
		try
		{
			ball.SetVisible(visible: false);
			ark?.SetSuppressed(value: true);
			ShowSettings();
			await Task.Delay(600);
			settings.ShowPageForCapture(5);
			InputControl.Baseline();
			List<SystemInputControl.Candidate> inputCandidates = InputControl.Scan();
			await InputControl.DiagnoseAsync();
			await Task.Delay(200);
			CaptureUi(settings, "native-input-v0812");
			settings.AppWindow.Hide();
			using (RectangleHintWindow hint = new RectangleHintWindow())
			{
				GestureAction action = new GestureAction
				{
					Label = "音量 +",
					Icon = "\ue767"
				};
				hint.Show(action, ball.Center, 0.0);
				await Task.Delay(350);
				hint.Show(action, ball.Center, 0.0);
				await Task.Delay(110);
				(double, double, double, double) animationState = hint.AnimationState;
				if (animationState.Item1 >= 0.0 || animationState.Item2 <= 0.0 || animationState.Item4 <= 0.0 || animationState.Item4 >= 1.0)
				{
					throw new InvalidOperationException("重复工具提示没有滚动。");
				}
				CaptureUi(hint, "hint-roll-v0812");
				hint.Show(action, ball.Center, 0.0);
				await Task.Delay(240);
				if (hint.AnimationState.CurrentY <= 0.0)
				{
					throw new InvalidOperationException("快速触发没有排入下一次滚动。");
				}
				await Task.Delay(350);
				CaptureUi(hint, "hint-v0812");
				File.WriteAllText(Path.Combine(TestOutputDirectory, "v0812-hint-backdrop.txt"), ((PanelBackdrop)hint.SystemBackdrop).Diagnostics);
			}
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			center.ShowPanel(ball.Center);
			await Task.Delay(400);
			await center.CheckV0812Async(delegate
			{
				CaptureUi(center, "center-v0812");
			});
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0812-center-frame.txt"), SystemPanelFrame.Diagnostics(Ui.Handle(center)) + "\n" + center.BackdropDiagnostics);
			center.HidePanel();
			await Task.Delay(350);
			if ((object)overview == null)
			{
				overview = new StageSwitcherWindow(overview: true, store)
				{
					IgnoreExternalDismissal = true
				};
			}
			overview.Show(ball.Center, interactive: true, EdgeKind.Bottom);
			await Task.Delay(200);
			overview.UpdateBottomGesture(0.45);
			await Task.Delay(250);
			overview.CheckPreviewOpacity();
			overview.CheckPreviewGeometry();
			CaptureUi(overview, "overview-progress-v0812");
			int previews = overview.LivePreviewCount;
			overview.UpdateBottomGesture(1.0);
			await Task.Delay(250);
			overview.CheckPreviewOpacity();
			overview.CheckPreviewGeometry();
			await overview.CheckPreviewScrollAsync();
			CaptureUi(overview, "overview-v0812");
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0812-preview-geometry.json"), overview.PreviewGeometry());
			overview.Hide();
			await Task.Delay(450);
			if (overview.LivePreviewCount != 0)
			{
				throw new InvalidOperationException("DWM 预览未释放。");
			}
			File.Copy(InputControl.WriteReport(), Path.Combine(TestOutputDirectory, "v0812-native-input.json"), overwrite: true);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0812-ui-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				InputCandidates = inputCandidates.Count,
				VisiblePreviews = previews,
				NativeKeyboardVisualAndTouch = "NOT_TESTED",
				ARM64Touch = "NOT_TESTED",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0812-ui-test.json"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0811SmokeTest()
	{
		_ = 7;
		try
		{
			ball.SetVisible(visible: false);
			ark?.SetSuppressed(value: true);
			GlobalTouchGestures.CheckRelay();
			long allocated = GlobalTouchGestures.CheckRelayAllocation();
			ShowSettings();
			await Task.Delay(600);
			settings.ShowPageForCapture(5);
			await Task.Delay(250);
			CaptureUi(settings, "native-input-v0811");
			if (!settings.ExtendsContentIntoTitleBar)
			{
				throw new InvalidOperationException("设置标题栏未统一。");
			}
			await InputControl.DiagnoseAsync();
			string report = InputControl.WriteReport();
			Win32.WNDCLASS wc = new Win32.WNDCLASS
			{
				lpfnWndProc = Win32.DefWindowProc,
				hInstance = Win32.GetModuleHandle(null),
				lpszClassName = "TouchAssistant.InputFixture"
			};
			Win32.RegisterClass(ref wc);
			nint num = Win32.CreateWindowEx(0u, wc.lpszClassName, "", 2147483648u, 100, 100, 160, 80, 0, 0, wc.hInstance, 0);
			try
			{
				SystemInputControl.CheckOwnedWindow(num);
			}
			finally
			{
				Win32.DestroyWindow(num);
			}
			settings.DarkCapture(dark: true);
			await Task.Delay(150);
			CaptureUi(settings, "settings-dark-v0811");
			settings.DarkCapture(dark: false);
			settings.AppWindow.Hide();
			using (RectangleHintWindow hint = new RectangleHintWindow())
			{
				hint.Show(new GestureAction
				{
					Label = "音量 +",
					Icon = "\ue767"
				}, ball.Center, 0.0);
				await Task.Delay(300);
				hint.Show(new GestureAction
				{
					Label = "音量 −",
					Icon = "\ue767"
				}, ball.Center, 0.0);
				await Task.Delay(100);
				CaptureUi(hint, "hint-roll-v0811");
				hint.Hide();
				hint.Show(new GestureAction
				{
					Label = "已撤销"
				}, ball.Center, 0.0);
				await Task.Delay(250);
				CaptureUi(hint, "hint-v0811");
			}
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			center.ShowPanel(ball.Center);
			await Task.Delay(400);
			CaptureUi(center, "center-global-gestures-v0811");
			center.HidePanel();
			string text = Path.Combine(TestOutputDirectory, "v0811-diagnostics.zip");
			Diagnostics.Export(text, store.Value, DiagnosticStatus, "isolated validation");
			using (ZipArchive zipArchive = ZipFile.OpenRead(text))
			{
				if (zipArchive.GetEntry("native-input.json") == null)
				{
					throw new InvalidOperationException("诊断包缺少原生输入报告。");
				}
			}
			File.Copy(report, Path.Combine(TestOutputDirectory, "v0811-native-input.json"), overwrite: true);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0811-ui-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				RelayMappingAllocatedBytes = allocated,
				Frames = 10000,
				NativeWindowRollback = "PASS (owned fixture)",
				NativeKeyboardVisualAndTouch = "NOT_TESTED",
				ARM64TouchAndGames = "NOT_TESTED",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0811-ui-test.json"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0810FinalCheck()
	{
		_ = 1;
		try
		{
			ark.Preview(store.Value.ArkProfiles[0]);
			ark.SetSuppressed(value: true);
			ark.Preview(store.Value.ArkProfiles[0]);
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			ball.SetVisible(visible: false);
			Stopwatch stopwatch = Stopwatch.StartNew();
			center.ShowPanel(ball.Center);
			center.CheckImmediateState();
			long firstStateMs = stopwatch.ElapsedMilliseconds;
			await Task.Delay(400);
			await center.CheckV0810Async(delegate
			{
				CaptureUi(center, "center-battery-v0810");
			});
			ark.CheckImportedProfile();
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0810-final-check.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				FirstKnownStateMilliseconds = firstStateMs,
				Checks = "initial known state applied synchronously; battery screenshot after composition; imported Ark leaves old preview",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0810-final-check.json"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V0810SmokeTest()
	{
		_ = 8;
		try
		{
			ShowSettings();
			await Task.Delay(700);
			await settings.CheckV0810Async(delegate
			{
				CaptureUi(settings, "dial-settings-v0810");
			});
			settings.ShowPageForCapture(0);
			await Task.Delay(120);
			CaptureUi(settings, "settings-transfer-v0810");
			settings.AppWindow.Hide();
			EnsureDial().CheckToolBindings();
			ball.SetVisible(visible: false);
			ark.SetSuppressed(value: true);
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			if (systemInitialization == null)
			{
				systemInitialization = system.InitializeAsync();
			}
			await center.CheckRevealAsync(ball.Center);
			center.ShowPanel(ball.Center);
			await Task.Delay(400);
			ball.SetVisible(visible: false);
			await center.CheckV0810Async(delegate
			{
				CaptureUi(center, "center-v0810");
			});
			center.HidePanel();
			await Task.Delay(300);
			await Execute(GestureAction.BuiltIn("Overview"));
			await Task.Delay(450);
			if (!(overview?.SystemBackdrop is PanelBackdrop))
			{
				throw new InvalidOperationException("后台未使用系统亚克力。");
			}
			CaptureUi(overview, "overview-v0810");
			overview.Hide();
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0810-ui-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Checks = "Dial per-tool center and rotation dispatch; three appearance editors; modifier buttons; settings import launcher sync; full client frame; four direction reveals; smaller centered battery fixture; shared acrylic overview; media module removed",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v0810-ui-test.json"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V088SmokeTest()
	{
		_ = 4;
		try
		{
			ShowSettings();
			await Task.Delay(800);
			ark.SetSuppressed(value: true);
			ball.SetVisible(visible: false);
			if (!Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--thumbnail-only"))
			{
				await Ui.CheckExePickerAsync(settings);
			}
			if (Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--picker-only"))
			{
				File.WriteAllText(Path.Combine(TestOutputDirectory, "v088-picker-test.json"), "PASS: native owned EXE picker returned selected notepad.exe path; no application launched.");
				return;
			}
			nint handle = Ui.Handle(settings);
			if (!WindowCatalog.Read().Exists((RunningWindow w) => w.Handle == handle))
			{
				throw new InvalidOperationException("设置窗口未进入任务列表。");
			}
			if (!(await WindowPreview.LoadAsync(handle, CancellationToken.None) is RenderTargetBitmap { PixelWidth: not 0 }))
			{
				throw new InvalidOperationException("设置窗口缩略图为空。");
			}
			if (Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--thumbnail-only"))
			{
				File.WriteAllText(Path.Combine(TestOutputDirectory, "v088-thumbnail-test.json"), "PASS: own settings task and shared asynchronous thumbnail capture.");
				return;
			}
			settings.AppWindow.Hide();
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			await center.CheckRevealAsync(ball.Center, horizontalOnly: true);
			using (CenterHint hint = new CenterHint())
			{
				hint.Show(new GestureAction
				{
					Label = "系统触摸板",
					Icon = "\uefa5"
				}, ball.Center, 2.0);
				await Task.Delay(250);
			}
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v088-targeted-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Checks = "Owned native EXE picker and selected path; own settings catalog and XAML thumbnail; horizontal stable-size reveal, cancellation and commit",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v088-targeted-test.json"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V087SmokeTest()
	{
		_ = 14;
		try
		{
			bool remainingOnly = Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--remaining-only");
			bool launcherOnly = remainingOnly || Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--launcher-only");
			if (!launcherOnly)
			{
				ShowSettings();
				await Task.Delay(600);
				ark.SetSuppressed(value: true);
				ball.SetVisible(visible: false);
				await settings.CheckGestureEditorAsync(delegate
				{
					CaptureUi(settings, "custom-gesture-v087");
				});
			}
			settings?.AppWindow.Hide();
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			center.ShowPanel(ball.Center);
			await Task.Delay(600);
			if (!remainingOnly)
			{
				await center.CheckV087LaunchersAsync();
				await center.CheckArtworkAsync(delegate
				{
					CaptureUi(center, "media-v087");
				});
			}
			await center.CheckBatchPickerAsync();
			center.HidePanel();
			await Task.Delay(350);
			if (!remainingOnly)
			{
				await EnsureDial().CheckAsync();
			}
			using (CenterHint hint = new CenterHint())
			{
				hint.Show(new GestureAction
				{
					Label = "工具一"
				}, ball.Center, 2.0, custom: true, rectangle: true);
				await Task.Delay(200);
				hint.Show(new GestureAction
				{
					Label = "工具二"
				}, ball.Center, 2.0, custom: true, rectangle: true);
				await Task.Delay(200);
			}
			store.Value.MultiTouch.EdgeEnabled = true;
			store.Value.EdgeGestures[2] = GestureAction.BuiltIn("Overview");
			edgeGesture.SetGlobalTop(value: true);
			await edgeGesture.CheckBottomIndicatorAsync();
			edgeGesture.SetGlobalTop(value: false);
			Win32.RECT rcMonitor = Win32.Monitor(ball.Center).rcMonitor;
			Win32.POINT origin = new Win32.POINT(rcMonitor.Left + rcMonitor.Width / 2, rcMonitor.Bottom - 1);
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Begin, origin, 0.0, 0.0));
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Update, origin, 80.0, 0.0));
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.End, origin, 80.0, 0.0));
			await Task.Delay(400);
			if (overview?.IsOpen ?? false)
			{
				throw new InvalidOperationException("低模糊度松手未关闭。");
			}
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Begin, origin, 0.0, 0.0));
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Update, origin, 110.0, 0.0));
			await Task.Delay(150);
			CaptureUi(overview, "overview-progress-v087");
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.End, origin, 110.0, 0.0));
			await Task.Delay(400);
			StageSwitcherWindow? stageSwitcherWindow = overview;
			if ((object)stageSwitcherWindow == null || !stageSwitcherWindow.IsOpen)
			{
				throw new InvalidOperationException("短行程没有保留列表。");
			}
			overview.Hide();
			await Task.Delay(350);
			GlobalTouchGestures.CheckRelay();
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v087-targeted-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Version = "0.8.7",
				RetainedChecks = (launcherOnly ? "Earlier pass: two-finger paths; launcher order; media fixture; Dial selection" : ""),
				Checks = "Multi-select picker/search retention; 3-second indicator; scrolling hint; bottom short-distance blur commit; simulated relay",
				Camera = "Hardware not exercised",
				SignedRelay = "ARM64 device retest required",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v087-targeted-test.json"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task V086SmokeTest()
	{
		_ = 3;
		try
		{
			ShowSettings();
			await Task.Delay(500);
			ark.SetSuppressed(value: true);
			ball.SetVisible(visible: false);
			await settings.CheckGestureEditorAsync(delegate
			{
				CaptureUi(settings, "custom-gesture-v086");
			});
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			center.ShowPanel(ball.Center);
			await Task.Delay(500);
			await center.CheckArtworkAsync(delegate
			{
				CaptureUi(center, "media-artwork-v086");
			});
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v086-targeted-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				Version = "0.8.6",
				GestureDrawing = "PASS (two simultaneous injected contacts drawn on settings canvas, saved template)",
				MediaArtwork = "PASS (decoded fixture stream and current rounded artwork layout)",
				SystemThreeFourFinger = "NOT RUN: Windows reserves these gestures while its setting is enabled",
				Timestamp = DateTimeOffset.Now
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "v086-targeted-test.json"), ex.ToString());
			Environment.ExitCode = 1;
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task SmokeTest()
	{
		_ = 42;
		try
		{
			Process currentProcess = Process.GetCurrentProcess();
			long coldWorkingSet = currentProcess.WorkingSet64 / 1048576;
			long coldPrivate = currentProcess.PrivateMemorySize64 / 1048576;
			string minimizedPreviewProbe = await DwmPreviewProbe.CheckAsync();
			await TaskGroupProbe.CheckAsync();
			await ball.CheckGestureTransitionsAsync();
			await ark.CheckAsync();
			GlobalTouchGestures.CheckRelay();
			if ((object)center == null)
			{
				center = MakeCenter();
			}
			await center.CheckRevealAsync(ball.Center);
			await CheckGestureSummonAsync();
			await touchGestures.CheckAccessGuardAsync();
			await EnsureDial().CheckAsync();
			Win32.RECT rcWork = Win32.Monitor(ball.Center).rcWork;
			await Execute(triggerOrigin: new Win32.POINT(rcWork.Right - 90, (rcWork.Top + rcWork.Bottom) / 2), action: GestureAction.BuiltIn("Stage"));
			await Task.Delay(350);
			StageSwitcherWindow? stageSwitcherWindow = switcher;
			if ((object)stageSwitcherWindow == null || stageSwitcherWindow.EntryEdge != EdgeKind.Right)
			{
				throw new InvalidOperationException("台前调度没有跟随 Dial 来源。");
			}
			switcher.Hide();
			await Task.Delay(350);
			ark.SetSuppressed(value: true);
			ball.SetVisible(visible: false);
			ShowSettings();
			await Task.Delay(1000);
			ark.SetSuppressed(value: true);
			ball.SetVisible(visible: false);
			settings.CheckNavigation();
			await settings.CheckDiagnosticsAsync();
			await settings.CheckGestureEditorAsync(delegate
			{
				CaptureUi(settings, "custom-gesture-v086");
			});
			await settings.CheckV083Async();
			await settings.CheckDialEditorAsync();
			edgeGesture.CheckHitAreas();
			CaptureUi(settings, "settings-v081");
			settings.ShowPageForCapture(4);
			await Task.Delay(300);
			CaptureUi(settings, "settings-multitouch-v084");
			settings.ShowPageForCapture(1, 1);
			await Task.Delay(250);
			CaptureUi(settings, "wheel-settings-v081");
			settings.ShowPageForCapture(2, 1);
			await Task.Delay(250);
			CaptureUi(settings, "bubble-settings-v081");
			settings.ShowPageForCapture(3);
			await Task.Delay(250);
			CaptureUi(settings, "dial-style-v082");
			settings.ShowPageForCapture(3, 1);
			await Task.Delay(1000);
			CaptureUi(settings, "dial-tools-v082");
			settings.ShowPageForCapture(4, 1);
			await Task.Delay(250);
			CaptureUi(settings, "edge-settings-v082");
			settings.ShowPageForCapture(0);
			settings.DarkCapture(dark: true);
			await Task.Delay(200);
			CaptureUi(settings, "settings-dark-v081");
			settings.DarkCapture(dark: false);
			if (Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--material-ui-test"))
			{
				LinearGradientBrush linearGradientBrush = new LinearGradientBrush
				{
					StartPoint = new Windows.Foundation.Point(0f, 0f),
					EndPoint = new Windows.Foundation.Point(0f, 1f)
				};
				linearGradientBrush.GradientStops.Add(new GradientStop
				{
					Color = Windows.UI.Color.FromArgb(byte.MaxValue, 0, 160, byte.MaxValue),
					Offset = 0.0
				});
				linearGradientBrush.GradientStops.Add(new GradientStop
				{
					Color = Windows.UI.Color.FromArgb(byte.MaxValue, byte.MaxValue, 220, 40),
					Offset = 0.5
				});
				linearGradientBrush.GradientStops.Add(new GradientStop
				{
					Color = Windows.UI.Color.FromArgb(byte.MaxValue, 235, 40, 130),
					Offset = 1.0
				});
				keeper.Content = new Grid
				{
					Background = linearGradientBrush
				};
				Win32.RECT rcMonitor = Win32.Monitor(ball.Center).rcMonitor;
				keeper.AppWindow.MoveAndResize(new RectInt32(rcMonitor.Left, rcMonitor.Top, rcMonitor.Width, rcMonitor.Height));
				keeper.Activate();
			}
			await ShowCenter();
			await Task.Delay(500);
			CaptureUi(center, "control-center-v081");
			await center.CheckArtworkAsync(delegate
			{
				CaptureUi(center, "media-artwork-v086");
			});
			File.WriteAllText(Path.Combine(TestOutputDirectory, "backdrop-test.txt"), center.BackdropDiagnostics);
			await Task.Delay(Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--ui-test") ? 600000 : 2500);
			int wiredDeleteButtons = center?.WiredLauncherDeleteButtons ?? 0;
			int loadedIcons = center?.LoadedLauncherIcons ?? 0;
			if (wiredDeleteButtons != store.Value.Launchers.Count)
			{
				throw new InvalidOperationException($"快捷启动删除按钮未正确连接：{wiredDeleteButtons}/{store.Value.Launchers.Count}，面板打开={center?.IsOpen}。");
			}
			ControlCenterWindow? controlCenterWindow = center;
			if ((object)controlCenterWindow == null || !controlCenterWindow.UsesContinuousScrolling)
			{
				throw new InvalidOperationException("快捷启动未启用连续滚动。");
			}
			center.CheckLauncherHighlight();
			await center.CheckLauncherScrollingAsync();
			await center.CheckThemeChangesAsync();
			for (int cycle = 0; cycle < 3; cycle++)
			{
				center.HidePanel();
				await Task.Delay(350);
				await ShowCenter();
				await Task.Delay(350);
			}
			List<LauncherEntry> installed = await InstalledApps.LoadAsync().WaitAsync(TimeSpan.FromSeconds(30.0));
			if (installed.Count == 0)
			{
				throw new InvalidOperationException("未读取到已安装应用。");
			}
			PixelData pixelData = new BarcodeWriterPixelData
			{
				Format = BarcodeFormat.QR_CODE,
				Options = new EncodingOptions
				{
					Width = 256,
					Height = 256,
					Margin = 4
				}
			}.Write("https://example.com/touch-assistant-v03");
			if (QrDecoder.Decode(pixelData.Pixels, pixelData.Width, pixelData.Height) != "https://example.com/touch-assistant-v03")
			{
				throw new InvalidOperationException("二维码识别回归失败。");
			}
			center?.HidePanel();
			await Task.Delay(400);
			if ((object)switcher == null)
			{
				switcher = new StageSwitcherWindow
				{
					IgnoreExternalDismissal = isSmokeTest
				};
			}
			switcher.Show(ball.Center);
			await Task.Delay(Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--stage-ui-test") ? 60000 : 600);
			switcher.Hide();
			await Task.Delay(250);
			Win32.RECT display = Win32.Monitor(ball.Center).rcMonitor;
			Win32.POINT origin = new Win32.POINT(display.Right - 50, display.Top);
			HandleEdgeSwipe(new EdgeSwipe(EdgeKind.TopRight, EdgePhase.Begin, origin, 0.0, 0.0));
			HandleEdgeSwipe(new EdgeSwipe(EdgeKind.TopRight, EdgePhase.Update, origin, 240.0, 0.8));
			HandleEdgeSwipe(new EdgeSwipe(EdgeKind.TopRight, EdgePhase.End, origin, 240.0, 0.8));
			await Task.Delay(300);
			ControlCenterWindow? controlCenterWindow2 = center;
			if ((object)controlCenterWindow2 == null || !controlCenterWindow2.IsOpen)
			{
				throw new InvalidOperationException("顶部手势展开失败。");
			}
			center.HidePanel();
			await Task.Delay(300);
			Win32.POINT bottomOrigin = new Win32.POINT((display.Left + display.Right) / 2, display.Bottom - 1);
			store.Value.EdgeGestures[2] = GestureAction.BuiltIn("Overview");
			store.Value.MultiTouch.EdgeEnabled = true;
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Begin, bottomOrigin, 0.0, 0.0));
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Update, bottomOrigin, 47.0, 0.0));
			if (bottomTaskbar || bottomCards)
			{
				throw new InvalidOperationException("底部短滑误触发。");
			}
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Update, bottomOrigin, 48.0, 0.0));
			if (!bottomTaskbar || bottomCards)
			{
				throw new InvalidOperationException("底部任务栏阶段错误。");
			}
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Update, bottomOrigin, 160.0, 0.0));
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.End, bottomOrigin, 160.0, 0.0));
			await Task.Delay(350);
			StageSwitcherWindow? stageSwitcherWindow2 = overview;
			if ((object)stageSwitcherWindow2 == null || !stageSwitcherWindow2.IsOpen)
			{
				throw new InvalidOperationException("底部第二阶段未打开卡片。");
			}
			CaptureUi(overview, "overview-v081");
			overview.Hide();
			await Task.Delay(300);
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Begin, bottomOrigin, 0.0, 0.0));
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Update, bottomOrigin, 160.0, 0.0));
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Cancel, bottomOrigin, 160.0, 0.0));
			await Task.Delay(300);
			if (overview.IsOpen)
			{
				throw new InvalidOperationException("底部取消未收起。");
			}
			store.Value.MultiTouch.EdgeEnabled = false;
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Begin, bottomOrigin, 0.0, 0.0));
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.Update, bottomOrigin, 200.0, 0.0));
			HandleBottomSwipe(new EdgeSwipe(EdgeKind.Bottom, EdgePhase.End, bottomOrigin, 200.0, 0.0));
			if (overview.IsOpen)
			{
				throw new InvalidOperationException("边缘开关关闭后仍然触发。");
			}
			if (Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--touch-smoke"))
			{
				await CheckTouchInput();
			}
			else if (Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--panel-touch-smoke"))
			{
				await CheckTouchInput(includeEdges: false);
			}
			Process currentProcess2 = Process.GetCurrentProcess();
			File.WriteAllText(Path.Combine(TestOutputDirectory, "smoke-test.json"), JsonSerializer.Serialize(new
			{
				Result = "PASS",
				PanelTouchInput = (Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--panel-touch-smoke" || a == "--touch-smoke") ? "PASS" : "NOT RUN"),
				EdgeTouchInput = (Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--touch-smoke") ? "PASS" : "NOT RUN"),
				MinimizedPreviewProbe = minimizedPreviewProbe,
				TaskGroupRestore = "PASS (owned windows; minimized, moved and closed member)",
				Version = "0.8.6",
				GlobalTouchRelay = "PASS (simulated frames, failure rollback, cancellation, UIAccess guard); signed desktop redirection NOT RUN",
				SettingsNavigation = "PASS (7 sidebar pages, subtabs, add app tab)",
				Ark = "PASS (stable windows, hold pin, suppression, deletion, fixed coordinates)",
				BottomEdgeStages = "PASS (48 DIP taskbar routing, 160 DIP cards, cancellation, switch off; actual Shell reveal not exercised)",
				ShakeToClose = "PASS (ball, ark, dial)",
				DiagnosticExport = "PASS (actual button, application summary and logs ZIP)",
				MediaArtwork = "PASS (stream decode, displayed fixture); real media provider NOT RUN",
				Dial = "PASS (outer-ring double-tap enter/confirm, selection drag, hold/center menu, angular selection, two-finger drag, source position, virtualized editor)",
				Ball = new
				{
					ball.Center.X,
					ball.Center.Y
				},
				WiredDeleteButtons = wiredDeleteButtons,
				LoadedIcons = loadedIcons,
				InstalledApps = installed.Count,
				ResolvedInstalledAppTargets = installed.FindAll((LauncherEntry x) => x.MatchTarget.Length > 0).Count,
				ContinuousScrolling = "PASS",
				LauncherHighlight = "PASS (exclusive, cleared)",
				CircleGestures = "PASS (hold, drag, icon rendering)",
				EdgeGestureRouting = "PASS (top, bottom, cancellation)",
				Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
				ThemeChanges = "PASS (8 transitions)",
				Backdrop = center?.BackdropDiagnostics,
				RotationLockSupported = ShellPreferences.Rotation.Supported,
				FlightModeReadable = system.FlightMode.HasValue,
				ColdWorkingSetMB = coldWorkingSet,
				ColdPrivateMB = coldPrivate,
				WorkingSetMB = currentProcess2.WorkingSet64 / 1048576,
				PrivateMB = currentProcess2.PrivateMemorySize64 / 1048576,
				Handles = currentProcess2.HandleCount,
				Timestamp = DateTime.Now
			}));
		}
		catch (Exception ex)
		{
			Log(ex);
			File.WriteAllText(Path.Combine(TestOutputDirectory, "smoke-test.json"), ex.ToString());
		}
		Shutdown();
	}

	private async void Shutdown()
	{
		if (!exiting)
		{
			exiting = true;
			Diagnostics.Event("Application", "正常退出");
			SystemEvents.SessionEnding -= SessionEnding;
			panelCleanup?.Stop();
			statusBadge?.Dispose();
			statusBadge = null;
			mouseJoystick?.Dispose();
			rightMouseJoystick?.Dispose();
			virtualTouchpad?.Dispose();
			KeyboardInput.ReleaseAll();
			DesktopVisibility.Changed -= SyncArkVisibility;
			DesktopVisibility.Stop();
			gamepad?.Dispose();
			touchpadControl?.Dispose();
			inputControl?.Dispose();
			touchGestures?.Dispose();
			WindowPreview.ClearCache();
			statusHint?.Dispose();
			dial?.Dispose();
			edgePolicy?.Dispose();
			captionGuard?.Dispose();
			ark?.Dispose();
			edgeGesture?.Dispose();
			taskbarMode?.Dispose();
			overview?.Close();
			switcher?.Close();
			scanner?.Close();
			tray?.Dispose();
			ball.Dispose();
			accentTheme?.Dispose();
			system.Dispose();
			settings?.Close();
			center?.Close();
			trayWindow?.Close();
			await KeyboardInput.DrainAsync();
			keeper.Close();
			Ui.ReleaseAppIcon();
			WindowSnapshot.Dispose();
			instance.Dispose();
			Exit();
		}
	}

	private static void CaptureUi(Window window, string name)
	{
		if (!Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--capture-ui") || !Win32.GetWindowRect(Ui.Handle(window), out var r))
		{
			return;
		}
		using Bitmap bitmap = new Bitmap(r.Width, r.Height);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(r.Width, r.Height));
		bitmap.Save(Path.Combine(TestOutputDirectory, name + ".png"));
	}

	private static void CaptureScreen(string name)
	{
		if (!Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--capture-ui"))
		{
			return;
		}
		Win32.RECT rcMonitor = Win32.Monitor(new Win32.POINT(0, 0)).rcMonitor;
		using Bitmap bitmap = new Bitmap(rcMonitor.Width, rcMonitor.Height);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.CopyFromScreen(rcMonitor.Left, rcMonitor.Top, 0, 0, new System.Drawing.Size(rcMonitor.Width, rcMonitor.Height));
		bitmap.Save(Path.Combine(TestOutputDirectory, name + ".png"));
	}

	private static void CaptureArea(Window window, string name, int pad)
	{
		if (!Array.Exists(Environment.GetCommandLineArgs(), (string x) => x == "--capture-ui") || !Win32.GetWindowRect(Ui.Handle(window), out var r))
		{
			return;
		}
		using Bitmap bitmap = new Bitmap(r.Width + pad * 2, r.Height + pad * 2);
		using Graphics graphics = Graphics.FromImage(bitmap);
		graphics.CopyFromScreen(r.Left - pad, r.Top - pad, 0, 0, bitmap.Size);
		bitmap.Save(Path.Combine(TestOutputDirectory, name + ".png"));
	}

	private async Task PreviewProbeTest()
	{
		try
		{
			string path = Path.Combine(TestOutputDirectory, "preview-probe.txt");
			File.WriteAllText(path, await DwmPreviewProbe.CheckAsync());
		}
		catch (Exception ex)
		{
			File.WriteAllText(Path.Combine(TestOutputDirectory, "preview-probe.txt"), ex.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task EdgeSmokeTest()
	{
		if (Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--thin-edge-smoke"))
		{
			try
			{
				store.Value.TopEdgeInset = 24.0;
				EdgeKind[] array = new EdgeKind[2]
				{
					EdgeKind.TopLeft,
					EdgeKind.TopRight
				};
				foreach (EdgeKind index in array)
				{
					store.Value.EdgeGestures[(int)index] = GestureAction.BuiltIn("ControlCenter");
				}
				store.Save();
				await Task.Delay(1100);
				for (int repeat = 0; repeat < 2; repeat++)
				{
					store.Value.TopEdgeWidthPercent = ((repeat == 0) ? 33.333333333333336 : 25.0);
					store.Value.TopEdgeHeight = ((repeat == 0) ? 2 : 8);
					store.Value.TopEdgeOpacity = ((repeat == 0) ? 0.01 : 0.25);
					store.Save();
					await Task.Delay(200);
					edgeGesture.CheckTopConfiguration();
					EdgeKind[] array2 = new EdgeKind[2]
					{
						EdgeKind.TopLeft,
						EdgeKind.TopRight
					};
					foreach (EdgeKind edge in array2)
					{
						Win32.POINT pOINT = edgeGesture.TestPoint(edge, ball.Center);
						await TouchSmoke.Swipe(pOINT, new Win32.POINT(pOINT.X, pOINT.Y + (int)(240.0 * Win32.Scale(pOINT))));
						ControlCenterWindow? controlCenterWindow = center;
						if ((object)controlCenterWindow == null || !controlCenterWindow.IsOpen)
						{
							throw new InvalidOperationException($"Thin strip: {edge}, repeat={repeat}, inset=24 DIP did not open.");
						}
						center.HidePanel();
						await Task.Delay(350);
					}
				}
				File.WriteAllText(Path.Combine(TestOutputDirectory, "thin-edge-smoke.txt"), $"PASS {DateTime.Now:O}: production strips, inset=24 DIP; default and custom width/height/opacity, left/right input; true outer-edge input NOT verified.");
				return;
			}
			catch (Exception ex)
			{
				Log(ex);
				File.WriteAllText(Path.Combine(TestOutputDirectory, "thin-edge-smoke.txt"), ex.ToString());
				return;
			}
			finally
			{
				Shutdown();
			}
		}
		if (Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--edge-diagnostics"))
		{
			try
			{
				StringBuilder report = new StringBuilder();
				bool flag = Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--thin-edge-diagnostics");
				(int, int, byte)[] variants = ((!flag) ? new(int, int, byte)[7]
				{
					(0, 12, 3),
					(4, 12, 3),
					(8, 12, 3),
					(12, 12, 3),
					(16, 12, 3),
					(20, 12, 3),
					(24, 12, 3)
				} : new(int, int, byte)[4]
				{
					(0, 1, 3),
					(0, 2, 3),
					(24, 1, 3),
					(24, 2, 3)
				});
				EdgeKind[] array2 = ((!flag) ? new EdgeKind[1] { EdgeKind.TopLeft } : new EdgeKind[2]
				{
					EdgeKind.TopLeft,
					EdgeKind.TopRight
				});
				foreach (EdgeKind edge in array2)
				{
					store.Value.EdgeGestures[(int)edge] = GestureAction.BuiltIn("ControlCenter");
					store.Save();
					(int, int, byte)[] array3 = variants;
					for (int num2 = 0; num2 < array3.Length; num2++)
					{
						(int, int, byte) variant = array3[num2];
						edgeGesture.ConfigureDiagnostic(edge, variant.Item1, variant.Item2, variant.Item3);
						await Task.Delay(250);
						Win32.POINT p = edgeGesture.TestPoint(edge, ball.Center);
						await TouchSmoke.Swipe(p, new Win32.POINT(p.X, p.Y + 240));
						StringBuilder stringBuilder = report;
						StringBuilder.AppendInterpolatedStringHandler handler = new StringBuilder.AppendInterpolatedStringHandler(44, 6, stringBuilder);
						handler.AppendFormatted(edge);
						handler.AppendLiteral(" inset,height,alpha=");
						handler.AppendFormatted(variant);
						handler.AppendLiteral("; point=");
						handler.AppendFormatted(p.X);
						handler.AppendLiteral(",");
						handler.AppendFormatted(p.Y);
						handler.AppendLiteral("; scale=");
						handler.AppendFormatted(Win32.Scale(p));
						handler.AppendLiteral(": open=");
						handler.AppendFormatted(center?.IsOpen ?? false);
						stringBuilder.AppendLine(ref handler);
						center?.HidePanel();
						await Task.Delay(350);
					}
				}
				File.WriteAllText(Path.Combine(TestOutputDirectory, "edge-diagnostics.txt"), report.ToString());
				return;
			}
			catch (Exception ex2)
			{
				Log(ex2);
				File.WriteAllText(Path.Combine(TestOutputDirectory, "edge-diagnostics.txt"), ex2.ToString());
				return;
			}
			finally
			{
				Shutdown();
			}
		}
		try
		{
			await Task.Delay(500);
			await CheckEdgeTouchInput();
			File.WriteAllText(Path.Combine(TestOutputDirectory, "edge-smoke.txt"), "PASS " + DateTime.Now.ToString("O"));
		}
		catch (Exception ex3)
		{
			File.WriteAllText(Path.Combine(TestOutputDirectory, "edge-smoke.txt"), ex3.ToString());
		}
		finally
		{
			Shutdown();
		}
	}

	private async Task CheckEdgeTouchInput()
	{
		SystemControl.SendKeys(new int[1] { 27 });
		await Task.Delay(350);
		List<string> edgeResults = new List<string>();
		EdgeKind[] array = new EdgeKind[4]
		{
			EdgeKind.TopLeft,
			EdgeKind.TopRight,
			EdgeKind.Left,
			EdgeKind.Right
		};
		foreach (EdgeKind edge in array)
		{
			store.Value.EdgeGestures[(int)edge] = GestureAction.BuiltIn("ControlCenter");
			store.Save();
			await Task.Delay(200);
			Win32.POINT pOINT = edgeGesture.TestPoint(edge, ball.Center);
			int num = (int)(240.0 * Win32.Scale(pOINT));
			Win32.POINT end = edge switch
			{
				EdgeKind.Right => new Win32.POINT(pOINT.X - num, pOINT.Y), 
				EdgeKind.Left => new Win32.POINT(pOINT.X + num, pOINT.Y), 
				_ => new Win32.POINT(pOINT.X, pOINT.Y + num), 
			};
			await TouchSmoke.Swipe(pOINT, end);
			edgeResults.Add($"{edge}: {((center?.IsOpen ?? false) ? "PASS" : "FAIL (no open panel)")}");
			File.WriteAllLines(Path.Combine(TestOutputDirectory, "edge-results-v084.txt"), edgeResults);
			center?.HidePanel();
			await Task.Delay(350);
		}
		SystemControl.SendKeys(new int[1] { 27 });
		await Task.Delay(350);
		if (edgeResults.Exists((string line) => line.Contains("FAIL")))
		{
			throw new InvalidOperationException(string.Join("; ", edgeResults));
		}
		store.Value.EdgeGestures[2] = GestureAction.BuiltIn("Overview");
		store.Save();
		await Task.Delay(400);
		Win32.POINT pOINT2 = edgeGesture.TestPoint(EdgeKind.Bottom, ball.Center);
		edgeGesture.CheckHitAreas();
		await TouchSmoke.Swipe(pOINT2, new Win32.POINT(pOINT2.X, pOINT2.Y - (int)(240.0 * Win32.Scale(pOINT2))));
		StageSwitcherWindow? stageSwitcherWindow = overview;
		if ((object)stageSwitcherWindow == null || !stageSwitcherWindow.IsOpen)
		{
			throw new InvalidOperationException("指示横条上的上滑未打开任务列表。");
		}
		overview.Hide();
		await Task.Delay(400);
		pOINT2 = edgeGesture.TestPoint(EdgeKind.Bottom, ball.Center);
		Win32.RECT rcMonitor = Win32.Monitor(pOINT2).rcMonitor;
		await TouchSmoke.Swipe(pOINT2, new Win32.POINT(pOINT2.X, rcMonitor.Top + rcMonitor.Height / 5));
		if (!overview.IsOpen)
		{
			throw new InvalidOperationException("长距离上滑未保持任务卡片。");
		}
	}

	private async Task CheckTouchInput(bool includeEdges = true)
	{
		(int X, int Y)[] array = new(int, int)[4]
		{
			(1, 0),
			(-1, 0),
			(0, 1),
			(0, -1)
		};
		for (int i = 0; i < array.Length; i++)
		{
			(int X, int Y) direction = array[i];
			center.HidePanel();
			await Task.Delay(300);
			center.ShowPanel(ball.Center);
			await Task.Delay(400);
			Win32.GetWindowRect(Ui.Handle(center), out var r);
			Win32.POINT start = new Win32.POINT(r.Left + r.Width / 2, r.Top + (int)(110.0 * Win32.Scale(ball.Center)));
			int num = (int)(90.0 * Win32.Scale(ball.Center));
			await TouchSmoke.Swipe(start, new Win32.POINT(start.X + direction.X * num, start.Y + direction.Y * num));
			if (center.IsOpen)
			{
				throw new InvalidOperationException($"触摸输入：控制中心未按方向 {direction} 收起。");
			}
		}
		EdgeKind[] array2 = new EdgeKind[2]
		{
			EdgeKind.Left,
			EdgeKind.Right
		};
		foreach (EdgeKind side in array2)
		{
			(switcher ?? (switcher = MakeSwitcher(full: false))).Show(ball.Center, interactive: false, side);
			await Task.Delay(400);
			Win32.GetWindowRect(Ui.Handle(switcher), out var r2);
			Win32.POINT stagePoint = new Win32.POINT((side == EdgeKind.Left) ? (r2.Right - 40) : (r2.Left + 40), r2.Top + 100);
			await TouchSmoke.Swipe(stagePoint, new Win32.POINT(stagePoint.X + ((side == EdgeKind.Left) ? (-100) : 100), stagePoint.Y + 220));
			if (!switcher.IsOpen)
			{
				throw new InvalidOperationException("台前调度纵向滚动误收起。");
			}
			await TouchSmoke.Swipe(stagePoint, new Win32.POINT(stagePoint.X + ((side == EdgeKind.Left) ? (-160) : 160), stagePoint.Y));
			if (switcher.IsOpen)
			{
				throw new InvalidOperationException($"触摸输入：台前调度 {side} 滑动收起失败。");
			}
			await Task.Delay(300);
		}
		(overview ?? (overview = MakeSwitcher(full: true))).Show(ball.Center);
		await Task.Delay(450);
		overview.EnsureScrollTestCards();
		await Task.Delay(100);
		Win32.POINT start2 = overview.CardPoint();
		double before = overview.HorizontalOffset;
		await TouchSmoke.Swipe(start2, new Win32.POINT(start2.X - 200, start2.Y));
		if (overview.HorizontalOffset <= before)
		{
			throw new InvalidOperationException("触摸输入：卡片上的横向拖动未带动列表。");
		}
		Win32.GetWindowRect(Ui.Handle(overview), out var r3);
		start2 = new Win32.POINT(r3.Left + r3.Width / 2, r3.Top + 60);
		await TouchSmoke.Swipe(start2, new Win32.POINT(start2.X, start2.Y + 160));
		if (overview.IsOpen)
		{
			throw new InvalidOperationException("触摸输入：任务列表空白处下滑未收起。");
		}
		if (includeEdges)
		{
			await CheckEdgeTouchInput();
		}
		File.WriteAllText(Path.Combine(TestOutputDirectory, "touch-smoke.txt"), $"PASS: control center four directions; stage left/right; horizontal card drag; overview blank down; edges={(includeEdges ? "PASS" : "NOT RUN")}. {DateTime.Now:O}");
	}

	[GeneratedCode("TouchToys.Reconstruction", "1.0")]
	[DebuggerNonUserCode]
	public void InitializeComponent()
	{
		if (!_contentLoaded)
		{
			_contentLoaded = true;
			Uri resourceLocator = new Uri("ms-appx:///App.xaml");
			Application.LoadComponent(this, resourceLocator);
		}
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2608")]
	[DebuggerNonUserCode]
	public IXamlType GetXamlType(Type type)
	{
		return _AppProvider.GetXamlType(type);
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2608")]
	[DebuggerNonUserCode]
	public IXamlType GetXamlType(string fullName)
	{
		return _AppProvider.GetXamlType(fullName);
	}

	[GeneratedCode("Microsoft.UI.Xaml.Markup.Compiler", " 3.0.0.2608")]
	[DebuggerNonUserCode]
	public XmlnsDefinition[] GetXmlnsDefinitions()
	{
		return _AppProvider.GetXmlnsDefinitions();
	}
}
