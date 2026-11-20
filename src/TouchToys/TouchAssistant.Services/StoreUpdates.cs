using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using TouchAssistant.UI;
using WinRT.Interop;
using Windows.ApplicationModel;
using Windows.Services.Store;

namespace TouchAssistant.Services;

internal static class StoreUpdates
{
	internal static bool Available
	{
		get
		{
			try
			{
				return Package.Current.SignatureKind == PackageSignatureKind.Store;
			}
			catch
			{
				return false;
			}
		}
	}

	internal static async Task<string> CheckAsync(Window owner)
	{
		_ = 1;
		try
		{
			StoreContext context = StoreContext.GetDefault();
			InitializeWithWindow.Initialize(context, Ui.Handle(owner));
			IReadOnlyList<StorePackageUpdate> readOnlyList = await context.GetAppAndOptionalStorePackageUpdatesAsync();
			if (readOnlyList.Count == 0)
			{
				return "已是最新版本";
			}
			return ((await context.RequestDownloadAndInstallStorePackageUpdatesAsync(readOnlyList)).OverallState == StorePackageUpdateState.Completed) ? "更新已安装" : "更新尚未完成，可稍后重试";
		}
		catch (Exception error)
		{
			Diagnostics.Error(error, "Store.Update");
			return "无法检查更新，请稍后重试";
		}
	}
}
