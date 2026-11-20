using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using TouchAssistant.Core;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal static class KeyboardInput
{
	private static readonly Dictionary<int, int> owners;

	private static readonly Dictionary<int, long> due;

	private static readonly GamepadKeyboard sender;

	private static DispatcherQueue? ownerQueue;

	private static readonly InputDispatch delivery;

	private static DispatcherQueueTimer? timer;

	private static Action<IReadOnlyList<int>, bool>? testSend;

	private static readonly Dictionary<int, long> releasedAt;

	internal static Action<int[], bool, long>? Observed;

	private static readonly object motionGate;

	private static int motionX;

	private static int motionY;

	private static bool motionPending;

	internal static int MinimumNeutralMs { get; set; }

	static KeyboardInput()
	{
		owners = new Dictionary<int, int>();
		due = new Dictionary<int, long>();
		sender = new GamepadKeyboard();
		delivery = new InputDispatch(delegate(Exception error)
		{
			Diagnostics.Error(error, "Input.Send");
			ownerQueue?.TryEnqueue(delegate
			{
				ReleaseAll();
			});
		});
		releasedAt = new Dictionary<int, long>();
		MinimumNeutralMs = 22;
		motionGate = new object();
		delivery.Post(delegate
		{
		});
	}

	public static Task DrainAsync()
	{
		return delivery.Drain();
	}

	private static void Send(IReadOnlyList<int> keys, bool down, bool repeat = false)
	{
		if (keys.Count == 0)
		{
			return;
		}
		if (testSend != null)
		{
			testSend(keys, down);
			return;
		}
		if ((object)ownerQueue == null)
		{
			ownerQueue = DispatcherQueue.GetForCurrentThread();
		}
		int[] copy = keys.ToArray();
		delivery.Post(delegate
		{
			if (down)
			{
				long value;
				long num = copy.Select((int k) => (!releasedAt.TryGetValue(k, out value)) ? 0 : Math.Max(0L, MinimumNeutralMs - (Environment.TickCount64 - value))).DefaultIfEmpty(0L).Max();
				if (num > 0)
				{
					Thread.Sleep((int)num);
				}
			}
			sender.Send(copy, down);
			if (!down)
			{
				int[] array = copy;
				foreach (int key in array)
				{
					releasedAt[key] = Environment.TickCount64;
				}
			}
			Observed?.Invoke(copy, down, Environment.TickCount64);
		}, repeat);
	}

	internal static bool Repeats(int key)
	{
		bool flag;
		switch (key)
		{
		case 16:
		case 17:
		case 18:
		case 20:
		case 91:
		case 92:
		case 144:
		case 145:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (!flag && (key < 160 || key > 165))
		{
			if (key >= 513)
			{
				return key > 515;
			}
			return true;
		}
		return false;
	}

	public static void Held(IReadOnlyList<int> keys, bool down)
	{
		int[] array = keys.Distinct().ToArray();
		Send(array.Where((int k) => (!down) ? (owners.GetValueOrDefault(k) == 1) : (!owners.ContainsKey(k))).ToArray(), down);
		int[] array2 = array;
		foreach (int num2 in array2)
		{
			int num3 = owners.GetValueOrDefault(num2) + (down ? 1 : (-1));
			if (num3 <= 0)
			{
				owners.Remove(num2);
				due.Remove(num2);
				continue;
			}
			owners[num2] = num3;
			if (down && num3 == 1 && Repeats(num2))
			{
				due[num2] = Environment.TickCount64 + ((num2 >= 516) ? 80 : 350);
			}
		}
		if (due.Count == 0)
		{
			timer?.Stop();
			return;
		}
		if (timer == null)
		{
			timer = DispatcherQueue.GetForCurrentThread().CreateTimer();
			timer.Interval = TimeSpan.FromMilliseconds(40.0);
			timer.Tick += delegate
			{
				Repeat();
			};
		}
		timer.Start();
	}

	private static void Repeat()
	{
		try
		{
			long now = Environment.TickCount64;
			int[] array = (from p in due
				where p.Value <= now
				select p.Key).ToArray();
			if (array.Length != 0)
			{
				Send(array, down: true, repeat: true);
				int[] array2 = array;
				foreach (int num2 in array2)
				{
					due[num2] = now + ((num2 >= 516) ? 80 : 40);
				}
			}
		}
		catch (Exception error)
		{
			timer?.Stop();
			Diagnostics.Error(error, "Input.Repeat");
			try
			{
				ReleaseAll();
			}
			catch
			{
			}
		}
	}

	public static void Tap(IReadOnlyList<int> keys)
	{
		Send(keys, down: true);
		Send(keys.Where((int k) => !owners.ContainsKey(k)).ToArray(), down: false);
	}

	internal static async Task TapAsync(IReadOnlyList<int> keys, int holdMs = 40)
	{
		Held(keys, down: true);
		await Task.Delay(Math.Max(1, holdMs));
		Held(keys, down: false);
	}

	internal static async Task CheckRepeatAsync()
	{
		if (owners.Count != 0)
		{
			throw new InvalidOperationException("测试前存在按住输入");
		}
		List<(int[] Keys, bool Down)> events = new List<(int[], bool)>();
		testSend = delegate(IReadOnlyList<int> keys, bool down)
		{
			events.Add((keys.ToArray(), down));
		};
		try
		{
			Held(new int[2] { 17, 33 }, down: true);
			await Task.Delay(450);
			if (events.Count<(int[], bool)>(((int[] Keys, bool Down) e) => e.Down && e.Keys.Contains(33)) < 2 || events.Count<(int[], bool)>(((int[] Keys, bool Down) e) => e.Down && e.Keys.Contains(17)) != 1)
			{
				throw new InvalidOperationException("按键重复或修饰键保持错误");
			}
			Held(new int[2] { 17, 33 }, down: false);
			int stopped = events.Count;
			await Task.Delay(100);
			if (events.Count != stopped)
			{
				throw new InvalidOperationException("释放后仍在重复");
			}
			events.Clear();
			Held(new int[1] { 516 }, down: true);
			await Task.Delay(220);
			Held(new int[1] { 516 }, down: false);
			if (events.Count<(int[], bool)>(((int[] Keys, bool Down) e) => e.Down && e.Keys.Contains(516)) < 3)
			{
				throw new InvalidOperationException("滚轮没有连续触发");
			}
			Held(new int[1] { 513 }, down: true);
			Held(new int[1] { 513 }, down: true);
			Held(new int[1] { 513 }, down: false);
			if (!owners.ContainsKey(513))
			{
				throw new InvalidOperationException("共享鼠标键过早释放");
			}
			Held(new int[1] { 513 }, down: false);
		}
		finally
		{
			ReleaseAll();
			testSend = null;
		}
	}

	internal static async Task<string> MeasureDoubleTapAsync()
	{
		List<(bool Down, long At)> events = new List<(bool, long)>();
		object gate = new object();
		long start = Environment.TickCount64;
		Observed = delegate(int[] keys, bool down, long at)
		{
			lock (gate)
			{
				events.Add((down, at));
			}
		};
		try
		{
			Held(new int[1] { 135 }, down: true);
			await Task.Delay(5);
			Held(new int[1] { 135 }, down: false);
			await Task.Delay(5);
			Held(new int[1] { 135 }, down: true);
			await Task.Delay(5);
			Held(new int[1] { 135 }, down: false);
			await Task.Delay(120);
			lock (gate)
			{
				(bool, long) tuple = events.FirstOrDefault(((bool Down, long At) e) => e.Down);
				(bool, long) tuple2 = events.FirstOrDefault(((bool Down, long At) e) => !e.Down);
				(bool, long) tuple3 = events.Where(((bool Down, long At) e) => e.Down).Skip(1).FirstOrDefault();
				(bool, long) tuple4 = events.Where(((bool Down, long At) e) => !e.Down).Skip(1).FirstOrDefault();
				return $"events={events.Count}; firstDownMs={tuple.Item2 - start}; releaseGapMs={((tuple3.Item2 > 0 && tuple2.Item2 > 0) ? (tuple3.Item2 - tuple2.Item2) : (-1))}; secondReleaseMs={((tuple4.Item2 > 0 && tuple3.Item2 > 0) ? (tuple4.Item2 - tuple3.Item2) : (-1))}";
			}
		}
		finally
		{
			Observed = null;
			ReleaseAll();
		}
	}

	public static void Wheel(int amount, bool horizontal = false)
	{
		delivery.Post(delegate
		{
			SendWheel(amount, horizontal);
		});
	}

	public static void Move(int x, int y)
	{
		if (x == 0 && y == 0)
		{
			return;
		}
		lock (motionGate)
		{
			motionX = Math.Clamp(motionX + x, -2000, 2000);
			motionY = Math.Clamp(motionY + y, -2000, 2000);
			if (motionPending)
			{
				return;
			}
			motionPending = true;
		}
		delivery.Post(delegate
		{
			int dx;
			int dy;
			lock (motionGate)
			{
				dx = motionX;
				dy = motionY;
				motionX = (motionY = 0);
				motionPending = false;
			}
			Win32.INPUT iNPUT = new Win32.INPUT
			{
				type = 0u,
				data = new Win32.INPUTUNION
				{
					mi = new Win32.MOUSEINPUT
					{
						dx = dx,
						dy = dy,
						dwFlags = 1u
					}
				}
			};
			if (Win32.SendInput(1u, new Win32.INPUT[1] { iNPUT }, Marshal.SizeOf<Win32.INPUT>()) != 1)
			{
				throw new InvalidOperationException("Windows 未接收鼠标移动。");
			}
		});
	}

	private static void SendWheel(int amount, bool horizontal)
	{
		Win32.INPUT iNPUT = new Win32.INPUT
		{
			type = 0u,
			data = new Win32.INPUTUNION
			{
				mi = new Win32.MOUSEINPUT
				{
					dwFlags = (horizontal ? 4096u : 2048u),
					mouseData = (uint)amount
				}
			}
		};
		if (Win32.SendInput(1u, new Win32.INPUT[1] { iNPUT }, Marshal.SizeOf<Win32.INPUT>()) != 1)
		{
			throw new InvalidOperationException("Windows 未接收鼠标滚轮输入。");
		}
	}

	public static void ReleaseAll()
	{
		timer?.Stop();
		Send(owners.Keys.ToArray(), down: false);
		owners.Clear();
		due.Clear();
	}
}
