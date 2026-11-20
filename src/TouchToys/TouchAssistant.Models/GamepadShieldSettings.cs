namespace TouchAssistant.Models;

public sealed class GamepadShieldSettings
{
	public string Name { get; set; } = "防误触区域";

	public bool Enabled { get; set; }

	public double Width { get; set; } = 900.0;

	public double Height { get; set; } = 360.0;

	public int? X { get; set; }

	public int? Y { get; set; }
}
