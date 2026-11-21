using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace TouchAssistant.Services;

/// <summary>
/// Resolves icon and text font families against the fonts actually installed on this Windows
/// installation. The glyph-aware path is shared by WinUI FontIcon and GDI drawing so a glyph
/// never silently depends on the OS-specific default font.
/// </summary>
public static class IconFontResolver
{
	public const string FluentIcons = "Segoe Fluent Icons";
	public const string Mdl2Assets = "Segoe MDL2 Assets";
	public const string SegoeUiSymbol = "Segoe UI Symbol";

	private const uint MarkMissingGlyphs = 1;
	private const uint GdiError = 0xFFFFFFFF;
	private const ushort MissingGlyph = 0xFFFF;

	private static readonly Lazy<HashSet<string>> installedFamilies = new(LoadInstalledFamilies, true);
	private static readonly Lazy<string> preferredFamily = new(SelectPreferredFamily, true);
	private static readonly ConcurrentDictionary<string, bool> glyphCoverage = new(StringComparer.Ordinal);

	[DllImport("gdi32.dll", EntryPoint = "GetGlyphIndicesW", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern uint GetGlyphIndices(IntPtr hdc, string text, int count, [Out] ushort[] glyphs, uint flags);

	[DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
	private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

	[DllImport("gdi32.dll", EntryPoint = "CreateFontW", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern IntPtr CreateFont(int height, int width, int escapement, int orientation, int weight, uint italic, uint underline, uint strikeOut, uint charSet, uint outputPrecision, uint clipPrecision, uint quality, uint pitchAndFamily, string faceName);

	[DllImport("gdi32.dll")]
	private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteObject(IntPtr obj);

	[DllImport("gdi32.dll")]
	private static extern bool DeleteDC(IntPtr hdc);

	public static string PreferredFontFamilyName => preferredFamily.Value;

	public static string DiagnosticSummary
	{
		get
		{
			string installed = string.Join(", ", GetAvailableIconFontFamilies());
			return $"OSBuild={Environment.OSVersion.Version.Build}; Preferred={PreferredFontFamilyName}; Installed=[{installed}]";
		}
	}

	/// <summary>Runs font enumeration during application startup, before any settings or UI are loaded.</summary>
	public static void Initialize()
	{
		_ = installedFamilies.Value;
		_ = preferredFamily.Value;
	}

	public static bool IsFontInstalled(string? familyName)
	{
		return !string.IsNullOrWhiteSpace(familyName) && installedFamilies.Value.Contains(familyName.Trim());
	}

	/// <summary>
	/// Returns installed icon families in preference order. A saved custom family is included
	/// when it is still installed, allowing settings created on another machine to migrate safely.
	/// </summary>
	public static string[] GetAvailableIconFontFamilies(string? includeFamily = null)
	{
		List<string> result = new();
		bool isWindows11OrLater = Environment.OSVersion.Version.Build >= 22000;
		void Add(string? family)
		{
			if (IsFontInstalled(family) && !result.Any(existing => string.Equals(existing, family, StringComparison.OrdinalIgnoreCase)))
			{
				result.Add(family!);
			}
		}

		if (isWindows11OrLater || !string.Equals(includeFamily, FluentIcons, StringComparison.OrdinalIgnoreCase))
		{
			Add(includeFamily);
		}
		Add(PreferredFontFamilyName);
		if (isWindows11OrLater)
		{
			Add(FluentIcons);
		}
		Add(Mdl2Assets);
		Add(SegoeUiSymbol);
		return result.ToArray();
	}

	/// <summary>
	/// Resolves a family for a specific glyph. A saved preference is honored only if the family
	/// exists and contains that glyph; otherwise the OS-preferred family and then the legacy
	/// MDL2 family are tried. On Windows 10 MDL2 is deliberately preferred over Fluent Icons.
	/// </summary>
	public static string ResolveFontFamily(string? glyph, string? preferred = null)
	{
		List<string> candidates = new();
		void Add(string? family)
		{
			if (!string.IsNullOrWhiteSpace(family) && !candidates.Any(existing => string.Equals(existing, family.Trim(), StringComparison.OrdinalIgnoreCase)))
			{
				candidates.Add(family.Trim());
			}
		}

		bool isWindows11OrLater = Environment.OSVersion.Version.Build >= 22000;
		if (isWindows11OrLater || !string.Equals(preferred, FluentIcons, StringComparison.OrdinalIgnoreCase))
		{
			Add(preferred);
		}
		Add(PreferredFontFamilyName);
		Add(FluentIcons);
		Add(Mdl2Assets);
		Add(SegoeUiSymbol);
		Add("Segoe UI");

		foreach (string family in candidates)
		{
			if (IsFontInstalled(family) && (string.IsNullOrEmpty(glyph) || SupportsGlyph(family, glyph)))
			{
				return family;
			}
		}

		return candidates.FirstOrDefault(IsFontInstalled) ?? PreferredFontFamilyName;
	}

	public static string NormalizeIconFont(string? savedFamily, string? glyph)
	{
		return ResolveFontFamily(glyph, savedFamily);
	}

	public static bool SupportsGlyph(string? familyName, string? glyph)
	{
		if (string.IsNullOrWhiteSpace(familyName) || !IsFontInstalled(familyName))
		{
			return false;
		}
		if (string.IsNullOrEmpty(glyph))
		{
			return true;
		}
		string key = familyName.Trim() + "\0" + glyph;
		return glyphCoverage.GetOrAdd(key, _ => CheckGlyphCoverage(familyName.Trim(), glyph));
	}

	public static Microsoft.UI.Xaml.Media.FontFamily CreateXamlFontFamily(string? glyph, string? preferred = null)
	{
		return new Microsoft.UI.Xaml.Media.FontFamily(ResolveFontFamily(glyph, preferred));
	}

	public static FontFamily CreateGdiFontFamily(string? glyph, string? preferred = null)
	{
		return new FontFamily(ResolveFontFamily(glyph, preferred));
	}

	public static Font CreateGdiFont(string? glyph, float size, GraphicsUnit unit = GraphicsUnit.Pixel, string? preferred = null)
	{
		return new Font(ResolveFontFamily(glyph, preferred), size, unit);
	}

	public static string ResolveTextFontFamily(string preferred, params string[] fallbacks)
	{
		if (IsFontInstalled(preferred))
		{
			return preferred;
		}
		foreach (string fallback in fallbacks ?? Array.Empty<string>())
		{
			if (IsFontInstalled(fallback))
			{
				return fallback;
			}
		}
		return IsFontInstalled("Segoe UI") ? "Segoe UI" : (installedFamilies.Value.FirstOrDefault() ?? preferred);
	}

	public static Font CreateTextFont(string preferred, float size, GraphicsUnit unit, params string[] fallbacks)
	{
		return new Font(ResolveTextFontFamily(preferred, fallbacks), size, unit);
	}

	private static string SelectPreferredFamily()
	{
		bool isWindows11OrLater = Environment.OSVersion.Version.Build >= 22000;
		string[] order = isWindows11OrLater
			? new[] { FluentIcons, Mdl2Assets, SegoeUiSymbol, "Segoe UI" }
			: new[] { Mdl2Assets, FluentIcons, SegoeUiSymbol, "Segoe UI" };
		return order.FirstOrDefault(IsFontInstalled) ?? "Segoe UI";
	}

	private static HashSet<string> LoadInstalledFamilies()
	{
		try
		{
			using InstalledFontCollection collection = new();
			return collection.Families.Select(family => family.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
		}
		catch
		{
			return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}
	}

	private static bool CheckGlyphCoverage(string familyName, string glyph)
	{
		try
		{
			IntPtr hdc = CreateCompatibleDC(IntPtr.Zero);
			if (hdc == IntPtr.Zero)
			{
				return false;
			}
			IntPtr hfont = CreateFont(-16, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 4, 0, familyName);
			IntPtr previous = IntPtr.Zero;
			bool selected = false;
			try
			{
				if (hfont == IntPtr.Zero)
				{
					return false;
				}
				previous = SelectObject(hdc, hfont);
				selected = previous != IntPtr.Zero && previous != new IntPtr(-1);
				if (!selected)
				{
					return false;
				}

				foreach (Rune rune in glyph.EnumerateRunes())
				{
					string scalar = rune.ToString();
					ushort[] indices = new ushort[scalar.Length];
					uint count = GetGlyphIndices(hdc, scalar, scalar.Length, indices, MarkMissingGlyphs);
					if (count == GdiError || count != scalar.Length || indices.Any(index => index == MissingGlyph))
					{
						return false;
					}
				}
				return true;
			}
			finally
			{
				if (selected)
				{
					SelectObject(hdc, previous);
				}
				if (hfont != IntPtr.Zero)
				{
					DeleteObject(hfont);
				}
				DeleteDC(hdc);
			}
		}
		catch
		{
			return false;
		}
	}
}
