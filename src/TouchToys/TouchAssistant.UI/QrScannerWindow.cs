using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TouchAssistant.Native;
using TouchAssistant.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Media.Capture;
using Windows.Media.Capture.Frames;
using Windows.Media.MediaProperties;
using Windows.Storage.Streams;

namespace TouchAssistant.UI;

internal sealed class QrScannerWindow : Window
{
	private readonly TextBox result = new TextBox
	{
		Header = Localization.T("识别内容"),
		IsReadOnly = true,
		AcceptsReturn = true,
		TextWrapping = TextWrapping.Wrap,
		MinHeight = 100.0
	};

	private readonly TextBlock status = Ui.Text("摄像头默认关闭。可选择打开摄像头、识别屏幕或图片。内容仅在本机处理。", 13.0);

	private readonly Image preview = new Image
	{
		Height = 200.0,
		Stretch = Stretch.Uniform
	};

	private readonly DispatcherQueueTimer timer;

	private MediaCapture? camera;

	private MediaFrameReader? reader;

	private SoftwareBitmapSource? previewSource;

	private bool previewBusy;

	private bool decodeBusy;

	private long lastDecode;

	private readonly SemaphoreSlim cameraGate = new SemaphoreSlim(1, 1);

	private Task frameTask = Task.CompletedTask;

	private Task decodeTask = Task.CompletedTask;

	private bool processing;

	private bool closed;

	private bool cameraStarting;

	private int cameraEpoch;

	private readonly Button cameraButton;

	private bool switchingMode;

	public QrScannerWindow()
	{
		base.Title = Localization.T("TouchToys · 扫码");
		Ui.SetAppIcon(this);
		base.SystemBackdrop = new MicaBackdrop();
		StackPanel stackPanel = new StackPanel
		{
			Padding = new Thickness(24.0),
			Spacing = 14.0
		};
		stackPanel.Children.Add(Ui.Text("扫码", 28.0));
		stackPanel.Children.Add(status);
		stackPanel.Children.Add(Ui.Button("识别屏幕中的二维码", async delegate
		{
			await ReadScreen();
		}));
		stackPanel.Children.Add(Ui.Button("选择二维码图片", async delegate
		{
			await ReadFile();
		}));
		cameraButton = Ui.Button("打开摄像头", async delegate
		{
			await ToggleCamera();
		});
		stackPanel.Children.Add(cameraButton);
		stackPanel.Children.Add(preview);
		stackPanel.Children.Add(result);
		stackPanel.Children.Add(Ui.Button("复制内容", delegate
		{
			if (result.Text.Length != 0)
			{
				DataPackage dataPackage = new DataPackage();
				dataPackage.SetText(result.Text);
				Clipboard.SetContent(dataPackage);
			}
		}));
		stackPanel.Children.Add(Ui.Button("打开识别的网址", delegate
		{
			Uri uri;
			bool flag = Uri.TryCreate(result.Text, UriKind.Absolute, out uri);
			if (flag)
			{
				string scheme = uri.Scheme;
				bool flag2 = ((scheme == "http" || scheme == "https") ? true : false);
				flag = flag2;
			}
			if (flag)
			{
				SystemControl.Launch(uri.AbsoluteUri);
			}
			else
			{
				status.Text = Localization.T("识别内容不是 HTTP / HTTPS 网址。");
			}
		}));
		base.Content = new ScrollViewer
		{
			Content = stackPanel
		};
		Win32.GetCursorPos(out var p);
		double num = Win32.Scale(p);
		Win32.RECT rcWork = Win32.Monitor(p).rcWork;
		int num2 = Math.Min(rcWork.Width, (int)(500.0 * num));
		int num3 = Math.Min(rcWork.Height, (int)(800.0 * num));
		base.AppWindow.MoveAndResize(new RectInt32(rcWork.Left + (rcWork.Width - num2) / 2, rcWork.Top + (rcWork.Height - num3) / 2, num2, num3));
		timer = base.DispatcherQueue.CreateTimer();
		timer.Interval = TimeSpan.FromMilliseconds(50.0);
		timer.Tick += delegate
		{
			if (frameTask.IsCompleted)
			{
				frameTask = ReadCamera();
			}
		};
		base.Closed += async delegate
		{
			closed = true;
			await StopCamera();
		};
	}

