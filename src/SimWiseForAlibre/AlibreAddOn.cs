using System;
using AlibreAddOn;
using AlibreX;

// Alibre looks for this exact namespace and class in a managed add-on.
namespace AlibreAddOnAssembly
{
    public static class AlibreAddOn
    {
        static SimWiseForAlibre.AddOnMenu s_menu;

        public static void AddOnLoad(IntPtr hwnd, IAutomationHook pAutomationHook, IntPtr unused)
        {
            SimWiseForAlibre.Log.Write("load", "AddOnLoad");
            try
            {
                s_menu = new SimWiseForAlibre.AddOnMenu((IADRoot)pAutomationHook.Root, hwnd);
            }
            catch (Exception ex)
            {
                SimWiseForAlibre.Log.Write("load", "failed: " + ex);
            }
        }

        public static void AddOnInvoke(IntPtr hwnd, IntPtr pAutomationHook, string sessionName,
                                       bool isLicensed, int reserved1, int reserved2)
        {
        }

        public static void AddOnUnload(IntPtr hwnd, bool forceUnload, ref bool cancel, int reserved1, int reserved2)
        {
            SimWiseForAlibre.Log.Write("load", "AddOnUnload");
        }

        public static IAlibreAddOn GetAddOnInterface()
        {
            return s_menu;
        }
    }
}
