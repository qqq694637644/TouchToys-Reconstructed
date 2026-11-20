using System;
using System.Collections.Generic;
using System.Linq;

namespace TouchAssistant.Core;

internal sealed class TouchpadGesture
{
	private sealed class Contact(double x, double y)
	{
		public double StartX = x;

		public double StartY = y;

		public double X = x;

		public double Y = y;

		public double Travel => Math.Sqrt(Math.Pow(X - StartX, 2.0) + Math.Pow(Y - StartY, 2.0));
	}

	private readonly Dictionary<uint, Contact> contacts = new Dictionary<uint, Contact>();

	private readonly Action<PadEvent> emit;

	private readonly Func<int, string, bool> bound;

	private readonly Action<int, string> custom;

	private readonly Dictionary<uint, Contact> sequence = new Dictionary<uint, Contact>();

	private bool customHeld;

	private bool customInvalid;

	private bool pinching;

	private double pinchDistance;

	private int pendingTaps;

	private int pendingFingers;

	private long tapDeadline;

	private int maximum;

	private bool moved;

	private bool dragging;

	private bool ending;

	private long began;

	private uint scrolling;

	private bool twoScroll;

	private long pairBegan;

	public bool NeedsTick
	{
		get
		{
			if (contacts.Count <= 0)
			{
				return pendingTaps > 0;
			}
			return true;
		}
	}

	public TouchpadGesture(Action<PadEvent> emit, Func<int, string, bool>? bound = null, Action<int, string>? custom = null)
	{
		this.emit = emit;
		this.bound = bound ?? ((Func<int, string, bool>)((int _, string _) => false));
		this.custom = custom ?? ((Action<int, string>)delegate
		{
		});
	}

	private bool HasSwipes(int count)
	{
		if (!bound(count, "Up") && !bound(count, "Down") && !bound(count, "Left"))
		{
			return bound(count, "Right");
		}
		return true;
	}

	public void Tick(long now, double holdMilliseconds = 500.0)
	{
		if (contacts.Count > 0 && !moved && !customHeld && !customInvalid && (double)(now - began) >= holdMilliseconds && bound(maximum, "Hold"))
		{
			customHeld = true;
			pendingTaps = 0;
			ReleaseDrag();
			custom(maximum, "Hold");
		}
		if (contacts.Count == 0 && pendingTaps > 0 && now >= tapDeadline)
		{
			FlushTaps();
		}
	}

	private void FlushTaps()
	{
		int num = pendingFingers;
		int num2 = pendingTaps;
		pendingTaps = 0;
		string arg = ((num2 >= 3) ? "TripleTap" : ((num2 == 2) ? "DoubleTap" : "Tap"));
		if (bound(num, arg))
		{
			custom(num, arg);
		}
		else if (num2 == 1 && num <= 3)
		{
			emit(new PadEvent("Click", 0.0, 0.0, num switch
			{
				2 => 514, 
				3 => 515, 
				_ => 513, 
			}));
		}
	}

	private void Tap(int fingers, long now)
	{
		if (pendingTaps > 0 && (pendingFingers != fingers || now > tapDeadline))
		{
			FlushTaps();
		}
		pendingFingers = fingers;
		pendingTaps++;
		tapDeadline = now + 350;
		int num = (bound(fingers, "TripleTap") ? 3 : ((!bound(fingers, "DoubleTap")) ? 1 : 2));
		if (pendingTaps >= num)
		{
			FlushTaps();
		}
	}

