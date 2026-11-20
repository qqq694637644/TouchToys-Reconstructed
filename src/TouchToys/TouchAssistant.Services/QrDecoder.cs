using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using ZXing;
using ZXing.Common;

namespace TouchAssistant.Services;

internal static class QrDecoder
{
	public static string? Decode(byte[] bgra, int width, int height)
	{
		BarcodeReaderGeneric barcodeReaderGeneric = new BarcodeReaderGeneric();
		barcodeReaderGeneric.AutoRotate = true;
		barcodeReaderGeneric.Options = new DecodingOptions
		{
			TryHarder = true,
			TryInverted = true,
			PossibleFormats = new BarcodeFormat[1] { BarcodeFormat.QR_CODE }
		};
		return barcodeReaderGeneric.Decode(bgra, width, height, RGBLuminanceSource.BitmapFormat.BGRA32)?.Text;
	}

	public static string? Decode(Bitmap bitmap)
	{
		using Bitmap bitmap2 = new Bitmap(bitmap.Width, bitmap.Height, PixelFormat.Format32bppArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap2))
		{
			graphics.DrawImageUnscaled(bitmap, 0, 0);
		}
		BitmapData bitmapData = bitmap2.LockBits(new Rectangle(0, 0, bitmap2.Width, bitmap2.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
		try
		{
			byte[] array = new byte[bitmap2.Width * bitmap2.Height * 4];
			for (int i = 0; i < bitmap2.Height; i++)
			{
				Marshal.Copy(bitmapData.Scan0 + i * bitmapData.Stride, array, i * bitmap2.Width * 4, bitmap2.Width * 4);
			}
			return Decode(array, bitmap2.Width, bitmap2.Height);
		}
		finally
		{
			bitmap2.UnlockBits(bitmapData);
		}
	}

	[DllImport("user32")]
	private static extern int GetSystemMetrics(int index);

	public static string? Screen()
	{
		int systemMetrics = GetSystemMetrics(76);
		int systemMetrics2 = GetSystemMetrics(77);
		int systemMetrics3 = GetSystemMetrics(78);
		int systemMetrics4 = GetSystemMetrics(79);
		using Bitmap bitmap = new Bitmap(systemMetrics3, systemMetrics4);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.CopyFromScreen(systemMetrics, systemMetrics2, 0, 0, new Size(systemMetrics3, systemMetrics4));
		}
		return Decode(bitmap);
	}
}
