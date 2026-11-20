using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TouchAssistant.Native;
using TouchAssistant.UI;
using Windows.Storage.Streams;

namespace TouchAssistant.Services;

internal static class WindowPreview
{
	private static bool bypassDecodedReuseForTest;

	private static readonly SemaphoreSlim gate = new SemaphoreSlim((RuntimeInformation.ProcessArchitecture == Architecture.Arm64) ? 1 : 2);

	private static readonly Dictionary<nint, (uint Pid, byte[] Png, long Time)> cache = new Dictionary<nint, (uint, byte[], long)>();

	private static readonly Dictionary<nint, (uint Pid, ImageSource Image)> decoded = new Dictionary<nint, (uint, ImageSource)>();

	private static Window? settingsWindow;

	private static RenderTargetBitmap? settingsPreview;

	private static Task<ImageSource?>? settingsCapture;

	private static Task warming = Task.CompletedTask;

	private static int trimGeneration;

	internal static Task PendingWarm => warming;

	internal static bool HasSettingsCaptureReference
	{
		get
		{
			if (!(settingsWindow != null) && !(settingsPreview != null))
			{
				return settingsCapture != null;
			}
			return true;
		}
	}

	internal static event Action<nint>? Prepared;

	[DllImport("user32")]
	private static extern bool PrintWindow(nint hwnd, nint dc, uint flags);

	[DllImport("user32")]
	private static extern bool IsHungAppWindow(nint hwnd);

	internal static void RegisterSettings(Window window)
	{
		settingsWindow = window;
		window.Activated += async delegate
		{
			await CaptureSettings();
		};
		window.Closed += delegate
		{
			if ((object)settingsWindow == window)
			{
				settingsWindow = null;
				settingsPreview = null;
				settingsCapture = null;
			}
		};
	}

	private static Task<ImageSource?> CaptureSettings()
	{
		Task<ImageSource> task = settingsCapture;
		if (task != null && !task.IsCompleted)
		{
			return settingsCapture;
		}
		return settingsCapture = CaptureSettingsCore();
	}

