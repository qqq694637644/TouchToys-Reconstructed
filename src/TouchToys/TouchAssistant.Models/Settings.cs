using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TouchAssistant.Models;

public sealed class Settings
{
	public string Language { get; set; } = "system";

	public GamepadSettings Gamepad { get; set; } = new GamepadSettings();

	public VirtualTouchpadSettings VirtualTouchpad { get; set; } = new VirtualTouchpadSettings();

	public MouseJoystickSettings MouseJoysticks { get; set; } = new MouseJoystickSettings();

	public int TouchKeyboardOpacity { get; set; } = 100;

	public int TouchpadOpacity { get; set; } = 100;

	public bool AutoTouchpadOpacity { get; set; } = true;

	public bool AutoNativeInputOpacity { get; set; } = true;

	public string NativeInputOpacityTarget { get; set; } = "";

	public string TouchpadOpacityTarget { get; set; } = "";

	public MultiTouchSettings MultiTouch { get; set; } = new MultiTouchSettings
	{
		EdgeEnabled = true
	};

	public DialSettings Dial { get; set; } = new DialSettings();

	public List<DialAppProfile> DialApps { get; set; } = new List<DialAppProfile>();

	public List<BottomAppProfile> BottomApps { get; set; } = new List<BottomAppProfile>();

	public List<GestureAppProfile> GestureApps { get; set; } = new List<GestureAppProfile>();

	public bool BallShakeToClose { get; set; } = true;

	public bool ArkShakeToClose { get; set; } = true;

	public bool BallEnabled { get; set; } = true;

	public bool WheelCenterHint { get; set; } = true;

	public bool WheelCancelOutside { get; set; }

	public double WheelCancelRadius { get; set; } = 2.0;

	public bool UseSystemAccent { get; set; } = true;

	public string AccentColor { get; set; } = "#0078D4";

	public bool ArkEnabled { get; set; }

	public double ArkSize { get; set; } = 58.0;

	public double ArkOpacity { get; set; } = 1.0;

	public double ArkIdleOpacity { get; set; } = 0.2;

	public double ArkIdleSeconds { get; set; } = 2.0;

	public double ArkHoldSeconds { get; set; } = 0.2;

	public bool ArkMoveOutline { get; set; } = true;

	public bool ArkMouseEnabled { get; set; } = true;

	public bool ArkFollow { get; set; } = true;

	public int? ArkX { get; set; }

	public int? ArkY { get; set; }

	public List<ArkProfile> ArkProfiles { get; set; } = new List<ArkProfile>
	{
		new ArkProfile()
	};

	public int SchemaVersion { get; set; }

	public double Opacity { get; set; } = 1.0;

	public double IdleSeconds { get; set; } = 2.0;

	public double IdleOpacity { get; set; } = 0.2;

	public double HoldSeconds { get; set; } = 0.2;

	public bool MoveOutline { get; set; } = true;

	public double LauncherSpacing { get; set; } = 4.0;

	public double ControlCenterScale { get; set; } = 1.0;

	public int ControlCenterOpenCount { get; set; }

	public bool LauncherHintDismissed { get; set; }

	public bool MouseEnabled { get; set; } = true;

	public bool SingleTapMode { get; set; }

	public double BallSize { get; set; } = 58.0;

	public bool EdgeSwitcherEnabled { get; set; } = true;

	public bool TopEdgeEnabled { get; set; } = true;

	public bool TopEdgeHintShown { get; set; }

	public bool ShowSystemStatusBadge { get; set; } = true;

	public double TopEdgeInset { get; set; } = 24.0;

	public double TopEdgeWidthPercent { get; set; } = 33.333333333333336;

	public double TopEdgeHeight { get; set; } = 2.0;

	public double TopEdgeOpacity { get; set; } = 0.01;

	public bool BottomGesturesEnabled { get; set; }

	public bool CloseGuardEnabled { get; set; }

	public bool OverviewHintDismissed { get; set; }

	public int? BallX { get; set; }

	public int? BallY { get; set; }

	public List<GestureAction> Gestures { get; set; } = Defaults();

	public bool WheelMask { get; set; }

	public bool WheelMaskClock { get; set; } = true;

	public List<GestureAction> EdgeGestures { get; set; } = EdgeDefaults();

	public Dictionary<string, GestureAction> IndicatorActions { get; set; } = IndicatorDefaults();

	public List<LauncherEntry> Launchers { get; set; } = new List<LauncherEntry>();

	public static Dictionary<string, GestureAction> IndicatorDefaults()
	{
		return new Dictionary<string, GestureAction>
		{
			["Left"] = GestureAction.BuiltIn("NextApp"),
			["Right"] = GestureAction.BuiltIn("PreviousApp"),
			["Tap"] = GestureAction.BuiltIn("StartMenu"),
			["DoubleTap"] = GestureAction.BuiltIn("Desktop"),
			["Down"] = new GestureAction()
		};
	}

	public static List<GestureAction> Defaults()
	{
		return new List<GestureAction>
		{
			GestureAction.BuiltIn("TaskView"),
			GestureAction.BuiltIn("ControlCenter"),
			GestureAction.BuiltIn("NextApp"),
			GestureAction.BuiltIn("AppSettings"),
			GestureAction.BuiltIn("Desktop"),
			GestureAction.BuiltIn("ArkToggle"),
			GestureAction.BuiltIn("PreviousApp"),
			GestureAction.BuiltIn("VirtualDial"),
			GestureAction.BuiltIn("ControlCenter"),
			new GestureAction(),
			new GestureAction(),
			GestureAction.BuiltIn("Move"),
			GestureAction.BuiltIn("Wheel")
		};
	}

