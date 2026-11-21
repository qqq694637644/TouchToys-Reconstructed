using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.Win32;
using TouchAssistant.Core;
using TouchAssistant.Models;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class VirtualGamepad : IDisposable
{
	private sealed class Control : IDisposable
	{
		private Bitmap? iconImage;

		internal readonly IdleOpacity fade;

		private readonly VirtualGamepad parent;

		internal readonly GamepadControl option;

		private readonly Win32.WndProc callback;

		internal readonly nint handle;

		private readonly string cls;

		internal Win32.POINT position;

		private Win32.POINT start;

		private Win32.POINT initial;

		private uint pointer;

		private readonly TwoFingerDrag twoFinger = new TwoFingerDrag();

		private bool pressed;

		private bool mouse;

		private bool resizing;

		private double initialWidth;

		private double initialHeight;

		private int direction = -1;

		private double dx;

		private double dy;

		private Control? slideTarget;

		private bool centerCandidate;

		private bool pendingCenter;

		private long centerAt;

		private DispatcherQueueTimer? centerTimer;

		internal int ActiveDirection => direction;

		private double Scale => Win32.Scale(position);

		internal double Width
		{
			get
			{
				if (!option.Joystick)
				{
					return option.Width;
				}
				return option.Size;
			}
		}

		private double Height
		{
			get
			{
				if (option.Joystick)
				{
					return option.Size;
				}
				string style = option.Style;
				bool flag = ((style == "Circle" || style == "Square") ? true : false);
				return flag ? option.Width : option.Height;
			}
		}

		internal int Pixels => (int)Math.Ceiling(Width * Scale);

		internal int PixelHeight => (int)Math.Ceiling(Height * Scale);

		[DllImport("user32", CharSet = CharSet.Unicode)]
		private static extern bool UnregisterClass(string name, nint module);

		public Control(VirtualGamepad parent, GamepadControl option, int index)
		{
			Control control = this;
			this.parent = parent;
			this.option = option;
			string text = option.Appearance?.IconPath;
			if (text != null && File.Exists(text))
			{
				try
				{
					using Image original = Image.FromFile(text);
					iconImage = new Bitmap(original, new Size(64, 64));
				}
				catch
				{
				}
			}
			Win32.GetCursorPos(out var p);
			Win32.RECT rcWork = Win32.Monitor(p).rcWork;
			double num = Win32.Scale(p);
			int num2 = (int)(Width * num);
			position = new Win32.POINT(option.X ?? (option.Joystick ? (rcWork.Left + (int)((double)(40 + index % 4 * 88) * num)) : (rcWork.Right - num2 - (int)((double)(40 + index % 4 * 88) * num))), option.Y ?? (rcWork.Bottom - num2 - (int)((double)(40 + index / 4 * 100) * num)));
			if (option.IsLayoutSelector && !option.X.HasValue && !option.Y.HasValue)
			{
				position = new Win32.POINT(rcWork.Left + (rcWork.Width - num2) / 2, rcWork.Top + (int)(48.0 * num));
			}
			if (option.WorkWidth > 0 && option.WorkHeight > 0)
			{
				Win32.RECT rcWork2 = Win32.Monitor(new Win32.POINT(option.WorkLeft + option.WorkWidth / 2, option.WorkTop + option.WorkHeight / 2)).rcWork;
				position = new Win32.POINT((int)Math.Round(GamepadGeometry.Reflow(position.X, option.WorkLeft, option.WorkWidth, rcWork2.Left, rcWork2.Width, Width * Win32.Scale(position))), (int)Math.Round(GamepadGeometry.Reflow(position.Y, option.WorkTop, option.WorkHeight, rcWork2.Top, rcWork2.Height, Height * Win32.Scale(position))));
			}
			Constrain();
			option.X = position.X;
			option.Y = position.Y;
			fade = new IdleOpacity(() => option.Opacity, () => option.IdleOpacity, delegate
			{
				control.Draw();
			});
			cls = "TouchToys.Gamepad." + Guid.NewGuid().ToString("N");
			callback = Procedure;
			Win32.WNDCLASS wc = new Win32.WNDCLASS
			{
				lpfnWndProc = callback,
				hInstance = Win32.GetModuleHandle(null),
				hCursor = Win32.ArrowCursor,
				lpszClassName = cls
			};
			Win32.RegisterClass(ref wc);
			handle = Win32.CreateWindowEx(134742152u, cls, option.Name, 2147483648u, position.X, position.Y, Pixels, PixelHeight, 0, 0, wc.hInstance, 0);
			Win32.SuppressFeedback(handle);
			TouchMessages.Register(handle);
			Draw();
			Win32.ShowWindow(handle, 4);
			OverlayOrder.Register(handle, -10);
			fade.Active(held: false);
		}

		private void Constrain()
		{
			Win32.RECT rcWork = Win32.Monitor(position).rcWork;
			option.WorkLeft = rcWork.Left;
			option.WorkTop = rcWork.Top;
			option.WorkWidth = rcWork.Width;
			option.WorkHeight = rcWork.Height;
			position = new Win32.POINT(Math.Clamp(position.X, rcWork.Left, Math.Max(rcWork.Left, rcWork.Right - Pixels)), Math.Clamp(position.Y, rcWork.Top, Math.Max(rcWork.Top, rcWork.Bottom - PixelHeight)));
		}

		private void CenterClick()
		{
			if (option.CenterDoubleTap.Enabled && option.CenterDoubleTap.Kind != ActionKind.None)
			{
				if (pendingCenter && Environment.TickCount64 - centerAt <= 400)
				{
					pendingCenter = false;
					centerTimer?.Stop();
					parent.Trigger?.Invoke(option.CenterDoubleTap, new Win32.POINT(position.X + Pixels / 2, position.Y + PixelHeight / 2));
					return;
				}
				pendingCenter = true;
				centerAt = Environment.TickCount64;
				if ((object)centerTimer == null)
				{
					centerTimer = MakeCenterTimer();
				}
				centerTimer.Stop();
				centerTimer.Start();
			}
			else if (option.CenterTap.Enabled && option.CenterTap.Kind != ActionKind.None)
			{
				parent.Trigger?.Invoke(option.CenterTap, new Win32.POINT(position.X + Pixels / 2, position.Y + PixelHeight / 2));
			}
		}

		private DispatcherQueueTimer MakeCenterTimer()
		{
			DispatcherQueueTimer dispatcherQueueTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
			dispatcherQueueTimer.IsRepeating = false;
			dispatcherQueueTimer.Interval = TimeSpan.FromMilliseconds(400.0);
			dispatcherQueueTimer.Tick += delegate
			{
				if (pendingCenter)
				{
					pendingCenter = false;
					if (option.CenterTap.Enabled && option.CenterTap.Kind != ActionKind.None)
					{
						parent.Trigger?.Invoke(option.CenterTap, new Win32.POINT(position.X + Pixels / 2, position.Y + PixelHeight / 2));
					}
				}
			};
			return dispatcherQueueTimer;
		}

		private void Change(int next)
		{
			if (direction == next)
			{
				return;
			}
			direction = next;
			if (next < 0)
			{
				parent.keys.Set(this, Array.Empty<int>());
				return;
			}
			GestureAction gestureAction = (option.Joystick ? option.Directions[next] : option.Action);
			if (!gestureAction.Enabled)
			{
				parent.keys.Set(this, Array.Empty<int>());
				return;
			}
			if (gestureAction.Kind == ActionKind.Keys)
			{
				parent.keys.Set(this, gestureAction.Keys);
				return;
			}
			parent.keys.Set(this, Array.Empty<int>());
			if (gestureAction.Kind != ActionKind.None)
			{
				parent.Trigger?.Invoke(gestureAction, new Win32.POINT(position.X + Pixels / 2, position.Y + Pixels / 2));
			}
		}

		internal void Point(uint msg, uint id, Win32.POINT point, bool cancel = false)
		{
			if (cancel || parent.suppressed)
			{
				Cancel();
				return;
			}
			switch (msg)
			{
			case 582u:
				fade.Active(held: true);
				break;
			case 583u:
				fade.Active(held: false);
				break;
			}
			if (id != 0 && twoFinger.Feed(msg, id, point.X, point.Y, delegate
			{
				centerTimer?.Stop();
				pendingCenter = false;
				centerCandidate = false;
				Release();
			}, delegate(int x, int y)
			{
				position = new Win32.POINT(position.X + x, position.Y + y);
				Constrain();
				option.X = position.X;
				option.Y = position.Y;
				Draw();
			}, delegate
			{
				parent.store.Save();
			}))
			{
				return;
			}
			bool flag = pressed && pointer == id && !parent.Editing && !option.Joystick && !option.IsLayoutSelector && parent.store.Value.Gamepad.SlideInput;
			if (flag)
			{
				bool flag2 = ((msg == 581 || msg == 583) ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				Control control = ((msg == 583) ? null : parent.controls.LastOrDefault((Control c) => !c.option.Joystick && !c.option.IsLayoutSelector && (!c.pressed || c == this || c == slideTarget) && point.X >= c.position.X && point.X < c.position.X + c.Pixels && point.Y >= c.position.Y && point.Y < c.position.Y + c.PixelHeight));
				if (slideTarget != control)
				{
					if (slideTarget != null && slideTarget != this)
					{
						slideTarget.Release();
					}
					Change(-1);
					slideTarget = control;
					if (control != null)
					{
						control.pressed = true;
						control.fade.Active(held: true);
						control.Change(0);
						control.Draw();
					}
				}
				if (msg == 583)
				{
					Release();
					slideTarget = null;
				}
				else
				{
					Draw();
				}
				return;
			}
			if (msg == 582)
			{
				if (pressed)
				{
					return;
				}
				pressed = true;
				pointer = id;
				start = point;
				centerCandidate = option.Joystick && Math.Pow((double)(point.X - position.X) / Scale - option.Size / 2.0, 2.0) + Math.Pow((double)(point.Y - position.Y) / Scale - option.Size / 2.0, 2.0) <= Math.Pow(option.Size * 0.18, 2.0);
				initial = position;
				resizing = parent.Editing && (double)(point.X - position.X) > (double)Pixels - 32.0 * Scale && (double)(point.Y - position.Y) > (double)PixelHeight - 32.0 * Scale;
				initialWidth = Width;
				initialHeight = Height;
				if (!parent.Editing && !option.Joystick && !option.IsLayoutSelector)
				{
					slideTarget = this;
					Change(0);
				}
			}
			if (!pressed || pointer != id)
			{
				return;
			}
			if ((double)(Math.Abs(point.X - start.X) + Math.Abs(point.Y - start.Y)) > 8.0 * Scale)
			{
				centerCandidate = false;
			}
			if (parent.Editing)
			{
				if (resizing)
				{
					if (option.Joystick)
					{
						option.Size = Math.Clamp(initialWidth + (double)Math.Max(point.X - start.X, point.Y - start.Y) / Scale, 48.0, 400.0);
					}
					else
					{
						option.Width = Math.Clamp(initialWidth + (double)(point.X - start.X) / Scale, 20.0, 600.0);
						option.Height = Math.Clamp(initialHeight + (double)(point.Y - start.Y) / Scale, 20.0, 400.0);
					}
				}
				else
				{
					position = new Win32.POINT(initial.X + point.X - start.X, initial.Y + point.Y - start.Y);
					Constrain();
					option.X = position.X;
					option.Y = position.Y;
				}
			}
			else if (option.Joystick)
			{
				double num = option.Size / 2.0;
				dx = (double)(point.X - position.X) / Scale - num;
				dy = (double)(point.Y - position.Y) / Scale - num;
				Change(GamepadGeometry.Direction(dx, dy, num));
				double num2 = Math.Sqrt(dx * dx + dy * dy);
				double num3 = num * 0.56;
				if (num2 > num3)
				{
					dx *= num3 / num2;
					dy *= num3 / num2;
				}
			}
			if (msg == 583)
			{
				bool num4 = (double)(Math.Abs(point.X - start.X) + Math.Abs(point.Y - start.Y)) < 8.0 * Scale;
				Release();
				if (num4)
				{
					if (parent.Editing && !option.IsLayoutSelector)
					{
						parent.Selected?.Invoke(option);
					}
					else if (!parent.Editing && option.IsLayoutSelector)
					{
						parent.NextLayout();
					}
					else if (!parent.Editing && centerCandidate)
					{
						CenterClick();
					}
				}
			}
			else
			{
				Draw();
			}
		}

		public void Cancel()
		{
			centerTimer?.Stop();
			pendingCenter = false;
			twoFinger.Reset();
			Release();
		}

		public void Release()
		{
			Control control = slideTarget;
			slideTarget = null;
			if (control != null && control != this)
			{
				control.Release();
			}
			parent.keys.Set(this, Array.Empty<int>());
			pressed = (mouse = false);
			direction = -1;
			dx = (dy = 0.0);
			fade.Active(held: false);
			Draw();
		}

		private nint Procedure(nint h, uint msg, nuint w, nint l)
		{
			try
			{
				if ((msg == 33 || msg == 587) ? true : false)
				{
					return 3;
				}
				bool flag;
				switch (msg)
				{
				case 576u:
					TouchMessages.Read(w, l, delegate(uint id, uint flags, Win32.POINT point)
					{
						Point(((flags & 2) != 0) ? 582u : (((flags & 4) != 0) ? 583u : 581u), id, point);
					});
					return 0;
				case 581u:
				case 582u:
				case 583u:
					flag = true;
					break;
				default:
					flag = false;
					break;
				}
				if (flag)
				{
					flag = Win32.GetPointerInfo((uint)((int)w & 0xFFFF), out var info);
					if (flag)
					{
						uint pointerType = info.pointerType;
						bool flag2 = pointerType - 2 <= 1;
						flag = flag2;
					}
					if (flag)
					{
						Point(msg, info.pointerId, info.ptPixelLocation, (info.pointerFlags & 0x8000) != 0);
					}
					return 0;
				}
				if (msg - 512 <= 2)
				{
					if (((ulong)Win32.GetMessageExtraInfo() & 0xFFFFFF00uL) == 4283520768u)
					{
						return 0;
					}
					Win32.GetCursorPos(out var p);
					if (msg == 513)
					{
						mouse = true;
						Win32.SetCapture(h);
						Point(582u, 0u, p);
					}
					else if (mouse)
					{
						Point((msg == 514) ? 583u : 581u, 0u, p);
						if (msg == 514)
						{
							Win32.ReleaseCapture();
						}
					}
					return 0;
				}
				if ((msg == 533 || msg == 588) ? true : false)
				{
					Cancel();
				}
			}
			catch (Exception ex)
			{
				parent.keys.Clear();
				Diagnostics.Error(ex, "Gamepad.Input");
				App.CurrentApp.Report("虚拟按键已释放：" + ex.Message);
			}
			return Win32.DefWindowProc(h, msg, w, l);
		}

		public void Draw(string? capture = null)
		{
			if (handle == 0)
			{
				return;
			}
			int pixels = Pixels;
			using Bitmap bitmap = new Bitmap(pixels, PixelHeight, PixelFormat.Format32bppPArgb);
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.SmoothingMode = SmoothingMode.AntiAlias;
				graphics.ScaleTransform((float)Scale, (float)Scale);
				float num = (float)Width;
				float num2 = (float)Height;
				Color baseColor = ColorTranslator.FromHtml(option.BackgroundColor);
				using SolidBrush brush = new SolidBrush(Color.FromArgb((pressed && !parent.Editing) ? 115 : 65, baseColor));
				using GraphicsPath graphicsPath = new GraphicsPath();
				if (option.Joystick || option.Style == "Circle")
				{
					graphicsPath.AddEllipse(2f, 2f, num - 4f, num2 - 4f);
				}
				else
				{
					float num3 = (float)Math.Clamp(option.CornerRadius, 0.0, Math.Min(num, num2) / 2f - 2f);
					if (num3 < 1f)
					{
						graphicsPath.AddRectangle(new RectangleF(2f, 2f, num - 4f, num2 - 4f));
					}
					else
					{
						float num4 = num3 * 2f;
						graphicsPath.AddArc(2f, 2f, num4, num4, 180f, 90f);
						graphicsPath.AddArc(num - 2f - num4, 2f, num4, num4, 270f, 90f);
						graphicsPath.AddArc(num - 2f - num4, num2 - 2f - num4, num4, num4, 0f, 90f);
						graphicsPath.AddArc(2f, num2 - 2f - num4, num4, num4, 90f, 90f);
						graphicsPath.CloseFigure();
					}
				}
				graphics.FillPath(brush, graphicsPath);
				using Pen pen = new Pen(Color.FromArgb(parent.Editing ? 230 : 160, ColorTranslator.FromHtml(option.OutlineColor)), parent.Editing ? 2f : 1.2f);
				if (parent.Editing)
				{
					pen.DashStyle = DashStyle.Dash;
				}
				graphics.DrawPath(pen, graphicsPath);
				if (parent.Editing)
				{
					using SolidBrush brush2 = new SolidBrush(Color.FromArgb(90, 90, 100, 110));
					graphics.FillRectangle(brush2, num - 32f, num2 - 32f, 30f, 30f);
					graphics.DrawLine(pen, num - 18f, num2 - 6f, num - 6f, num2 - 18f);
					graphics.DrawLine(pen, num - 12f, num2 - 6f, num - 6f, num2 - 12f);
				}
				if (option.Joystick)
				{
					float num5 = num * 0.18f;
					float num6 = num / 2f + (float)dx;
					float num7 = num / 2f + (float)dy;
					using SolidBrush brush3 = new SolidBrush(Color.FromArgb(100, ColorTranslator.FromHtml(option.CenterColor)));
					graphics.FillEllipse(brush3, num6 - num5, num7 - num5, num5 * 2f, num5 * 2f);
					graphics.DrawEllipse(pen, num6 - num5, num7 - num5, num5 * 2f, num5 * 2f);
				}
				using Font font = IconFontResolver.CreateTextFont("Microsoft YaHei UI", Math.Clamp(num * 0.23f, 12f, 24f), GraphicsUnit.Pixel, "Microsoft YaHei", "DengXian", "Segoe UI");
				GestureAction appearance = option.Appearance;
				using SolidBrush brush4 = new SolidBrush(Color.FromArgb((int)((appearance?.TextOpacity ?? 0.9) * 255.0), ColorTranslator.FromHtml(appearance?.TextColor ?? "#FFFFFF")));
				using StringFormat format = new StringFormat
				{
					Alignment = StringAlignment.Center,
					LineAlignment = StringAlignment.Center,
					Trimming = StringTrimming.EllipsisCharacter,
					FormatFlags = StringFormatFlags.NoWrap
				};
				object obj;
				if (!option.IsLayoutSelector)
				{
					if (appearance == null || appearance.Label.Length <= 0)
					{
						if (!option.Joystick)
						{
							if (!option.Name.StartsWith("按钮 "))
							{
								obj = option.Name;
							}
							else
							{
								string name = option.Name;
								obj = name.Substring(3, name.Length - 3);
							}
						}
						else
						{
							obj = "";
						}
					}
					else
					{
						obj = appearance.Label;
					}
				}
				else
				{
					obj = option.Text;
				}
				string text = (string)obj;
				string text2 = appearance?.Icon ?? option.Icon;
				if (iconImage != null)
				{
					graphics.DrawImage(iconImage, num / 2f - 12f, num2 / 2f - 20f, 24f, 24f);
				}
				else if (text2.Length > 0)
				{
					using Font font2 = IconFontResolver.CreateGdiFont(text2, Math.Clamp(num2 * 0.3f, 16f, 28f), GraphicsUnit.Pixel, appearance?.IconFont);
					graphics.DrawString(text2, font2, brush4, new RectangleF(6f, (text.Length > 0) ? ((0f - num2) * 0.17f) : 0f, num - 12f, num2), format);
				}
				graphics.DrawString(text, font, brush4, new RectangleF(6f, (text2.Length > 0 || iconImage != null) ? (num2 * 0.23f) : 0f, num - 12f, num2), format);
			}
			if (capture != null)
			{
				bitmap.Save(capture, ImageFormat.Png);
			}
			nint dC = Win32.GetDC(0);
			nint num8 = Win32.CreateCompatibleDC(dC);
			nint hbitmap = bitmap.GetHbitmap(Color.FromArgb(0));
			nint o = Win32.SelectObject(num8, hbitmap);
			try
			{
				Win32.SIZE size = new Win32.SIZE(pixels, PixelHeight);
				Win32.POINT origin = default(Win32.POINT);
				Win32.BLENDFUNCTION blend = new Win32.BLENDFUNCTION
				{
					SourceConstantAlpha = (byte)Math.Round((parent.Editing ? Math.Max(0.75, option.Opacity) : fade.Value) * 255.0),
					AlphaFormat = 1
				};
				Win32.UpdateLayeredWindow(handle, dC, ref position, ref size, num8, ref origin, 0u, ref blend, 2u);
			}
			finally
			{
				Win32.SelectObject(num8, o);
				Win32.DeleteObject(hbitmap);
				Win32.DeleteDC(num8);
				Win32.ReleaseDC(0, dC);
			}
		}

		public void Dispose()
		{
			Cancel();
			fade.Dispose();
			iconImage?.Dispose();
			OverlayOrder.Remove(handle);
			Win32.DestroyWindow(handle);
			UnregisterClass(cls, Win32.GetModuleHandle(null));
		}
	}

	private readonly bool recording;

	private int? manualLayout;

	private readonly SettingsStore store;

	private readonly List<Control> controls = new List<Control>();

	private readonly GamepadKeys keys;

	private string stamp = "";

	private bool suppressed;

	private bool disposed;

	private readonly List<GamepadShield> shields = new List<GamepadShield>();

	private readonly GamepadKeyboard keyboard = new GamepadKeyboard();

	private readonly DispatcherQueue displayQueue = DispatcherQueue.GetForCurrentThread();

	internal bool IsSuppressed => suppressed;

	internal int ShieldCount => shields.Count;

	public bool Editing { get; private set; }

	public event Action<GestureAction, Win32.POINT>? Trigger;

	public event Action<GamepadControl>? Selected;

	public event Action? LayoutChanged;

	private void DisplayChanged(object? sender, EventArgs args)
	{
		displayQueue.TryEnqueue(delegate
		{
			if (!disposed)
			{
				stamp = "";
				Sync();
				store.Save();
			}
		});
	}

	public void SetSuppressed(bool value)
	{
		if (disposed || suppressed == value)
		{
			return;
		}
		suppressed = value;
		foreach (GamepadShield shield in shields)
		{
			shield.SetVisible(!value);
		}
		foreach (Control control in controls)
		{
			control.Cancel();
			Win32.ShowWindow(control.handle, (!value) ? 4 : 0);
			if (value)
			{
				control.fade.Pause();
			}
			else
			{
				control.fade.Active(held: false);
			}
		}
		if (!value)
		{
			OverlayOrder.RaiseAll();
		}
	}

	private void NextLayout()
	{
		DispatcherQueue.GetForCurrentThread().TryEnqueue(delegate
		{
			if (!disposed)
			{
				GamepadSettings gamepad = store.Value.Gamepad;
				manualLayout = ((manualLayout ?? gamepad.Layouts.IndexOf(ApplicationBindings.Layout(gamepad))) + 1) % gamepad.Layouts.Count;
				gamepad.SelectLayout(manualLayout.Value);
				store.Save();
				this.LayoutChanged?.Invoke();
			}
		});
	}

	public VirtualGamepad(SettingsStore store, Action<IReadOnlyList<int>, bool>? sender = null)
	{
		this.store = store;
		recording = sender != null;
		keys = new GamepadKeys(sender ?? new Action<IReadOnlyList<int>, bool>(SystemControl.SetKeysHeld));
		OverlayOrder.ForegroundChanged += Foreground;
		SystemEvents.DisplaySettingsChanged += DisplayChanged;
		Sync();
	}

	private void Foreground(nint h)
	{
		if (disposed)
		{
			return;
		}
		Win32.GetWindowThreadProcessId(h, out var process);
		if (process != Environment.ProcessId)
		{
			manualLayout = null;
		}
		if (!suppressed)
		{
			foreach (GamepadShield shield in shields)
			{
				shield.SetVisible(visible: true);
			}
			foreach (Control control in controls)
			{
				OverlayOrder.Raise(control.handle);
			}
		}
		foreach (Control control2 in controls)
		{
			control2.Cancel();
		}
		keys.Clear();
		if (!recording && store.Value.Gamepad.AutoSwitchEnglish)
		{
			GamepadInputLanguage.EnsureEnglish();
		}
		Sync();
	}

	public void Sync()
	{
		if (disposed)
		{
			return;
		}
		GamepadLayout gamepadLayout;
		if (!Editing)
		{
			int? num = manualLayout;
			if (num.HasValue)
			{
				int valueOrDefault = num.GetValueOrDefault();
				if (valueOrDefault < store.Value.Gamepad.Layouts.Count)
				{
					gamepadLayout = store.Value.Gamepad.Layouts[valueOrDefault];
					goto IL_00a9;
				}
			}
			gamepadLayout = ApplicationBindings.Layout(store.Value.Gamepad);
		}
		else
		{
			gamepadLayout = store.Value.Gamepad.Layouts[store.Value.Gamepad.ActiveLayout];
		}
		goto IL_00a9;
		IL_00a9:
		GamepadLayout gamepadLayout2 = gamepadLayout;
		string text = JsonSerializer.Serialize(new
		{
			Gamepad = store.Value.Gamepad,
			Active = store.Value.Gamepad.Layouts.IndexOf(gamepadLayout2)
		});
		if (text == stamp)
		{
			return;
		}
		stamp = text;
		foreach (GamepadShield shield in shields)
		{
			shield.Dispose();
		}
		shields.Clear();
		foreach (Control control3 in controls)
		{
			control3.Dispose();
		}
		controls.Clear();
		keys.Clear();
		if (!store.Value.Gamepad.Enabled)
		{
			Editing = false;
		}
		if (store.Value.Gamepad.Enabled)
		{
			keyboard.ScanCodes = true;
			foreach (GamepadShieldSettings shield2 in gamepadLayout2.Shields)
			{
				if (shield2.Enabled)
				{
					GamepadShield gamepadShield = new GamepadShield(shield2, shields.Count);
					shields.Add(gamepadShield);
					gamepadShield.SetEditing(Editing);
					gamepadShield.SetVisible(!suppressed);
				}
			}
		}
		if (store.Value.Gamepad.Enabled)
		{
			for (int i = 0; i < gamepadLayout2.Controls.Count; i++)
			{
				Control control = new Control(this, gamepadLayout2.Controls[i], i);
				controls.Add(control);
				if (suppressed)
				{
					Win32.ShowWindow(control.handle, 0);
				}
			}
		}
		if (store.Value.Gamepad.Enabled && store.Value.Gamepad.ShowLayoutSelector)
		{
			GamepadControl layoutSelector = store.Value.Gamepad.LayoutSelector;
			layoutSelector.IsLayoutSelector = true;
			layoutSelector.Text = gamepadLayout2.Name + "  ›";
			Control control2 = new Control(this, layoutSelector, 0);
			controls.Add(control2);
			if (suppressed)
			{
				Win32.ShowWindow(control2.handle, 0);
			}
		}
		OverlayOrder.RaiseAll();
	}

	public void SetEditing(bool value)
	{
		foreach (Control control in controls)
		{
			control.Cancel();
		}
		keys.Clear();
		Editing = value;
		Sync();
		foreach (GamepadShield shield in shields)
		{
			shield.SetEditing(value);
		}
		foreach (Control control2 in controls)
		{
			control2.Draw();
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}
		disposed = true;
		foreach (GamepadShield shield in shields)
		{
			shield.Dispose();
		}
		shields.Clear();
		OverlayOrder.ForegroundChanged -= Foreground;
		SystemEvents.DisplaySettingsChanged -= DisplayChanged;
		foreach (Control control in controls)
		{
			control.Dispose();
		}
		controls.Clear();
		keys.Dispose();
	}

	internal void CheckSlideInput()
	{
		Control[] array = controls.Where((Control c) => !c.option.Joystick && !c.option.IsLayoutSelector).Take(2).ToArray();
		if (array.Length != 2)
		{
			throw new InvalidOperationException("Need two buttons");
		}
		Control control = array[0];
		Control control2 = array[1];
		Win32.POINT position = control2.position;
		try
		{
			control2.position = new Win32.POINT(control.position.X + control.Pixels + 40, control.position.Y);
			Win32.POINT point = new Win32.POINT(control.position.X + control.Pixels / 2, control.position.Y + control.PixelHeight / 2);
			Win32.POINT point2 = new Win32.POINT(control2.position.X + control2.Pixels / 2, control2.position.Y + control2.PixelHeight / 2);
			control.Point(582u, 101u, point);
			if (control.ActiveDirection != 0)
			{
				throw new InvalidOperationException("Initial press missing");
			}
			control.Point(581u, 101u, point2);
			if (control.ActiveDirection != -1 || control2.ActiveDirection != 0)
			{
				throw new InvalidOperationException("Slide did not transfer input");
			}
			control.Point(581u, 101u, new Win32.POINT(control.position.X - 20, control.position.Y - 20));
			if (control2.ActiveDirection != -1)
			{
				throw new InvalidOperationException("Leaving buttons retained input");
			}
			control.Point(581u, 101u, point);
			if (control.ActiveDirection != 0)
			{
				throw new InvalidOperationException("Slide reentry missing");
			}
			control.Point(583u, 101u, point);
			if (control.ActiveDirection != -1 || control2.ActiveDirection != -1)
			{
				throw new InvalidOperationException("Lift retained input");
			}
			control.Point(582u, 102u, point);
			control.Point(581u, 102u, point2);
			control.Cancel();
			if (control2.ActiveDirection != -1)
			{
				throw new InvalidOperationException("Cancel retained transferred input");
			}
		}
		finally
		{
			control.Cancel();
			control2.Cancel();
			control2.position = position;
		}
	}

	internal async Task CheckCenterClicksAsync()
	{
		Control control = controls.First((Control c) => c.option.Joystick);
		List<GestureAction> received = new List<GestureAction>();
		Trigger += Record;
		GestureAction tap = control.option.CenterTap;
		GestureAction twice = control.option.CenterDoubleTap;
		Win32.POINT point;
		try
		{
			control.option.CenterTap = GestureAction.BuiltIn("Desktop");
			control.option.CenterDoubleTap = GestureAction.BuiltIn("StartMenu");
			point = new Win32.POINT(control.position.X + control.Pixels / 2, control.position.Y + control.PixelHeight / 2);
			Click();
			await Task.Delay(450);
			if (received.Count != 1 || received[0] != control.option.CenterTap)
			{
				throw new InvalidOperationException("中心单击失败");
			}
			received.Clear();
			Click();
			Click();
			await Task.Delay(450);
			if (received.Count != 1 || received[0] != control.option.CenterDoubleTap)
			{
				throw new InvalidOperationException("中心双击重复触发");
			}
			received.Clear();
			Click();
			control.Cancel();
			await Task.Delay(450);
			if (received.Count != 0)
			{
				throw new InvalidOperationException("取消后延迟单击仍触发");
			}
			control.Point(582u, 101u, point);
			control.Point(581u, 101u, new Win32.POINT(point.X + 40, point.Y));
			control.Point(583u, 101u, point);
			await Task.Delay(450);
			if (received.Count != 0)
			{
				throw new InvalidOperationException("拖动被识别成单击");
			}
		}
		finally
		{
			control.Cancel();
			control.option.CenterTap = tap;
			control.option.CenterDoubleTap = twice;
			Trigger -= Record;
		}
		void Click()
		{
			control.Point(582u, 101u, point);
			control.Point(583u, 101u, point);
		}
		void Record(GestureAction action, Win32.POINT pOINT)
		{
			received.Add(action);
		}
	}

	internal void CheckV0822()
	{
		SetEditing(value: true);
		foreach (Control control2 in controls)
		{
			double width = control2.Width;
			Win32.POINT point = new Win32.POINT(control2.position.X + control2.Pixels - 8, control2.position.Y + control2.PixelHeight - 8);
			control2.Point(582u, 0u, point);
			control2.Point(581u, 0u, new Win32.POINT(point.X + 20, point.Y + 20));
			control2.Point(583u, 0u, new Win32.POINT(point.X + 20, point.Y + 20));
			if (control2.Width <= width)
			{
				throw new InvalidOperationException("控件拖动缩放失败");
			}
		}
		SetEditing(value: false);
		Control control = controls[0];
		Win32.POINT position = control.position;
		Win32.POINT point2 = new Win32.POINT(position.X + control.Pixels / 2, position.Y + control.PixelHeight / 2);
		control.Point(582u, 501u, point2);
		control.Point(582u, 502u, new Win32.POINT(point2.X + 20, point2.Y));
		control.Point(581u, 501u, new Win32.POINT(point2.X + 30, point2.Y));
		control.Point(581u, 502u, new Win32.POINT(point2.X + 50, point2.Y));
		control.Point(583u, 501u, new Win32.POINT(point2.X + 30, point2.Y));
		control.Point(583u, 502u, new Win32.POINT(point2.X + 50, point2.Y));
		if (control.position.X <= position.X)
		{
			throw new InvalidOperationException("双指没有移动控件");
		}
	}

	internal void CheckV0821()
	{
		SetEditing(value: true);
		Control control = controls.First((Control c) => !c.option.IsLayoutSelector);
		GamepadControl selected = null;
		Selected += delegate(GamepadControl c)
		{
			selected = c;
		};
		Win32.POINT point = new Win32.POINT(control.position.X + control.Pixels / 2, control.position.Y + control.PixelHeight / 2);
		control.Point(582u, 201u, point);
		control.Point(583u, 201u, point);
		if (selected != control.option)
		{
			throw new InvalidOperationException("编辑点击没有定位事件");
		}
		Control control2 = controls.Single((Control c) => c.option.IsLayoutSelector);
		double width = control2.option.Width;
		point = new Win32.POINT(control2.position.X + control2.Pixels - 4, control2.position.Y + control2.PixelHeight - 4);
		control2.Point(582u, 202u, point);
		control2.Point(581u, 202u, new Win32.POINT(point.X + 24, point.Y + 12));
		control2.Point(583u, 202u, new Win32.POINT(point.X + 24, point.Y + 12));
		if (control2.option.Width <= width)
		{
			throw new InvalidOperationException("布局选择按钮无法调整大小");
		}
		SetEditing(value: false);
	}

	internal void CheckControls(string directory)
	{
		if (controls.Count < 2)
		{
			throw new InvalidOperationException("缺少默认控件。");
		}
		foreach (Control control3 in controls)
		{
			control3.Draw(Path.Combine(directory, control3.option.Joystick ? "gamepad-stick-v0818.png" : "gamepad-button-v0818.png"));
		}
		SetEditing(value: true);
		Control control = controls[0];
		Win32.POINT position = control.position;
		Win32.POINT point = new Win32.POINT(position.X + control.Pixels / 2, position.Y + control.Pixels / 2);
		control.Point(582u, 99u, point);
		control.Point(581u, 99u, new Win32.POINT(point.X + 20, point.Y - 20));
		control.Point(583u, 99u, new Win32.POINT(point.X + 20, point.Y - 20));
		if (control.option.X == position.X && control.option.Y == position.Y)
		{
			throw new InvalidOperationException("编辑模式不能移动控件。");
		}
		SetEditing(value: false);
		Control control2 = controls.First((Control c) => !c.option.Joystick);
		point = new Win32.POINT(control2.position.X + control2.Pixels / 2, control2.position.Y + control2.Pixels / 2);
		control2.Point(582u, 98u, point);
		control2.Point(583u, 98u, point);
	}

	internal void CheckStyles(string directory)
	{
		if (shields.Count == 0)
		{
			throw new InvalidOperationException("防误触区域未创建");
		}
		GamepadShield gamepadShield = shields[0];
		nint foregroundWindow = Win32.GetForegroundWindow();
		SetEditing(value: true);
		gamepadShield.Point(582u, 501u, new Win32.POINT(400, 400));
		gamepadShield.Point(581u, 501u, new Win32.POINT(420, 380));
		gamepadShield.Point(583u, 501u, new Win32.POINT(420, 380));
		SetEditing(value: false);
		if (Win32.GetForegroundWindow() != foregroundWindow)
		{
			throw new InvalidOperationException("防误触区域改变了焦点");
		}
		Control control = controls.First((Control c) => c.option.Joystick);
		Win32.POINT point = new Win32.POINT(control.position.X + control.Pixels / 2, control.position.Y + control.Pixels / 8);
		control.Point(582u, 301u, point);
		control.Draw(Path.Combine(directory, "joystick-direction-v0819.png"));
		control.Point(583u, 301u, point);
		foreach (Control item in controls.Where((Control c) => !c.option.Joystick))
		{
			item.Draw(Path.Combine(directory, "button-" + item.option.Style + "-v0819.png"));
		}
		SetSuppressed(value: true);
		if (!IsSuppressed)
		{
			throw new InvalidOperationException("面板未隐藏控件");
		}
		SetSuppressed(value: false);
	}
}
