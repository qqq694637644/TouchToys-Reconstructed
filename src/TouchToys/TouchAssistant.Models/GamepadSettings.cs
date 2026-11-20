using System;
using System.Collections.Generic;
using System.Text.Json;

namespace TouchAssistant.Models;

public sealed class GamepadSettings
{
	public bool AutoSwitchEnglish { get; set; } = true;

	public bool SlideInput { get; set; } = true;

	public bool FastRepeat { get; set; } = true;

	public bool ShowLayoutSelector { get; set; }

	public GamepadControl LayoutSelector { get; set; } = new GamepadControl
	{
		Name = "选择布局",
		Style = "Rounded",
		Width = 160.0,
		Height = 48.0
	};

	public List<GamepadLayout> Layouts { get; set; } = new List<GamepadLayout>();

	public int ActiveLayout { get; set; }

	public bool Enabled { get; set; }

	public List<GamepadControl> Controls { get; set; } = new List<GamepadControl>
	{
		new GamepadControl
		{
			Joystick = true,
			Name = "摇杆",
			Size = 180.0
		},
		new GamepadControl
		{
			Name = "按钮 1",
			Action = new GestureAction
			{
				Kind = ActionKind.Keys,
				Keys = new List<int> { 32 }
			}
		},
		new GamepadControl
		{
			Name = "按钮 2",
			Action = new GestureAction
			{
				Kind = ActionKind.Keys,
				Keys = new List<int> { 69 }
			}
		}
	};

	public void EnsureLayouts()
	{
		if (Layouts == null)
		{
			List<GamepadLayout> list = (Layouts = new List<GamepadLayout>());
		}
		Layouts.RemoveAll((GamepadLayout x) => x == null);
		if (Layouts.Count == 0)
		{
			Layouts.Add(new GamepadLayout
			{
				Name = "默认布局",
				Controls = (Controls ?? new List<GamepadControl>())
			});
		}
		ActiveLayout = Math.Clamp(ActiveLayout, 0, Layouts.Count - 1);
		foreach (GamepadLayout layout in Layouts)
		{
			GamepadLayout gamepadLayout = layout;
			if (gamepadLayout.Target == null)
			{
				string text = (gamepadLayout.Target = "");
			}
			layout.NormalizeShields();
			gamepadLayout = layout;
			if (gamepadLayout.Name == null)
			{
				string text = (gamepadLayout.Name = "布局");
			}
			gamepadLayout = layout;
			if (gamepadLayout.Controls == null)
			{
				List<GamepadControl> list3 = (gamepadLayout.Controls = new List<GamepadControl>());
			}
			layout.Controls.RemoveAll((GamepadControl c) => c == null);
			if (layout.Controls.Count > 64)
			{
				layout.Controls.RemoveRange(64, layout.Controls.Count - 64);
			}
		}
		if (Controls == null)
		{
			List<GamepadControl> list3 = (Controls = new List<GamepadControl>());
		}
		Layouts[ActiveLayout].Controls = Controls;
	}

	public void SelectLayout(int index)
	{
		EnsureLayouts();
		if (index >= 0 && index < Layouts.Count)
		{
			ActiveLayout = index;
			Controls = Layouts[index].Controls;
		}
	}

	public void AddLayout(string name, bool empty = false)
	{
		EnsureLayouts();
		GamepadLayout value = Layouts[ActiveLayout];
		GamepadLayout gamepadLayout = (empty ? new GamepadLayout
		{
			Shields = new List<GamepadShieldSettings>(),
			Shield = null
		} : JsonSerializer.Deserialize<GamepadLayout>(JsonSerializer.Serialize(value)));
		gamepadLayout.Name = name;
		gamepadLayout.Target = "";
		Layouts.Add(gamepadLayout);
		SelectLayout(Layouts.Count - 1);
	}
}
