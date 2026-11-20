namespace TouchAssistant.Models;

public sealed class ArkOptions
{
	public bool ArkEnabled { get; set; }

	public double ArkSize { get; set; } = 58.0;

	public double ArkOpacity { get; set; } = 1.0;

	public double ArkIdleOpacity { get; set; } = 0.2;

	public double ArkIdleSeconds { get; set; } = 2.0;

	public double ArkHoldSeconds { get; set; } = 0.2;

	public bool ArkMoveOutline { get; set; } = true;

	public bool ArkMouseEnabled { get; set; } = true;

	public bool ArkFollow { get; set; } = true;

	public int? ArkX { get; set; }

	public int? ArkY { get; set; }

	public bool ArkShakeToClose { get; set; } = true;
}
