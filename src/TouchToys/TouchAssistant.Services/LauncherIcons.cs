using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TouchAssistant.Native;
using Windows.Storage.Streams;

namespace TouchAssistant.Services;

internal static class LauncherIcons
{
	private struct Size
	{
		public int Width;

		public int Height;
	}

	[ComImport]
	[Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IShellItemImageFactory
	{
		[PreserveSig]
		int GetImage(Size size, uint flags, out nint bitmap);
	}

	private struct NativeBitmap
	{
		public int Type;

		public int Width;

		public int Height;

		public int WidthBytes;

		public ushort Planes;

		public ushort BitsPixel;

		public nint Bits;
	}

	private struct BitmapInfo
	{
		public uint Size;

		public int Width;

		public int Height;

		public ushort Planes;

		public ushort BitCount;

		public uint Compression;

		public uint ImageSize;

		public int XPels;

		public int YPels;

		public uint Colors;

		public uint Important;
	}

	private struct PropertyKey
	{
		public Guid Format;

		public uint Id;
	}

	[StructLayout(LayoutKind.Explicit, Size = 24)]
	private struct PropertyValue
	{
		[FieldOffset(0)]
		public ushort Type;

		[FieldOffset(8)]
		public nint Text;
	}

	[ComImport]
	[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
	[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	private interface IPropertyStore
	{
		void GetCount(out uint count);

		void GetAt(uint index, out PropertyKey key);

		void GetValue(ref PropertyKey key, out PropertyValue value);
	}

	private static readonly Dictionary<string, Task<ImageSource?>> cache = new Dictionary<string, Task<ImageSource>>(StringComparer.OrdinalIgnoreCase);

	private static readonly Dictionary<(nint, uint), Task<ImageSource?>> windowCache = new Dictionary<(nint, uint), Task<ImageSource>>();

	[DllImport("shell32", CharSet = CharSet.Unicode)]
	private static extern int SHCreateItemFromParsingName(string name, nint context, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory factory);

	[DllImport("gdi32")]
	private static extern bool DeleteObject(nint value);

	[DllImport("gdi32", EntryPoint = "GetObjectW")]
	private static extern int GetObject(nint value, int size, out NativeBitmap bitmap);

	[DllImport("gdi32")]
	private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, nint bits, ref BitmapInfo info, uint usage);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern nint SendMessageTimeout(nint h, uint m, nuint w, nint l, uint flags, uint timeout, out nint result);

	private static nint GetClassIcon(nint h, int index)
	{
		return WindowLong.Class(h, index);
	}

	[DllImport("shell32")]
	private static extern int SHGetPropertyStoreForWindow(nint window, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IPropertyStore store);

	[DllImport("ole32")]
	private static extern int PropVariantClear(ref PropertyValue value);

	private static string? WindowAppId(nint window)
	{
		IPropertyStore store = null;
		PropertyValue value = default(PropertyValue);
		try
		{
			Guid iid = typeof(IPropertyStore).GUID;
			if (SHGetPropertyStoreForWindow(window, ref iid, out store) != 0)
			{
				return null;
			}
			PropertyKey key = new PropertyKey
			{
				Format = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
				Id = 5u
			};
			store.GetValue(ref key, out value);
			return (value.Type == 31) ? Marshal.PtrToStringUni(value.Text) : null;
		}
		catch
		{
			return null;
		}
		finally
		{
			PropVariantClear(ref value);
			if (store != null)
			{
				Marshal.ReleaseComObject(store);
			}
		}
	}

	public static async Task<ImageSource?> LoadWindowAsync(RunningWindow window)
	{
		Win32.GetWindowThreadProcessId(window.Handle, out var process);
		(nint Handle, uint pid) key = (Handle: window.Handle, pid: process);
		if (!windowCache.TryGetValue(key, out Task<ImageSource> value))
		{
			if (windowCache.Count >= 96)
			{
				foreach (var item in new List<(nint, uint)>(windowCache.Keys))
				{
					if (!WindowCatalog.IsWindow(item.Item1))
					{
						windowCache.Remove(item);
					}
				}
				if (windowCache.Count >= 96)
				{
					windowCache.Clear();
				}
			}
			value = (windowCache[key] = LoadWindowCore(window));
		}
		ImageSource obj = await value;
		if (obj == null)
		{
			windowCache.Remove(key);
		}
		return obj;
	}

	private static async Task<ImageSource?> LoadWindowCore(RunningWindow window)
	{
		string text = await Task.Run(() => WindowAppId(window.Handle));
		if (text != null && text.Contains('!'))
		{
			ImageSource imageSource = await LoadAsync(text);
			if (imageSource != null)
			{
				return imageSource;
			}
		}
		ImageSource imageSource2 = ((!window.Target.Contains('!')) ? null : (await LoadAsync(window.Target)));
		ImageSource imageSource3 = imageSource2;
		if (imageSource3 != null)
		{
			return imageSource3;
		}
		if (WindowCatalog.Minimized(window.Handle))
		{
			ImageSource imageSource4 = await LoadAsync(window.Target);
			if (imageSource4 != null)
			{
				return imageSource4;
			}
			foreach (string item in WindowCatalog.TargetsForWindow(window.Handle))
			{
				imageSource4 = await LoadAsync(item);
				if (imageSource4 != null)
				{
					return imageSource4;
				}
			}
		}
		byte[] array = await Task.Run(delegate
		{
			try
			{
				nint result = 0;
				nuint[] array2 = new nuint[3]
				{
					1u,
					default(nuint),
					2u
				};
				foreach (nuint w in array2)
				{
					SendMessageTimeout(window.Handle, 127u, w, 0, 2u, 100u, out result);
					if (result != 0)
					{
						break;
					}
				}
				if (result == 0)
				{
					result = GetClassIcon(window.Handle, -14);
				}
				if (result == 0)
				{
					result = GetClassIcon(window.Handle, -34);
				}
				if (result != 0)
				{
					using Icon icon = (Icon)Icon.FromHandle(result).Clone();
					using Bitmap bitmap = icon.ToBitmap();
					using MemoryStream memoryStream = new MemoryStream();
					bitmap.Save(memoryStream, ImageFormat.Png);
					return memoryStream.ToArray();
				}
				return (byte[])null;
			}
			catch
			{
				return (byte[])null;
			}
		});
		if (array != null)
		{
			using (MemoryStream memory = new MemoryStream(array))
			{
				using IRandomAccessStream stream = memory.AsRandomAccessStream();
				BitmapImage image = new BitmapImage();
				await image.SetSourceAsync(stream);
				return image;
			}
		}
		ImageSource imageSource5 = await LoadAsync(window.Target);
		if (imageSource5 != null)
		{
			return imageSource5;
		}
		string text2;
		switch (Path.GetFileName(window.Target).ToLowerInvariant())
		{
		case "explorer.exe":
			text2 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
			break;
		case "systemsettings.exe":
			text2 = "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel";
			break;
		case "calculatorapp.exe":
		case "calculator.exe":
			text2 = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";
			break;
		default:
			text2 = "";
			break;
		}
		string text3 = text2;
		return (text3.Length != 0) ? (await LoadAsync(text3)) : null;
	}

	public static Task<ImageSource?> LoadAsync(string target)
	{
		if (cache.Count >= 160 && !cache.ContainsKey(target))
		{
			cache.Clear();
		}
		if (!cache.TryGetValue(target, out Task<ImageSource> value))
		{
			value = (cache[target] = LoadCore(target));
		}
		return value;
	}

	private static async Task<ImageSource?> LoadCore(string target)
	{
		try
		{
			byte[] array = await Task.Run(() => Extract(target));
			if (array == null)
			{
				return null;
			}
			using MemoryStream memory = new MemoryStream(array);
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

	private static byte[]? Extract(string target)
	{
		IShellItemImageFactory factory = null;
		nint bitmap = 0;
		try
		{
			if (target.Contains('!') && !target.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !File.Exists(target))
			{
				target = "shell:AppsFolder\\" + target;
			}
			Guid iid = typeof(IShellItemImageFactory).GUID;
			Marshal.ThrowExceptionForHR(SHCreateItemFromParsingName(target, 0, ref iid, out factory));
			Marshal.ThrowExceptionForHR(factory.GetImage(new Size
			{
				Width = 64,
				Height = 64
			}, 256u, out bitmap));
			GetObject(bitmap, Marshal.SizeOf<NativeBitmap>(), out var bitmap2);
			using Bitmap bitmap3 = new Bitmap(bitmap2.Width, Math.Abs(bitmap2.Height), PixelFormat.Format32bppArgb);
			BitmapData bitmapData = bitmap3.LockBits(new Rectangle(0, 0, bitmap3.Width, bitmap3.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
			nint dC = Win32.GetDC(0);
			try
			{
				BitmapInfo info = new BitmapInfo
				{
					Size = 40u,
					Width = bitmap3.Width,
					Height = -bitmap3.Height,
					Planes = 1,
					BitCount = 32
				};
				if (GetDIBits(dC, bitmap, 0u, (uint)bitmap3.Height, bitmapData.Scan0, ref info, 0u) == 0)
				{
					return null;
				}
			}
			finally
			{
				Win32.ReleaseDC(0, dC);
				bitmap3.UnlockBits(bitmapData);
			}
			using MemoryStream memoryStream = new MemoryStream();
			bitmap3.Save(memoryStream, ImageFormat.Png);
			return memoryStream.ToArray();
		}
		catch
		{
			return null;
		}
		finally
		{
			if (bitmap != 0)
			{
				DeleteObject(bitmap);
			}
			if (factory != null)
			{
				Marshal.ReleaseComObject(factory);
			}
		}
	}
}
