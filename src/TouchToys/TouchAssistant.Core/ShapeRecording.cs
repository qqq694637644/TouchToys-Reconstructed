using System;
using System.Collections.Generic;
using System.Linq;
using TouchAssistant.Models;

namespace TouchAssistant.Core;

internal sealed class ShapeRecording
{
	private readonly Dictionary<uint, GesturePoint> contacts = new Dictionary<uint, GesturePoint>();

	private bool invalid;

	private bool releasing;

	private long started;

	private int peak;

	private int target;

	public Dictionary<uint, List<GesturePoint>> Paths { get; } = new Dictionary<uint, List<GesturePoint>>();

	public List<GesturePoint> Path { get; } = new List<GesturePoint>();

	public int Active => contacts.Count;

	public bool Complete { get; private set; }

	public bool Contains(uint id)
	{
		return contacts.ContainsKey(id);
	}

	public void Reset()
	{
		contacts.Clear();
		Path.Clear();
		Paths.Clear();
		Complete = (invalid = (releasing = false));
		peak = 0;
	}

	public void Down(uint id, double x, double y, long now, int fingers, double spacing)
	{
		if (contacts.Count == 0)
		{
			Reset();
			started = now;
			target = fingers;
		}
		if (releasing || now - started > 220 || contacts.ContainsKey(id) || contacts.Values.Any((GesturePoint p) => Math.Sqrt(Math.Pow(x - p.X, 2.0) + Math.Pow(y - p.Y, 2.0)) > spacing))
		{
			invalid = true;
		}
		contacts[id] = new GesturePoint(x, y);
		Paths[id] = new List<GesturePoint>
		{
			new GesturePoint(x, y)
		};
		peak = Math.Max(peak, contacts.Count);
		if (target > 0 && peak > target)
		{
			invalid = true;
		}
		Path.Clear();
		Sample();
	}

	public void Move(uint id, double x, double y)
	{
		if (!contacts.ContainsKey(id))
		{
			return;
		}
		contacts[id] = new GesturePoint(x, y);
		List<GesturePoint> list = Paths[id];
		if (Math.Abs(list[list.Count - 1].X - x) + Math.Abs(list[list.Count - 1].Y - y) >= 2.0)
		{
			list.Add(new GesturePoint(x, y));
		}
		if (list.Count > 512)
		{
			for (int num = list.Count - 2; num > 0; num -= 2)
			{
				list.RemoveAt(num);
			}
		}
		Sample();
	}

	private void Sample()
	{
		if (invalid || releasing || contacts.Count != ((target == 0) ? peak : target))
		{
			return;
		}
		GesturePoint gesturePoint = new GesturePoint(contacts.Values.Average((GesturePoint p) => p.X), contacts.Values.Average((GesturePoint p) => p.Y));
		if (Path.Count != 0)
		{
			List<GesturePoint> path = Path;
			double num = Math.Abs(path[path.Count - 1].X - gesturePoint.X);
			List<GesturePoint> path2 = Path;
			if (!(num + Math.Abs(path2[path2.Count - 1].Y - gesturePoint.Y) >= 2.0))
			{
				goto IL_0105;
			}
		}
		Path.Add(gesturePoint);
		goto IL_0105;
		IL_0105:
		if (Path.Count > 512)
		{
			for (int num2 = Path.Count - 2; num2 > 0; num2 -= 2)
			{
				Path.RemoveAt(num2);
			}
		}
	}

	public void Up(uint id)
	{
		if (!contacts.Remove(id))
		{
			return;
		}
		releasing = true;
		if (contacts.Count == 0)
		{
			Complete = !invalid && (target == 0 || peak == target) && Paths.Values.Any((List<GesturePoint> p) => ShapeGesture.Length(p) >= 60.0);
		}
	}
}
