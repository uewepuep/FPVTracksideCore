using Composition;
using Composition.Input;
using Composition.Layers;
using Spreadsheets;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace UI
{
    public static class FileTools
    {
        public static bool ExportCSV(PlatformTools platformTools, string title, string[][] table, PopupLayer popupLayer)
        {
            try
            {
                string csv = string.Join("\n", table.Select(line => string.Join(",", line)));

                string filename = platformTools.SaveFileDialog(title, "CSV|*.csv");
                if (filename != null)
                {
                    File.WriteAllText(filename, csv);
                    return true;
                }
            }
            catch (Exception e)
            {
                popupLayer.PopupMessage(e.Message);
                Tools.Logger.UI.LogException(null, e);
            }
            return false;
        }

        public static bool ExportXLSX(PlatformTools platformTools, string title, string[][] table, PopupLayer popupLayer)
        {
            try
            {
                string filename = platformTools.SaveFileDialog(title, "XLSX|*.xlsx");
                if (filename != null)
                {
                    OpenSheet openSheet = new OpenSheet();
                    openSheet.Open(new FileInfo(filename), "Sheet1");
                    openSheet.SetValues(table);
                    openSheet.Save();
                    return true;
                }
            }
            catch (Exception e)
            {
                popupLayer.PopupMessage(e.Message);
                Tools.Logger.UI.LogException(null, e);
            }
            return false;
        }

        public static void ExportMenu(MouseMenu menu, string name, PlatformTools platformTools, string title, string[][] table, PopupLayer popupLayer)
        {
            MouseMenu subMenu = menu.AddSubmenu(name);
            subMenu.AddItem(".xlsx", () => { ExportXLSX(platformTools, title, table, popupLayer); });
            subMenu.AddItem(".csv", () => { ExportCSV(platformTools, title, table, popupLayer); });
        }
    }
}
