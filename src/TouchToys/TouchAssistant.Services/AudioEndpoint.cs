using System;
using System.Runtime.InteropServices;

namespace TouchAssistant.Services;

internal static class AudioEndpoint
{
	private static T Use<T>(Func<IAudioEndpointVolume, T> action)
	{
		IMMDeviceEnumerator iMMDeviceEnumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
		IMMDevice device = null;
		object instance = null;
		try
		{
			Marshal.ThrowExceptionForHR(iMMDeviceEnumerator.GetDefaultAudioEndpoint(0, 0, out device));
			Guid id = typeof(IAudioEndpointVolume).GUID;
			Marshal.ThrowExceptionForHR(device.Activate(ref id, 23u, 0, out instance));
			return action((IAudioEndpointVolume)instance);
		}
		finally
		{
			if (instance != null)
			{
				Marshal.ReleaseComObject(instance);
			}
			if (device != null)
			{
				Marshal.ReleaseComObject(device);
			}
			Marshal.ReleaseComObject(iMMDeviceEnumerator);
		}
	}

	public static bool ReadMute()
	{
		return Use(delegate(IAudioEndpointVolume e)
		{
			Marshal.ThrowExceptionForHR(e.GetMute(out var mute));
			return mute;
		});
	}

	public static void ToggleMute()
	{
		Use(delegate(IAudioEndpointVolume e)
		{
			Guid context = Guid.Empty;
			Marshal.ThrowExceptionForHR(e.GetMute(out var mute));
			Marshal.ThrowExceptionForHR(e.SetMute(!mute, ref context));
			return 0;
		});
	}

	public static void Adjust(double percent)
	{
		Use(delegate(IAudioEndpointVolume e)
		{
			Marshal.ThrowExceptionForHR(e.GetMasterVolumeLevelScalar(out var level));
			Marshal.ThrowExceptionForHR(e.GetMute(out var mute));
			float num = (float)Math.Clamp((double)(mute ? 0f : level) + percent / 100.0, 0.0, 1.0);
			Guid context = Guid.Empty;
			Marshal.ThrowExceptionForHR(e.SetMasterVolumeLevelScalar(num, ref context));
			if (num > 0f)
			{
				Marshal.ThrowExceptionForHR(e.SetMute(mute: false, ref context));
			}
			return 0;
		});
	}

	public static double Read()
	{
		return Use(delegate(IAudioEndpointVolume e)
		{
			Marshal.ThrowExceptionForHR(e.GetMasterVolumeLevelScalar(out var level));
			Marshal.ThrowExceptionForHR(e.GetMute(out var mute));
			return (!mute) ? ((double)level * 100.0) : 0.0;
		});
	}

	public static void Write(float value)
	{
		Use(delegate(IAudioEndpointVolume e)
		{
			Guid context = Guid.Empty;
			Marshal.ThrowExceptionForHR(e.SetMasterVolumeLevelScalar(value, ref context));
			Marshal.ThrowExceptionForHR(e.SetMute(value == 0f, ref context));
			return 0;
		});
	}
}
