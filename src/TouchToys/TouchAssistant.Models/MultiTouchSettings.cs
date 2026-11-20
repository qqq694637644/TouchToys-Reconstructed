using System;
using System.Collections.Generic;

namespace TouchAssistant.Models;

public sealed class MultiTouchSettings
{
	public static readonly string[] Gestures = new string[9] { "Tap", "DoubleTap", "TripleTap", "Hold", "Up", "Down", "Left", "Right", "Custom" };

	public bool Enabled { get; set; }

	public int GestureSchema { get; set; }

	public bool EdgeEnabled { get; set; }

	public double MatchStrictness { get; set; } = 60.0;

	public double MaxSpacing { get; set; } = 800.0;

	public double HoldSeconds { get; set; } = 0.2;

	public List<MultiTouchBinding> Bindings { get; set; } = new List<MultiTouchBinding>();

	public static bool Allowed(int fingers, string kind)
	{
		bool flag = Array.Exists(Gestures, (string g) => g == kind);
		if (flag)
		{
			bool flag2 = fingers != 1;
			if (!flag2)
			{
				string text = kind;
				bool flag3 = ((text == "Hold" || text == "Custom") ? true : false);
				flag2 = flag3;
			}
			flag = flag2;
		}
		return flag;
	}
}
