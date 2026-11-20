using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading;
using System.Threading.Tasks;
using TouchAssistant.Native;
using Windows.Devices.Radios;
using Windows.Media.Control;
using Windows.Networking.Connectivity;
using Windows.Networking.NetworkOperators;
using Windows.Storage.Streams;
using Windows.System;
using Windows.System.Power;

namespace TouchAssistant.Services;

internal sealed class SystemControl : IDisposable
{
	private bool disposed;

	private GlobalSystemMediaTransportControlsSession? currentSession;

	private GlobalSystemMediaTransportControlsSessionManager? media;

	private readonly List<Radio> radios = new List<Radio>();

	private bool radioAccess;

	private DateTime brightnessChecked;

	private readonly SemaphoreSlim refreshGate = new SemaphoreSlim(1, 1);

	public string MediaTitle { get; private set; } = "暂无媒体";

	public string MediaArtworkKey { get; private set; } = "";

	public IRandomAccessStreamReference? MediaArtwork { get; private set; }

	public bool IsPlaying { get; private set; }

	public bool? Muted { get; private set; }

	public double Volume { get; private set; }

	public double? Brightness { get; private set; }

	public bool Wifi => radios.Any((Radio r) => r.Kind == RadioKind.WiFi && r.State == RadioState.On);

	public bool Bluetooth => radios.Any((Radio r) => r.Kind == RadioKind.Bluetooth && r.State == RadioState.On);

	public bool HasWifi => radios.Any((Radio r) => r.Kind == RadioKind.WiFi);

	public bool HasBluetooth => radios.Any((Radio r) => r.Kind == RadioKind.Bluetooth);

	public bool EnergySaver => PowerManager.EnergySaverStatus == EnergySaverStatus.On;

	public bool Hotspot { get; private set; }

	public bool? FlightMode { get; private set; }

	public event Action<string>? Notice;

	public event Action? PlaybackChanged;

	public event Action? StateChanged;

	private void PublishState()
	{
		if (!disposed)
		{
			this.StateChanged?.Invoke();
		}
	}

	public async Task InitializeAsync()
	{
		await Task.WhenAll(Radios(), Media(), RefreshAsync());
		async Task Media()
		{
			_ = 1;
			try
			{
				GlobalSystemMediaTransportControlsSessionManager globalSystemMediaTransportControlsSessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
				if (!disposed)
				{
					media = globalSystemMediaTransportControlsSessionManager;
					media.CurrentSessionChanged += MediaSessionChanged;
					AttachSession();
					await RefreshMediaAsync();
				}
			}
			catch (Exception error)
			{
				Diagnostics.Error(error, "Media.Initialize");
			}
		}
		async Task Radios()
		{
			_ = 1;
			try
			{
				RadioAccessStatus access = await Radio.RequestAccessAsync();
				IReadOnlyList<Radio> collection = await Radio.GetRadiosAsync();
				if (!disposed)
				{
					radioAccess = access == RadioAccessStatus.Allowed;
					radios.AddRange(collection);
					PublishState();
				}
			}
			catch (Exception error)
			{
				Diagnostics.Error(error, "Radio.Initialize");
			}
		}
	}

	private void MediaSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
	{
		if (!disposed)
		{
			AttachSession();
		}
	}

	private void AttachSession()
	{
		if (currentSession != null)
		{
			currentSession.PlaybackInfoChanged -= OnPlaybackChanged;
		}
		currentSession = media?.GetCurrentSession();
		if (currentSession != null)
		{
			currentSession.PlaybackInfoChanged += OnPlaybackChanged;
		}
		UpdatePlayback();
	}

	private void OnPlaybackChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
	{
		UpdatePlayback();
	}

	private void UpdatePlayback()
	{
		try
		{
			GlobalSystemMediaTransportControlsSession? globalSystemMediaTransportControlsSession = currentSession;
			IsPlaying = (object)globalSystemMediaTransportControlsSession != null && globalSystemMediaTransportControlsSession.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
			this.PlaybackChanged?.Invoke();
		}
		catch
		{
		}
	}