	public static List<GestureAction> EdgeDefaults()
	{
		return new List<GestureAction>
		{
			new GestureAction
			{
				Kind = ActionKind.BuiltIn,
				Command = "Stage",
				Enabled = false
			},
			GestureAction.BuiltIn("ControlCenter"),
			GestureAction.BuiltIn("Overview"),
			new GestureAction
			{
				Kind = ActionKind.BuiltIn,
				Command = "NotificationCenter",
				Enabled = false
			},
			new GestureAction()
		};
	}

	public void Normalize()
	{
		ControlCenterScale = (double.IsFinite(ControlCenterScale) ? Math.Clamp(ControlCenterScale, 0.5, 3.0) : 1.0);
		VirtualTouchpadSettings virtualTouchpadSettings;
		if (VirtualTouchpad == null)
		{
			virtualTouchpadSettings = (VirtualTouchpad = new VirtualTouchpadSettings());
		}
		virtualTouchpadSettings = VirtualTouchpad;
		if (virtualTouchpadSettings.MiddleActions == null)
		{
			Dictionary<string, GestureAction> dictionary = (virtualTouchpadSettings.MiddleActions = VirtualTouchpadSettings.DefaultMiddleActions());
		}
		foreach (KeyValuePair<string, GestureAction> item in VirtualTouchpadSettings.DefaultMiddleActions())
		{
			if (!VirtualTouchpad.MiddleActions.TryGetValue(item.Key, out GestureAction value) || value == null)
			{
				value = (VirtualTouchpad.MiddleActions[item.Key] = item.Value);
			}
			NormalizeAction(value);
		}
		VirtualTouchpad.GestureHoldSeconds = Clamp(VirtualTouchpad.GestureHoldSeconds, 0.1, 3.0, 0.5);
		virtualTouchpadSettings = VirtualTouchpad;
		if (virtualTouchpadSettings.Gestures == null)
		{
			List<MultiTouchBinding> list = (virtualTouchpadSettings.Gestures = VirtualTouchpadSettings.DefaultGestures());
		}
		foreach (MultiTouchBinding zoom in VirtualTouchpadSettings.DefaultGestures())
		{
			if (!VirtualTouchpad.Gestures.Any((MultiTouchBinding b) => b != null && b.Fingers == 2 && b.Gesture == zoom.Gesture))
			{
				VirtualTouchpad.Gestures.Add(zoom);
			}
		}
		VirtualTouchpad.Gestures.RemoveAll(delegate(MultiTouchBinding b)
		{
			bool flag5 = b == null || b.Fingers < 2 || b.Fingers > 5 || b.Gesture == "Custom";
			if (!flag5)
			{
				bool flag6 = MultiTouchSettings.Allowed(b.Fingers, b.Gesture);
				if (!flag6)
				{
					bool flag7 = b.Fingers == 2;
					if (flag7)
					{
						string gesture = b.Gesture;
						bool flag8 = ((gesture == "PinchIn" || gesture == "PinchOut") ? true : false);
						flag7 = flag8;
					}
					flag6 = flag7;
				}
				flag5 = !flag6;
			}
			return flag5;
		});
		HashSet<string> padUnique = new HashSet<string>();
		VirtualTouchpad.Gestures.RemoveAll((MultiTouchBinding b) => !padUnique.Add($"{b.Fingers}:{b.Gesture}"));
		foreach (MultiTouchBinding gesture2 in VirtualTouchpad.Gestures)
		{
			MultiTouchBinding current2;
			MultiTouchBinding multiTouchBinding = (current2 = gesture2);
			if (current2.Action == null)
			{
				GestureAction gestureAction = (current2.Action = new GestureAction());
			}
			NormalizeAction(multiTouchBinding.Action);
		}
		VirtualTouchpad.IdleOpacity = Clamp(VirtualTouchpad.IdleOpacity, 0.0, 1.0, 0.4);
		VirtualTouchpad.RightJoystickSize = Clamp(VirtualTouchpad.RightJoystickSize, 32.0, 160.0, 80.0);
		VirtualTouchpad.RightJoystickX = Clamp(VirtualTouchpad.RightJoystickX, 0.0, 900.0, 90.0);
		VirtualTouchpad.RightJoystickY = Clamp(VirtualTouchpad.RightJoystickY, 0.0, 700.0, 110.0);
		virtualTouchpadSettings = VirtualTouchpad;
		if (virtualTouchpadSettings.RightJoystickTap == null)
		{
			GestureAction gestureAction = (virtualTouchpadSettings.RightJoystickTap = new GestureAction());
		}
		NormalizeAction(VirtualTouchpad.RightJoystickTap);
		VirtualTouchpad.JoystickSize = Clamp(VirtualTouchpad.JoystickSize, 32.0, 160.0, 80.0);
		VirtualTouchpad.JoystickX = Clamp(VirtualTouchpad.JoystickX, 0.0, 900.0, 270.0);
		VirtualTouchpad.JoystickY = Clamp(VirtualTouchpad.JoystickY, 0.0, 700.0, 110.0);
		virtualTouchpadSettings = VirtualTouchpad;
		if (virtualTouchpadSettings.JoystickTap == null)
		{
			GestureAction gestureAction = (virtualTouchpadSettings.JoystickTap = new GestureAction());
		}
		NormalizeAction(VirtualTouchpad.JoystickTap);
		VirtualTouchpad.Width = Clamp(VirtualTouchpad.Width, 180.0, 900.0, 360.0);
		VirtualTouchpad.Height = Clamp(VirtualTouchpad.Height, 140.0, 700.0, 260.0);
		VirtualTouchpad.Opacity = Clamp(VirtualTouchpad.Opacity, 0.0, 1.0, 0.75);
		GamepadSettings gamepadSettings;
		if (Gamepad == null)
		{
			gamepadSettings = (Gamepad = new GamepadSettings());
		}
		gamepadSettings = Gamepad;
		if (gamepadSettings.LayoutSelector == null)
		{
			GamepadSettings gamepadSettings3 = gamepadSettings;
			GamepadControl obj = new GamepadControl
			{
				Name = "选择布局",
				Style = "Rounded",
				Width = 160.0,
				Height = 48.0
			};
			GamepadControl gamepadControl = obj;
			gamepadSettings3.LayoutSelector = obj;
		}
		Gamepad.EnsureLayouts();
		gamepadSettings = Gamepad;
		if (gamepadSettings.Controls == null)
		{
			List<GamepadControl> list3 = (gamepadSettings.Controls = new List<GamepadControl>());
		}
		Gamepad.Controls.RemoveAll((GamepadControl c) => c == null);
		if (Gamepad.Controls.Count > 64)
		{
			Gamepad.Controls.RemoveRange(64, Gamepad.Controls.Count - 64);
		}
		foreach (GamepadControl item2 in Gamepad.Layouts.SelectMany((GamepadLayout l) => l.Controls).Append(Gamepad.LayoutSelector))
		{
			item2.Opacity = Clamp(item2.Opacity, 0.0, 1.0, 1.0);
			item2.IdleOpacity = Clamp(item2.IdleOpacity, 0.0, 1.0, 0.45);
			if (string.IsNullOrWhiteSpace(item2.BackgroundColor) || !Regex.IsMatch(item2.BackgroundColor, "^#[0-9a-fA-F]{6}$"))
			{
				item2.BackgroundColor = "#141414";
			}
			if (string.IsNullOrWhiteSpace(item2.OutlineColor) || !Regex.IsMatch(item2.OutlineColor, "^#[0-9a-fA-F]{6}$"))
			{
				item2.OutlineColor = "#FFFFFF";
			}
			if (string.IsNullOrWhiteSpace(item2.CenterColor) || !Regex.IsMatch(item2.CenterColor, "^#[0-9a-fA-F]{6}$"))
			{
				item2.CenterColor = "#FFFFFF";
			}
			GamepadControl gamepadControl = item2;
			if (gamepadControl.Name == null)
			{
				string text = (gamepadControl.Name = (item2.Joystick ? "摇杆" : "按钮"));
			}
			item2.Size = Clamp(item2.Size, item2.Joystick ? 48 : 20, 400.0, item2.Joystick ? 180 : 72);
			item2.Width = Clamp((item2.Width <= 0.0) ? item2.Size : item2.Width, item2.Joystick ? 48 : 20, 600.0, 72.0);
			item2.Height = Clamp((item2.Height <= 0.0) ? item2.Size : item2.Height, item2.Joystick ? 48 : 20, 400.0, 72.0);
			item2.CornerRadius = Clamp(item2.CornerRadius, 0.0, 200.0, 16.0);
			gamepadControl = item2;
			if (gamepadControl.Icon == null)
			{
				string text = (gamepadControl.Icon = "");
			}
			gamepadControl = item2;
			if (gamepadControl.Text == null)
			{
				string text = (gamepadControl.Text = "");
			}
			gamepadControl = item2;
			bool flag;
			switch (item2.Style)
			{
			case "Circle":
			case "Square":
			case "Rounded":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			gamepadControl.Style = (flag ? item2.Style : "Circle");
			gamepadControl = item2;
			if (gamepadControl.Appearance == null)
			{
				GamepadControl gamepadControl2 = gamepadControl;
				GestureAction obj2 = new GestureAction
				{
					Label = item2.Text,
					Icon = item2.Icon,
					TextOpacity = 0.9
				};
				GestureAction gestureAction = obj2;
				gamepadControl2.Appearance = obj2;
			}
			NormalizeAction(item2.Appearance);
			gamepadControl = item2;
			if (gamepadControl.Action == null)
			{
				GestureAction gestureAction = (gamepadControl.Action = new GestureAction());
			}
			NormalizeAction(item2.Action);
			gamepadControl = item2;
			if (gamepadControl.CenterTap == null)
			{
				GestureAction gestureAction = (gamepadControl.CenterTap = new GestureAction());
			}
			gamepadControl = item2;
			if (gamepadControl.CenterDoubleTap == null)
			{
				GestureAction gestureAction = (gamepadControl.CenterDoubleTap = new GestureAction());
			}
			NormalizeAction(item2.CenterTap);
			NormalizeAction(item2.CenterDoubleTap);
			gamepadControl = item2;
			if (gamepadControl.Directions == null)
			{
				List<GestureAction> list5 = (gamepadControl.Directions = GamepadControl.DefaultDirections());
			}
			while (item2.Directions.Count < 8)
			{
				item2.Directions.Add(new GestureAction());
			}
			if (item2.Directions.Count > 8)
			{
				item2.Directions.RemoveRange(8, item2.Directions.Count - 8);
			}
			for (int num = 0; num < 8; num++)
			{
				List<GestureAction> list5 = item2.Directions;
				int index = num;
				if (list5[index] == null)
				{
					GestureAction gestureAction = (list5[index] = new GestureAction());
				}
				NormalizeAction(item2.Directions[num]);
			}
		}
		if (MultiTouch == null)
		{
			MultiTouchSettings obj3 = new MultiTouchSettings
			{
				EdgeEnabled = true
			};
			MultiTouchSettings multiTouchSettings = obj3;
			MultiTouch = obj3;
		}
		NormalizeMultiTouch(MultiTouch);
		if (GestureApps == null)
		{
			List<GestureAppProfile> list7 = (GestureApps = new List<GestureAppProfile>());
		}
		GestureApps.RemoveAll((GestureAppProfile p) => p == null);
		foreach (GestureAppProfile gestureApp in GestureApps)
		{
			GestureAppProfile current4;
			GestureAppProfile gestureAppProfile = (current4 = gestureApp);
			if (current4.Name == null)
			{
				string text = (current4.Name = "应用");
			}
			current4 = gestureAppProfile;
			if (current4.Target == null)
			{
				string text = (current4.Target = "");
			}
			current4 = gestureAppProfile;
			if (current4.Options == null)
			{
				MultiTouchSettings multiTouchSettings = (current4.Options = new MultiTouchSettings());
			}
			NormalizeMultiTouch(gestureAppProfile.Options);
		}
		MouseJoystickSettings mouseJoystickSettings;
		if (MouseJoysticks == null)
		{
			mouseJoystickSettings = (MouseJoysticks = new MouseJoystickSettings());
		}
		mouseJoystickSettings = MouseJoysticks;
		if (mouseJoystickSettings.Pointer == null)
		{
			MouseJoystickOptions mouseJoystickOptions = (mouseJoystickSettings.Pointer = new MouseJoystickOptions());
		}
		mouseJoystickSettings = MouseJoysticks;
		if (mouseJoystickSettings.RightDrag == null)
		{
			MouseJoystickOptions mouseJoystickOptions = (mouseJoystickSettings.RightDrag = new MouseJoystickOptions());
		}
		if (!MouseJoysticks.Migrated)
		{
			MouseJoysticks.Pointer.Enabled = VirtualTouchpad.Enabled && VirtualTouchpad.JoystickEnabled;
			MouseJoysticks.RightDrag.Enabled = VirtualTouchpad.Enabled && VirtualTouchpad.RightJoystickEnabled;
			MouseJoysticks.Pointer.Size = VirtualTouchpad.JoystickSize;
			MouseJoysticks.RightDrag.Size = VirtualTouchpad.RightJoystickSize;
			MouseJoysticks.Pointer.Tap = VirtualTouchpad.JoystickTap;
			MouseJoysticks.RightDrag.Tap = VirtualTouchpad.RightJoystickTap;
			MouseJoysticks.Migrated = true;
		}
		MouseJoystickOptions[] array = new MouseJoystickOptions[2] { MouseJoysticks.Pointer, MouseJoysticks.RightDrag };
		foreach (MouseJoystickOptions obj4 in array)
		{
			obj4.Size = Clamp(obj4.Size, 32.0, 160.0, 80.0);
			obj4.Opacity = Clamp(obj4.Opacity, 0.0, 1.0, 0.75);
			MouseJoystickOptions mouseJoystickOptions = obj4;
			if (mouseJoystickOptions.Tap == null)
			{
				GestureAction gestureAction = (mouseJoystickOptions.Tap = new GestureAction());
			}
			NormalizeAction(obj4.Tap);
		}
		if (Dial == null)
		{
			DialSettings dialSettings = (Dial = new DialSettings());
		}
		if (SchemaVersion < 10 && Dial.OuterColor == "#5A5A5E" && Dial.InnerColor == "#19191C")
		{
			Dial.OuterColor = "#000000";
			Dial.InnerColor = "#3A3A3A";
		}
		NormalizeDial(Dial);
		if (DialApps == null)
		{
			List<DialAppProfile> list9 = (DialApps = new List<DialAppProfile>());
		}
		DialApps.RemoveAll((DialAppProfile x) => x == null);
		foreach (DialAppProfile dialApp in DialApps)
		{
			DialAppProfile current5;
			DialAppProfile dialAppProfile = (current5 = dialApp);
			if (current5.Name == null)
			{
				string text = (current5.Name = "应用");
			}
			current5 = dialAppProfile;
			if (current5.Target == null)
			{
				string text = (current5.Target = "");
			}
			current5 = dialAppProfile;
			if (current5.Options == null)
			{
				DialSettings dialSettings = (current5.Options = new DialSettings());
			}
			NormalizeDial(dialAppProfile.Options);
		}
		if (BottomApps == null)
		{
			List<BottomAppProfile> list11 = (BottomApps = new List<BottomAppProfile>());
		}
		BottomApps.RemoveAll((BottomAppProfile x) => x == null);
		foreach (BottomAppProfile bottomApp in BottomApps)
		{
			BottomAppProfile bottomAppProfile = bottomApp;
			if (bottomAppProfile.Name == null)
			{
				string text = (bottomAppProfile.Name = "应用");
			}
			bottomAppProfile = bottomApp;
			if (bottomAppProfile.Target == null)
			{
				string text = (bottomAppProfile.Target = "");
			}
			bottomAppProfile = bottomApp;
			if (bottomAppProfile.Bottom == null)
			{
				GestureAction gestureAction = (bottomAppProfile.Bottom = GestureAction.BuiltIn("Overview"));
			}
			NormalizeAction(bottomApp.Bottom);
			bottomAppProfile = bottomApp;
			if (bottomAppProfile.Indicator == null)
			{
				Dictionary<string, GestureAction> dictionary = (bottomAppProfile.Indicator = IndicatorDefaults());
			}
			foreach (KeyValuePair<string, GestureAction> item3 in IndicatorDefaults())
			{
				if (!bottomApp.Indicator.TryGetValue(item3.Key, out GestureAction value3) || value3 == null)
				{
					value3 = (bottomApp.Indicator[item3.Key] = item3.Value);
				}
				NormalizeAction(value3);
			}
		}
		bool flag2 = SchemaVersion < 9;
		Opacity = Clamp(Opacity, 0.0, 1.0, 1.0);
		IdleOpacity = Clamp(IdleOpacity, 0.0, 1.0, 0.2);
		BallSize = Clamp(BallSize, 40.0, 128.0, 58.0);
		if (SchemaVersion < 6)
		{
			List<GestureAction> gestures = Gestures;
			if (gestures != null && gestures.Count == 11 && HoldSeconds == 1.5)
			{
				HoldSeconds = 0.1;
			}
		}
		if (SchemaVersion < 7)
		{
			EdgeGestures = EdgeDefaults();
			if (!EdgeSwitcherEnabled)
			{
				EdgeGestures[0] = new GestureAction();
			}
			if (!TopEdgeEnabled)
			{
				EdgeGestures[1] = new GestureAction();
			}
			if (BottomGesturesEnabled)
			{
				EdgeGestures[2] = GestureAction.BuiltIn("Overview");
			}
		}
		SchemaVersion = 11;
		LauncherSpacing = Clamp(LauncherSpacing, 0.0, 20.0, 4.0);
		WheelCancelRadius = Clamp(WheelCancelRadius, 1.0, 6.0, 2.0);
		TopEdgeInset = Clamp(TopEdgeInset, 0.0, 64.0, 0.0);
		TopEdgeWidthPercent = Clamp(TopEdgeWidthPercent, 1.0, 50.0, 33.333333333333336);
		TopEdgeHeight = Clamp(TopEdgeHeight, 1.0, 64.0, 2.0);
		TopEdgeOpacity = Clamp(TopEdgeOpacity, 0.0, 1.0, 0.01);
		AccentColor = NormalizeColor(AccentColor, "#0078D4");
		ArkSize = Clamp(ArkSize, 40.0, 128.0, 58.0);
		ArkOpacity = Clamp(ArkOpacity, 0.0, 1.0, 1.0);
		ArkIdleOpacity = Clamp(ArkIdleOpacity, 0.0, 1.0, 0.2);
		ArkIdleSeconds = Clamp(ArkIdleSeconds, 0.0, 10.0, 2.0);
		ArkHoldSeconds = Clamp(ArkHoldSeconds, 0.0, 3.0, 0.2);
		if (ArkProfiles == null)
		{
			List<ArkProfile> list13 = (ArkProfiles = new List<ArkProfile>());
		}
		ArkProfiles.RemoveAll((ArkProfile x) => x == null);
		if (ArkProfiles.Count == 0)
		{
			ArkProfiles.Add(new ArkProfile());
		}
		ArkProfiles[0].Target = "";
		foreach (ArkProfile arkProfile3 in ArkProfiles)
		{
			ArkProfile arkProfile = arkProfile3;
			if (arkProfile.Options == null)
			{
				ArkProfile arkProfile2 = arkProfile;
				ArkOptions obj5 = new ArkOptions
				{
					ArkEnabled = ArkEnabled,
					ArkSize = ArkSize,
					ArkOpacity = ArkOpacity,
					ArkIdleOpacity = ArkIdleOpacity,
					ArkIdleSeconds = ArkIdleSeconds,
					ArkHoldSeconds = ArkHoldSeconds,
					ArkMoveOutline = ArkMoveOutline,
					ArkMouseEnabled = ArkMouseEnabled,
					ArkFollow = ArkFollow,
					ArkX = ArkX,
					ArkY = ArkY,
					ArkShakeToClose = ArkShakeToClose
				};
				ArkOptions arkOptions = obj5;
				arkProfile2.Options = obj5;
			}
			ArkOptions? options = arkProfile3.Options;
			options.ArkSize = Clamp(options.ArkSize, 40.0, 128.0, 58.0);
			options.ArkOpacity = Clamp(options.ArkOpacity, 0.0, 1.0, 1.0);
			options.ArkIdleOpacity = Clamp(options.ArkIdleOpacity, 0.0, 1.0, 0.2);
			options.ArkIdleSeconds = Clamp(options.ArkIdleSeconds, 0.0, 10.0, 2.0);
			options.ArkHoldSeconds = Clamp(options.ArkHoldSeconds, 0.0, 3.0, 0.2);
			arkProfile = arkProfile3;
			bool? follow = arkProfile.Follow;
			bool flag = follow == true;
			if (!follow.HasValue)
			{
				flag = ArkFollow;
				bool? flag3 = (arkProfile.Follow = flag);
			}
			arkProfile = arkProfile3;
			if (arkProfile.Name == null)
			{
				string text = (arkProfile.Name = "配置");
			}
			arkProfile = arkProfile3;
			if (arkProfile.Target == null)
			{
				string text = (arkProfile.Target = "");
			}
			arkProfile3.Color = NormalizeColor(arkProfile3.Color, "#30343B");
			if (flag2)
			{
				arkProfile3.BubbleColor = arkProfile3.Color;
			}
			arkProfile3.BubbleColor = NormalizeColor(arkProfile3.BubbleColor, arkProfile3.Color);
			arkProfile3.Opacity = Clamp(arkProfile3.Opacity, 0.0, 1.0, 1.0);
			arkProfile3.BubbleOpacity = Clamp(arkProfile3.BubbleOpacity, 0.0, 1.0, 1.0);
			arkProfile = arkProfile3;
			if (arkProfile.Buttons == null)
			{
				List<ArkBubble> list15 = (arkProfile.Buttons = new List<ArkBubble>());
			}
			arkProfile3.Buttons.RemoveAll((ArkBubble x) => x == null);
			arkProfile = arkProfile3;
			if (arkProfile.Tap == null)
			{
				GestureAction gestureAction = (arkProfile.Tap = GestureAction.BuiltIn("ArkMenu"));
			}
			arkProfile = arkProfile3;
			if (arkProfile.Hold == null)
			{
				GestureAction gestureAction = (arkProfile.Hold = GestureAction.BuiltIn("ArkMove"));
			}
			arkProfile = arkProfile3;
			if (arkProfile.DoubleTap == null)
			{
				GestureAction gestureAction = (arkProfile.DoubleTap = new GestureAction());
			}
			arkProfile = arkProfile3;
			if (arkProfile.TripleTap == null)
			{
				GestureAction gestureAction = (arkProfile.TripleTap = new GestureAction());
			}
			arkProfile = arkProfile3;
			if (arkProfile.Drag == null)
			{
				GestureAction gestureAction = (arkProfile.Drag = GestureAction.BuiltIn("ArkMove"));
			}
			GestureAction[] array2 = new GestureAction[5] { arkProfile3.Tap, arkProfile3.DoubleTap, arkProfile3.TripleTap, arkProfile3.Drag, arkProfile3.Hold };
			for (int index = 0; index < array2.Length; index++)
			{
				NormalizeAction(array2[index]);
			}
			foreach (ArkBubble button in arkProfile3.Buttons)
			{
				ArkBubble current9;
				ArkBubble arkBubble = (current9 = button);
				if (current9.Action == null)
				{
					GestureAction gestureAction = (current9.Action = new GestureAction());
				}
				NormalizeAction(arkBubble.Action);
				arkBubble.X = Clamp(arkBubble.X, -10000.0, 10000.0, 0.0);
				arkBubble.Y = Clamp(arkBubble.Y, -10000.0, 10000.0, 0.0);
			}
		}
		IdleSeconds = Clamp(IdleSeconds, 0.0, 3.0, 2.0);
		HoldSeconds = Clamp(HoldSeconds, 0.0, 3.0, 0.2);
		if (Gestures == null)
		{
			List<GestureAction> list5 = (Gestures = Defaults());
		}
		if (Launchers == null)
		{
			List<LauncherEntry> list18 = (Launchers = new List<LauncherEntry>());
		}
		if (EdgeGestures == null)
		{
			List<GestureAction> list5 = (EdgeGestures = EdgeDefaults());
		}
		if (IndicatorActions == null)
		{
			Dictionary<string, GestureAction> dictionary = (IndicatorActions = IndicatorDefaults());
		}
		foreach (KeyValuePair<string, GestureAction> item4 in IndicatorDefaults())
		{
			if (!IndicatorActions.TryGetValue(item4.Key, out GestureAction value5) || value5 == null)
			{
				value5 = (IndicatorActions[item4.Key] = item4.Value);
			}
			NormalizeAction(value5);
		}
		while (Gestures.Count < 13)
		{
			Gestures.Add(Defaults()[Gestures.Count]);
		}
		if (Gestures.Count > 13)
		{
			Gestures.RemoveRange(13, Gestures.Count - 13);
		}
		while (EdgeGestures.Count < 5)
		{
			EdgeGestures.Add(EdgeDefaults()[EdgeGestures.Count]);
		}
		if (EdgeGestures.Count > 5)
		{
			EdgeGestures.RemoveRange(5, EdgeGestures.Count - 5);
		}
		for (int num2 = 0; num2 < Gestures.Count; num2++)
		{
			List<GestureAction> list5 = Gestures;
			int index = num2;
			GestureAction gestureAction;
			if (list5[index] == null)
			{
				gestureAction = (list5[index] = new GestureAction());
			}
			gestureAction = Gestures[num2];
			if (gestureAction.Keys == null)
			{
				List<int> list21 = (gestureAction.Keys = new List<int>());
			}
			gestureAction = Gestures[num2];
			if (gestureAction.Command == null)
			{
				string text = (gestureAction.Command = "");
			}
			if (!Enum.IsDefined(Gestures[num2].Kind))
			{
				Gestures[num2].Kind = ActionKind.None;
			}
		}
		foreach (GestureAction gesture3 in Gestures)
		{
			NormalizeAction(gesture3);
			GestureAction gestureAction = gesture3;
			if (gestureAction.Icon == null)
			{
				string text = (gestureAction.Icon = "");
			}
			if (gesture3.Icon.Length > 2)
			{
				gesture3.Icon = "";
			}
			gestureAction = gesture3;
			if (gestureAction.IconPath == null)
			{
				string text = (gestureAction.IconPath = "");
			}
			gestureAction = gesture3;
			if (gestureAction.Label == null)
			{
				string text = (gestureAction.Label = "");
			}
			if (gesture3.Label.Length > 20)
			{
				gesture3.Label = gesture3.Label.Substring(0, 20);
			}
			bool flag;
			switch (gesture3.IconFont)
			{
			case "Segoe Fluent Icons":
			case "Segoe MDL2 Assets":
			case "Segoe UI Symbol":
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (!flag)
			{
				gesture3.IconFont = "Segoe Fluent Icons";
			}
		}
		for (int num3 = 0; num3 < EdgeGestures.Count; num3++)
		{
			List<GestureAction> list5 = EdgeGestures;
			int index = num3;
			if (list5[index] == null)
			{
				GestureAction gestureAction = (list5[index] = new GestureAction());
			}
		}
		foreach (GestureAction edgeGesture in EdgeGestures)
		{
			NormalizeAction(edgeGesture);
			GestureAction gestureAction = edgeGesture;
			if (gestureAction.Keys == null)
			{
				List<int> list21 = (gestureAction.Keys = new List<int>());
			}
			gestureAction = edgeGesture;
			if (gestureAction.Command == null)
			{
				string text = (gestureAction.Command = "");
			}
			gestureAction = edgeGesture;
			if (gestureAction.Icon == null)
			{
				string text = (gestureAction.Icon = "");
			}
			gestureAction = edgeGesture;
			if (gestureAction.IconPath == null)
			{
				string text = (gestureAction.IconPath = "");
			}
			gestureAction = edgeGesture;
			if (gestureAction.Label == null)
			{
				string text = (gestureAction.Label = "");
			}
			if (!Enum.IsDefined(edgeGesture.Kind))
			{
				edgeGesture.Kind = ActionKind.None;
			}
		}
		Launchers.RemoveAll((LauncherEntry x) => x == null || (string.IsNullOrWhiteSpace(x.Target) && (x.Keys == null || x.Keys.Count == 0)));
	}

	public static string NormalizeColor(string? value, string fallback)
	{
		if (value == null || value.Length != 7 || value[0] != '#' || !int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, null, out var _))
		{
			return fallback;
		}
		return value.ToUpperInvariant();
	}

	private static void NormalizeMultiTouch(MultiTouchSettings options)
	{
		options.MatchStrictness = Clamp(options.MatchStrictness, 30.0, 90.0, 60.0);
		if (options.GestureSchema < 1)
		{
			options.EdgeEnabled = options.EdgeEnabled || options.Enabled;
			options.GestureSchema = 1;
		}
		options.MaxSpacing = Clamp(options.MaxSpacing, 40.0, 3000.0, 800.0);
		options.HoldSeconds = Clamp(options.HoldSeconds, 0.1, 3.0, 0.2);
		if (options.Bindings == null)
		{
			List<MultiTouchBinding> list = (options.Bindings = new List<MultiTouchBinding>());
		}
		options.Bindings.RemoveAll((MultiTouchBinding b) => b == null || b.Fingers < 1 || b.Fingers > 5 || !MultiTouchSettings.Allowed(b.Fingers, b.Gesture));
		HashSet<string> unique = new HashSet<string>();
		options.Bindings.RemoveAll((MultiTouchBinding b) => b.Gesture != "Custom" && !unique.Add($"{b.Fingers}:{b.Gesture}"));
		foreach (MultiTouchBinding binding in options.Bindings)
		{
			MultiTouchBinding multiTouchBinding = binding;
			if (multiTouchBinding.Points == null)
			{
				List<GesturePoint> list3 = (multiTouchBinding.Points = new List<GesturePoint>());
			}
			binding.Points = binding.Points.FindAll((GesturePoint p) => double.IsFinite(p.X) && double.IsFinite(p.Y));
			if (binding.Points.Count > 512)
			{
				binding.Points = binding.Points.GetRange(0, 512);
			}
			multiTouchBinding = binding;
			if (multiTouchBinding.Paths == null)
			{
				List<List<GesturePoint>> list5 = (multiTouchBinding.Paths = new List<List<GesturePoint>>());
			}
			binding.Paths = binding.Paths.FindAll((List<GesturePoint> p) => p != null && p.Count > 0).GetRange(0, Math.Min(5, binding.Paths.FindAll((List<GesturePoint> p) => p != null && p.Count > 0).Count));
			foreach (List<GesturePoint> path in binding.Paths)
			{
				path.RemoveAll((GesturePoint p) => p == null || !double.IsFinite(p.X) || !double.IsFinite(p.Y));
				if (path.Count > 512)
				{
					path.RemoveRange(512, path.Count - 512);
				}
			}
			multiTouchBinding = binding;
			if (multiTouchBinding.Action == null)
			{
				GestureAction gestureAction = (multiTouchBinding.Action = new GestureAction());
			}
			NormalizeAction(binding.Action);
		}
	}

	private static void NormalizeDial(DialSettings Dial)
	{
		Dial.Size = Clamp(Dial.Size, 80.0, 320.0, 164.0);
		Dial.Opacity = Clamp(Dial.Opacity, 0.0, 1.0, 1.0);
		Dial.IdleOpacity = Clamp(Dial.IdleOpacity, 0.0, 1.0, 0.35);
		Dial.IdleSeconds = Clamp(Dial.IdleSeconds, 0.0, 10.0, 3.0);
		Dial.HoldSeconds = Clamp(Dial.HoldSeconds, 0.1, 3.0, 0.2);
		Dial.OuterColor = NormalizeColor(Dial.OuterColor, "#000000");
		Dial.InnerColor = NormalizeColor(Dial.InnerColor, "#3A3A3A");
		Dial.Sensitivity = Clamp(Dial.Sensitivity, 0.05, 4.0, 1.0);
		DialSettings dialSettings = Dial;
		if (dialSettings.Tap == null)
		{
			DialSettings dialSettings2 = dialSettings;
			GestureAction obj = new GestureAction
			{
				Kind = ActionKind.Keys,
				Keys = new List<int> { 173 }
			};
			GestureAction gestureAction = obj;
			dialSettings2.Tap = obj;
		}
		dialSettings = Dial;
		if (dialSettings.DoubleTap == null)
		{
			GestureAction gestureAction = (dialSettings.DoubleTap = new GestureAction());
		}
		NormalizeAction(Dial.Tap);
		NormalizeAction(Dial.DoubleTap);
		dialSettings = Dial;
		if (dialSettings.Tools == null)
		{
			List<DialTool> list = (dialSettings.Tools = DialTool.Defaults());
		}
		Dial.Tools.RemoveAll((DialTool x) => x == null);
		if (Dial.Tools.Count > 360)
		{
			Dial.Tools.RemoveRange(360, Dial.Tools.Count - 360);
		}
		foreach (DialTool tool in Dial.Tools)
		{
			DialTool dialTool = tool;
			if (dialTool.Tap == null)
			{
				GestureAction gestureAction = (dialTool.Tap = JsonSerializer.Deserialize<GestureAction>(JsonSerializer.Serialize(Dial.Tap)));
			}
			NormalizeAction(tool.Tap);
			dialTool = tool;
			if (dialTool.Name == null)
			{
				string text = (dialTool.Name = "");
			}
			if (tool.Preset == "Undo" && tool.Name == "撤销 / 重做")
			{
				tool.Name = "撤销";
			}
			dialTool = tool;
			if (dialTool.Preset == null)
			{
				string text = (dialTool.Preset = "Custom");
			}
			dialTool = tool;
			if (dialTool.Clockwise == null)
			{
				GestureAction gestureAction = (dialTool.Clockwise = new GestureAction());
			}
			dialTool = tool;
			if (dialTool.CounterClockwise == null)
			{
				GestureAction gestureAction = (dialTool.CounterClockwise = new GestureAction());
			}
			NormalizeAction(tool.Clockwise);
			NormalizeAction(tool.CounterClockwise);
		}
		Dial.Tap = null;
	}

	private static void NormalizeAction(GestureAction action)
	{
		bool flag = action.Kind == ActionKind.BuiltIn;
		if (flag)
		{
			bool flag2;
			switch (action.Command)
			{
			case "PlayerToggle":
			case "PlayerBack":
			case "PlayerForward":
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
			action.Kind = ActionKind.None;
			action.Command = "";
		}
		action.AppearanceDestination = Math.Clamp(action.AppearanceDestination, 0, 3);
		action.TextColor = NormalizeColor(action.TextColor, "#FFFFFF");
		action.TextOpacity = Clamp(action.TextOpacity, 0.0, 1.0, 0.8);
		GestureAction gestureAction = action;
		if (gestureAction.Label == null)
		{
			string text = (gestureAction.Label = "");
		}
		gestureAction = action;
		if (gestureAction.Keys == null)
		{
			List<int> list = (gestureAction.Keys = new List<int>());
		}
		gestureAction = action;
		if (gestureAction.Command == null)
		{
			string text = (gestureAction.Command = "");
		}
		gestureAction = action;
		if (gestureAction.Icon == null)
		{
			string text = (gestureAction.Icon = "");
		}
		gestureAction = action;
		if (gestureAction.IconPath == null)
		{
			string text = (gestureAction.IconPath = "");
		}
	}

	private static double Clamp(double n, double min, double max, double fallback)
	{
		if (!double.IsFinite(n))
		{
			return fallback;
		}
		return Math.Clamp(n, min, max);
	}
}