	public void Feed(uint message, uint id, double x, double y, long now)
	{
		if (message == 582)
		{
			Tick(now);
			if (contacts.Count == 0)
			{
				maximum = 0;
				moved = (ending = (customHeld = (customInvalid = false)));
				sequence.Clear();
				began = now;
			}
			else if (now - began > 220 || sequence.ContainsKey(id))
			{
				customInvalid = true;
			}
			contacts[id] = new Contact(x, y);
			sequence[id] = contacts[id];
			maximum = Math.Max(maximum, contacts.Count);
			if (contacts.Count == 2)
			{
				pairBegan = now;
				Contact[] array = contacts.Values.ToArray();
				pinchDistance = Math.Sqrt(Math.Pow(array[0].X - array[1].X, 2.0) + Math.Pow(array[0].Y - array[1].Y, 2.0));
			}
			if (contacts.Count >= 3)
			{
				ReleaseDrag();
				scrolling = id;
				pairBegan = now;
			}
		}
		else
		{
			if (!contacts.TryGetValue(id, out Contact value))
			{
				return;
			}
			double num = x - value.X;
			double num2 = y - value.Y;
			value.X = x;
			value.Y = y;
			if (value.Travel > 6.0)
			{
				moved = true;
			}
			if (message == 581 && !ending && !customHeld)
			{
				if (maximum == 2 && contacts.Count == 2 && (bound(2, "PinchIn") || bound(2, "PinchOut")))
				{
					Contact[] array2 = contacts.Values.ToArray();
					double num3 = array2[0].X - array2[0].StartX;
					double num4 = array2[0].Y - array2[0].StartY;
					double num5 = array2[1].X - array2[1].StartX;
					double num6 = array2[1].Y - array2[1].StartY;
					double num7 = Math.Sqrt(Math.Pow(array2[0].X - array2[1].X, 2.0) + Math.Pow(array2[0].Y - array2[1].Y, 2.0));
					double num8 = Math.Sqrt(Math.Pow(array2[0].StartX - array2[1].StartX, 2.0) + Math.Pow(array2[0].StartY - array2[1].StartY, 2.0));
					double num9 = Math.Sqrt(Math.Pow((num3 + num5) / 2.0, 2.0) + Math.Pow((num4 + num6) / 2.0, 2.0));
					if (!pinching && Math.Abs(num7 - num8) > 12.0 && array2.All((Contact p) => p.Travel > 4.0) && num3 * num5 + num4 * num6 < 0.0 && Math.Abs(num7 - num8) > num9 * 1.3)
					{
						pinching = true;
						twoScroll = false;
						ReleaseDrag();
					}
					if (pinching)
					{
						int num10 = (int)((num7 - pinchDistance) / 12.0);
						if (num10 == 0)
						{
							return;
						}
						pinchDistance += num10 * 12;
						string arg = ((num10 > 0) ? "PinchOut" : "PinchIn");
						if (bound(2, arg))
						{
							for (int num11 = 0; num11 < Math.Abs(num10); num11++)
							{
								custom(2, arg);
							}
						}
						return;
					}
				}
				if (!customInvalid && HasSwipes(maximum))
				{
					ReleaseDrag();
					return;
				}
				if (maximum == 1)
				{
					emit(new PadEvent("Move", num, num2));
				}
				else if (maximum == 2 && contacts.Count == 2)
				{
					Contact value2 = contacts.First<KeyValuePair<uint, Contact>>((KeyValuePair<uint, Contact> p) => p.Key != id).Value;
					if (value.Travel > 6.0 && value2.Travel > 6.0)
					{
						double num12 = value.X - value.StartX;
						double num13 = value.Y - value.StartY;
						double num14 = value2.X - value2.StartX;
						double num15 = value2.Y - value2.StartY;
						if (num12 * num14 + num13 * num15 > 0.0)
						{
							ReleaseDrag();
							twoScroll = true;
						}
					}
					if (twoScroll)
					{
						emit(new PadEvent("Wheel", num * 1.5, (0.0 - num2) * 1.5));
					}
					else if (value.Travel > 6.0 && value2.Travel < 8.0 && now - pairBegan >= 130)
					{
						if (!dragging)
						{
							dragging = true;
							emit(new PadEvent("Down", 0.0, 0.0, 513));
						}
						emit(new PadEvent("Move", num, num2));
					}
				}
				else if (maximum == 3 && contacts.Count == 3 && id == scrolling)
				{
					Contact[] source = (from p in contacts
						where p.Key != id
						select p.Value).ToArray();
					if (now - pairBegan >= 130 && value.Travel > 6.0 && source.All((Contact p) => p.Travel < 8.0) && value.X >= source.Min((Contact p) => p.X) - 24.0 && value.X <= source.Max((Contact p) => p.X) + 24.0)
					{
						emit(new PadEvent("Wheel", 0.0, (0.0 - num2) * 3.0));
					}
				}
			}
			if (message != 583)
			{
				return;
			}
			if (dragging)
			{
				ReleaseDrag();
			}
			contacts.Remove(id);
			if (contacts.Count == 0)
			{
				int num16 = maximum;
				bool num17 = !moved && !customHeld && now - began <= 600;
				string text = null;
				if (!pinching && !customHeld && !customInvalid && moved && now - began <= 1800 && sequence.Count == maximum)
				{
					double num18 = sequence.Values.Average((Contact p) => p.X - p.StartX);
					double num19 = sequence.Values.Average((Contact p) => p.Y - p.StartY);
					bool horizontal = Math.Abs(num18) > Math.Abs(num19);
					double major = (horizontal ? num18 : num19);
					double value3 = (horizontal ? num19 : num18);
					if (Math.Abs(major) >= 40.0 && Math.Abs(major) > 1.4 * Math.Abs(value3) && sequence.Values.All((Contact p) => (horizontal ? (p.X - p.StartX) : (p.Y - p.StartY)) * (double)Math.Sign(major) >= 24.0))
					{
						text = ((!horizontal) ? ((major > 0.0) ? "Down" : "Up") : ((major > 0.0) ? "Right" : "Left"));
					}
				}
				ResetContacts();
				if (num17)
				{
					Tap(num16, now);
					return;
				}
				pendingTaps = 0;
				if (text != null && bound(num16, text))
				{
					custom(num16, text);
				}
			}
			else
			{
				ending = false;
				twoScroll = false;
				scrolling = 0u;
			}
		}
	}

	private void ReleaseDrag()
	{
		if (dragging)
		{
			dragging = false;
			emit(new PadEvent("Up", 0.0, 0.0, 513));
		}
	}

	private void ResetContacts()
	{
		ReleaseDrag();
		contacts.Clear();
		sequence.Clear();
		maximum = 0;
		ending = (customHeld = (customInvalid = (moved = (twoScroll = (pinching = false)))));
	}

	public void Reset()
	{
		ResetContacts();
		pendingTaps = 0;
	}
}