	private async Task RefreshMediaAsync()
	{
		try
		{
			GlobalSystemMediaTransportControlsSession session = media?.GetCurrentSession();
			if (session != null)
			{
				GlobalSystemMediaTransportControlsSessionMediaProperties globalSystemMediaTransportControlsSessionMediaProperties = await session.TryGetMediaPropertiesAsync();
				MediaArtwork = globalSystemMediaTransportControlsSessionMediaProperties.Thumbnail;
				MediaArtworkKey = session.SourceAppUserModelId + "|" + globalSystemMediaTransportControlsSessionMediaProperties.Title + "|" + globalSystemMediaTransportControlsSessionMediaProperties.Artist + "|" + globalSystemMediaTransportControlsSessionMediaProperties.AlbumTitle;
				MediaTitle = (string.IsNullOrWhiteSpace(globalSystemMediaTransportControlsSessionMediaProperties.Title) ? Localization.T("正在播放媒体") : globalSystemMediaTransportControlsSessionMediaProperties.Title);
				if (!string.IsNullOrWhiteSpace(globalSystemMediaTransportControlsSessionMediaProperties.Artist))
				{
					MediaTitle = MediaTitle + " · " + globalSystemMediaTransportControlsSessionMediaProperties.Artist;
				}
				SystemControl systemControl = this;
				GlobalSystemMediaTransportControlsSessionPlaybackInfo playbackInfo = session.GetPlaybackInfo();
				systemControl.IsPlaying = (object)playbackInfo != null && playbackInfo.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
			}
			else
			{
				MediaArtwork = null;
				MediaTitle = Localization.T("暂无媒体");
				IsPlaying = false;
			}
		}
		catch (Exception error)
		{
			MediaArtwork = null;
			MediaTitle = Localization.T("媒体暂不可用");
			Diagnostics.Error(error, "Media.Refresh");
		}
		PublishState();
	}

	public async Task RefreshAsync()
	{
		bool flag = disposed;
		if (!flag)
		{
			flag = !(await refreshGate.WaitAsync(0));
		}
		if (flag)
		{
			return;
		}
		try
		{
			await Task.WhenAll(Audio(), BrightnessState(), Flight(), HotspotState(), RefreshMediaAsync());
		}
		finally
		{
			refreshGate.Release();
		}
		async Task Audio()
		{
			try
			{
				(double, bool) tuple = await Task.Run(() => (AudioEndpoint.Read(), AudioEndpoint.ReadMute()));
				if (!disposed)
				{
					Volume = tuple.Item1;
					Muted = tuple.Item2;
					PublishState();
				}
			}
			catch (Exception error)
			{
				Muted = null;
				Diagnostics.Error(error, "Audio.Read");
			}
		}
		async Task BrightnessState()
		{
			if (Brightness.HasValue || (DateTime.UtcNow - brightnessChecked).TotalSeconds > 15.0)
			{
				double? brightness = await Task.Run((Func<double?>)ReadBrightness);
				if (!disposed)
				{
					Brightness = brightness;
					brightnessChecked = DateTime.UtcNow;
					PublishState();
				}
			}
		}
		async Task Flight()
		{
			bool? flightMode = await Task.Run((Func<bool?>)AirplaneMode.Read);
			if (!disposed)
			{
				FlightMode = flightMode;
				PublishState();
			}
		}
		async Task HotspotState()
		{
			try
			{
				bool hotspot = await Task.Run(delegate
				{
					NetworkOperatorTetheringManager? networkOperatorTetheringManager = Tethering();
					return (object)networkOperatorTetheringManager != null && networkOperatorTetheringManager.TetheringOperationalState == TetheringOperationalState.On;
				});
				if (!disposed)
				{
					Hotspot = hotspot;
					PublishState();
				}
			}
			catch
			{
			}
		}
	}

	public async Task ToggleFlightAsync()
	{
		if (!(await Task.Run((Func<bool>)AirplaneMode.Toggle)))
		{
			this.Notice?.Invoke(Localization.T("此 Windows 版本或设备不允许直接切换飞行模式，已打开系统设置。"));
			Launch("ms-settings:network-airplanemode");
		}
	}

