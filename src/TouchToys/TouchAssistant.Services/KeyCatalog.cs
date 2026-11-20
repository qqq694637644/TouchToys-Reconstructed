using System.Collections.Generic;
using System.Linq;
using Windows.System;

namespace TouchAssistant.Services;

internal static class KeyCatalog
{
	public static readonly (string Name, int Code)[] Special = new(string, int)[90]
	{
		("鼠标左键", 513),
		("鼠标右键", 514),
		("鼠标中键", 515),
		("滚轮向前", 516),
		("滚轮向后", 517),
		("Win", 91),
		("Ctrl", 17),
		("Shift", 16),
		("Alt", 18),
		("上一首键", 177),
		("下一首键", 176),
		("播放暂停键", 179),
		("静音键", 173),
		("音量加键", 175),
		("音量减键", 174),
		("ESC键", 27),
		("TAB键", 9),
		("Insert键", 45),
		("Home键", 36),
		("PgUp键", 33),
		("PgDn键", 34),
		("Delete键", 46),
		("End键", 35),
		("Backspace键", 8),
		("空格键", 32),
		("Enter键", 13),
		("方向上键", 38),
		("方向下键", 40),
		("方向左键", 37),
		("方向右键", 39),
		("CapsLock键", 20),
		("Print键", 42),
		("滚动锁定键", 145),
		("增加显示器亮度", 256),
		("减小显示器亮度", 257),
		("搜索键", 170),
		("PrintScreen", 44),
		("F1", 112),
		("F2", 113),
		("F3", 114),
		("F4", 115),
		("F5", 116),
		("F6", 117),
		("F7", 118),
		("F8", 119),
		("F9", 120),
		("F10", 121),
		("F11", 122),
		("F12", 123),
		("F13", 124),
		("F14", 125),
		("F15", 126),
		("F16", 127),
		("F17", 128),
		("F18", 129),
		("F19", 130),
		("F20", 131),
		("F21", 132),
		("F22", 133),
		("F23", 134),
		("F24", 135),
		("NumLock", 144),
		("Pause / Break", 19),
		("应用菜单键", 93),
		("浏览器后退", 166),
		("浏览器前进", 167),
		("浏览器刷新", 168),
		("浏览器停止", 169),
		("收藏夹", 171),
		("浏览器主页", 172),
		("停止播放", 178),
		("邮件", 180),
		("选择媒体", 181),
		("启动应用1", 182),
		("启动应用2", 183),
		("小键盘0", 96),
		("小键盘1", 97),
		("小键盘2", 98),
		("小键盘3", 99),
		("小键盘4", 100),
		("小键盘5", 101),
		("小键盘6", 102),
		("小键盘7", 103),
		("小键盘8", 104),
		("小键盘9", 105),
		("小键盘乘", 106),
		("小键盘加", 107),
		("小键盘减", 109),
		("小键盘小数点", 110),
		("小键盘除", 111)
	};

	public static string Name(int key)
	{
		(string, int) tuple = Special.FirstOrDefault(((string Name, int Code) x) => x.Code == key);
		if (tuple.Item1 != null)
		{
			string text = Localization.T(tuple.Item1);
			if (!(Localization.Language == "zh-CN"))
			{
				return text;
			}
			return text.Replace("键", "");
		}
		if (key >= 48 && key <= 90)
		{
			return ((char)key).ToString();
		}
		if (key >= 112 && key <= 135)
		{
			return "F" + (key - 111);
		}
		string text2 = Symbol(key, shift: false);
		if (text2 == null)
		{
			VirtualKey virtualKey = (VirtualKey)key;
			text2 = virtualKey.ToString();
		}
		return text2;
	}

	private static string? Symbol(int key, bool shift)
	{
		switch (key)
		{
		case 48:
		case 49:
		case 50:
		case 51:
		case 52:
		case 53:
		case 54:
		case 55:
		case 56:
		case 57:
			if (shift)
			{
				return ")!@#$%^&*("[key - 48].ToString();
			}
			break;
		case 186:
			return shift ? ":" : ";";
		case 187:
			return shift ? "+" : "=";
		case 188:
			return shift ? "<" : ",";
		case 189:
			return shift ? "_" : "-";
		case 190:
			return shift ? ">" : ".";
		case 191:
			return shift ? "?" : "/";
		case 192:
			return shift ? "~" : "`";
		case 219:
			return shift ? "{" : "[";
		case 220:
			return shift ? "|" : "\\";
		case 221:
			return shift ? "}" : "]";
		case 222:
			return shift ? "\"" : "'";
		}
		return null;
	}

	public static string Format(IEnumerable<int> keys)
	{
		int[] source = keys.Select(Normalize).ToArray();
		bool flag = source.Contains(16);
		bool symbol = flag && source.Count((int k) => k != 16) == 1 && Symbol(source.First((int k) => k != 16), shift: true) != null;
		return string.Join(" + ", from k in source
			where !symbol || k != 16
			select (!symbol) ? Name(k) : Symbol(k, shift: true));
	}

	public static int Normalize(int key)
	{
		switch (key)
		{
		case 160:
		case 161:
			return 16;
		case 162:
		case 163:
			return 17;
		case 164:
		case 165:
			return 18;
		case 92:
			return 91;
		default:
			return key;
		}
	}
}
