using System.Runtime.InteropServices;

namespace TouchAssistant.Native;

internal static class InputPackets
{
	[DllImport("user32")]
	private static extern uint MapVirtualKey(uint code, uint kind);

	public static Win32.INPUT Build(int key, bool up)
	{
		uint num;
		uint dwFlags;
		switch (key)
		{
		case 513:
			num = (up ? 4u : 2u);
			goto IL_0057;
		case 514:
			num = (up ? 16u : 8u);
			goto IL_0057;
		case 515:
			num = (up ? 64u : 32u);
			goto IL_0057;
		case 516:
		case 517:
			num = 2048u;
			goto IL_0057;
		default:
			{
				return GamepadKeyboard.Build(key, MapVirtualKey((uint)key, 4u), scanCodes: true, up);
			}
			IL_0057:
			dwFlags = num;
			return new Win32.INPUT
			{
				type = 0u,
				data = new Win32.INPUTUNION
				{
					mi = new Win32.MOUSEINPUT
					{
						dwFlags = dwFlags,
						mouseData = key switch
						{
							517 => 4294967176u, 
							516 => 120u, 
							_ => 0u, 
						}
					}
				}
			};
		}
	}

	public static Win32.INPUT Release(Win32.INPUT input)
	{
		if (input.type == 1)
		{
			input.data.ki.dwFlags |= 2u;
		}
		else
		{
			uint dwFlags = input.data.mi.dwFlags;
			input.data.mi.dwFlags = dwFlags switch
			{
				2u => 4u, 
				8u => 16u, 
				32u => 64u, 
				_ => 0u, 
			};
		}
		return input;
	}
}
