using System;
using System.Runtime.InteropServices;

namespace TouchAssistant.Native;

internal static class ShellPreferences
{
	private struct ShellState
	{
		public uint Flags;

		public uint Unused1;

		public uint Unused2;

		public int Sort;

		public int Direction;

		public uint Version;

		public uint Unused3;

		public uint Flags2;
	}

	public static bool SingleClick
	{
		get
		{
			ShellState state = default(ShellState);
			SHGetSetSettings(ref state, 128u, set: false);
			return (state.Flags & 0x20) == 0;
		}
		set
		{
			ShellState state = default(ShellState);
			SHGetSetSettings(ref state, 128u, set: false);
			state.Flags = (value ? (state.Flags & 0xFFFFFFDFu) : (state.Flags | 0x20));
			SHGetSetSettings(ref state, 128u, set: true);
		}
	}

	public static (bool Supported, bool Locked) Rotation
	{
		get
		{
			try
			{
				uint state;
				return GetAutoRotationState(out state) ? (Supported: (state & 0xFC) == 0, Locked: (state & 1) != 0) : (Supported: false, Locked: false);
			}
			catch
			{
				return (Supported: false, Locked: false);
			}
		}
	}

	[DllImport("shell32")]
	private static extern void SHGetSetSettings(ref ShellState state, uint mask, [MarshalAs(UnmanagedType.Bool)] bool set);

	[DllImport("user32")]
	private static extern bool GetAutoRotationState(out uint state);

	[DllImport("user32")]
	private static extern bool SetAutoRotation([MarshalAs(UnmanagedType.Bool)] bool enabled);

	public static bool ToggleRotationLock()
	{
		(bool, bool) rotation = Rotation;
		if (!rotation.Item1)
		{
			return false;
		}
		try
		{
			return SetAutoRotation(rotation.Item2);
		}
		catch (EntryPointNotFoundException)
		{
			return false;
		}
	}
}
