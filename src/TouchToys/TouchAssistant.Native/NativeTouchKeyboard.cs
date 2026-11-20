using System;
using System.IO;
using System.Runtime.InteropServices;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal static class NativeTouchKeyboard
{
	[ComImport]
	[Guid("37C994E7-432B-4834-A2F7-DCE1F13B834B")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface ITipInvocation
	{
		void Toggle(nint window);
	}

	[DllImport("user32")]
	private static extern nint GetDesktopWindow();

	public static void Toggle()
	{
		object obj = null;
		try
		{
			obj = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("4CE576FA-83DC-4F88-951C-9D0782B4E376"), throwOnError: true));
			((ITipInvocation)obj).Toggle(GetDesktopWindow());
		}
		catch (COMException ex)
		{
			Diagnostics.Error(ex, "NativeKeyboard.Toggle");
			string text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles), "Microsoft Shared", "ink", "TabTip.exe");
			if (!File.Exists(text))
			{
				throw new InvalidOperationException("系统触摸键盘组件不可用。", ex);
			}
			SystemControl.Launch(text);
			App.CurrentApp.Report("已启动触摸键盘服务；若未显示，请再点一次键盘按钮。");
		}
		finally
		{
			if (obj != null && Marshal.IsComObject(obj))
			{
				Marshal.FinalReleaseComObject(obj);
			}
		}
	}
}
