using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TouchAssistant.Native;

internal static class TouchSmoke
{
	private struct Touch
	{
		public Win32.POINTER_INFO Pointer;

		public uint Flags;

		public uint Mask;

		public Win32.RECT Contact;

		public Win32.RECT RawContact;

		public uint Orientation;

		public uint Pressure;
	}

	[StructLayout(LayoutKind.Auto)]
	[CompilerGenerated]
	private struct _003C_003Ec__DisplayClass6_2
	{
		public Touch[] contacts;
	}

	private static bool initialized;

	[DllImport("user32", SetLastError = true)]
	private static extern bool InitializeTouchInjection(uint count, uint mode);

	[DllImport("user32", SetLastError = true)]
	private static extern bool InjectTouchInput(uint count, [In] Touch[] contacts);

	[DllImport("user32")]
	private static extern int GetSystemMetrics(int index);

	internal static async Task DrawShape(Win32.POINT start, int fingers, double dpi, bool mixed = false)
	{
		if (!initialized)
		{
			if (!InitializeTouchInjection(10u, 3u))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			initialized = true;
		}
		int dx = 0;
		int dy = 0;
		Send(65542u);
		try
		{
			await Task.Delay(40);
			for (int step = 1; step <= 20; step++)
			{
				dy = Math.Min(step * 8, 80);
				dx = Math.Max(0, (step - 10) * 8);
				Send(131078u);
				await Task.Delay(16);
			}
			Send(262144u);
		}
		catch
		{
			try
			{
				Send(294912u);
			}
			catch
			{
			}
			throw;
		}
		await Task.Delay(200);
		void Send(uint flags)
		{
			Touch[] array = new Touch[fingers];
			for (int i = 0; i < fingers; i++)
			{
				Win32.POINT ptPixelLocation = new Win32.POINT(start.X + (int)((double)(i * 26 + ((!mixed || i != 0) ? dx : 0)) * dpi), start.Y + (int)((double)((!mixed || i != 0) ? dy : 0) * dpi));
				array[i] = new Touch
				{
					Pointer = new Win32.POINTER_INFO
					{
						pointerType = 2u,
						pointerId = (uint)(i + 1),
						pointerFlags = flags,
						ptPixelLocation = ptPixelLocation
					},
					Mask = 1u,
					Contact = new Win32.RECT
					{
						Left = ptPixelLocation.X - 2,
						Right = ptPixelLocation.X + 2,
						Top = ptPixelLocation.Y - 2,
						Bottom = ptPixelLocation.Y + 2
					}
				};
			}
			for (int j = 0; j < 4; j++)
			{
				if (InjectTouchInput((uint)fingers, array))
				{
					break;
				}
				int lastWin32Error = Marshal.GetLastWin32Error();
				if (lastWin32Error != 21 || j == 3)
				{
					throw new Win32Exception(lastWin32Error);
				}
				Thread.Sleep(1);
			}
		}
	}

	internal static async Task MultiFingerFixture(int x, int y)
	{
		if (!InitializeTouchInjection(10u, 3u))
		{
			throw new Win32Exception(Marshal.GetLastWin32Error());
		}
		int fingers;
		_003C_003Ec__DisplayClass6_2 CS_0024_003C_003E8__locals3 = default(_003C_003Ec__DisplayClass6_2);
		for (fingers = 2; fingers <= 4; fingers++)
		{
			CS_0024_003C_003E8__locals3.contacts = new Touch[fingers];
			SendFrame(0, 65542u);
			try
			{
				await Task.Delay(60);
				for (int step = 1; step <= 20; step++)
				{
					SendFrame(step * 4, 131078u);
					await Task.Delay(12);
				}
				for (int step = 0; step < 12; step++)
				{
					SendFrame(80, 131078u);
					await Task.Delay(50);
				}
				SendFrame(80, 262144u);
			}
			catch
			{
				try
				{
					SendFrame(80, 294912u);
				}
				catch
				{
				}
				throw;
			}
			await Task.Delay(150);
			CS_0024_003C_003E8__locals3 = default(_003C_003Ec__DisplayClass6_2);
			void SendFrame(int dx, uint flags)
			{
				for (int i = 0; i < fingers; i++)
				{
					Win32.POINT ptPixelLocation = new Win32.POINT(x + i * 35 + dx, y);
					CS_0024_003C_003E8__locals3.contacts[i] = new Touch
					{
						Pointer = new Win32.POINTER_INFO
						{
							pointerType = 2u,
							pointerId = (uint)(i + 1),
							pointerFlags = flags,
							ptPixelLocation = ptPixelLocation
						},
						Mask = 1u,
						Contact = new Win32.RECT
						{
							Left = ptPixelLocation.X - 2,
							Top = ptPixelLocation.Y - 2,
							Right = ptPixelLocation.X + 2,
							Bottom = ptPixelLocation.Y + 2
						}
					};
				}
				for (int j = 0; j < 4; j++)
				{
					if (InjectTouchInput((uint)fingers, CS_0024_003C_003E8__locals3.contacts))
					{
						break;
					}
					int lastWin32Error = Marshal.GetLastWin32Error();
					if (lastWin32Error != 21 || j == 3)
					{
						throw new Win32Exception(lastWin32Error);
					}
					Thread.Sleep(1);
				}
			}
		}
	}

	public static async Task Swipe(Win32.POINT start, Win32.POINT end, int holdMilliseconds = 0)
	{
		if (!initialized)
		{
			if (!InitializeTouchInjection(10u, 3u))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error());
			}
			initialized = true;
		}
		int desktopLeft = GetSystemMetrics(76);
		int desktopTop = GetSystemMetrics(77);
		int desktopRight = desktopLeft + GetSystemMetrics(78);
		int desktopBottom = desktopTop + GetSystemMetrics(79);
		Win32.POINT current = start;
		Send(start, 65542u);
		try
		{
			await Task.Delay(40);
			for (int t = 0; t < holdMilliseconds; t += 40)
			{
				Send(current, 131078u);
				await Task.Delay(40);
			}
			for (int t = 1; t <= 18; t++)
			{
				current = new Win32.POINT(start.X + (end.X - start.X) * t / 18, start.Y + (end.Y - start.Y) * t / 18);
				Send(current, 131078u);
				await Task.Delay(16);
			}
			Send(current, 262144u);
		}
		catch
		{
			try
			{
				Send(current, 294912u);
			}
			catch
			{
			}
			throw;
		}
		await Task.Delay(300);
		void Send(Win32.POINT point, uint flags)
		{
			Touch touch = new Touch
			{
				Pointer = new Win32.POINTER_INFO
				{
					pointerType = 2u,
					pointerId = 0u,
					pointerFlags = flags,
					ptPixelLocation = point
				},
				Mask = 7u,
				Orientation = 90u,
				Pressure = 32000u,
				Contact = new Win32.RECT
				{
					Left = Math.Max(desktopLeft, point.X - 2),
					Top = Math.Max(desktopTop, point.Y - 2),
					Right = Math.Min(desktopRight, point.X + 2),
					Bottom = Math.Min(desktopBottom, point.Y + 2)
				}
			};
			if (!InjectTouchInput(1u, new Touch[1] { touch }))
			{
				throw new Win32Exception(Marshal.GetLastWin32Error(), $"Touch ({point.X},{point.Y}), flags={flags:X}, virtual desktop=({GetSystemMetrics(76)},{GetSystemMetrics(77)},{GetSystemMetrics(78)},{GetSystemMetrics(79)}), size={Marshal.SizeOf<Touch>()}");
			}
		}
	}
}