	private void Display(string? text)
	{
		if (!closed)
		{
			result.Text = text ?? "";
			status.Text = Localization.T((text == null) ? "没有找到二维码，请调整距离或选择清晰图片。" : "识别成功。可以复制内容，或手动打开网址。");
		}
	}

	private async Task ReadScreen()
	{
		if (!(await PrepareMode()))
		{
			return;
		}
		processing = true;
		try
		{
			base.AppWindow.Hide();
			await Task.Delay(250);
			string text = await Task.Run((Func<string>)QrDecoder.Screen);
			if (!closed)
			{
				Activate();
				Display(text);
			}
		}
		catch (Exception ex)
		{
			if (!closed)
			{
				Activate();
				status.Text = ex.Message;
			}
		}
		finally
		{
			processing = false;
		}
	}

	private async Task ReadFile()
	{
		if (!(await PrepareMode()))
		{
			return;
		}
		processing = true;
		try
		{
			string text = await TouchFilePicker.Pick(this, "选择二维码图片", ".png", ".jpg", ".jpeg", ".bmp", ".webp");
			if (text == null || closed)
			{
				return;
			}
			using FileStream source = File.OpenRead(text);
			using IRandomAccessStream input = source.AsRandomAccessStream();
			BitmapDecoder bitmapDecoder = await BitmapDecoder.CreateAsync(input);
			double num = Math.Min(1.0, 4096.0 / (double)Math.Max(bitmapDecoder.PixelWidth, bitmapDecoder.PixelHeight));
			BitmapTransform transform = new BitmapTransform
			{
				ScaledWidth = (uint)Math.Max(1.0, (double)bitmapDecoder.PixelWidth * num),
				ScaledHeight = (uint)Math.Max(1.0, (double)bitmapDecoder.PixelHeight * num)
			};
			using SoftwareBitmap bitmap = await bitmapDecoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
			int w = bitmap.PixelWidth;
			int h = bitmap.PixelHeight;
			byte[] pixels = new byte[checked(w * h * 4)];
			bitmap.CopyToBuffer(pixels.AsBuffer());
			Display(await Task.Run(() => QrDecoder.Decode(pixels, w, h)));
		}
		catch (Exception ex)
		{
			if (!closed)
			{
				status.Text = Localization.T("无法识别图片：") + ex.Message;
			}
		}
		finally
		{
			processing = false;
		}
	}