	public async Task<bool> MediaAsync(string operation)
	{
		_ = 2;
		try
		{
			GlobalSystemMediaTransportControlsSession globalSystemMediaTransportControlsSession = media?.GetCurrentSession();
			if (globalSystemMediaTransportControlsSession == null)
			{
				SendKeys(new int[1] { (operation == "Previous") ? 177 : ((operation == "Next") ? 176 : 179) });
				return true;
			}
			bool flag = ((operation == "Previous") ? (await globalSystemMediaTransportControlsSession.TrySkipPreviousAsync()) : ((!(operation == "Next")) ? (await globalSystemMediaTransportControlsSession.TryTogglePlayPauseAsync()) : (await globalSystemMediaTransportControlsSession.TrySkipNextAsync())));
			if (!flag)
			{
				this.Notice?.Invoke(Localization.T("当前媒体应用不支持此控制。"));
			}
			return flag;
		}
		catch (Exception ex)
		{
			Diagnostics.Error(ex, "SystemControl");
			this.Notice?.Invoke(Localization.T("媒体控制失败：") + ex.Message);
		}
		return false;
	}

	public async Task ToggleRadioAsync(RadioKind kind)
	{
		Radio radio = radios.FirstOrDefault((Radio x) => x.Kind == kind);
		try
		{
			bool flag = radio != null && radioAccess;
			if (flag)
			{
				flag = await radio.SetStateAsync((radio.State != RadioState.On) ? RadioState.On : RadioState.Off) == RadioAccessStatus.Allowed;
			}
			if (flag)
			{
				return;
			}
		}
		catch
		{
		}
		this.Notice?.Invoke(Localization.T((kind == RadioKind.WiFi) ? "此设备需要在系统设置中切换 Wi-Fi。" : "此设备需要在系统设置中切换蓝牙。"));
		Launch((kind == RadioKind.WiFi) ? "ms-settings:network-wifi" : "ms-settings:bluetooth");
	}

	private static NetworkOperatorTetheringManager? Tethering()
	{
		ConnectionProfile internetConnectionProfile = NetworkInformation.GetInternetConnectionProfile();
		if (internetConnectionProfile == null)
		{
			return null;
		}
		return NetworkOperatorTetheringManager.CreateFromConnectionProfile(internetConnectionProfile);
	}

	public async Task ToggleHotspotAsync()
	{
		_ = 1;
		try
		{
			NetworkOperatorTetheringManager networkOperatorTetheringManager = Tethering();
			if (networkOperatorTetheringManager != null)
			{
				NetworkOperatorTetheringOperationResult networkOperatorTetheringOperationResult = ((networkOperatorTetheringManager.TetheringOperationalState != TetheringOperationalState.On) ? (await networkOperatorTetheringManager.StartTetheringAsync()) : (await networkOperatorTetheringManager.StopTetheringAsync()));
				if (networkOperatorTetheringOperationResult.Status == TetheringOperationStatus.Success)
				{
					return;
				}
			}
		}
		catch
		{
		}
		this.Notice?.Invoke(Localization.T("此设备需要在系统设置中切换移动热点。"));
		Launch("ms-settings:network-mobilehotspot");
	}

	public void ToggleMute()
	{
		try
		{
			AudioEndpoint.ToggleMute();
			Volume = AudioEndpoint.Read();
			Muted = AudioEndpoint.ReadMute();
		}
		catch (Exception ex)
		{
			Diagnostics.Error(ex, "SystemControl");
			this.Notice?.Invoke(Localization.T("无法切换静音：") + ex.Message);
		}
	}

	public void SetVolume(double value)
	{
		try
		{
			AudioEndpoint.Write((float)(Math.Clamp(value, 0.0, 100.0) / 100.0));
			Volume = value;
			Muted = AudioEndpoint.ReadMute();
		}
		catch (Exception ex)
		{
			Diagnostics.Error(ex, "SystemControl");
			this.Notice?.Invoke(Localization.T("无法调整音量：") + ex.Message);
		}
	}

