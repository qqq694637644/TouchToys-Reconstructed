using System.Runtime.InteropServices;
using ABI.Microsoft.UI.Xaml.Markup;

namespace WinRT.TouchToysVtableClasses;

internal sealed class TouchAssistant_TouchToys_XamlTypeInfo_XamlMemberWinRTTypeDetails : IWinRTExposedTypeDetails
{
	public ComWrappers.ComInterfaceEntry[] GetExposedInterfaces()
	{
		return new ComWrappers.ComInterfaceEntry[1]
		{
			new ComWrappers.ComInterfaceEntry
			{
				IID = IXamlMemberMethods.IID,
				Vtable = IXamlMemberMethods.AbiToProjectionVftablePtr
			}
		};
	}
}
