using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using Microsoft.Win32;
using TouchAssistant.Models;
using TouchAssistant.Native;

namespace TouchAssistant.Services;

internal static class Diagnostics
{
	private static readonly object gate = new object();

	private static readonly Dictionary<string, string> states = new Dictionary<string, string>();

	private static readonly Dictionary<string, long> errors = new Dictionary<string, long>();

	private const long MaxBytes = 1048576L;

	private static readonly Queue<string> edgeEvents = new Queue<string>();

	private static long edgeTotal;

	public static string DirectoryPath => Path.Combine(SettingsStore.DirectoryPath, "diagnostics");

	internal static void Edge(string message)
	{
		lock (gate)
		{
			edgeTotal++;
			if (edgeEvents.Count >= 2048)
			{
				edgeEvents.Dequeue();
			}
			edgeEvents.Enqueue($"{DateTimeOffset.Now:O} {message}");
		}
	}

	public static void Error(Exception error, string component = "Application")
	{
		string key = component + ":" + error.GetType().Name + ":" + error.HResult + ":" + error.Message;
		long tickCount = Environment.TickCount64;
		lock (gate)
		{
			if (errors.TryGetValue(key, out var value) && tickCount - value < 60000)
			{
				return;
			}
			if (errors.Count > 256)
			{
				errors.Clear();
			}
			errors[key] = tickCount;
		}
		Write("errors.log", $"[{component}] {error}");
	}

	public static void Event(string component, string message)
	{
		Write("application.log", "[" + component + "] " + message);
	}

	public static void State(string component, string message)
	{
		lock (gate)
		{
			if (states.TryGetValue(component, out string value) && value == message)
			{
				return;
			}
			states[component] = message;
		}
		Event(component, message);
	}

	public static void Relay(string message)
	{
		Write("touch-relay.log", message);
	}

	private static void Write(string name, string message)
	{
		try
		{
			lock (gate)
			{
				Directory.CreateDirectory(DirectoryPath);
				string text = Path.Combine(DirectoryPath, name);
				if (File.Exists(text) && new FileInfo(text).Length > 1048576)
				{
					File.Move(text, text + ".previous", overwrite: true);
				}
				File.AppendAllText(text, $"{DateTimeOffset.Now:O} v{typeof(Diagnostics).Assembly.GetName().Version} {RuntimeInformation.ProcessArchitecture} {message}\n");
			}
		}
		catch
		{
		}
	}

	internal static string Redact(string value)
	{
		foreach (string item in from p in new string[2]
			{
				Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
				AppContext.BaseDirectory
			}
			where !string.IsNullOrEmpty(p)
			orderby p.Length descending
			select p)
		{
			value = value.Replace(JsonSerializer.Serialize(item).Trim('"'), "[local-path]", StringComparison.OrdinalIgnoreCase);
			value = value.Replace(item, "[local-path]", StringComparison.OrdinalIgnoreCase);
		}
		return value;
	}

