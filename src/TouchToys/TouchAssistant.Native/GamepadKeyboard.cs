using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class GamepadKeyboard
{
	private readonly Dictionary<int, Win32.INPUT> active = new Dictionary<int, Win32.INPUT>();

	private uint cachedPid;

	private string cachedProcess = "";

	public bool ScanCodes { get; set; } = true;

	[DllImport("user32")]
	private static extern uint MapVirtualKey(uint code, uint kind);

	[DllImport("user32", SetLastError = true)]
	private static extern uint SendInput(uint count, Win32.INPUT[] inputs, int size);

	internal static Win32.INPUT Build(int key, uint scan, bool scanCodes, bool up)
	{
		bool flag = (scan & 0xFF00) == 57344;
		if (!flag)
		{
			bool flag2;
			switch (key)
			{
			case 33:
			case 34:
			case 35:
			case 36:
			case 37:
			case 38:
			case 39:
			case 40:
			case 45:
			case 46:
			case 91:
			case 92:
			case 111:
			case 163:
			case 165:
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		bool flag3 = flag;
		bool flag4 = scanCodes && (scan & 0xFF) != 0 && key != 19;
		return new Win32.INPUT
		{
			type = 1u,
			data = new Win32.INPUTUNION
			{
				ki = new Win32.KEYBDINPUT
				{
					wVk = (ushort)((!flag4) ? ((ushort)key) : 0),
					wScan = (ushort)(flag4 ? ((ushort)(scan & 0xFF)) : 0),
					dwFlags = ((uint)(up ? 2 : 0) | (flag3 ? 1u : 0u) | (uint)(flag4 ? 8 : 0))
				}
			}
		};
	}

	public void Send(IReadOnlyList<int> keys, bool down)
	{
		object source;
		if (!down)
		{
			source = keys.Reverse().Where(delegate(int k)
			{
				bool flag3 = (uint)(k - 516) <= 1u;
				return !flag3;
			});
		}
		else
		{
			source = keys;
		}
		int[] array = ((IEnumerable<int>)source).ToArray();
		Win32.INPUT[] array2 = array.Select(delegate(int k)
		{
			Win32.INPUT value2;
			Win32.INPUT iNPUT = (active.TryGetValue(k, out value2) ? value2 : InputPackets.Build(k, up: false));
			if (!down)
			{
				iNPUT = InputPackets.Release(iNPUT);
			}
			return iNPUT;
		}).ToArray();
		if (array2.Length == 0)
		{
			return;
		}
		uint num = SendInput((uint)array2.Length, array2, Marshal.SizeOf<Win32.INPUT>());
		int lastWin32Error = Marshal.GetLastWin32Error();
		for (int num2 = 0; num2 < array2.Length; num2++)
		{
			bool flag = down;
			if (flag)
			{
				int num3 = array[num2];
				bool flag2 = (uint)(num3 - 516) <= 1u;
				flag = !flag2;
			}
			if (flag)
			{
				active[array[num2]] = array2[num2];
			}
			else if (num2 < num)
			{
				active.Remove(array[num2]);
			}
		}
		if (num != array2.Length)
		{
			Win32.GetWindowThreadProcessId(Win32.GetForegroundWindow(), out var process);
			if (process != cachedPid)
			{
				cachedPid = process;
				cachedProcess = "";
				try
				{
					using Process process2 = Process.GetProcessById((int)process);
					cachedProcess = process2.ProcessName;
				}
				catch
				{
				}
			}
			string value = cachedProcess;
			Diagnostics.State("Gamepad.Input", $"Mode={(ScanCodes ? "ScanCode" : "VirtualKey")}; Down={down}; Count={array2.Length}; Sent={num}; Error={((num != array2.Length) ? lastWin32Error : 0)}; Target={value}; Pid={process}");
		}
		if (num == array2.Length)
		{
			return;
		}
		if (down)
		{
			Win32.INPUT[] array3 = array2.Where((Win32.INPUT i) => i.type == 1 || i.data.mi.dwFlags != 2048).Select(InputPackets.Release).ToArray();
			SendInput((uint)array3.Length, array3, Marshal.SizeOf<Win32.INPUT>());
		}
		throw new InvalidOperationException("Windows 未完整接收摇杆按键，请检查游戏和 TouchToys 的权限是否一致。");
	}
}
