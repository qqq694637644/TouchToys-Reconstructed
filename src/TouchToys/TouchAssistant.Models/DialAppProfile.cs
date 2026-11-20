namespace TouchAssistant.Models;

public sealed class DialAppProfile
{
	public string Name { get; set; } = "应用";

	public string Target { get; set; } = "";

	public DialSettings Options { get; set; } = new DialSettings();
}
