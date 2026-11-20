using System.Collections.Generic;

namespace TouchAssistant.Core;

internal sealed class RelayContactRecovery
{
	private readonly Dictionary<uint, (uint Frame, long Changed)> contacts = new Dictionary<uint, (uint, long)>();

	internal void Clear()
	{
		contacts.Clear();
	}

	internal bool Wait(uint id, uint frame, long now)
	{
		if (!contacts.TryGetValue(id, out (uint, long) value) || value.Item1 != frame)
		{
			contacts[id] = (frame, now);
			return true;
		}
		return now - value.Item2 < 5000;
	}
}