	private static double? ReadBrightness()
	{
		try
		{
			using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT CurrentBrightness FROM WmiMonitorBrightness WHERE Active=True");
			using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
			using ManagementObjectCollection.ManagementObjectEnumerator managementObjectEnumerator = managementObjectCollection.GetEnumerator();
			if (managementObjectEnumerator.MoveNext())
			{
				ManagementObject managementObject = (ManagementObject)managementObjectEnumerator.Current;
				using (managementObject)
				{
					return Convert.ToDouble(managementObject["CurrentBrightness"]);
				}
			}
		}
		catch
		{
		}
		return null;
	}

	public async Task SetBrightnessAsync(double value)
	{
		if (await Task.Run(delegate
		{
			try
			{
				using ManagementObjectSearcher managementObjectSearcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods WHERE Active=True");
				using ManagementObjectCollection managementObjectCollection = managementObjectSearcher.Get();
				bool result = false;
				foreach (ManagementObject item in managementObjectCollection)
				{
					using (item)
					{
						item.InvokeMethod("WmiSetBrightness", new object[2]
						{
							0u,
							(byte)Math.Clamp(value, 0.0, 100.0)
						});
					}
					result = true;
				}
				return result;
			}
			catch
			{
				return false;
			}
		}))
		{
			Brightness = value;
		}
		else
		{
			this.Notice?.Invoke(Localization.T("显示器未提供 Windows 亮度接口，请使用显示器的亮度控制。"));
		}
	}

	public async Task KeysAsync(IReadOnlyList<int> keys, CancellationToken cancellation = default(CancellationToken))
	{
		cancellation.ThrowIfCancellationRequested();
		if (keys.Contains(256))
		{
			await SetBrightnessAsync((Brightness ?? ReadBrightness() ?? 50.0) + 10.0);
		}
		if (keys.Contains(257))
		{
			await SetBrightnessAsync((Brightness ?? ReadBrightness() ?? 50.0) - 10.0);
		}
		cancellation.ThrowIfCancellationRequested();
		SendKeys(keys.Where((int x) => x > 0 && (x < 256 || (x >= 513 && x <= 517))).ToArray());
	}

	public static void SendKeys(IReadOnlyList<int> keys)
	{
		KeyboardInput.Tap(keys);
	}

	internal static Task SendHotkeyAsync(IReadOnlyList<int> keys, int holdMs = 45)
	{
		return KeyboardInput.TapAsync(keys, holdMs);
	}

	internal static void SetKeysHeld(IReadOnlyList<int> keys, bool down)
	{
		KeyboardInput.Held(keys, down);
	}

	public static void Launch(string target)
	{
		target = target.Trim();
		if (target.Length == 0)
		{
			return;
		}
		if (target.StartsWith("shell:AppsFolder\\", StringComparison.OrdinalIgnoreCase))
		{
			Process.Start(new ProcessStartInfo("explorer.exe")
			{
				Arguments = target,
				UseShellExecute = true
			});
			return;
		}
		if (target.Contains('!') && !File.Exists(target))
		{
			Process.Start(new ProcessStartInfo("explorer.exe")
			{
				Arguments = "shell:AppsFolder\\" + target,
				UseShellExecute = true
			});
			return;
		}
		try
		{
			Process.Start(new ProcessStartInfo(target)
			{
				UseShellExecute = true
			});
		}
		catch (Win32Exception error) when (target.StartsWith("ms-settings:", StringComparison.OrdinalIgnoreCase))
		{
			Diagnostics.Error(error, "Settings.ShellActivation");
			LaunchSettingsFallback(target);
		}
	}

	private static async Task LaunchSettingsFallback(string target)
	{
		try
		{
			if (!(await Launcher.LaunchUriAsync(new Uri(target))))
			{
				throw new InvalidOperationException(Localization.T("系统设置未接受打开请求。"));
			}
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Settings.UriActivation");
			App.CurrentApp.Report(Localization.T("系统设置暂时无法打开，请从 Windows 设置中进入。"));
		}
	}

	public static void LaunchRecorder()
	{
		Launch("shell:AppsFolder\\Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe!App");
	}

	public void Dispose()
	{
		disposed = true;
		if (media != null)
		{
			media.CurrentSessionChanged -= MediaSessionChanged;
		}
		if (currentSession != null)
		{
			currentSession.PlaybackInfoChanged -= OnPlaybackChanged;
		}
		currentSession = null;
		radios.Clear();
		media = null;
	}
}
