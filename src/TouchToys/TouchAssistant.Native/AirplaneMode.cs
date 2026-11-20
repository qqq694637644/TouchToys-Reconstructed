using System;
using System.Runtime.InteropServices;

namespace TouchAssistant.Native;

internal static class AirplaneMode
{
	[ComImport]
	[Guid("581333F6-28DB-41BE-BC7A-FF201F12F3F6")]
	private class RadioManagementApi
	{
	}

	[ComImport]
	[Guid("DB3AFBFB-08E6-46C6-AA70-BF9A34C30AB7")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IRadioManager
	{
		[PreserveSig]
		int IsRMSupported(out int supported);

		[PreserveSig]
		int GetUIRadioInstances(out nint instances);

		[PreserveSig]
		int GetSystemRadioState(out int radiosEnabled, out int hardwareState, out int reason);

		[PreserveSig]
		int SetSystemRadioState(int radiosEnabled);
	}

	private static T Use<T>(Func<IRadioManager, T> action)
	{
		IRadioManager radioManager = (IRadioManager)new RadioManagementApi();
		try
		{
			Marshal.ThrowExceptionForHR(radioManager.IsRMSupported(out var supported));
			if (supported == 0)
			{
				throw new NotSupportedException();
			}
			return action(radioManager);
		}
		finally
		{
			Marshal.ReleaseComObject(radioManager);
		}
	}

	public static bool? Read()
	{
		try
		{
			return Use(delegate(IRadioManager m)
			{
				Marshal.ThrowExceptionForHR(m.GetSystemRadioState(out var radiosEnabled, out var _, out var _));
				return ((uint)radiosEnabled > 1u) ? ((bool?)null) : new bool?(radiosEnabled == 0);
			});
		}
		catch
		{
			return null;
		}
	}

	public static bool Toggle()
	{
		try
		{
			return Use(delegate(IRadioManager m)
			{
				Marshal.ThrowExceptionForHR(m.GetSystemRadioState(out var radiosEnabled, out var _, out var _));
				if ((uint)radiosEnabled > 1u)
				{
					return false;
				}
				Marshal.ThrowExceptionForHR(m.SetSystemRadioState((radiosEnabled == 0) ? 1 : 0));
				return true;
			});
		}
		catch
		{
			return false;
		}
	}
}