	public static string ExportDefault(Settings settings, string status, string description)
	{
		string text = Path.Combine(DirectoryPath, "exports");
		Directory.CreateDirectory(text);
		string text2 = Path.Combine(text, $"TouchToys-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N").Substring(0, 6)}.zip");
		try
		{
			Export(text2, settings, status, description);
			Event("Diagnostics", "诊断包已导出");
			return text2;
		}
		catch
		{
			if (File.Exists(text2))
			{
				File.Delete(text2);
			}
			throw;
		}
	}

	public static void Export(string destination, Settings application, string status, string description, bool includeLogs = true)
	{
		MultiTouchSettings multiTouch = application.MultiTouch;
		using FileStream stream = File.Create(destination);
		ZipArchive zip = new ZipArchive(stream, ZipArchiveMode.Create);
		try
		{
			JsonSerializerOptions options = new JsonSerializerOptions
			{
				WriteIndented = true
			};
			Add("edge-configuration.json", JsonSerializer.Serialize(new
			{
				ControlCenterScale = application.ControlCenterScale,
				EdgeEnabled = application.MultiTouch.EdgeEnabled,
				CloseGuardEnabled = application.CloseGuardEnabled,
				Edges = application.EdgeGestures.Select(delegate(GestureAction a, int i)
				{
					EdgeKind edgeKind = (EdgeKind)i;
					return new
					{
						Edge = edgeKind.ToString(),
						Enabled = a.Enabled,
						Kind = a.Kind.ToString(),
						BuiltIn = ((a.Kind == ActionKind.BuiltIn) ? a.Command : null)
					};
				})
			}, options));
			string[] array;
			long value;
			lock (gate)
			{
				array = edgeEvents.ToArray();
				value = edgeTotal;
			}
			Add("edge-events.log", string.Join("\n", array));
			Add("environment.json", JsonSerializer.Serialize(new
			{
				BuildId = typeof(Diagnostics).Assembly.ManifestModule.ModuleVersionId,
				Session = Environment.ProcessId,
				OS = Environment.OSVersion.Version.ToString(),
				Revision = Registry.GetValue("HKEY_LOCAL_MACHINE\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", "UBR", null),
				Displays = Screen.AllScreens.Select((Screen s) => new
				{
					Primary = s.Primary,
					Bounds = new
					{
						s.Bounds.X,
						s.Bounds.Y,
						s.Bounds.Width,
						s.Bounds.Height
					},
					Work = new
					{
						s.WorkingArea.X,
						s.WorkingArea.Y,
						s.WorkingArea.Width,
						s.WorkingArea.Height
					},
					Scale = Win32.Scale(new Win32.POINT(s.Bounds.X + s.Bounds.Width / 2, s.Bounds.Y + s.Bounds.Height / 2))
				})
			}, options));
			Add("diagnostic-report.txt", $"边缘手势诊断 v2\n构建标识：{typeof(Diagnostics).Assembly.ManifestModule.ModuleVersionId}\n当前状态（不是历史状态）：{status}\n本次进程累计事件：{value}；保留最近 {array.Length} 条，较早记录可能已覆盖。\n测试前请重启应用，关闭设置窗口后分别从顶部左右角、左侧中部滑入三次，再立即导出。\n请在问题描述注明每次测试时间、边缘、是否旋转及结果。\nedge-events.log：Start 是接收到触点，Finish 包含取消/排除/位移，Top 是识别结果，Side 是侧边输入，Recovery 是恢复等待。\n缺少事件不直接证明驱动故障，应结合边缘配置、窗口状态和接管注册日志。\n仅记录边缘距离和状态，不记录完整轨迹、窗口标题或键盘输入。\n");
			Add("application.json", JsonSerializer.Serialize(new
			{
				SchemaVersion = application.SchemaVersion,
				BallEnabled = application.BallEnabled,
				BallSize = application.BallSize,
				Opacity = application.Opacity,
				ArkEnabled = application.ArkEnabled,
				ArkSize = application.ArkSize,
				ArkOpacity = application.ArkOpacity,
				CloseGuardEnabled = application.CloseGuardEnabled,
				UseSystemAccent = application.UseSystemAccent,
				Dial = new
				{
					Size = application.Dial.Size,
					Opacity = application.Dial.Opacity,
					CenterToolHint = application.Dial.CenterToolHint,
					Tools = application.Dial.Tools.Select((DialTool t) => new
					{
						Preset = t.Preset,
						Clockwise = t.Clockwise.Kind.ToString(),
						CounterClockwise = t.CounterClockwise.Kind.ToString()
					})
				},
				ArkProfiles = application.ArkProfiles.Count,
				Launchers = application.Launchers.Count,
				Edges = application.EdgeGestures.Select((GestureAction a) => a.Kind.ToString()),
				BallBindings = application.Gestures.Select((GestureAction a) => a.Kind.ToString()),
				WorkingSet = Process.GetCurrentProcess().WorkingSet64,
				ManagedMemory = GC.GetTotalMemory(forceFullCollection: false)
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			Add("summary.json", JsonSerializer.Serialize(new
			{
				Version = typeof(Diagnostics).Assembly.GetName().Version?.ToString(),
				OS = RuntimeInformation.OSDescription,
				OSArchitecture = RuntimeInformation.OSArchitecture.ToString(),
				ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
				ExportedAt = DateTimeOffset.Now,
				Status = status,
				Enabled = multiTouch.Enabled,
				EdgeEnabled = multiTouch.EdgeEnabled,
				MaxSpacing = multiTouch.MaxSpacing,
				HoldSeconds = multiTouch.HoldSeconds,
				Bindings = multiTouch.Bindings.Select((MultiTouchBinding b) => new
				{
					Fingers = b.Fingers,
					Gesture = b.Gesture,
					TemplatePoints = b.Points.Count
				}),
				Description = description
			}, new JsonSerializerOptions
			{
				WriteIndented = true
			}));
			if (includeLogs)
			{
				lock (gate)
				{
					string[] array2 = new string[8] { "application.log", "application.log.previous", "errors.log", "errors.log.previous", "touch-relay.log", "touch-relay.log.previous", "native-input.json", "native-touchpad.json" };
					foreach (string text in array2)
					{
						string path = Path.Combine(DirectoryPath, text);
						if (File.Exists(path))
						{
							Add(text, File.ReadAllText(path));
						}
					}
					string path2 = Path.Combine(SettingsStore.DirectoryPath, "errors.log");
					if (File.Exists(path2))
					{
						using FileStream fileStream = File.OpenRead(path2);
						if (fileStream.Length > 1048576)
						{
							fileStream.Seek(-1048576L, SeekOrigin.End);
						}
						using StreamReader streamReader = new StreamReader(fileStream);
						Add("legacy-errors.log", streamReader.ReadToEnd());
					}
				}
			}
			Add("README.txt", "诊断包由用户主动导出，不会自动上传。请先检查内容后作为附件反馈。包含应用各模块的配置概况、运行事件、版本、系统、手势种类、错误堆栈和最近转发帧的数量/标记/时序。包含屏幕几何信息、触点起始边缘距离及结束位移；不含屏幕截图、完整触摸轨迹、绘制模板、按键绑定或启动目标。旧异常消息可能包含第三方程序信息，请检查后再分享。\n1460 = ERROR_TIMEOUT（操作超时），仅凭错误码不能判断驱动或应用的具体原因。");
		}
		finally
		{
			if (zip != null)
			{
				((IDisposable)zip).Dispose();
			}
		}
		void Add(string name, string value2)
		{
			using StreamWriter streamWriter = new StreamWriter(zip.CreateEntry(name).Open());
			streamWriter.Write(Redact(value2));
		}
	}
}
