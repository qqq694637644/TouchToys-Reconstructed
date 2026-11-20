using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;

namespace TouchAssistant.UI;

internal sealed class DocumentView : ListView
{
	private sealed record Paragraph(string Text, double Size, Thickness Spacing);

	public DocumentView(string text)
	{
		base.SelectionMode = ListViewSelectionMode.None;
		base.IsItemClickEnabled = false;
		base.Padding = new Thickness(20.0, 0.0, 20.0, 28.0);
		base.ItemTemplate = (DataTemplate)XamlReader.Load("<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">\r\n    <TextBlock Text=\"{Binding Text}\" FontSize=\"{Binding Size}\" Margin=\"{Binding Spacing}\"\r\n        TextWrapping=\"Wrap\" IsTextSelectionEnabled=\"True\" />\r\n</DataTemplate>");
		base.ItemContainerStyle = new Style(typeof(ListViewItem))
		{
			Setters = 
			{
				(SetterBase)new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch),
				(SetterBase)new Setter(Control.PaddingProperty, new Thickness(0.0))
			}
		};
		List<Paragraph> list = new List<Paragraph>();
		bool flag = false;
		string[] array = text.Replace("\r", "").Split('\n');
		for (int i = 0; i < array.Length; i++)
		{
			string text2 = array[i].Trim();
			if (text2.StartsWith("```", StringComparison.Ordinal))
			{
				flag = !flag;
			}
			else
			{
				if (text2.Length == 0 || text2 == "---")
				{
					continue;
				}
				int j = 0;
				if (!flag)
				{
					for (; j < text2.Length && text2[j] == '#'; j++)
					{
					}
					if (j > 0 && j < text2.Length && text2[j] == ' ')
					{
						string text3 = text2;
						int num = j + 1;
						text2 = text3.Substring(num, text3.Length - num);
					}
					else
					{
						j = 0;
					}
				}
				if (!flag)
				{
					text2 = Regex.Replace(text2, "\\[([^\\]]+)\\]\\(([^)]+)\\)", "$1（$2）");
					text2 = text2.Replace("**", "").Replace("`", "");
					if (text2.StartsWith("- ") || text2.StartsWith("* "))
					{
						string text3 = text2;
						text2 = "•  " + text3.Substring(2, text3.Length - 2);
					}
				}
				list.Add(new Paragraph(text2, (j == 1) ? 28 : ((j == 2) ? 22 : ((j > 2) ? 18 : 15)), new Thickness(0.0, (j > 0) ? 20 : 3, 0.0, (j > 0) ? 8 : 7)));
			}
		}
		base.ItemsSource = list;
	}
}
