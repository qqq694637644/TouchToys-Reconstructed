using System;
using System.Collections.Generic;
using System.Linq;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class HeldKeys : IDisposable
{
	private readonly Action<IReadOnlyList<int>, bool> send;

	private int[] keys = Array.Empty<int>();

	public bool Active => keys.Length != 0;

	public HeldKeys(Action<IReadOnlyList<int>, bool>? sender = null)
	{
		send = sender ?? new Action<IReadOnlyList<int>, bool>(SystemControl.SetKeysHeld);
	}

	public void Press(IEnumerable<int> values)
	{
		Release();
		int[] array = values.Distinct().ToArray();
		if (array.Length == 0 || array.Any((int k) => k <= 0 || (k >= 256 && (k < 513 || k > 517))))
		{
			throw new InvalidOperationException("此功能不支持持续按住，请绑定键盘按键。");
		}
		keys = array;
		try
		{
			send(keys, arg2: true);
		}
		catch
		{
			Release();
			throw;
		}
	}

	public void Release()
	{
		if (Active)
		{
			send(keys, arg2: false);
			keys = Array.Empty<int>();
		}
	}

	public void Dispose()
	{
		Release();
	}
}
