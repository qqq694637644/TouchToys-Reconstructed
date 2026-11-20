using System.Collections.Generic;

namespace TouchAssistant.Models;

public sealed class ArkProfile
{
	public ArkOptions? Options { get; set; }

	public bool? Follow { get; set; }

	public bool AllPinned { get; set; }

	public string Name { get; set; } = "全局设置";

	public string Target { get; set; } = "";

	public string Color { get; set; } = "#30343B";

	public string BubbleColor { get; set; } = "#30343B";

	public double Opacity { get; set; } = 1.0;

	public double BubbleOpacity { get; set; } = 1.0;

	public GestureAction Tap { get; set; } = GestureAction.BuiltIn("ArkMenu");

	public GestureAction DoubleTap { get; set; } = new GestureAction();

	public GestureAction TripleTap { get; set; } = new GestureAction();

	public GestureAction Drag { get; set; } = GestureAction.BuiltIn("ArkMove");

	public GestureAction Hold { get; set; } = GestureAction.BuiltIn("ArkMove");

	public List<ArkBubble> Buttons { get; set; } = DefaultButtons();

	public static List<ArkBubble> DefaultButtons()
	{
		List<ArkBubble> list = new List<ArkBubble>();
		int[] array = new int[6] { 67, 86, 65, 90, 89, 13 };
		for (int i = 0; i < array.Length; i++)
		{
			list.Add(new ArkBubble
			{
				X = ((i % 2 == 0) ? (-145) : 70),
				Y = (i / 2 - 1) * 58,
				Action = new GestureAction
				{
					Kind = ActionKind.Keys,
					Keys = ((i == 5) ? new List<int> { array[i] } : new List<int>
					{
						17,
						array[i]
					})
				}
			});
		}
		return list;
	}
}