	private async Task ToggleCamera()
	{
		if (cameraStarting || closed || switchingMode || processing)
		{
			return;
		}
		cameraStarting = true;
		cameraButton.IsEnabled = false;
		await cameraGate.WaitAsync();
		MediaCapture capture = null;
		MediaFrameReader opened = null;
		try
		{
			try
			{
				if (closed)
				{
					goto end_IL_0108;
				}
				if (camera != null)
				{
					await StopCameraCore();
					if (!closed)
					{
						status.Text = Localization.T("摄像头已关闭。");
					}
					goto end_IL_0108;
				}
				status.Text = Localization.T("正在打开摄像头…");
				int epoch = ++cameraEpoch;
				capture = new MediaCapture();
				await capture.InitializeAsync(new MediaCaptureInitializationSettings
				{
					StreamingCaptureMode = StreamingCaptureMode.Video,
					SharingMode = MediaCaptureSharingMode.ExclusiveControl,
					MemoryPreference = MediaCaptureMemoryPreference.Cpu
				});
				if (closed || epoch != cameraEpoch)
				{
					goto end_IL_0108;
				}
				MediaFrameSource input = capture.FrameSources.Values.FirstOrDefault(delegate(MediaFrameSource x)
				{
					bool flag = x.Info.SourceKind == MediaFrameSourceKind.Color;
					if (flag)
					{
						MediaStreamType mediaStreamType = x.Info.MediaStreamType;
						bool flag2 = (uint)mediaStreamType <= 1u;
						flag = flag2;
					}
					return flag;
				}) ?? throw new InvalidOperationException(Localization.T("未找到彩色视频源。"));
				MediaFrameFormat mediaFrameFormat = (from f in input.SupportedFormats
					where f.VideoFormat.Width <= 1280 && f.VideoFormat.Height <= 720
					orderby Math.Abs((long)f.VideoFormat.Width - 640L), Math.Abs((double)f.FrameRate.Numerator / (double)Math.Max(1u, f.FrameRate.Denominator) - 30.0)
					select f).FirstOrDefault();
				if (mediaFrameFormat != null)
				{
					await input.SetFormatAsync(mediaFrameFormat);
				}
				VideoMediaFrameFormat videoFormat = input.CurrentFormat.VideoFormat;
				double num2 = Math.Min(640.0 / (double)videoFormat.Width, 480.0 / (double)videoFormat.Height);
				opened = await capture.CreateFrameReaderAsync(input, MediaEncodingSubtypes.Bgra8, new BitmapSize
				{
					Width = (uint)Math.Max(1.0, (double)videoFormat.Width * num2),
					Height = (uint)Math.Max(1.0, (double)videoFormat.Height * num2)
				});
				opened.AcquisitionMode = MediaFrameReaderAcquisitionMode.Realtime;
				if (await opened.StartAsync() != MediaFrameReaderStartStatus.Success)
				{
					throw new InvalidOperationException(Localization.T("摄像头视频流启动失败。"));
				}
				if (closed || epoch != cameraEpoch)
				{
					goto end_IL_0108;
				}
				previewSource = new SoftwareBitmapSource();
				preview.Source = previewSource;
				reader = opened;
				camera = capture;
				opened = null;
				capture = null;
				lastDecode = 0L;
				cameraButton.Content = Localization.T("关闭摄像头");
				status.Text = Localization.T("摄像头识别中，请将二维码放入画面。");
				timer.Start();
				goto end_IL_00e5;
				end_IL_0108:;
			}
			catch (Exception ex)
			{
				Diagnostics.Error(ex, "Scanner.Start");
				if (!closed)
				{
					status.Text = Localization.T("摄像头不可用：") + ex.Message;
				}
				goto end_IL_00e5;
			}
			end_IL_00e5:;
		}
		finally
		{
			await ReleaseCamera(opened, capture);
			cameraGate.Release();
			cameraStarting = false;
			if (!closed)
			{
				cameraButton.IsEnabled = true;
			}
		}
	}

	private async Task<bool> PrepareMode()
	{
		if (switchingMode || closed || processing || cameraStarting)
		{
			return false;
		}
		switchingMode = true;
		try
		{
			await StopCamera();
			return !closed;
		}
		finally
		{
			switchingMode = false;
		}
	}

	private async Task StopCamera()
	{
		cameraEpoch++;
		timer.Stop();
		await cameraGate.WaitAsync();
		try
		{
			await StopCameraCore();
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Scanner.Stop");
		}
		finally
		{
			cameraGate.Release();
		}
	}

	private async Task StopCameraCore()
	{
		cameraEpoch++;
		timer.Stop();
		MediaCapture old = camera;
		MediaFrameReader oldReader = reader;
		SoftwareBitmapSource source = previewSource;
		camera = null;
		reader = null;
		try
		{
			await Task.WhenAll(frameTask, decodeTask);
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Scanner.Drain");
		}
		try
		{
			preview.Source = null;
		}
		catch (Exception error2)
		{
			Diagnostics.Error(error2, "Scanner.Detach");
		}
		previewSource = null;
		await ReleaseCamera(oldReader, old);
		try
		{
			source?.Dispose();
		}
		catch (Exception error3)
		{
			Diagnostics.Error(error3, "Scanner.PreviewDispose");
		}
		if (!closed)
		{
			cameraButton.Content = Localization.T("打开摄像头");
		}
	}

