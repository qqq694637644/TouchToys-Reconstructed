using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TouchAssistant.Models;

public sealed class GamepadLayout
{
	public string Target { get; set; } = "";

	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public GamepadShieldSettings? Shield { get; set; } = new GamepadShieldSettings();

	public List<GamepadShieldSettings>? Shields { get; set; }

	public bool ScanCodeInput { get; set; }

	public string Name { get; set; } = "布局";

	public List<GamepadControl> Controls { get; set; } = new List<GamepadControl>();

	public void NormalizeShields()
	{
		if (Shields == null)
		{
			Shields = ((Shield == null) ? new List<GamepadShieldSettings>() : new List<GamepadShieldSettings> { Shield });
		}
		Shield = null;
		Shields.RemoveAll((GamepadShieldSettings x) => x == null);
		if (Shields.Count > 32)
		{
			Shields.RemoveRange(32, Shields.Count - 32);
		}
		foreach (GamepadShieldSettings shield in Shields)
		{
			GamepadShieldSettings gamepadShieldSettings = shield;
			if (gamepadShieldSettings.Name == null)
			{
				string text = (gamepadShieldSettings.Name = "防误触区域");
			}
			shield.Width = (double.IsFinite(shield.Width) ? Math.Clamp(shield.Width, 100.0, 3840.0) : 900.0);
			shield.Height = (double.IsFinite(shield.Height) ? Math.Clamp(shield.Height, 80.0, 2160.0) : 360.0);
		}
	}
}
