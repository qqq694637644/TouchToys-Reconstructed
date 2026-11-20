using System.Runtime.InteropServices;

namespace TouchAssistant.Services;

[ComImport]
[Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
	[PreserveSig]
	int EnumAudioEndpoints(int flow, uint mask, out nint devices);

	[PreserveSig]
	int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
}
