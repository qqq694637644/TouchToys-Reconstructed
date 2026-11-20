using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using TouchAssistant.Models;

namespace TouchAssistant.Services;

internal static class InstalledApps
{
	private static Dictionary<string, string> aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private static Dictionary<string, string> names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

	private static readonly Lazy<Task<List<LauncherEntry>>> initialCatalogue = new Lazy<Task<List<LauncherEntry>>>(LoadAsync);

	private static readonly object catalogueGate = new object();

	private static Task<List<LauncherEntry>>? catalogue;

	private static long catalogueStarted;

	internal static string? DisplayName(string target)
	{
		return Volatile.Read(in names).GetValueOrDefault(WindowCatalog.NormalizeTarget(target));
	}

	internal static Task<List<LauncherEntry>> EnsureAliasesAsync()
	{
		return initialCatalogue.Value;
	}

	internal static string ResolveMatchTarget(string target)
	{
		return Volatile.Read(in aliases).GetValueOrDefault(WindowCatalog.NormalizeTarget(target), target);
	}

	public static async Task<List<LauncherEntry>> LoadAsync()
	{
		Task<List<LauncherEntry>> task;
		lock (catalogueGate)
		{
			if (catalogue == null || catalogue.IsFaulted || catalogue.IsCanceled || (catalogue.IsCompleted && Environment.TickCount64 - catalogueStarted >= 60000))
			{
				catalogue = LoadCoreAsync();
				catalogueStarted = Environment.TickCount64;
			}
			task = catalogue;
		}
		return (await task).Select((LauncherEntry app) => new LauncherEntry
		{
			Name = app.Name,
			Target = app.Target,
			MatchTarget = app.MatchTarget,
			IconGlyph = app.IconGlyph,
			IconPath = app.IconPath,
			Keys = new List<int>(app.Keys)
		}).ToList();
	}

	private static Task<List<LauncherEntry>> LoadCoreAsync()
	{
		TaskCompletionSource<List<LauncherEntry>> result = new TaskCompletionSource<List<LauncherEntry>>(TaskCreationOptions.RunContinuationsAsynchronously);
		Thread thread = new Thread((ThreadStart)delegate
		{
			dynamic val = null;
			dynamic val2 = null;
			dynamic val3 = null;
			try
			{
				val = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
				val2 = val.NameSpace("shell:AppsFolder");
				val3 = val2.Items();
				List<LauncherEntry> list = new List<LauncherEntry>();
				int num = val3.Count;
				for (int i = 0; i < num; i++)
				{
					dynamic val4 = null;
					try
					{
						val4 = val3.Item(i);
						string name = val4.Name;
						string text = Convert.ToString(val4.ExtendedProperty("System.AppUserModel.ID")) ?? "";
						string text2 = val4.Path;
						string text3 = (string.IsNullOrEmpty(text) ? text2 : ("shell:AppsFolder\\" + text));
						string matchTarget = "";
						try
						{
							string text4 = Convert.ToString(val4.ExtendedProperty("System.Link.TargetParsingPath")) ?? "";
							if (text4.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
							{
								matchTarget = text4;
							}
						}
						catch
						{
						}
						if (!string.IsNullOrWhiteSpace(text3))
						{
							list.Add(new LauncherEntry
							{
								Name = name,
								Target = text3,
								MatchTarget = matchTarget
							});
						}
					}
					finally
					{
						if ((object)val4 != null)
						{
							Marshal.ReleaseComObject((object)val4);
						}
					}
				}
				Dictionary<string, string> dictionary = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (LauncherEntry item in list)
				{
					if (item.MatchTarget.Length > 0)
					{
						dictionary[WindowCatalog.NormalizeTarget(item.Target)] = item.MatchTarget;
					}
				}
				Volatile.Write(ref aliases, dictionary);
				Dictionary<string, string> dictionary2 = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (LauncherEntry item2 in list)
				{
					dictionary2[WindowCatalog.NormalizeTarget(item2.Target)] = item2.Name;
					if (item2.MatchTarget.Length > 0)
					{
						dictionary2[WindowCatalog.NormalizeTarget(item2.MatchTarget)] = item2.Name;
					}
				}
				Volatile.Write(ref names, dictionary2);
				result.SetResult(list.DistinctBy<LauncherEntry, string>((LauncherEntry x) => x.Target, StringComparer.OrdinalIgnoreCase).OrderBy<LauncherEntry, string>((LauncherEntry x) => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
			}
			catch (Exception exception)
			{
				result.SetException(exception);
			}
			finally
			{
				if ((object)val3 != null)
				{
					Marshal.ReleaseComObject((object)val3);
				}
				if ((object)val2 != null)
				{
					Marshal.ReleaseComObject((object)val2);
				}
				if ((object)val != null)
				{
					Marshal.ReleaseComObject((object)val);
				}
			}
		});
		thread.IsBackground = true;
		thread.Name = "Installed application catalogue";
		thread.SetApartmentState(ApartmentState.STA);
		thread.Start();
		return result.Task;
	}
}
