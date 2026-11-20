using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TouchAssistant.Models;
using TouchAssistant.Native;
using TouchAssistant.Services;

namespace TouchAssistant.UI;

internal sealed class SystemInputPage : StackPanel, IDisposable
{
	private readonly SystemInputControl control;

	private readonly SettingsStore store;

	private readonly TextBlock status = Ui.Text("", 12.0);

	private readonly Slider opacity = new Slider
	{
		Minimum = 40.0,
		Maximum = 100.0,
		StepFrequency = 1.0,
		Header = Localization.T("不透明度（100% 为完全不透明）")
	};

	private readonly NumberBox percent = new NumberBox
	{
		Minimum = 40.0,
		Maximum = 100.0,
		SmallChange = 1.0,
		LargeChange = 5.0,
		Width = 110.0,
		SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact
	};

	private readonly ComboBox candidates = new ComboBox
	{
		Header = Localization.T("输入窗口（自动识别不准确时选择）"),
		HorizontalAlignment = HorizontalAlignment.Stretch
	};

	private readonly DispatcherQueueTimer apply;

	private bool syncing;

	private bool closed;

	private bool touchpad;

	private int Saved
	{
		get
		{
			if (!touchpad)
			{
				return store.Value.TouchKeyboardOpacity;
			}
			return store.Value.TouchpadOpacity;
		}
	}

	public SystemInputPage(SystemInputControl service, SettingsStore value)
	{
		control = service;
		store = value;
		touchpad = control.Layout == "系统触摸板";
		base.Spacing = 14.0;
		base.Padding = new Thickness(28.0, 0.0, 28.0, 28.0);
		base.Children.Add(Ui.Text(touchpad ? "触摸板透明度独立保存；识别后自动应用。若系统宿主不支持，将保留原样。" : "实验性功能：原生触摸键盘透明度。不同 Windows 版本和键盘布局可能不支持；设置自动保存，异常时可恢复 100%。", 12.0));
		StackPanel stackPanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		stackPanel.Children.Add(Ui.Button(touchpad ? "切换触摸板" : "切换触摸键盘", delegate
		{
			Run(delegate
			{
				if (touchpad)
				{
					App.CurrentApp.ToggleTouchpad();
				}
				else
				{
					App.CurrentApp.ToggleTouchKeyboard();
				}
			});
		}));
		base.Children.Add(stackPanel);
		ToggleSwitch auto = new ToggleSwitch
		{
			OnContent = Localization.T("开"),
			OffContent = Localization.T("关"),
			Header = Localization.T("自动应用已保存的透明度"),
			IsOn = (touchpad ? store.Value.AutoTouchpadOpacity : store.Value.AutoNativeInputOpacity)
		};
		auto.Toggled += delegate
		{
			Run(delegate
			{
				if (touchpad)
				{
					store.Value.AutoTouchpadOpacity = auto.IsOn;
				}
				else
				{
					store.Value.AutoNativeInputOpacity = auto.IsOn;
				}
				store.Save();
				if (auto.IsOn)
				{
					control.ScheduleAutoApply();
				}
				else
				{
					control.Restore();
				}
			});
		};
		base.Children.Add(auto);
		opacity.Value = (percent.Value = Math.Clamp(Saved, 40, 100));
		base.Children.Add(opacity);
		StackPanel stackPanel2 = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		stackPanel2.Children.Add(percent);
		stackPanel2.Children.Add(Ui.Text("%"));
		stackPanel2.Children.Add(Ui.Button("恢复 100%", delegate
		{
			SetValue(100.0);
		}));
		base.Children.Add(stackPanel2);
		status.IsTextSelectionEnabled = true;
		base.Children.Add(status);
		apply = base.DispatcherQueue.CreateTimer();
		apply.Interval = TimeSpan.FromMilliseconds(100.0);
		apply.IsRepeating = false;
		apply.Tick += delegate
		{
			Run(delegate
			{
				store.Save();
				control.ApplyOpacity(Saved);
			});
		};
		opacity.ValueChanged += delegate
		{
			if (!syncing)
			{
				SetValue(opacity.Value);
			}
		};
		percent.ValueChanged += delegate
		{
			if (!syncing && double.IsFinite(percent.Value))
			{
				SetValue(percent.Value);
			}
		};
		StackPanel stackPanel3 = new StackPanel
		{
			Spacing = 10.0
		};
		stackPanel3.Children.Add(candidates);
		stackPanel3.Children.Add(Ui.Button("重新识别输入面板", async delegate
		{
			await Refresh();
		}));
		stackPanel3.Children.Add(Ui.Text("通常无需选择窗口；多个候选时选一次即可记住。切换键盘布局后可重新识别。", 12.0));
		stackPanel3.Children.Add(Ui.Button("恢复原始透明度", delegate
		{
			Run(delegate
			{
				apply.Stop();
				control.ResetControl();
				candidates.ItemsSource = null;
			});
		}));
		stackPanel3.Children.Add(Ui.Button(touchpad ? "系统任务栏设置" : "系统键盘设置", delegate
		{
			Run(delegate
			{
				SystemControl.Launch(touchpad ? "ms-settings:taskbar" : "ms-settings:typing");
			});
		}));
		stackPanel3.Children.Add(Ui.Button("保存诊断报告", delegate
		{
			Run(delegate
			{
				status.Text = Localization.T("已保存：") + control.WriteReport();
			});
		}));
		base.Children.Add(new Expander
		{
			Header = Localization.T("识别与恢复"),
			Content = stackPanel3,
			HorizontalAlignment = HorizontalAlignment.Stretch,
			HorizontalContentAlignment = HorizontalAlignment.Stretch
		});
		candidates.SelectionChanged += async delegate
		{
			if (!syncing)
			{
				object selectedItem = candidates.SelectedItem;
				if (selectedItem is SystemInputControl.Candidate c)
				{
					try
					{
						await control.SelectAsync(c);
						if (touchpad)
						{
							store.Value.TouchpadOpacityTarget = SystemInputControl.Signature(c);
							NativeTouchpad.Remember(c);
						}
						else
						{
							store.Value.NativeInputOpacityTarget = SystemInputControl.Signature(c);
						}
						store.Save();
						control.ApplyOpacity(Saved);
					}
					catch (Exception error)
					{
						ShowError(error);
					}
				}
			}
		};
		control.Changed += Update;
		base.Loaded += async delegate
		{
			if (!Array.Exists(Environment.GetCommandLineArgs(), (string a) => a == "--smoke-test"))
			{
				await Refresh();
			}
		};
		Update();
	}