	private static async Task<ImageSource?> CaptureSettingsCore()
	{
		Window window = settingsWindow;
		if (window == null || !(window.Content is FrameworkElement { IsLoaded: not false }) || !Win32.IsWindowVisible(Ui.Handle(window)) || WindowCatalog.Minimized(Ui.Handle(window)))
		{
			return settingsPreview;
		}
		try
		{
			RenderTargetBitmap bitmap = new RenderTargetBitmap();
			await bitmap.RenderAsync(window.Content, 480, 0);
			if ((object)window == settingsWindow && bitmap.PixelWidth > 0 && bitmap.PixelHeight > 0)
			{
				settingsPreview = bitmap;
			}
		}
		catch (Exception ex) when (ex.HResult == -2147467260)
		{
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "WindowPreview.Settings");
		}
		return settingsPreview;
	}

	internal static Task Prewarm(bool overview)
	{
		trimGeneration++;
		if (!warming.IsCompleted)
		{
			return warming;
		}
		return warming = WarmCore(overview);
	}

	private static async Task WarmCore(bool overview)
	{
		foreach (RunningWindow item in (overview ? WindowCatalog.Read() : WindowCatalog.ReadStageTasks()).Take(overview ? 8 : 5))
		{
			if (IsFresh(item.Handle))
			{
				await CachedAsync(item.Handle);
				continue;
			}
			try
			{
				using (DwmThumbnail host = DwmThumbnail.Create(item.Handle))
				{
					if (host == null)
					{
						goto end_IL_0101;
					}
					double val = WindowCatalog.Aspect(item.Handle);
					host.PrepareOffscreen(Math.Max(1, (int)(480.0 * Math.Min(1.0, val))), Math.Max(1, (int)(480.0 / Math.Max(1.0, val))));
					using (CancellationTokenSource timeout = new CancellationTokenSource(650))
					{
						byte[] array = await WindowSnapshot.Capture(host.Handle, timeout.Token);
						if (array != null)
						{
							Store(item.Handle, array);
							await CachedAsync(item.Handle);
							WindowPreview.Prepared?.Invoke(item.Handle);
						}
					}
					goto end_IL_00e2;
					end_IL_0101:;
				}
				end_IL_00e2:;
			}
			catch (OperationCanceledException)
			{
			}
			catch (Exception error)
			{
				Diagnostics.Error(error, "WindowPreview.Prewarm");
			}
		}
	}

	internal static void TrimWhenIdle()
	{
		TrimLater(++trimGeneration);
	}

	private static async Task TrimLater(int stamp)
	{
		await Task.Delay(60000);
		if (stamp == trimGeneration)
		{
			decoded.Clear();
		}
	}

	public static void ClearCache()
	{
		cache.Clear();
		decoded.Clear();
	}

	internal static ImageSource? Cached(nint hwnd)
	{
		Win32.GetWindowThreadProcessId(hwnd, out var process);
		if (!decoded.TryGetValue(hwnd, out (uint, ImageSource) value) || value.Item1 != process)
		{
			return null;
		}
		return value.Item2;
	}

	internal static bool IsFresh(nint hwnd)
	{
		Win32.GetWindowThreadProcessId(hwnd, out var process);
		if (cache.TryGetValue(hwnd, out (uint, byte[], long) value) && value.Item1 == process)
		{
			if (!WindowCatalog.Minimized(hwnd))
			{
				return Environment.TickCount64 - value.Item3 < 15000;
			}
			return true;
		}
		return false;
	}

	internal static void Store(nint hwnd, byte[] png)
	{
		Win32.GetWindowThreadProcessId(hwnd, out var process);
		if (cache.Count >= 24 && !cache.ContainsKey(hwnd))
		{
			nint key = cache.MinBy<KeyValuePair<nint, (uint, byte[], long)>, long>((KeyValuePair<nint, (uint Pid, byte[] Png, long Time)> pair) => pair.Value.Time).Key;
			cache.Remove(key);
			decoded.Remove(key);
		}
		decoded.Remove(hwnd);
		cache[hwnd] = (process, png, Environment.TickCount64);
	}

	internal static async Task<ImageSource?> CachedAsync(nint hwnd)
	{
		ImageSource imageSource = Cached(hwnd);
		if ((object)imageSource != null)
		{
			return imageSource;
		}
		Win32.GetWindowThreadProcessId(hwnd, out var pid);
		if (!cache.TryGetValue(hwnd, out var saved) || saved.Pid != pid)
		{
			return null;
		}
		using MemoryStream memory = new MemoryStream(saved.Png);
		using IRandomAccessStream stream = memory.AsRandomAccessStream();
		BitmapImage image = new BitmapImage();
		await image.SetSourceAsync(stream);
		if (cache.TryGetValue(hwnd, out (uint, byte[], long) value) && value.Item2 == saved.Png)
		{
			decoded[hwnd] = (pid, image);
		}
		return image;
	}

	private static async Task<byte[]?> ReadBytes(nint hwnd, CancellationToken token)
	{
		Win32.GetWindowThreadProcessId(hwnd, out var process);
		cache.TryGetValue(hwnd, out (uint, byte[], long) value);
		byte[] bytes = ((value.Item1 == process) ? value.Item2 : null);
		bool flag = !WindowCatalog.Minimized(hwnd) && (bytes == null || Environment.TickCount64 - value.Item3 > 15000);
		if (flag)
		{
			flag = await gate.WaitAsync(0, token);
		}
		if (flag)
		{
			try
			{
				byte[] array = await Task.Run(delegate
				{
					try
					{
						return Capture(hwnd);
					}
					finally
					{
						gate.Release();
					}
				}).WaitAsync(TimeSpan.FromSeconds(2.0), token);
				if (array != null && !token.IsCancellationRequested)
				{
					bytes = array;
					Store(hwnd, array);
				}
			}
			catch (OperationCanceledException)
			{
				throw;
			}
			catch
			{
			}
		}
		return bytes;
	}

	public static async Task<ImageSource?> LoadAsync(nint hwnd, CancellationToken token)
	{
		_ = 3;
		try
		{
			if (settingsWindow != null && hwnd == Ui.Handle(settingsWindow))
			{
				return (!token.IsCancellationRequested) ? (await CaptureSettings()) : null;
			}
			byte[] png = await ReadBytes(hwnd, token);
			if (png == null || token.IsCancellationRequested)
			{
				return null;
			}
			if (!bypassDecodedReuseForTest && cache.TryGetValue(hwnd, out (uint, byte[], long) value) && value.Item2 == png)
			{
				ImageSource imageSource = await CachedAsync(hwnd);
				if (token.IsCancellationRequested)
				{
					return null;
				}
				if (imageSource != null)
				{
					return imageSource;
				}
			}
			using MemoryStream memory = new MemoryStream(png);
			using IRandomAccessStream stream = memory.AsRandomAccessStream();
			BitmapImage image = new BitmapImage();
			await image.SetSourceAsync(stream);
			return image;
		}
		catch
		{
			return null;
		}
	}

	internal static async Task<object> CheckReuseAsync(Window owner)
	{
		nint handle = Ui.Handle(owner);
		byte[] png;
		using (Bitmap bitmap = new Bitmap(480, 320))
		{
			using (Graphics graphics = Graphics.FromImage(bitmap))
			{
				graphics.Clear(Color.CornflowerBlue);
			}
			using MemoryStream stream = new MemoryStream();
			bitmap.Save(stream, ImageFormat.Png);
			png = stream.ToArray();
			try
			{
				object before = await Measure(baseline: true);
				object after = await Measure(baseline: false);
				ImageSource previous = Cached(handle);
				Store(handle, (byte[])png.Clone());
				ImageSource imageSource = await LoadAsync(handle, CancellationToken.None);
				if (imageSource == null || (object)imageSource == previous)
				{
					throw new InvalidOperationException("缩略图内容更新后仍使用旧缓存");
				}
				using CancellationTokenSource canceled = new CancellationTokenSource();
				canceled.Cancel();
				if (await LoadAsync(handle, canceled.Token) != null)
				{
					throw new InvalidOperationException("取消后仍返回缩略图");
				}
				ClearCache();
				if (Cached(handle) != null)
				{
					throw new InvalidOperationException("缩略图缓存未清空");
				}
				return new
				{
					Before = before,
					After = after,
					Invalidation = "PASS",
					Cancellation = "PASS",
					Clear = "PASS",
					Limit = "Synthetic 480x320 snapshot; elapsed/managed allocation include UI runtime activity, not an end-to-end card benchmark"
				};
			}
			finally
			{
				bypassDecodedReuseForTest = false;
				ClearCache();
			}
		}
		async Task<object> Measure(bool baseline)
		{
			ClearCache();
			Store(handle, png);
			await CachedAsync(handle);
			bypassDecodedReuseForTest = baseline;
			HashSet<ImageSource> seen = new HashSet<ImageSource>(ReferenceEqualityComparer.Instance);
			long allocated = GC.GetTotalAllocatedBytes(precise: true);
			Stopwatch watch = Stopwatch.StartNew();
			for (int i = 0; i < 100; i++)
			{
				ImageSource imageSource2 = await LoadAsync(handle, CancellationToken.None);
				if (imageSource2 == null)
				{
					throw new InvalidOperationException("缩略图加载失败");
				}
				seen.Add(imageSource2);
			}
			if (seen.Count != ((!baseline) ? 1 : 100))
			{
				throw new InvalidOperationException("解码图像复用数量错误");
			}
			return new
			{
				Baseline = baseline,
				Loads = 100,
				DistinctDecodedImages = seen.Count,
				ElapsedMilliseconds = watch.Elapsed.TotalMilliseconds,
				ManagedAllocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocated
			};
		}
	}

	private static byte[]? Capture(nint hwnd)
	{
		if (IsHungAppWindow(hwnd) || !Win32.GetWindowRect(hwnd, out var r) || r.Width <= 0 || r.Height <= 0 || r.Width > 8192 || r.Height > 8192 || (long)r.Width * (long)r.Height > 12000000)
		{
			return null;
		}
		using Bitmap bitmap = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppRgb);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			nint hdc = graphics.GetHdc();
			try
			{
				if (!PrintWindow(hwnd, hdc, 2u))
				{
					return null;
				}
			}
			finally
			{
				graphics.ReleaseHdc(hdc);
			}
		}
		double num = Math.Min(1.0, 480.0 / (double)Math.Max(r.Width, r.Height));
		int num2 = Math.Max(1, (int)((double)r.Width * num));
		int num3 = Math.Max(1, (int)((double)r.Height * num));
		using Bitmap bitmap2 = new Bitmap(bitmap, new Size(num2, num3));
		bool flag = true;
		for (int i = 1; i < 8 && flag; i++)
		{
			for (int j = 1; j < 8; j++)
			{
				Color pixel = bitmap2.GetPixel(j * (num2 - 1) / 8, i * (num3 - 1) / 8);
				if (pixel.R > 12 || pixel.G > 12 || pixel.B > 12)
				{
					flag = false;
					break;
				}
			}
		}
		if (flag)
		{
			return null;
		}
		using MemoryStream memoryStream = new MemoryStream();
		bitmap2.Save(memoryStream, ImageFormat.Png);
		return memoryStream.ToArray();
	}
}
