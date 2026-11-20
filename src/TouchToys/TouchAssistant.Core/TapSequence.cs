using System;

namespace TouchAssistant.Core;

internal sealed class TapSequence
{
	private MultiTouchRecognizer.Match? pending;

	private int count;

	private int maximum;

	private long deadline;

	public void Reset()
	{
		pending = null;
		count = 0;
	}

	public MultiTouchRecognizer.Match? Displaced(MultiTouchRecognizer.Match match, long now)
	{
		MultiTouchRecognizer.Match? match2 = pending;
		if (match2.HasValue)
		{
			MultiTouchRecognizer.Match valueOrDefault = match2.GetValueOrDefault();
			if (now > deadline || valueOrDefault.Fingers != match.Fingers || Math.Sqrt(Math.Pow(valueOrDefault.X - match.X, 2.0) + Math.Pow(valueOrDefault.Y - match.Y, 2.0)) > 48.0)
			{
				return Flush(deadline);
			}
		}
		return null;
	}

	public MultiTouchRecognizer.Match? Flush(long now)
	{
		MultiTouchRecognizer.Match? match = pending;
		if (match.HasValue)
		{
			MultiTouchRecognizer.Match valueOrDefault = match.GetValueOrDefault();
			if (now >= deadline)
			{
				string gesture = count switch
				{
					3 => "TripleTap", 
					2 => "DoubleTap", 
					_ => "Tap", 
				};
				Reset();
				return valueOrDefault with
				{
					Gesture = gesture
				};
			}
		}
		return null;
	}

	public MultiTouchRecognizer.Match? Feed(MultiTouchRecognizer.Match match, long now, int maxTaps)
	{
		if (match.Gesture != "Tap")
		{
			Reset();
			return match;
		}
		MultiTouchRecognizer.Match? match2 = pending;
		if (match2.HasValue)
		{
			MultiTouchRecognizer.Match valueOrDefault = match2.GetValueOrDefault();
			if (now <= deadline && valueOrDefault.Fingers == match.Fingers && !(Math.Sqrt(Math.Pow(valueOrDefault.X - match.X, 2.0) + Math.Pow(valueOrDefault.Y - match.Y, 2.0)) > 48.0))
			{
				goto IL_00a3;
			}
		}
		count = 0;
		goto IL_00a3;
		IL_00a3:
		pending = match;
		maximum = maxTaps;
		count++;
		deadline = now + 400;
		if (count < maximum)
		{
			return null;
		}
		return Flush(deadline);
	}
}
