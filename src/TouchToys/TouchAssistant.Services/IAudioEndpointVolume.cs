using System;
using System.Runtime.InteropServices;

namespace TouchAssistant.Services;

[ComImport]
[Guid("5CDF2C82-841E-4546-9722-0CF74078229A")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IAudioEndpointVolume
{
	[PreserveSig]
	int RegisterControlChangeNotify(nint notify);

	[PreserveSig]
	int UnregisterControlChangeNotify(nint notify);

	[PreserveSig]
	int GetChannelCount(out uint count);

	[PreserveSig]
	int SetMasterVolumeLevel(float level, ref Guid context);

	[PreserveSig]
	int SetMasterVolumeLevelScalar(float level, ref Guid context);

	[PreserveSig]
	int GetMasterVolumeLevel(out float level);

	[PreserveSig]
	int GetMasterVolumeLevelScalar(out float level);

	[PreserveSig]
	int SetChannelVolumeLevel(uint channel, float level, ref Guid context);

	[PreserveSig]
	int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);

	[PreserveSig]
	int GetChannelVolumeLevel(uint channel, out float level);

	[PreserveSig]
	int GetChannelVolumeLevelScalar(uint channel, out float level);

	[PreserveSig]
	int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);

	[PreserveSig]
	int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
}
