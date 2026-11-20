using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TouchAssistant.Models;
using TouchAssistant.Services;

namespace TouchAssistant.UI;

internal static class SectorIconPicker
{
	public static async Task ShowAsync(Window owner, XamlRoot root, GestureAction action, SettingsStore store)
	{
		StackPanel stackPanel = new StackPanel
		{
			Spacing = 12.0
		};
		FontIcon preview = new FontIcon
		{
			Glyph = action.Icon,
			FontFamily = IconFontResolver.CreateXamlFontFamily(action.Icon, action.IconFont),
			FontSize = 32.0
		};
		ComboBox font = new ComboBox
		{
			Header = Localization.T("系统图标字体"),
			ItemsSource = IconFontResolver.GetAvailableIconFontFamilies(action.IconFont),
			SelectedItem = IconFontResolver.ResolveFontFamily(action.Icon, action.IconFont),
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		TextBox code = new TextBox
		{
			Header = Localization.T("Unicode 编码（十六进制）"),
			PlaceholderText = Localization.T("例如 E90C"),
			Text = ((action.Icon.Length > 0) ? ((int)action.Icon[0]).ToString("X4") : "")
		};
		TextBlock error = Ui.Text("点击下面的图标立即保存，也可输入字符编码。", 12.0);
		GridView grid = new GridView
		{
			SelectionMode = ListViewSelectionMode.Single,
			IsItemClickEnabled = true,
			Height = 220.0
		};
		ComboBox page = new ComboBox
		{
			Header = Localization.T("字符范围"),
			ItemsSource = (from i in Enumerable.Range(0, 25)
				select $"{57344 + i * 256:X4}–{57599 + i * 256:X4}").ToArray(),
			SelectedIndex = 9,
			HorizontalAlignment = HorizontalAlignment.Stretch
		};
		code.TextChanged += delegate
		{
			SaveGlyph();
		};
		font.SelectionChanged += delegate
		{
			SaveGlyph();
			Populate();
		};
		page.SelectionChanged += delegate
		{
			Populate();
		};
		grid.ItemClick += delegate(object _, ItemClickEventArgs e)
		{
			if (e.ClickedItem is FontIcon { Tag: var tag } && tag is int num)
			{
				code.Text = num.ToString("X4");
			}
		};
		stackPanel.Children.Add(preview);
		stackPanel.Children.Add(font);
		stackPanel.Children.Add(code);
		stackPanel.Children.Add(page);
		stackPanel.Children.Add(grid);
		stackPanel.Children.Add(error);
		Populate();
		bool upload = false;
		ContentDialog contentDialog = new ContentDialog();
		contentDialog.XamlRoot = root;
		contentDialog.Title = Localization.T("扇区图标");
		contentDialog.Content = stackPanel;
		contentDialog.PrimaryButtonText = Localization.T("上传图片…");
		contentDialog.SecondaryButtonText = Localization.T("清除图标");
		contentDialog.CloseButtonText = Localization.T("完成");
		contentDialog.PrimaryButtonClick += delegate
		{
			upload = true;
		};
		contentDialog.SecondaryButtonClick += delegate
		{
			GestureAction gestureAction = action;
			string icon = (action.IconPath = "");
			gestureAction.Icon = icon;
			store.Save();
		};
		await contentDialog.ShowAsync();
		if (!upload)
		{
			return;
		}
		try
		{
			string file = Ui.BrowseImage(owner);
			if (file != null)
			{
				string iconPath = await Task.Run(() => Import(file));
				action.IconPath = iconPath;
				action.Icon = "";
				store.Save();
			}
		}
		catch (Exception ex)
		{
			Diagnostics.Error(ex, "Icons.Import");
			App.CurrentApp.Report("无法导入图标：" + (string.IsNullOrWhiteSpace(ex.Message) ? $"错误 0x{ex.HResult:X8}" : ex.Message));
		}
		void Populate()
		{
			string familyName = (font.SelectedItem as string) ?? IconFontResolver.PreferredFontFamilyName;
			grid.Items.Clear();
			for (int i = 0; i < 256; i++)
			{
				int num = 57344 + Math.Max(0, page.SelectedIndex) * 256 + i;
				FontIcon fontIcon = new FontIcon
				{
					Glyph = char.ConvertFromUtf32(num),
					FontFamily = IconFontResolver.CreateXamlFontFamily(char.ConvertFromUtf32(num), familyName),
					FontSize = 24.0,
					Width = 40.0,
					Height = 40.0,
					Tag = num
				};
				ToolTipService.SetToolTip(fontIcon, $"U+{num:X4}");
				grid.Items.Add(fontIcon);
			}
		}
		void SaveGlyph()
		{
			string text = code.Text.Trim().Replace("U+", "", StringComparison.OrdinalIgnoreCase);
			int result;
			if (text.Length == 0)
			{
				action.Icon = "";
				action.IconPath = "";
				preview.Glyph = "";
				store.Save();
			}
			else if (!int.TryParse(text, NumberStyles.HexNumber, null, out result) || result < 32 || result > 65535 || (result >= 55296 && result <= 57343))
			{
				error.Text = Localization.T("请输入有效的字符编码。");
			}
			else
			{
				action.Icon = ((char)result).ToString();
				action.IconPath = "";
				action.IconFont = IconFontResolver.NormalizeIconFont(font.SelectedItem as string, action.Icon);
				preview.Glyph = action.Icon;
				preview.FontFamily = IconFontResolver.CreateXamlFontFamily(action.Icon, action.IconFont);
				error.Text = Localization.T("已保存 · 空白方框表示此字体不包含该字符。");
				store.Save();
			}
		}
	}

	internal static string Import(string path, string? targetDirectory = null)
	{
		if (new FileInfo(path).Length > 16777216)
		{
			throw new InvalidOperationException("请选择小于 16 MB 的图片。");
		}
		using System.Drawing.Image image = System.Drawing.Image.FromFile(path);
		if ((long)image.Width * (long)image.Height > 16000000)
		{
			throw new InvalidOperationException("图片尺寸过大，请选择图标文件。");
		}
		double num = Math.Min(1.0, 128.0 / (double)Math.Max(image.Width, image.Height));
		using Bitmap bitmap = new Bitmap(Math.Max(1, (int)((double)image.Width * num)), Math.Max(1, (int)((double)image.Height * num)), PixelFormat.Format32bppPArgb);
		using (Graphics graphics = Graphics.FromImage(bitmap))
		{
			graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
			graphics.DrawImage(image, 0, 0, bitmap.Width, bitmap.Height);
		}
		string text = targetDirectory ?? Path.Combine(SettingsStore.DirectoryPath, "Icons");
		Directory.CreateDirectory(text);
		using MemoryStream memoryStream = new MemoryStream();
		bitmap.Save(memoryStream, ImageFormat.Png);
		string path2 = Convert.ToHexString(SHA256.HashData(memoryStream.ToArray())) + ".png";
		string text2 = Path.Combine(text, path2);
		if (!File.Exists(text2))
		{
			File.WriteAllBytes(text2, memoryStream.ToArray());
		}
		return text2;
	}
}
