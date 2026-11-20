using System;

namespace TouchAssistant.Core;

internal static class InputSurfacePolicy
{
	public static bool IsSharedRoot(long parent, string className)
	{
		if (parent == 0L)
		{
			return className.Equals("Windows.UI.Core.CoreWindow", StringComparison.OrdinalIgnoreCase);
		}
		return false;
	}
}
