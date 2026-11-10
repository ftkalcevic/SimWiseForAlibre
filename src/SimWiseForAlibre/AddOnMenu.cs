using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AlibreAddOn;
using AlibreX;

namespace SimWiseForAlibre
{
    // The add-on's menu: SimWise > Export to SimWise..., in the Part, Sheet Metal and Assembly workspaces.
    sealed class AddOnMenu : IAlibreAddOn
    {
        const int kRoot = 1;
        const int kExport = 2;
        const int kLog = 3;

        readonly IADRoot m_root;
        readonly IntPtr m_alibreWindow;

        public AddOnMenu(IADRoot root, IntPtr alibreWindow)
        {
            m_root = root;
            m_alibreWindow = alibreWindow;
        }

        public int RootMenuItem => kRoot;
        public bool HasSubMenus(int menuID) => menuID == kRoot;
        public Array SubMenuItems(int menuID) => menuID == kRoot ? new[] { kExport, kLog } : new int[0];

        public string MenuItemText(int menuID)
        {
            switch (menuID)
            {
                case kRoot: return "SimWise";
                case kExport: return "Export to SimWise...";
                case kLog: return "Show Log";
                default: return "";
            }
        }

        public bool PopupMenu(int menuID) => false;
        public ADDONMenuStates MenuItemState(int menuID, string sessionIdentifier) => ADDONMenuStates.ADDON_MENU_ENABLED;

        public string MenuItemToolTip(int menuID)
        {
            switch (menuID)
            {
                case kExport: return "Send this part or assembly to SimWise 4D, with its constraints as joints";
                case kLog: return "Open the SimWiseForAlibre log";
                default: return "";
            }
        }

        public string MenuIcon(int menuID) => null;
        public bool UseDedicatedRibbonTab() => false;

        public IAlibreAddOnCommand InvokeCommand(int menuID, string sessionIdentifier)
        {
            try
            {
                if (menuID == kExport)
                    Export(sessionIdentifier);
                else if (menuID == kLog)
                    System.Diagnostics.Process.Start("notepad.exe", "\"" + Log.FilePath + "\"");
            }
            catch (Exception ex)
            {
                Log.Write("menu", "failed: " + ex);
                MessageBox.Show(AlibreWindow.Owner(m_alibreWindow), "The export failed:\n\n" + ex.Message + "\n\nThe log has the details.",
                                "SimWiseForAlibre", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            return null;
        }

        void Export(string sessionIdentifier)
        {
            IADSession session = FindSession(sessionIdentifier);
            if (session == null)
                throw new InvalidOperationException("Alibre didn't say which design to export.");
            var exporter = new Exporter(session, m_alibreWindow);
            ExportReport report = exporter.Run();
            if (report == null)
                return; // cancelled
            MessageBox.Show(AlibreWindow.Owner(m_alibreWindow), report.Text(), "SimWiseForAlibre",
                            MessageBoxButtons.OK, report.Problems.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        IADSession FindSession(string identifier)
        {
            foreach (IADSession s in m_root.Sessions)
            {
                try
                {
                    if (s.Identifier == identifier)
                        return s;
                }
                catch (COMException) { }
            }
            return m_root.TopmostSession;
        }

        public bool HasPersistentDataToSave(string sessionIdentifier) => false;
        public void LoadData(IStream pCustomData, string sessionIdentifier) { }
        public void SaveData(IStream pCustomData, string sessionIdentifier) { }
        public void setIsAddOnLicensed(bool isLicensed) { }
    }
}
