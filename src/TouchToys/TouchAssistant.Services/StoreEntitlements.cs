using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;

namespace TouchAssistant.Services;

internal static class StoreEntitlements
{
	internal const string OfferToken = "paid";

	internal const string StoreId = "9MTNG9XHK2KC";

	internal static bool AppBindingsAvailable => true;

	internal static string Status => "本地测试版：应用绑定已解锁";

	internal static event Action? Changed
	{
		add
		{
		}
		remove
		{
		}
	}

	internal static Task RefreshAsync(Window owner)
	{
		return Task.CompletedTask;
	}

	internal static Task<bool> UnlockAsync(Window owner)
	{
		return Task.FromResult(result: true);
	}
}
