using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;
using TouchAssistant.Core;
using TouchAssistant.Services;

namespace TouchAssistant.Native;

internal static class NativeTouchpad
{
	private static SystemInputControl.Candidate? observed;

	private static bool launching;

	private static int discoveryGeneration;

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern int GetWindowText(nint hwnd, StringBuilder text, int count);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern nint FindWindow(string name, string? title);

	internal static void CancelDiscovery()
	{
		discoveryGeneration++;
	}

	internal static bool CanAdjust(SystemInputControl.Candidate c)
	{
		return !InputSurfacePolicy.IsSharedRoot(c.Parent, c.Class);
	}

	internal static void Remember(SystemInputControl.Candidate candidate)
	{
		if (CanAdjust(candidate))
		{
			observed = candidate;
		}
	}

	internal static bool IsSurface(SystemInputControl.Candidate c)
	{
		if (!CanAdjust(c))
		{
			return false;
		}
		StringBuilder stringBuilder = new StringBuilder(256);
		GetWindowText((nint)c.Handle, stringBuilder, stringBuilder.Capacity);
		string name = stringBuilder.ToString();
		SystemInputControl.Candidate candidate = observed;
		if (((object)candidate == null || candidate.Handle != c.Handle || candidate.Pid != c.Pid || candidate.Started != c.Started || Math.Abs(candidate.Rect.Width - c.Rect.Width) > 32 || Math.Abs(candidate.Rect.Height - c.Rect.Height) > 32) && !c.Class.Contains("Touchpad", StringComparison.OrdinalIgnoreCase))
		{
			return IsPadName(name);
		}
		return true;
	}

	private static bool IsPadName(string name)
	{
		if (!name.Equals("Virtual touchpad", StringComparison.OrdinalIgnoreCase) && !name.Equals("Touchpad", StringComparison.OrdinalIgnoreCase) && !(name == "虚拟触摸板"))
		{
			return name == "触摸板";
		}
		return true;
	}

	private static bool ToggleTaskbar(CancellationToken token)
	{
		nint num = FindWindow("Shell_TrayWnd", null);
		if (num == 0)
		{
			return false;
		}
		AutomationElement automationElement = AutomationElement.FromHandle(num);
		OrCondition condition = new OrCondition(new PropertyCondition(AutomationElement.NameProperty, "Virtual touchpad"), new PropertyCondition(AutomationElement.NameProperty, "Touchpad"), new PropertyCondition(AutomationElement.NameProperty, "虚拟触摸板"), new PropertyCondition(AutomationElement.NameProperty, "触摸板"));
		AutomationElement automationElement2 = automationElement.FindFirst(TreeScope.Descendants, condition);
		if (automationElement2 == null || !automationElement2.Current.IsEnabled)
		{
			return false;
		}
		if (automationElement2.TryGetCurrentPattern(TogglePattern.Pattern, out var patternObject))
		{
			token.ThrowIfCancellationRequested();
			((TogglePattern)patternObject).Toggle();
			return true;
		}
		if (automationElement2.TryGetCurrentPattern(InvokePattern.Pattern, out var patternObject2))
		{
			token.ThrowIfCancellationRequested();
			((InvokePattern)patternObject2).Invoke();
			return true;
		}
		return false;
	}

	public static async void Toggle()
	{
		if (launching)
		{
			return;
		}
		launching = true;
		int generation = ++discoveryGeneration;
		try
		{
			CancellationTokenSource timeout = new CancellationTokenSource(1500);
			try
			{
				Task<bool> query = Task.Run(() => ToggleTaskbar(timeout.Token));
				if (await Task.WhenAny(query, Task.Delay(1500)) != query)
				{
					App.CurrentApp.Report("系统触摸板暂未响应，请稍后重试。");
					try
					{
						await query;
						return;
					}
					catch (OperationCanceledException)
					{
						return;
					}
				}
				bool flag = await query;
				if (generation != discoveryGeneration)
				{
					return;
				}
				if (!flag)
				{
					SystemControl.Launch("ms-virtualtouchpad:");
					App.CurrentApp.Report("已请求打开触摸板；如需切换开关，请在系统任务栏设置中显示“虚拟触摸板”按钮。");
				}
				Diagnostics.Event("Touchpad.Toggle", flag ? "System taskbar toggle" : "System activation URI; no host HWND mutation");
			}
			finally
			{
				if (timeout != null)
				{
					((IDisposable)timeout).Dispose();
				}
			}
		}
		catch (OperationCanceledException)
		{
			App.CurrentApp.Report("系统触摸板响应超时，请稍后重试。");
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Touchpad.Toggle");
			App.CurrentApp.Report("触摸板未响应，请尝试系统任务栏上的触摸板按钮。");
		}
		finally
		{
			launching = false;
		}
	}

	private static async Task ObserveOpened(List<SystemInputControl.Candidate> before, int generation)
	{
		for (int i = 0; i < 4; i++)
		{
			await Task.Delay(250);
			if (generation != discoveryGeneration)
			{
				return;
			}
			List<SystemInputControl.Candidate> added = (from c in SystemInputControl.Discover()
				where CanAdjust(c) && c.Visible && c.Rect.Width >= 160 && c.Rect.Height >= 100 && !before.Any((SystemInputControl.Candidate b) => b.Handle == c.Handle && b.Started == c.Started && b.Visible && b.Rect == c.Rect)
				select c).ToList();
			List<SystemInputControl.Candidate> list = added.Where((SystemInputControl.Candidate c) => !added.Any((SystemInputControl.Candidate child) => child.Parent == c.Handle)).ToList();
			if (list.Count == 1 && App.CurrentApp.KeyboardTarget?.Handle != list[0].Handle)
			{
				Remember(list[0]);
				App.CurrentApp.TouchpadControl.ScheduleAutoApply();
				return;
			}
		}
		App.CurrentApp.TouchpadControl.ScheduleAutoApply();
	}
}
