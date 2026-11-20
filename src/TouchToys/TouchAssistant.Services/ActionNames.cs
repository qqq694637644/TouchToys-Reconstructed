using System;
using System.IO;
using TouchAssistant.Models;

namespace TouchAssistant.Services;

internal static class ActionNames
{
	public static string Icon(GestureAction a)
	{
		if (a.Kind == ActionKind.BuiltIn)
		{
			string result;
			switch (a.Command)
			{
			case "Gamepad":
				result = "\uece9";
				break;
			case "ControlCenter":
				result = "\ue713";
				break;
			case "TouchKeyboard":
				result = "\ue765";
				break;
			case "Touchpad":
			case "SystemTouchpad":
				result = "\uefa5";
				break;
			case "VirtualDial":
				result = "\ue958";
				break;
			case "ArkOn":
			case "ArkToggle":
			case "ArkOff":
				result = "\ueca5";
				break;
			case "BallToggle":
				result = "\uea3a";
				break;
			case "Stage":
				result = "\ue90c";
				break;
			case "Overview":
				result = "\ue90e";
				break;
			case "AppSettings":
				result = "\ue713";
				break;
			default:
				result = "";
				break;
			}
			return result;
		}
		return "";
	}

	public static string Describe(GestureAction a)
	{
		if (a.Kind != ActionKind.Launch)
		{
			return Localization.T(DescribeCore(a));
		}
		return DescribeCore(a);
	}

	private static string DescribeCore(GestureAction a)
	{
		Uri result;
		return a.Kind switch
		{
			ActionKind.Keys => (a.Keys.Count == 0) ? "未设置快捷键" : KeyCatalog.Format(a.Keys), 
			ActionKind.None => "未绑定", 
			ActionKind.Launch => InstalledApps.DisplayName(a.Command) ?? ((Uri.TryCreate(a.Command, UriKind.Absolute, out result) && !result.IsFile) ? a.Command : Path.GetFileNameWithoutExtension(a.Command)), 
			_ => a.Command switch
			{
				"ControlCenter" => "控制中心", 
				"TouchKeyboard" => "切换触摸键盘", 
				"Touchpad" => "切换 TT触摸板", 
				"SystemTouchpad" => "切换系统原生触摸板", 
				"TaskView" => "任务视图", 
				"Stage" => "侧边任务列表", 
				"Overview" => "后台任务列表", 
				"NextApp" => "下个应用", 
				"PreviousApp" => "上个应用", 
				"Gamepad" => "虚拟摇杆和按钮", 
				"StartMenu" => "开始菜单", 
				"Desktop" => "显示桌面", 
				"NotificationCenter" => "通知中心", 
				"Wheel" => "切换环形菜单", 
				"Move" => "移动圆点", 
				"AppSettings" => "应用设置", 
				"VirtualDial" => "虚拟 Dial", 
				"ArkOn" => "打开方舟", 
				"ArkOff" => "关闭方舟", 
				"ArkToggle" => "切换方舟", 
				"ArkMenu" => "随行气泡", 
				"ArkMove" => "移动方舟", 
				"BallToggle" => "切换圆点", 
				_ => a.Command, 
			}, 
		};
	}
}
