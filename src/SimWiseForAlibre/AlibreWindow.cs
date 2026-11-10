using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SimWiseForAlibre
{
    // The owner for the add-on's message boxes and dialogs, so they're modal to Alibre:
    // Alibre's top-level window (the add-on runs inside Alibre's process).
    static class AlibreWindow
    {
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        const uint GA_ROOT = 2;

        public static IWin32Window Owner(IntPtr fromAddOnLoad)
        {
            IntPtr handle = IntPtr.Zero;
            try { handle = Process.GetCurrentProcess().MainWindowHandle; } catch (Exception) { }
            if (handle == IntPtr.Zero && fromAddOnLoad != IntPtr.Zero && IsWindow(fromAddOnLoad))
                handle = GetAncestor(fromAddOnLoad, GA_ROOT);
            return new Handle(handle);
        }

        sealed class Handle : IWin32Window
        {
            public Handle(IntPtr handle) { this.handle = handle; }
            readonly IntPtr handle;
            IntPtr IWin32Window.Handle => handle;
        }
    }
}