	private static async Task ReleaseCamera(MediaFrameReader? oldReader, MediaCapture? old)
	{
		try
		{
			if (oldReader != null)
			{
				await oldReader.StopAsync();
			}
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Scanner.Stop");
		}
		try
		{
			oldReader?.Dispose();
		}
		catch (Exception error2)
		{
			Diagnostics.Error(error2, "Scanner.ReaderDispose");
		}
		try
		{
			old?.Dispose();
		}
		catch (Exception error3)
		{
			Diagnostics.Error(error3, "Scanner.CaptureDispose");
		}
	}

	private async Task ReadCamera()
	{
		if (previewBusy || reader == null || previewSource == null || closed)
		{
			return;
		}
		previewBusy = true;
		int epoch = cameraEpoch;
		try
		{
			using MediaFrameReference frame = reader.TryAcquireLatestFrame();
			using SoftwareBitmap raw = frame?.VideoMediaFrame?.SoftwareBitmap;
			if (raw == null)
			{
				return;
			}
			using SoftwareBitmap converted = ((raw.BitmapPixelFormat == BitmapPixelFormat.Bgra8 && raw.BitmapAlphaMode == BitmapAlphaMode.Premultiplied) ? null : SoftwareBitmap.Convert(raw, BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied));
			SoftwareBitmap bitmap = converted ?? raw;
			await previewSource.SetBitmapAsync(bitmap);
			if (closed || epoch != cameraEpoch)
			{
				return;
			}
			if (!decodeBusy && Environment.TickCount64 - lastDecode >= 250)
			{
				lastDecode = Environment.TickCount64;
				decodeBusy = true;
				int pixelWidth = bitmap.PixelWidth;
				int pixelHeight = bitmap.PixelHeight;
				byte[] array = ArrayPool<byte>.Shared.Rent(checked(pixelWidth * pixelHeight * 4));
				try
				{
					bitmap.CopyToBuffer(array.AsBuffer(0, pixelWidth * pixelHeight * 4));
					decodeTask = DecodeCamera(array, pixelWidth, pixelHeight, epoch);
				}
				catch
				{
					ArrayPool<byte>.Shared.Return(array, clearArray: true);
					decodeBusy = false;
					throw;
				}
			}
		}
		catch (Exception ex)
		{
			if (!closed && epoch == cameraEpoch)
			{
				Diagnostics.Error(ex, "Scanner.Frame");
				status.Text = Localization.T("无法读取摄像头：") + ex.Message;
				StopCamera();
			}
		}
		finally
		{
			previewBusy = false;
		}
	}

	private async Task DecodeCamera(byte[] pixels, int w, int h, int epoch)
	{
		try
		{
			string text = await Task.Run(() => QrDecoder.Decode(pixels, w, h));
			if (!closed && epoch == cameraEpoch && text != null)
			{
				Display(text);
				StopCamera();
			}
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Scanner.Decode");
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(pixels, clearArray: true);
			decodeBusy = false;
		}
	}

	internal async Task CheckLifecycleAsync()
	{
		Activate();
		await Task.Delay(100);
		if (camera != null || reader != null || cameraStarting || timer.IsRunning)
		{
			throw new InvalidOperationException("扫码页面默认启动了摄像头。");
		}
		for (int i = 0; i < 20; i++)
		{
			TaskCompletionSource frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			TaskCompletionSource decode = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
			frameTask = frame.Task;
			decodeTask = decode.Task;
			Task first = StopCamera();
			Task repeated = StopCamera();
			await Task.Delay(5);
			if (first.IsCompleted || repeated.IsCompleted || cameraGate.CurrentCount != 0)
			{
				throw new InvalidOperationException("释放没有等待帧任务。");
			}
			frame.SetResult();
			await Task.Delay(5);
			if (first.IsCompleted)
			{
				throw new InvalidOperationException("释放没有等待解码任务。");
			}
			decode.SetResult();
			await Task.WhenAll(first, repeated);
			if (cameraGate.CurrentCount != 1 || previewSource != null)
			{
				throw new InvalidOperationException("重复关闭未正确释放状态。");
			}
		}
	}
}
