using System;
using System.Runtime.InteropServices;

namespace TouchAssistant.Services;

[ComImport]
[Guid("D666063F-1587-4E43-81F1-B948E807363F")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
	[PreserveSig]
	int Activate(ref Guid id, uint context, nint parameters, [MarshalAs(UnmanagedType.IUnknown)] out object instance);
}
