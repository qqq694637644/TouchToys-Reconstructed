namespace TouchAssistant.Core;

internal static class CloseGuardEdgePolicy
{
	internal static bool CanStart(bool edgeEnabled, bool firstContact, bool guardHit, int x, int y, int left, int top, int right, double scale)
	{
		if (edgeEnabled && firstContact && guardHit && scale > 0.0 && (double)x >= (double)left + (double)((right - left) * 2) / 3.0 && x < right && y >= top)
		{
			return (double)(y - top) <= 16.0 * scale;
		}
		return false;
	}
}