	private void SetValue(double value)
	{
		if (!closed)
		{
			int num = (int)Math.Clamp(Math.Round(value), 40.0, 100.0);
			syncing = true;
			Slider slider = opacity;
			double value2 = (percent.Value = num);
			slider.Value = value2;
			syncing = false;
			if (touchpad)
			{
				store.Value.TouchpadOpacity = num;
			}
			else
			{
				store.Value.TouchKeyboardOpacity = num;
			}
			apply.Stop();
			apply.Start();
		}
	}

	private async Task Refresh()
	{
		try
		{
			List<SystemInputControl.Candidate> itemsSource = await control.RefreshOpacityTarget();
			if (!closed)
			{
				syncing = true;
				candidates.ItemsSource = itemsSource;
				candidates.SelectedItem = control.Target;
				syncing = false;
				Update();
			}
		}
		catch (Exception error)
		{
			syncing = false;
			ShowError(error);
		}
	}

	private void Run(Action action)
	{
		try
		{
			action();
		}
		catch (Exception error)
		{
			ShowError(error);
		}
	}

	private void ShowError(Exception error)
	{
		Diagnostics.Error(error, "NativeInput.Opacity");
		if (!closed)
		{
			status.Text = Localization.T("暂未应用：") + error.Message;
		}
	}

	private void Update()
	{
		if (!closed)
		{
			status.Text = Localization.T(control.Status);
		}
	}

	internal void CheckPercentSteps()
	{
		int saved = Saved;
		SetValue(73.0);
		if (opacity.Value != 73.0 || percent.Value != 73.0 || Saved != 73)
		{
			throw new InvalidOperationException("不支持 1% 调整。");
		}
		SetValue(74.0);
		if (Saved != 74)
		{
			throw new InvalidOperationException("步进错误。");
		}
		SetValue(saved);
		apply.Stop();
	}

	public void Dispose()
	{
		closed = true;
		bool isRunning = apply.IsRunning;
		apply.Stop();
		control.Changed -= Update;
		if (isRunning)
		{
			try
			{
				store.Save();
			}
			catch (Exception error)
			{
				Diagnostics.Error(error, "Settings.CloseSave");
			}
		}
	}
}
