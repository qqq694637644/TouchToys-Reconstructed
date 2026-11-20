namespace TouchAssistant.Models;

public sealed class GestureAppProfile
{
	public string Name { get; set; } = "应用";

	public string Target { get; set; } = "";

	public MultiTouchSettings Options { get; set; } = new MultiTouchSettings();
}
