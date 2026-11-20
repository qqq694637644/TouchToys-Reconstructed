using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace TouchAssistant.Native;

internal static class DwmPreviewProbe
{
	private struct Properties
	{
		public uint Flags;

		public Win32.RECT Destination;

		public Win32.RECT Source;

		public byte Opacity;

		[MarshalAs(UnmanagedType.Bool)]
		public bool Visible;

		[MarshalAs(UnmanagedType.Bool)]
		public bool ClientOnly;
	}

	[DllImport("dwmapi")]
	private static extern int DwmRegisterThumbnail(nint destination, nint source, out nint thumbnail);

	[DllImport("dwmapi")]
	private static extern int DwmUpdateThumbnailProperties(nint thumbnail, ref Properties properties);

	[DllImport("dwmapi")]
	private static extern int DwmUnregisterThumbnail(nint thumbnail);

	[DllImport("dwmapi")]
	private static extern int DwmFlush();

	[DllImport("gdi32")]
	private static extern nint CreateSolidBrush(uint color);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool UnregisterClass(string name, nint instance);

	[DllImport("user32")]
	private static extern bool IsIconic(nint window);

	[DllImport("user32")]
	private static extern nint WindowFromPoint(Win32.POINT point);

	private static nint SetStyle(nint h, int index, nint value)
	{
		return WindowLong.Set(h, index, value);
	}

	[DllImport("user32")]
	private static extern bool SetLayeredWindowAttributes(nint h, uint key, byte alpha, uint flags);

	internal static async Task<string> CheckAsync()
	{
		Win32.WndProc callback = Win32.DefWindowProc;
		nint source = 0;
		nint destination = 0;
		nint thumbnail = 0;
		nint instance = Win32.GetModuleHandle(null);
		string sourceClass = "TouchAssistant.Probe.Source";
		string destinationClass = "TouchAssistant.Probe.Destination";
		nint red = CreateSolidBrush(2957516u);
		nint gray = CreateSolidBrush(4210752u);
		try
		{
			Win32.WNDCLASS wc = new Win32.WNDCLASS
			{
				lpfnWndProc = callback,
				hInstance = instance,
				hbrBackground = red,
				lpszClassName = sourceClass
			};
			Win32.RegisterClass(ref wc);
			wc.hbrBackground = gray;
			wc.lpszClassName = destinationClass;
			Win32.RegisterClass(ref wc);
			Win32.GetCursorPos(out var p);
			Win32.RECT work = Win32.Monitor(p).rcWork;
			source = Win32.CreateWindowEx(134217864u, sourceClass, "Preview source test", 2147483648u, work.Left + 40, work.Top + 60, 220, 160, 0, 0, instance, 0);
			destination = Win32.CreateWindowEx(134217864u, destinationClass, "Preview destination test", 2147483648u, work.Left + 300, work.Top + 60, 220, 160, 0, 0, instance, 0);
			Win32.ShowWindow(source, 4);
			Win32.ShowWindow(destination, 4);
			await Task.Delay(150);
			int num = DwmRegisterThumbnail(destination, source, out thumbnail);
			if (num != 0)
			{
				return $"Register HRESULT=0x{num:X8}";
			}
			Properties properties = new Properties
			{
				Flags = 29u,
				Destination = new Win32.RECT
				{
					Left = 0,
					Top = 0,
					Right = 220,
					Bottom = 160
				},
				Opacity = byte.MaxValue,
				Visible = true,
				ClientOnly = false
			};
			DwmUpdateThumbnailProperties(thumbnail, ref properties);
			DwmFlush();
			await Task.Delay(150);
			Color normal = Sample();
			Win32.ShowWindow(source, 6);
			await Task.Delay(350);
			DwmFlush();
			Color minimized = Sample();
			SetStyle(destination, -20, 134742184);
			SetLayeredWindowAttributes(destination, 0u, byte.MaxValue, 2u);
			DwmFlush();
			await Task.Delay(150);
			Color layered = Sample();
			DwmUnregisterThumbnail(thumbnail);
			thumbnail = 0;
			SetStyle(destination, -20, 134742152);
			using DwmThumbnail live = DwmThumbnail.Create(source);
			Win32.GetWindowRect(destination, out var target);
			live?.Update(target.Left, target.Top, 220, 160, 0, work, 1.0, 14);
			DwmFlush();
			await Task.Delay(150);
			Color liveColor = Sample();
			bool passes = WindowFromPoint(new Win32.POINT(target.Left + 110, target.Top + 80)) == destination;
			if (live == null || !passes || !IsIconic(source) || minimized.R <= 150 || minimized.G >= 80 || liveColor.R <= 150 || liveColor.G >= 80)
			{
				throw new InvalidOperationException($"Minimized preview regression: source={minimized}, host={liveColor}, passThrough={passes}.");
			}
			byte[] buffer = live.Snapshot();
			using MemoryStream stream = new MemoryStream(buffer);
			using Bitmap snapshot = new Bitmap(stream);
			Color pixel = snapshot.GetPixel(snapshot.Width / 2, snapshot.Height / 2);
			if (pixel.R <= 150 || pixel.G >= 80)
			{
				throw new InvalidOperationException("最小化预览缓存不是源窗口画面。");
			}
			live.PrepareOffscreen(220, 160);
			using MemoryStream stream2 = new MemoryStream((await WindowSnapshot.Capture(live.Handle, default(CancellationToken))) ?? throw new InvalidOperationException("离屏预览没有返回帧。"));
			using Bitmap bitmap = new Bitmap(stream2);
			Color pixel2 = bitmap.GetPixel(bitmap.Width / 2, bitmap.Height / 2);
			if (pixel2.R <= 150 || pixel2.G >= 80)
			{
				throw new InvalidOperationException($"离屏最小化预览颜色错误：{pixel2}。");
			}
			return $"Offscreen={pixel2.R},{pixel2.G},{pixel2.B}; LiveHost={liveColor.R},{liveColor.G},{liveColor.B}; PassThrough={passes}; Layered={layered.R},{layered.G},{layered.B}; Normal={normal.R},{normal.G},{normal.B}; Minimized={minimized.R},{minimized.G},{minimized.B}; Iconic={IsIconic(source)}; Retained={minimized.R > 150 && minimized.G < 80 && minimized.B < 90}";
		}
		finally
		{
			if (thumbnail != 0)
			{
				DwmUnregisterThumbnail(thumbnail);
			}
			if (destination != 0)
			{
				Win32.DestroyWindow(destination);
			}
			if (source != 0)
			{
				Win32.DestroyWindow(source);
			}
			UnregisterClass(sourceClass, instance);
			UnregisterClass(destinationClass, instance);
			Win32.DeleteObject(red);
			Win32.DeleteObject(gray);
			GC.KeepAlive(callback);
		}
		Color Sample()
		{
			Win32.GetWindowRect(destination, out var r);
			using Bitmap bitmap2 = new Bitmap(1, 1);
			using (Graphics graphics = Graphics.FromImage(bitmap2))
			{
				graphics.CopyFromScreen(r.Left + 110, r.Top + 80, 0, 0, new Size(1, 1));
			}
			return bitmap2.GetPixel(0, 0);
		}
	}
}
