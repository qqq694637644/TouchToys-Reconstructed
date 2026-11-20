using System;
using System.Runtime.InteropServices;

namespace TouchAssistant.Native;

internal static class TouchMessages
{
	private struct Contact
	{
		public int X;

		public int Y;

		public nint Source;

		public uint Id;

		public uint Flags;

		public uint Mask;

		public uint Time;

		public nuint Extra;

		public uint Width;

		public uint Height;
	}

	[DllImport("user32")]
	private static extern bool RegisterTouchWindow(nint h, uint flags);

	[DllImport("user32")]
	private static extern bool GetTouchInputInfo(nint handle, uint count, [Out] Contact[] contacts, int size);

	[DllImport("user32")]
	private static extern bool CloseTouchInputHandle(nint handle);

	public static void Register(nint h)
	{
		RegisterTouchWindow(h, 1u);
	}

	public static void Read(nuint w, nint l, Action<uint, uint, Win32.POINT> receive)
	{
		try
		{
			uint num = (uint)((int)w & 0xFFFF);
			if (num == 0 || num > 256)
			{
				return;
			}
			Contact[] array = new Contact[num];
			if (GetTouchInputInfo(l, num, array, Marshal.SizeOf<Contact>()))
			{
				Contact[] array2 = array;
				for (int i = 0; i < array2.Length; i++)
				{
					Contact contact = array2[i];
					receive(contact.Id, contact.Flags, new Win32.POINT(contact.X / 100, contact.Y / 100));
				}
			}
		}
		finally
		{
			CloseTouchInputHandle(l);
		}
	}
}
