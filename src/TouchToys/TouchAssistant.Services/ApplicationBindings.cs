using System;
using System.Collections.Generic;
using System.Linq;
using TouchAssistant.Models;
using TouchAssistant.Native;

namespace TouchAssistant.Services;

internal static class ApplicationBindings
{
	private static uint lastPid;

	private static nint last;

	private static HashSet<string> targets = new HashSet<string>();

	private static void Refresh()
	{
		nint foregroundWindow = Win32.GetForegroundWindow();
		Win32.GetWindowThreadProcessId(foregroundWindow, out var process);
		if (foregroundWindow != 0 && process != Environment.ProcessId && (foregroundWindow != last || process != lastPid))
		{
			last = foregroundWindow;
			lastPid = process;
			targets = WindowCatalog.TargetsForWindow(foregroundWindow);
		}
	}

	public static bool Matches(string? target)
	{
		if (!StoreEntitlements.AppBindingsAvailable || string.IsNullOrWhiteSpace(target))
		{
			return false;
		}
		Refresh();
		return WindowCatalog.MatchesTarget(target, targets);
	}

	public static ArkProfile Ark(Settings settings)
	{
		return settings.ArkProfiles.FirstOrDefault((ArkProfile p) => Matches(p.Target)) ?? settings.ArkProfiles[0];
	}

	public static ArkOptions ArkOptions(Settings settings)
	{
		return Ark(settings).Options;
	}

	public static DialSettings Dial(Settings settings)
	{
		return settings.DialApps.FirstOrDefault((DialAppProfile p) => Matches(p.Target))?.Options ?? settings.Dial;
	}

	public static BottomAppProfile? BottomProfile(Settings settings)
	{
		return settings.BottomApps.FirstOrDefault((BottomAppProfile p) => Matches(p.Target));
	}

	public static MultiTouchSettings Gestures(Settings settings)
	{
		return settings.GestureApps.FirstOrDefault((GestureAppProfile p) => Matches(p.Target))?.Options ?? settings.MultiTouch;
	}

	public static GestureAction Bottom(Settings settings)
	{
		return settings.EdgeGestures[2];
	}

	public static Dictionary<string, GestureAction> Indicator(Settings settings)
	{
		return settings.IndicatorActions;
	}

	public static GamepadLayout Layout(GamepadSettings settings)
	{
		GamepadLayout gamepadLayout = settings.Layouts.FirstOrDefault((GamepadLayout p) => Matches(p.Target));
		if (gamepadLayout != null)
		{
			return gamepadLayout;
		}
		GamepadLayout gamepadLayout2 = settings.Layouts[settings.ActiveLayout];
		GamepadLayout gamepadLayout3;
		if (!string.IsNullOrEmpty(gamepadLayout2.Target))
		{
			gamepadLayout3 = settings.Layouts.FirstOrDefault((GamepadLayout p) => string.IsNullOrEmpty(p.Target));
			if (gamepadLayout3 == null)
			{
				return gamepadLayout2;
			}
		}
		else
		{
			gamepadLayout3 = gamepadLayout2;
		}
		return gamepadLayout3;
	}
}
