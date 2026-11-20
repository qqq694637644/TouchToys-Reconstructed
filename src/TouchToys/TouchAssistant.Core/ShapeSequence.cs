using System;
using System.Collections.Generic;
using System.Linq;
using TouchAssistant.Models;

namespace TouchAssistant.Core;

internal sealed class ShapeSequence
{
	private readonly Dictionary<uint, GesturePoint> points = new Dictionary<uint, GesturePoint>();

	private readonly ShapeRecording recording = new ShapeRecording();

	private readonly List<GesturePoint> path = new List<GesturePoint>();

	private long started;

	private int peak;

	private bool invalid;

	private bool releasing;

	public GesturePoint EndCenter { get; private set; } = new GesturePoint();

	public void Reset()
	{
		points.Clear();
		path.Clear();
		recording.Reset();
		peak = 0;
		invalid = (releasing = false);
	}

	public MultiTouchBinding? Feed(uint id, double x, double y, bool down, bool up, bool cancel, long now, MultiTouchSettings options)
	{
		if (down)
		{
			recording.Down(id, x, y, now, 0, options.MaxSpacing);
		}
		else
		{
			recording.Move(id, x, y);
		}
		if (up || cancel)
		{
			recording.Up(id);
		}
		if (down)
		{
			if (points.Count == 0 && peak == 0)
			{
				started = now;
			}
			if (releasing || now - started > 220 || points.ContainsKey(id) || points.Values.Any((GesturePoint p) => Math.Sqrt(Math.Pow(x - p.X, 2.0) + Math.Pow(y - p.Y, 2.0)) > options.MaxSpacing))
			{
				invalid = true;
			}
			points[id] = new GesturePoint(x, y);
			if (points.Count > peak)
			{
				peak = points.Count;
				path.Clear();
			}
			if (peak > 5)
			{
				invalid = true;
			}
		}
		if (cancel)
		{
			invalid = true;
		}
		if (points.ContainsKey(id))
		{
			points[id] = new GesturePoint(x, y);
		}
		if (!releasing && points.Count == peak && points.Count > 0)
		{
			GesturePoint gesturePoint = new GesturePoint(points.Values.Average((GesturePoint p) => p.X), points.Values.Average((GesturePoint p) => p.Y));
			if (path.Count != 0)
			{
				double x2 = gesturePoint.X;
				List<GesturePoint> list = path;
				double num = Math.Abs(x2 - list[list.Count - 1].X);
				double y2 = gesturePoint.Y;
				List<GesturePoint> list2 = path;
				if (!(num + Math.Abs(y2 - list2[list2.Count - 1].Y) >= 2.0))
				{
					goto IL_0278;
				}
			}
			path.Add(gesturePoint);
			goto IL_0278;
		}
		goto IL_02ae;
		IL_0278:
		if (path.Count > 512)
		{
			for (int num2 = path.Count - 2; num2 > 0; num2 -= 2)
			{
				path.RemoveAt(num2);
			}
		}
		goto IL_02ae;
		IL_02ae:
		if (up || cancel)
		{
			points.Remove(id);
			releasing = true;
		}
		if (points.Count != 0 || peak == 0)
		{
			return null;
		}
		if (recording.Paths.Count > 0)
		{
			EndCenter = new GesturePoint(recording.Paths.Values.Average((List<GesturePoint> p) => p[p.Count - 1].X), recording.Paths.Values.Average((List<GesturePoint> p) => p[p.Count - 1].Y));
		}
		MultiTouchBinding? result = ((!invalid && now - started <= 10000) ? (ShapeGesture.MatchPaths(recording.Paths.Values.ToList(), options.Bindings, options.MatchStrictness) ?? ShapeGesture.Match(path, peak, options.Bindings, options.MatchStrictness)) : null);
		Reset();
		return result;
	}
}
