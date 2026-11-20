using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Automation;
using Microsoft.UI.Dispatching;
using TouchAssistant.Models;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal sealed class SystemInputControl : IDisposable
{
	internal record Bounds(int X, int Y, int Width, int Height);

	internal record Candidate(long Handle, long Parent, int Pid, long Started, string Path, string Class, bool Visible, Bounds Rect, long Style, long ExStyle, uint Dpi, Bounds? Dwm, string Integrity)
	{
		public string Title => "[omitted: may contain input]";

		public override string ToString()
		{
			return $"{System.IO.Path.GetFileName(Path)} · {Class} · {Rect.Width}×{Rect.Height} · 0x{Handle:X}";
		}
	}

	internal record Backup(Candidate Target, uint Color, byte Alpha, uint Flags, bool Opacity, bool Position, long Marker);

	private delegate bool EnumCallback(nint hwnd, nint param);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct MonitorInfo
	{
		public int Size;

		public Win32.RECT Monitor;

		public Win32.RECT Work;

		public uint Flags;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string Device;
	}

	private delegate void EventCallback(nint hook, uint ev, nint hwnd, int obj, int child, uint tid, uint time);

	private const string MarkerName = "TouchAssistant.NativeInput.Identity";

	private readonly string journal;

	private readonly List<object> events = new List<object>();

	private List<Candidate> baseline = new List<Candidate>();

	private bool baselineTaken;

	private bool disposed;

	private bool busy;

	private int experimentGeneration;

	private readonly object mutationGate = new object();

	private Backup? backup;

	private Candidate? target;

	private static readonly List<SystemInputControl> owners = new List<SystemInputControl>();

	private nint hook;

	private readonly EventCallback callback;

	private readonly DispatcherQueue dispatcher;

	private readonly DispatcherQueueTimer geometryCheck;

	private static readonly JsonSerializerOptions Json = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	private SettingsStore? settings;

	internal string RecoveryPath => journal;

	public Candidate? Target => target;

	public bool OpacityVerified { get; private set; }

	public bool OpacityPassed { get; private set; }

	public bool Busy => busy;

	public string Status { get; private set; } = "打开系统触摸键盘后自动应用；也可在“识别与恢复”中手动选择。";

	public string Layout { get; set; } = "默认键盘";

	private bool AutoApply
	{
		get
		{
			if (settings != null)
			{
				if (!(Layout == "系统触摸板"))
				{
					return settings.Value.AutoNativeInputOpacity;
				}
				return settings.Value.AutoTouchpadOpacity;
			}
			return false;
		}
	}

	public event Action? Changed;

	private bool Available(Candidate c)
	{
		return !owners.Any(delegate(SystemInputControl o)
		{
			if (o != this)
			{
				Candidate candidate = o.target;
				if ((object)candidate != null)
				{
					if (candidate.Handle != c.Handle && candidate.Handle != c.Parent)
					{
						return candidate.Parent == c.Handle;
					}
					return true;
				}
			}
			return false;
		});
	}

	public void ResetControl()
	{
		Restore();
		RestoreSaved(Path.GetDirectoryName(journal), Path.GetFileName(journal));
		target = null;
		baselineTaken = false;
		OpacityPassed = false;
		if (File.Exists(journal + ".stop"))
		{
			File.Delete(journal + ".stop");
		}
		SetStatus("已恢复原始透明度。再次打开输入面板时可自动识别并应用。");
	}

	public SystemInputControl(string directory, bool touchpad = false)
	{
		if (touchpad)
		{
			Status = "打开系统触摸板后尝试自动应用；识别不准确时可手动选择窗口。";
		}
		owners.Add(this);
		Layout = (touchpad ? "系统触摸板" : "默认键盘");
		journal = Path.Combine(directory, touchpad ? "touchpad-restore.json" : "native-input-restore.json");
		dispatcher = DispatcherQueue.GetForCurrentThread();
		callback = OnEvent;
		geometryCheck = dispatcher.CreateTimer();
		geometryCheck.IsRepeating = false;
		geometryCheck.Interval = TimeSpan.FromMilliseconds(250.0);
		geometryCheck.Tick += delegate
		{
			CheckGeometry();
		};
		hook = SetWinEventHook(32768u, 32779u, 0, callback, 0u, 0u, 0u);
	}

	private void Note(string operation, object detail)
	{
		if (events.Count >= 120)
		{
			events.RemoveAt(0);
		}
		events.Add(new
		{
			At = DateTimeOffset.Now,
			Operation = operation,
			Detail = detail
		});
		WriteReport();
	}

	private void SetStatus(string value)
	{
		if (!(Status == value))
		{
			Status = value;
			this.Changed?.Invoke();
		}
	}

	public string WriteReport()
	{
		string value = JsonSerializer.Serialize(new
		{
			OS = RuntimeInformation.OSDescription,
			Architecture = RuntimeInformation.ProcessArchitecture.ToString(),
			Layout = Layout,
			Status = Status,
			BaselineTaken = baselineTaken,
			Baseline = baseline,
			Target = target,
			OpacityPassed = OpacityPassed,
			OpacityVerified = OpacityVerified,
			Events = events
		}, Json);
		Directory.CreateDirectory(Diagnostics.DirectoryPath);
		string text = Path.Combine(Diagnostics.DirectoryPath, (Layout == "系统触摸板") ? "native-touchpad.json" : "native-input.json");
		File.WriteAllText(text, Diagnostics.Redact(value));
		return text;
	}

	public void ClearTarget()
	{
		RequireIdle();
		Restore();
		target = null;
		OpacityPassed = false;
		SetStatus("布局已切换，请重新记录隐藏状态并扫描。");
		baselineTaken = false;
	}

	public void Baseline()
	{
		RequireIdle();
		Restore();
		if (File.Exists(journal + ".stop"))
		{
			File.Delete(journal + ".stop");
		}
		target = null;
		baseline = Discover();
		baselineTaken = true;
		Note("HiddenSnapshot", baseline);
		SetStatus("已记录隐藏状态。请打开系统触摸键盘或触摸板，然后扫描。 ");
	}

	public List<Candidate> Scan()
	{
		RequireIdle();
		Restore();
		target = null;
		List<Candidate> list = Discover();
		Note("VisibleSnapshot", list);
		List<Candidate> list2 = list.Where((Candidate c) => (Layout != "系统触摸板" || NativeTouchpad.CanAdjust(c)) && c.Visible && c.Rect.Width > 80 && c.Rect.Height > 40).OrderByDescending(ChangedWindow).ToList();
		SetStatus((!baselineTaken) ? "请先隐藏面板并点击第 1 步记录，再打开面板扫描。" : ((list2.Count == 0) ? "未发现可访问的系统输入窗口。请打开原生键盘后重新扫描；能力诊断报告会记录当前构建信息。" : ((!list2.Any(ChangedWindow)) ? "输入宿主的窗口尺寸未变化，已列出仍可访问的候选。请选择实际键盘对应的窗口测试透明度；全屏宿主不代表可移动的键盘面板。" : "变化的窗口排在前面。请选择与实际键盘／触摸板一致的窗口，再执行实验。")));
		return list2;
		bool ChangedWindow(Candidate c)
		{
			return !baseline.Any((Candidate b) => Same(c, b) && b.Visible && b.Rect == c.Rect);
		}
	}

	public async Task DiagnoseAsync()
	{
		RequireIdle();
		Scan();
		busy = true;
		Task<object[]> query = Task.Run(() => (from c in Discover()
			where c.Visible
			select c).Take(4).Select((Func<Candidate, object>)delegate(Candidate c)
		{
			try
			{
				return new
				{
					Handle = c.Handle,
					Elements = Uia(c)
				};
			}
			catch (Exception ex)
			{
				return new
				{
					Handle = c.Handle,
					Error = ex.Message
				};
			}
		}).ToArray());
		try
		{
			if (await Task.WhenAny(query, Task.Delay(3000)) == query)
			{
				Note("ReadOnlyCapabilities", await query);
				SetStatus("只读能力报告已保存；这不代表已识别键盘或通过输入验证。");
			}
			else
			{
				Note("ReadOnlyCapabilities", "UIA timeout; no target modified");
				SetStatus("UI Automation 超时；窗口快照已保存，未修改原生面板。");
			}
		}
		finally
		{
			if (query.IsCompleted)
			{
				busy = false;
				this.Changed?.Invoke();
			}
			else
			{
				query.ContinueWith((Task<object[]> _) => dispatcher.TryEnqueue(delegate
				{
					busy = false;
					this.Changed?.Invoke();
				}));
			}
		}
	}

	public Task SelectAsync(Candidate value)
	{
		RequireIdle();
		if (Layout == "系统触摸板" && !NativeTouchpad.CanAdjust(value))
		{
			throw new InvalidOperationException("这是共享输入宿主，不能作为独立触摸板调整透明度。当前系统未暴露可调整的触摸板窗口。");
		}
		if (!Available(value))
		{
			throw new InvalidOperationException("此窗口已用于另一个输入面板，请选择独立的键盘或触摸板窗口。");
		}
		Restore();
		target = value;
		baselineTaken = true;
		bool opacityPassed = (OpacityVerified = false);
		OpacityPassed = opacityPassed;
		if (File.Exists(journal + ".stop"))
		{
			File.Delete(journal + ".stop");
		}
		if (!Valid(value))
		{
			throw new InvalidOperationException("输入面板已重建，请刷新列表。");
		}
		SetStatus("已识别输入面板，可直接调整透明度。");
		return Task.CompletedTask;
	}

	public void BindSettings(SettingsStore value)
	{
		settings = value;
		try
		{
			ResetControl();
			ScheduleAutoApply();
		}
		catch (Exception ex)
		{
			Diagnostics.Error(ex, "NativeInput.StartupRestore");
			SetStatus("原始透明度恢复失败：" + ex.Message);
		}
	}

	public void ScheduleAutoApply()
	{
		if (!disposed)
		{
			geometryCheck.Stop();
			geometryCheck.Start();
		}
	}

	public async Task<List<Candidate>> RefreshOpacityTarget()
	{
		RequireIdle();
		if (Layout == "系统触摸板")
		{
			Candidate candidate = target;
			if ((object)candidate != null)
			{
				Candidate candidate2 = Inspect((nint)candidate.Handle, candidate.Parent);
				if (candidate2 != null && (Math.Abs(candidate2.Rect.Width - candidate.Rect.Width) > 64 || Math.Abs(candidate2.Rect.Height - candidate.Rect.Height) > 64))
				{
					Restore();
					target = null;
					SetStatus("输入宿主尺寸已变化，已恢复透明度并等待重新识别触摸板。");
					return new List<Candidate>();
				}
			}
		}
		List<Candidate> options = (from c in Discover()
			where Available(c) && (Layout != "系统触摸板" || NativeTouchpad.CanAdjust(c)) && c.Visible && c.Rect.Width >= 160 && c.Rect.Height >= 100
			select c).ToList();
		string saved = ((!(Layout == "系统触摸板")) ? settings?.Value.NativeInputOpacityTarget : settings?.Value.TouchpadOpacityTarget) ?? "";
		List<Candidate> list = options.Where((Candidate c) => Signature(c) == saved && ((!(Layout == "系统触摸板")) ? (!NativeTouchpad.IsSurface(c)) : NativeTouchpad.IsSurface(c))).ToList();
		Candidate candidate3 = ((list.Count == 1) ? list[0] : null);
		if (candidate3 == null && Layout == "系统触摸板")
		{
			List<Candidate> list2 = options.Where(NativeTouchpad.IsSurface).ToList();
			if (list2.Count == 1)
			{
				candidate3 = list2[0];
			}
		}
		if (candidate3 == null && Layout != "系统触摸板")
		{
			List<Candidate> list3 = (from c in options
				where IsKeyboardSurface(c) && !NativeTouchpad.IsSurface(c)
				orderby (long)c.Rect.Width * (long)c.Rect.Height descending
				select c).ToList();
			if (list3.Count == 1)
			{
				candidate3 = list3[0];
			}
		}
		if (candidate3 != null && Layout == "系统触摸板" && candidate3.Parent != 0L)
		{
			Candidate surface = candidate3;
			List<Candidate> list4 = options.Where((Candidate c) => c.Parent == 0L && c.Pid == surface.Pid && Math.Abs(c.Rect.X - surface.Rect.X) <= 16 && Math.Abs(c.Rect.Y - surface.Rect.Y) <= 16 && Math.Abs(c.Rect.Width - surface.Rect.Width) <= 32 && Math.Abs(c.Rect.Height - surface.Rect.Height) <= 32).ToList();
			if (list4.Count == 1)
			{
				candidate3 = list4[0];
			}
		}
		if (candidate3 != null)
		{
			if (target == null || !Same(candidate3, target))
			{
				await SelectAsync(candidate3);
			}
			if (settings == null || AutoApply)
			{
				ApplyOpacity(((!(Layout == "系统触摸板")) ? settings?.Value.TouchKeyboardOpacity : settings?.Value.TouchpadOpacity) ?? 100);
			}
		}
		else
		{
			SetStatus((Layout == "系统触摸板") ? "未识别到独立触摸板窗口；本系统可能不支持触摸板透明度。键盘设置不受影响。" : ((options.Count == 0) ? "等待打开系统输入面板…" : "发现多个输入窗口，请在下方选择一次（通常选第一个即可）；以后自动识别同类面板。"));
		}
		return options;
	}

	internal static string Signature(Candidate c)
	{
		return Path.GetFileName(c.Path) + "|" + c.Class + "|" + ((c.Parent != 0L) ? "child" : "root");
	}

	private static bool IsKeyboardSurface(Candidate c)
	{
		double num = Math.Max(1.0, (double)c.Dpi / 96.0);
		Win32.RECT rcMonitor = Win32.Monitor(new Win32.POINT(c.Rect.X, c.Rect.Y)).rcMonitor;
		if (c.Parent != 0L && (double)c.Rect.Width >= 300.0 * num && (double)c.Rect.Height >= 180.0 * num && (double)c.Rect.Height < (double)rcMonitor.Height * 0.9)
		{
			return (double)c.Rect.Width > (double)c.Rect.Height * 1.3;
		}
		return false;
	}

	public void ApplyOpacity(int percent)
	{
		RequireIdle();
		if (target == null)
		{
			SetStatus("已保存，打开所选输入面板后自动应用。");
			return;
		}
		if (!Valid(target) || !Win32.IsWindowVisible((nint)target.Handle))
		{
			Restore();
			target = null;
			SetStatus("已保存，等待输入面板重新显示。");
			return;
		}
		if (percent >= 100)
		{
			Restore();
			SetStatus("当前不透明度 100%");
			return;
		}
		if (Layout == "系统触摸板" && !NativeTouchpad.CanAdjust(target))
		{
			Restore();
			target = null;
			SetStatus("共享输入宿主不支持独立触摸板透明度，已恢复。");
			return;
		}
		Alpha(percent);
		OpacityPassed = true;
		SetStatus($"已请求不透明度 {Math.Clamp(percent, 40, 100)}%，请以实际显示效果为准。");
	}

	private static object Uia(Candidate c)
	{
		AutomationElement automationElement = AutomationElement.FromHandle((nint)c.Handle);
		List<object> rows = new List<object>();
		Add(automationElement);
		AutomationElement automationElement2 = TreeWalker.ControlViewWalker.GetFirstChild(automationElement);
		int num = 0;
		while (automationElement2 != null && num < 24)
		{
			Add(automationElement2);
			automationElement2 = TreeWalker.ControlViewWalker.GetNextSibling(automationElement2);
			num++;
		}
		return rows;
		void Add(AutomationElement e)
		{
			AutomationElement.AutomationElementInformation current = e.Current;
			bool? canMove = null;
			try
			{
				if (e.TryGetCurrentPattern(TransformPattern.Pattern, out var patternObject))
				{
					canMove = ((TransformPattern)patternObject).Current.CanMove;
				}
			}
			catch
			{
			}
			rows.Add(new
			{
				ClassName = current.ClassName,
				ControlType = current.ControlType.ProgrammaticName,
				IsOffscreen = current.IsOffscreen,
				Bounds = current.BoundingRectangle.ToString(),
				CanMove = canMove
			});
		}
	}

	private void RequireIdle()
	{
		if (disposed)
		{
			throw new ObjectDisposedException("SystemInputControl");
		}
		if (busy)
		{
			throw new InvalidOperationException("请等待当前实验结束。");
		}
	}

	private Candidate RequireTarget()
	{
		Candidate candidate = target ?? throw new InvalidOperationException("请先选择目标窗口。");
		if (Layout == "系统触摸板" && !NativeTouchpad.CanAdjust(candidate))
		{
			throw new InvalidOperationException("共享输入宿主不支持独立触摸板透明度。");
		}
		if (File.Exists(journal + ".stop"))
		{
			throw new InvalidOperationException("已执行恢复命令，请重新记录隐藏状态。");
		}
		if (!baselineTaken)
		{
			throw new InvalidOperationException("请先记录隐藏状态。");
		}
		if (!Valid(candidate) || !Win32.IsWindowVisible((nint)candidate.Handle) || (backup != null && GetProp((nint)candidate.Handle, "TouchAssistant.NativeInput.Identity") != (nint)backup.Marker))
		{
			throw new InvalidOperationException("目标已隐藏或重建，请重新扫描。");
		}
		return candidate;
	}

	private void SaveBackup(bool opacity, bool position)
	{
		Candidate candidate = RequireTarget();
		if (backup != null)
		{
			backup = backup with
			{
				Opacity = (backup.Opacity || opacity),
				Position = (backup.Position || position)
			};
			Persist();
			return;
		}
		if (File.Exists(journal))
		{
			throw new InvalidOperationException("发现上次修改的恢复记录，请先点击恢复原始透明度和位置。");
		}
		candidate = candidate with
		{
			Rect = Rect((nint)candidate.Handle),
			ExStyle = Style((nint)candidate.Handle, -20)
		};
		uint color = 0u;
		uint flags = 0u;
		byte alpha = byte.MaxValue;
		if ((candidate.ExStyle & 0x80000) != 0L && !GetLayeredWindowAttributes((nint)candidate.Handle, out color, out alpha, out flags))
		{
			throw new InvalidOperationException("当前构建不支持安全恢复此分层窗口，已停止修改。");
		}
		long num = Random.Shared.NextInt64(1L, long.MaxValue);
		Error(SetProp((nint)candidate.Handle, "TouchAssistant.NativeInput.Identity", (nint)num), "SetProp identity");
		backup = new Backup(candidate, color, alpha, flags, opacity, position, num);
		Persist();
	}

	private void Persist()
	{
		Directory.CreateDirectory(Path.GetDirectoryName(journal));
		File.WriteAllText(journal, JsonSerializer.Serialize(backup, Json));
	}

	private static void Error(bool ok, string operation)
	{
		if (!ok)
		{
			throw new Win32Exception(Marshal.GetLastWin32Error(), operation);
		}
	}

	private static long Style(nint h, int index)
	{
		Marshal.SetLastPInvokeError(0);
		nint windowLongPtr = GetWindowLongPtr(h, index);
		int lastPInvokeError = Marshal.GetLastPInvokeError();
		if (windowLongPtr == 0 && lastPInvokeError != 0)
		{
			throw new Win32Exception(lastPInvokeError);
		}
		return ((IntPtr)windowLongPtr).ToInt64();
	}

	private static void Style(nint h, long value)
	{
		Marshal.SetLastPInvokeError(0);
		nint num = SetWindowLongPtr(h, -20, (nint)value);
		int lastPInvokeError = Marshal.GetLastPInvokeError();
		if (num == 0 && lastPInvokeError != 0)
		{
			throw new Win32Exception(lastPInvokeError);
		}
	}

	private void Alpha(int percent)
	{
		nint h = (nint)RequireTarget().Handle;
		byte b = (byte)Math.Round((double)Math.Clamp(percent, 40, 100) * 2.55);
		if (!(backup != null) || !GetLayeredWindowAttributes(h, out var color, out var alpha, out var flags) || (flags & 2) == 0 || alpha != b)
		{
			SaveBackup(opacity: true, position: false);
			Style(h, Style(h, -20) | 0x80000);
			Error(SetLayeredWindowAttributes(h, backup.Color, b, backup.Flags | 2), "SetLayeredWindowAttributes");
			Error(GetLayeredWindowAttributes(h, out color, out var alpha2, out var _), "GetLayeredWindowAttributes");
			if (alpha2 != b)
			{
				throw new InvalidOperationException("透明度读回不一致。");
			}
			Note("OpacityAPI", new
			{
				Percent = percent,
				Readback = alpha2,
				Result = "API_ONLY; visual/input unverified"
			});
		}
	}

	public async Task ProbeOpacity()
	{
		RequireIdle();
		RequireTarget();
		Restore();
		OpacityPassed = false;
		busy = true;
		int generation = experimentGeneration;
		try
		{
			int[] array = new int[4] { 100, 80, 60, 40 };
			foreach (int num in array)
			{
				if (disposed || generation != experimentGeneration)
				{
					throw new OperationCanceledException("实验已恢复或取消。");
				}
				SetStatus($"透明度实验 {num}%：请检查视觉、输入与候选词，随后自动恢复。");
				Alpha(num);
				await Task.Delay(2500);
			}
			OpacityPassed = !disposed && generation == experimentGeneration;
		}
		catch (Exception ex)
		{
			Note("OpacityFailed", new { ex.Message, ex.HResult });
			SetStatus("当前构建不支持透明度实验：" + ex.Message);
		}
		finally
		{
			bool opacityPassed = OpacityPassed;
			Restore();
			OpacityPassed = opacityPassed;
			busy = false;
			if (opacityPassed)
			{
				SetStatus("透明度接口实验已恢复；仅在实际外观和输入均正常时勾选确认。");
			}
			this.Changed?.Invoke();
		}
	}

	public void ConfirmOpacity(bool value)
	{
		OpacityVerified = value && OpacityPassed;
		if (!OpacityVerified)
		{
			Restore();
		}
		this.Changed?.Invoke();
	}

	public void SetOpacity(int percent)
	{
		RequireIdle();
		if (!OpacityVerified)
		{
			throw new InvalidOperationException("尚未确认视觉和输入。");
		}
		try
		{
			Alpha(percent);
		}
		catch
		{
			Restore();
			OpacityPassed = false;
			throw;
		}
	}

	private static Bounds Rect(nint h)
	{
		Error(Win32.GetWindowRect(h, out var r), "GetWindowRect");
		return new Bounds(r.Left, r.Top, r.Width, r.Height);
	}

	private static void Place(Candidate c, int x, int y)
	{
		int x2 = x;
		int y2 = y;
		if (c.Parent != 0L)
		{
			Win32.POINT p = new Win32.POINT(x, y);
			Error(ScreenToClient((nint)c.Parent, ref p), "ScreenToClient");
			x2 = p.X;
			y2 = p.Y;
		}
		Error(SetWindowPos((nint)c.Handle, 0, x2, y2, 0, 0, 21u), "SetWindowPos");
	}

	public void Restore()
	{
		lock (mutationGate)
		{
			experimentGeneration++;
			geometryCheck.Stop();
			OpacityVerified = false;
			if (backup == null)
			{
				return;
			}
			Backup b = backup;
			backup = null;
			try
			{
				string detail = RestoreBackup(b);
				if (File.Exists(journal))
				{
					File.Delete(journal);
				}
				Note("Restore", detail);
			}
			catch (Exception ex)
			{
				backup = b;
				Note("RestoreFailed", new { ex.Message, ex.HResult });
				throw;
			}
		}
	}

	private static string RestoreBackup(Backup b)
	{
		if (!Valid(b.Target))
		{
			return "SKIPPED: original window no longer exists";
		}
		if (GetProp((nint)b.Target.Handle, "TouchAssistant.NativeInput.Identity") != (nint)b.Marker)
		{
			throw new InvalidOperationException("恢复记录与窗口标记不一致，已保留恢复记录，未修改其它窗口。");
		}
		nint h = (nint)b.Target.Handle;
		if (b.Opacity)
		{
			if ((b.Target.ExStyle & 0x80000) != 0L)
			{
				Style(h, Style(h, -20) | 0x80000);
				Error(SetLayeredWindowAttributes(h, b.Color, b.Alpha, b.Flags), "Restore opacity");
			}
			else
			{
				Style(h, Style(h, -20) & -524289);
			}
		}
		if (b.Position)
		{
			Place(b.Target, b.Target.Rect.X, b.Target.Rect.Y);
		}
		RemoveProp(h, "TouchAssistant.NativeInput.Identity");
		return "RESTORED: original alpha/style/position";
	}

	public static void RestoreSaved(string directory, string filename = "native-input-restore.json")
	{
		Directory.CreateDirectory(directory);
		string text = Path.Combine(directory, filename);
		File.WriteAllText(text + ".stop", "restore requested");
		if (File.Exists(text))
		{
			Backup backup = JsonSerializer.Deserialize<Backup>(File.ReadAllText(text));
			if (backup != null)
			{
				RestoreBackup(backup);
			}
			File.Delete(text);
		}
	}

	private void OnEvent(nint h, uint ev, nint hwnd, int obj, int child, uint tid, uint time)
	{
		if (obj != 0)
		{
			return;
		}
		if (ev == 32770 && AutoApply)
		{
			StringBuilder stringBuilder = new StringBuilder(80);
			GetClassName(hwnd, stringBuilder, stringBuilder.Capacity);
			string text = stringBuilder.ToString();
			if ((text == "Windows.UI.Core.CoreWindow" || text == "IPTip_Main_Window") ? true : false)
			{
				dispatcher.TryEnqueue(ScheduleAutoApply);
			}
		}
		if (target == null || (hwnd != (nint)target.Handle && hwnd != (nint)target.Parent))
		{
			return;
		}
		if (ev == 32779)
		{
			dispatcher.TryEnqueue(delegate
			{
				if (!disposed)
				{
					geometryCheck.Stop();
					geometryCheck.Start();
				}
			});
		}
		if ((ev != 32769 && ev != 32771) || 1 == 0)
		{
			return;
		}
		Candidate observed = target;
		dispatcher.TryEnqueue(delegate
		{
			if (disposed || (object)target != observed)
			{
				return;
			}
			try
			{
				Restore();
				target = null;
				OpacityPassed = false;
				SetStatus("等待输入面板重新显示，届时自动应用已保存的透明度。");
			}
			catch (Exception ex)
			{
				SetStatus("恢复失败：" + ex.Message);
			}
		});
	}

	private async void CheckGeometry()
	{
		if (disposed || busy || settings == null || !AutoApply)
		{
			return;
		}
		try
		{
			await RefreshOpacityTarget();
		}
		catch (Exception ex)
		{
			Diagnostics.Error(ex, "NativeInput.AutoOpacity");
			SetStatus("透明度暂未应用：" + ex.Message);
		}
	}

	public void Dispose()
	{
		if (!disposed)
		{
			disposed = true;
			owners.Remove(this);
			geometryCheck.Stop();
			if (hook != 0)
			{
				UnhookWinEvent(hook);
			}
			try
			{
				Restore();
			}
			catch (Exception error)
			{
				Diagnostics.Error(error, "NativeInput.Restore");
			}
			this.Changed = null;
		}
	}

	private static bool Same(Candidate a, Candidate b)
	{
		if (a.Handle == b.Handle && a.Pid == b.Pid && a.Started == b.Started && a.Class == b.Class)
		{
			return a.Path == b.Path;
		}
		return false;
	}

	internal static void CheckOwnedWindow(nint hwnd)
	{
		Win32.GetWindowThreadProcessId(hwnd, out var process);
		if (process != Environment.ProcessId)
		{
			throw new InvalidOperationException("测试只能操作本进程窗口。");
		}
		long num = Style(hwnd, -20);
		uint color = 0u;
		uint flags = 0u;
		byte alpha = byte.MaxValue;
		bool flag = (num & 0x80000) != 0;
		if (flag)
		{
			Error(GetLayeredWindowAttributes(hwnd, out color, out alpha, out flags), "Read fixture alpha");
		}
		Bounds bounds = Rect(hwnd);
		new Candidate(((IntPtr)hwnd).ToInt64(), 0L, (int)process, 0L, "fixture", "fixture", Visible: true, bounds, 0L, num, 96u, null, "fixture");
		try
		{
			Style(hwnd, num | 0x80000);
			Error(SetLayeredWindowAttributes(hwnd, 0u, 204, 2u), "Fixture alpha");
			Error(GetLayeredWindowAttributes(hwnd, out var _, out var alpha2, out var _), "Fixture readback");
			if (alpha2 != 204)
			{
				throw new InvalidOperationException("透明度读回失败。");
			}
		}
		finally
		{
			if (flag)
			{
				Error(SetLayeredWindowAttributes(hwnd, color, alpha, flags), "Restore fixture alpha");
			}
			Style(hwnd, num);
		}
		if (Style(hwnd, -20) != num || Rect(hwnd) != bounds)
		{
			throw new InvalidOperationException("窗口未恢复。");
		}
	}

	private static bool Valid(Candidate c)
	{
		try
		{
			Candidate candidate = Inspect((nint)c.Handle, c.Parent);
			return (object)candidate != null && Same(c, candidate);
		}
		catch
		{
			return false;
		}
	}

	public static List<Candidate> Discover()
	{
		HashSet<uint> inputPids = new HashSet<uint>();
		string[] array = new string[4] { "TextInputHost", "TabTip", "InputApp", "WindowsInternal.ComposableShell.Experiences.TextInput.InputApp" };
		for (int i = 0; i < array.Length; i++)
		{
			Process[] processesByName = Process.GetProcessesByName(array[i]);
			foreach (Process process in processesByName)
			{
				using (process)
				{
					inputPids.Add((uint)process.Id);
				}
			}
		}
		List<Candidate> result = new List<Candidate>();
		EnumWindows(delegate(nint h, nint _)
		{
			Add(h, 0L);
			EnumChildWindows(h, delegate(nint child, nint num)
			{
				if (result.Count < 512)
				{
					Add(child, GetParent(child));
				}
				return result.Count < 512;
			}, 0);
			return result.Count < 512;
		}, 0);
		return result;
		void Add(nint h, long parent)
		{
			Win32.GetWindowThreadProcessId(h, out var process2);
			if (inputPids.Contains(process2))
			{
				Candidate candidate = Inspect(h, parent);
				if ((object)candidate != null)
				{
					result.Add(candidate);
				}
			}
		}
	}

	private static Candidate? Inspect(nint h, long parent)
	{
		Win32.GetWindowThreadProcessId(h, out var process);
		if (process == 0)
		{
			return null;
		}
		nint num = OpenProcess(4096u, inherit: false, process);
		if (num == 0)
		{
			return null;
		}
		try
		{
			StringBuilder stringBuilder = new StringBuilder(32768);
			uint size = (uint)stringBuilder.Capacity;
			if (!QueryFullProcessImageName(num, 0u, stringBuilder, ref size) || !GetProcessTimes(num, out var creation, out var _, out var _, out var _))
			{
				return null;
			}
			string text = stringBuilder.ToString();
			string fileName = Path.GetFileName(text);
			if (!new string[4] { "TextInputHost.exe", "TabTip.exe", "InputApp.exe", "WindowsInternal.ComposableShell.Experiences.TextInput.InputApp.exe" }.Contains<string>(fileName, StringComparer.OrdinalIgnoreCase))
			{
				return null;
			}
			if (!text.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && !text.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}
			StringBuilder stringBuilder2 = new StringBuilder(256);
			GetClassName(h, stringBuilder2, stringBuilder2.Capacity);
			Bounds rect = Rect(h);
			Bounds dwm = null;
			if (DwmGetWindowAttribute(h, 9, out var rect2, Marshal.SizeOf<Win32.RECT>()) == 0)
			{
				dwm = new Bounds(rect2.Left, rect2.Top, rect2.Width, rect2.Height);
			}
			return new Candidate(((IntPtr)h).ToInt64(), parent, (int)process, DateTime.FromFileTimeUtc(creation).Ticks, text, stringBuilder2.ToString(), Win32.IsWindowVisible(h), rect, Style(h, -16), Style(h, -20), Win32.GetDpiForWindow(h), dwm, Integrity(num));
		}
		catch
		{
			return null;
		}
		finally
		{
			CloseHandle(num);
		}
	}

	private static string Integrity(nint process)
	{
		if (!OpenProcessToken(process, 8u, out var token))
		{
			return "unavailable:" + Marshal.GetLastWin32Error();
		}
		try
		{
			GetTokenInformation(token, 25, 0, 0u, out var needed);
			if (needed == 0)
			{
				return "unavailable";
			}
			nint num = Marshal.AllocHGlobal((int)needed);
			try
			{
				if (!GetTokenInformation(token, 25, num, needed, out var _))
				{
					return "unavailable";
				}
				nint sid = Marshal.ReadIntPtr(num);
				byte b = Marshal.ReadByte(GetSidSubAuthorityCount(sid));
				return Marshal.ReadInt32(GetSidSubAuthority(sid, (uint)(b - 1))).ToString("X");
			}
			finally
			{
				Marshal.FreeHGlobal(num);
			}
		}
		finally
		{
			CloseHandle(token);
		}
	}

	[DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool SetProp(nint h, string name, nint value);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern nint GetProp(nint h, string name);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern nint RemoveProp(nint h, string name);

	[DllImport("user32")]
	private static extern nint GetParent(nint h);

	[DllImport("user32", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW", SetLastError = true)]
	private static extern bool GetMonitorInfoEx(nint h, ref MonitorInfo info);

	[DllImport("user32")]
	private static extern bool EnumWindows(EnumCallback callback, nint param);

	[DllImport("user32")]
	private static extern bool EnumChildWindows(nint parent, EnumCallback callback, nint param);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern int GetClassName(nint h, StringBuilder value, int max);

	private static nint GetWindowLongPtr(nint h, int index)
	{
		return WindowLong.Get(h, index);
	}

	private static nint SetWindowLongPtr(nint h, int index, nint value)
	{
		return WindowLong.Set(h, index, value);
	}

	[DllImport("user32", SetLastError = true)]
	private static extern bool SetLayeredWindowAttributes(nint h, uint color, byte alpha, uint flags);

	[DllImport("user32", SetLastError = true)]
	private static extern bool GetLayeredWindowAttributes(nint h, out uint color, out byte alpha, out uint flags);

	[DllImport("user32", SetLastError = true)]
	private static extern bool SetWindowPos(nint h, nint after, int x, int y, int width, int height, uint flags);

	[DllImport("user32", SetLastError = true)]
	private static extern bool ScreenToClient(nint h, ref Win32.POINT p);

	[DllImport("user32")]
	private static extern nint SetWinEventHook(uint min, uint max, nint module, EventCallback callback, uint pid, uint tid, uint flags);

	[DllImport("user32")]
	private static extern bool UnhookWinEvent(nint hook);

	[DllImport("dwmapi")]
	private static extern int DwmGetWindowAttribute(nint h, int attr, out Win32.RECT rect, int size);

	[DllImport("advapi32", SetLastError = true)]
	private static extern bool OpenProcessToken(nint p, uint access, out nint token);

	[DllImport("advapi32", SetLastError = true)]
	private static extern bool GetTokenInformation(nint token, int kind, nint data, uint length, out uint needed);

	[DllImport("advapi32")]
	private static extern nint GetSidSubAuthorityCount(nint sid);

	[DllImport("advapi32")]
	private static extern nint GetSidSubAuthority(nint sid, uint index);

	[DllImport("kernel32")]
	private static extern bool CloseHandle(nint h);

	[DllImport("kernel32", SetLastError = true)]
	private static extern nint OpenProcess(uint access, bool inherit, uint pid);

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder path, ref uint size);

	[DllImport("kernel32", SetLastError = true)]
	private static extern bool GetProcessTimes(nint process, out long creation, out long exit, out long kernel, out long user);
}
