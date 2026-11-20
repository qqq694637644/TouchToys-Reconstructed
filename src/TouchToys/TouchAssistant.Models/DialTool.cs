using System.Collections.Generic;

namespace TouchAssistant.Models;

public sealed class DialTool
{
	public string Name { get; set; } = "新工具";

	public string Preset { get; set; } = "Custom";

	public GestureAction? Tap { get; set; }

	public GestureAction Clockwise { get; set; } = new GestureAction();

	public GestureAction CounterClockwise { get; set; } = new GestureAction();

	public string RotationHint(int direction, string? boundName = null)
	{
		GestureAction gestureAction = ((direction > 0) ? Clockwise : CounterClockwise);
		if (!string.IsNullOrWhiteSpace(gestureAction.Label))
		{
			return gestureAction.Label;
		}
		return Preset switch
		{
			"Volume" => (direction > 0) ? "音量+" : "音量-", 
			"Scroll" => (direction > 0) ? "向下滚动" : "向上滚动", 
			"Zoom" => (direction > 0) ? "放大" : "缩小", 
			"Undo" => (direction > 0) ? "重做" : "撤销", 
			_ => boundName ?? Name, 
		};
	}

	public static List<DialTool> Defaults()
	{
		return new List<DialTool>
		{
			new DialTool
			{
				Name = "音量",
				Preset = "Volume"
			},
			new DialTool
			{
				Name = "滚动",
				Preset = "Scroll"
			},
			new DialTool
			{
				Name = "缩放",
				Preset = "Zoom"
			},
			new DialTool
			{
				Name = "撤销",
				Preset = "Undo"
			}
		};
	}
}
