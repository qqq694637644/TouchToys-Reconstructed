using System.Collections.Generic;
using TouchAssistant.Services;

namespace TouchAssistant.Models;

public sealed class LauncherEntry
{
	public string MatchTarget { get; set; } = "";

	public string Name { get; set; } = "应用";

	public string Target { get; set; } = "";

	public List<int> Keys { get; set; } = new List<int>();

	public string IconGlyph { get; set; } = "";

	public string IconFont { get; set; } = IconFontResolver.PreferredFontFamilyName;

	public string IconPath { get; set; } = "";
}
