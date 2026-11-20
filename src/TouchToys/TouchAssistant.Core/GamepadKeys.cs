using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchAssistant.Core;

internal sealed class GamepadKeys : IDisposable
{
	private readonly Dictionary<object, int[]> held = new Dictionary<object, int[]>();

	private readonly Action<IReadOnlyList<int>, bool> send;

	public GamepadKeys(Action<IReadOnlyList<int>, bool> send)
	{
		this.send = send;
	}

	public void Set(object control, IEnumerable<int> values)
	{
		int[] array = values.Distinct().ToArray();
		if (array.Any((int k) => k <= 0 || (k >= 256 && (k < 513 || k > 517))))
		{
			throw new InvalidOperationException("按键编号无效。");
		}
		int[] first = held.GetValueOrDefault(control) ?? Array.Empty<int>();
		HashSet<int> hashSet = held.Values.SelectMany((int[] x) => x).ToHashSet();
		if (array.Length == 0)
		{
			held.Remove(control);
		}
		else
		{
			held[control] = array;
		}
		HashSet<int> hashSet2 = held.Values.SelectMany((int[] x) => x).ToHashSet();
		int[] array2 = hashSet.Except(hashSet2).ToArray();
		int[] array3 = hashSet2.Except(hashSet).ToArray();
		try
		{
			if (array2.Length != 0)
			{
				send(array2, arg2: false);
			}
			if (array3.Length != 0)
			{
				send(array3, arg2: true);
			}
		}
		catch
		{
			held[control] = first.Concat(array).Distinct().ToArray();
			throw;
		}
	}

	public void Clear()
	{
		int[] array = held.Values.SelectMany((int[] x) => x).Distinct().ToArray();
		if (array.Length != 0)
		{
			send(array, arg2: false);
		}
		held.Clear();
	}

	public void Dispose()
	{
		Clear();
	}
}
