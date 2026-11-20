using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WinRT;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace TouchAssistant.Native;

internal static class WindowSnapshot
{
	[ComImport]
	[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface ICaptureInterop
	{
		nint CreateForWindow(nint window, ref Guid iid);

		nint CreateForMonitor(nint monitor, ref Guid iid);
	}

	private static IDirect3DDevice? device;

	private static int captures;

	private static int generation;

	internal static bool HasDevice => device != null;

	[DllImport("d3d11")]
	private static extern int D3D11CreateDevice(nint adapter, uint driver, nint software, uint flags, nint levels, uint count, uint sdk, out nint device, out uint level, out nint context);

	[DllImport("d3d11")]
	private static extern int CreateDirect3D11DeviceFromDXGIDevice(nint dxgi, out nint device);

	[DllImport("combase", CharSet = CharSet.Unicode)]
	private static extern int WindowsCreateString(string text, int length, out nint value);

	[DllImport("combase")]
	private static extern int WindowsDeleteString(nint value);

	[DllImport("combase")]
	private static extern int RoGetActivationFactory(nint name, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out ICaptureInterop factory);

	private static IDirect3DDevice Device()
	{
		if (device != null)
		{
			return device;
		}
		nint num = 0;
		nint context = 0;
		nint ppv = 0;
		nint num2 = 0;
		try
		{
			Marshal.ThrowExceptionForHR(D3D11CreateDevice(0, 1u, 0, 32u, 0, 0u, 7u, out num, out var _, out context));
			Guid iid = new Guid("54EC77FA-1377-44E6-8C32-88FD5F44C84C");
			Marshal.ThrowExceptionForHR(Marshal.QueryInterface(num, in iid, out ppv));
			Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(ppv, out num2));
			return device = MarshalInterface<IDirect3DDevice>.FromAbi(num2);
		}
		finally
		{
			if (num2 != 0)
			{
				Marshal.Release(num2);
			}
			if (ppv != 0)
			{
				Marshal.Release(ppv);
			}
			if (context != 0)
			{
				Marshal.Release(context);
			}
			if (num != 0)
			{
				Marshal.Release(num);
			}
		}
	}

	private static GraphicsCaptureItem Item(nint hwnd)
	{
		nint value = 0;
		nint num = 0;
		ICaptureInterop factory = null;
		try
		{
			Marshal.ThrowExceptionForHR(WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem", "Windows.Graphics.Capture.GraphicsCaptureItem".Length, out value));
			Guid iid = typeof(ICaptureInterop).GUID;
			Marshal.ThrowExceptionForHR(RoGetActivationFactory(value, ref iid, out factory));
			Guid iid2 = new Guid("79C3F95B-31F7-4EC2-A464-632EF5D30760");
			num = factory.CreateForWindow(hwnd, ref iid2);
			return MarshalInspectable<GraphicsCaptureItem>.FromAbi(num);
		}
		finally
		{
			if (num != 0)
			{
				Marshal.Release(num);
			}
			if (factory != null)
			{
				Marshal.ReleaseComObject(factory);
			}
			if (value != 0)
			{
				WindowsDeleteString(value);
			}
		}
	}

	internal static async Task<byte[]?> Capture(nint hwnd, CancellationToken token)
	{
		captures++;
		generation++;
		try
		{
			return await CaptureCore(hwnd, token);
		}
		finally
		{
			captures--;
			if (captures == 0)
			{
				ReleaseIdle(generation);
			}
		}
	}

	private static async Task ReleaseIdle(int stamp)
	{
		await Task.Delay(3000);
		if (captures == 0 && stamp == generation)
		{
			Dispose();
		}
	}

	private static async Task<byte[]?> CaptureCore(nint hwnd, CancellationToken token)
	{
		if (!GraphicsCaptureSession.IsSupported())
		{
			return null;
		}
		GraphicsCaptureItem graphicsCaptureItem = Item(hwnd);
		using Direct3D11CaptureFramePool pool = Direct3D11CaptureFramePool.CreateFreeThreaded(Device(), DirectXPixelFormat.B8G8R8A8UIntNormalized, 1, graphicsCaptureItem.Size);
		using GraphicsCaptureSession session = pool.CreateCaptureSession(graphicsCaptureItem);
		session.IsCursorCaptureEnabled = false;
		session.StartCapture();
		for (int i = 0; i < 30; i++)
		{
			await Task.Delay(15, token);
			using Direct3D11CaptureFrame frame = pool.TryGetNextFrame();
			if (frame == null)
			{
				continue;
			}
			using SoftwareBitmap bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(frame.Surface, BitmapAlphaMode.Premultiplied);
			using InMemoryRandomAccessStream stream = new InMemoryRandomAccessStream();
			BitmapEncoder obj = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
			obj.SetSoftwareBitmap(bitmap);
			await obj.FlushAsync();
			token.ThrowIfCancellationRequested();
			stream.Seek(0uL);
			using DataReader reader = new DataReader(stream);
			await reader.LoadAsync((uint)stream.Size);
			byte[] array = new byte[(uint)stream.Size];
			reader.ReadBytes(array);
			return array;
		}
		return null;
	}

	internal static void Dispose()
	{
		device?.Dispose();
		device = null;
	}
}
