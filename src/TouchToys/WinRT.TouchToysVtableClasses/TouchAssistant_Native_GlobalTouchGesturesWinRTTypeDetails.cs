using System.Runtime.InteropServices;
using ABI.System;

namespace WinRT.TouchToysVtableClasses;

internal sealed class TouchAssistant_Native_GlobalTouchGesturesWinRTTypeDetails : IWinRTExposedTypeDetails
{
	public ComWrappers.ComInterfaceEntry[] GetExposedInterfaces()
	{
		return new ComWrappers.ComInterfaceEntry[1]
		{
			new ComWrappers.ComInterfaceEntry
			{
				IID = IDisposableMethods.IID,
				Vtable = IDisposableMethods.AbiToProjectionVftablePtr
			}
		};
	}
}
