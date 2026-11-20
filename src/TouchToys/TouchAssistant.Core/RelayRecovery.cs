using System;

namespace TouchAssistant.Core;

internal static class RelayRecovery
{
	internal static uint RetryDelay(int failures)
	{
		return (uint)Math.Min(30000, 1500 * (1 << Math.Clamp(failures - 1, 0, 5)));
	}

	internal static bool CanReset(int active, double idleMs, int error, bool onlyDown)
	{
		bool flag = active == 0 && idleMs >= 60000.0 && onlyDown;
		if (flag)
		{
			bool flag2 = ((error == 0 || error == 1460) ? true : false);
			flag = flag2;
		}
		return flag;
	}
}
