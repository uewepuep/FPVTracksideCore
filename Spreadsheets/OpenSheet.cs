using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OfficeOpenXml;
using Tools;

namespace Spreadsheets
{
    public class OpenSheet : ISheet
    {

        private ExcelPackage package;
        private ExcelWorksheet sheet;

        public OpenSheet()
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
        }

        public void Dispose()
        {
            sheet?.Dispose();
            package?.Dispose();
            package = null;
            sheet = null;
        }

        public bool Open(FileInfo excelFile, string sheetname)
        {
            try
            {
                package = new ExcelPackage(excelFile);
                package.Workbook.CalcMode = ExcelCalcMode.Automatic;
                sheet = package.Workbook.Worksheets.FirstOrDefault(s => s.Name.ToLower() == sheetname.ToLower());

                if (sheet == null)
                {
                    sheet = package.Workbook.Worksheets.Add(sheetname);
                }

                if (sheet == null)
                {
                    Logger.Sheets.Log(this, "Failed opening sheet", null, Logger.LogType.Error);

                    Dispose();
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Logger.Sheets.LogException(this, e);
                return false;
            }
        }

        public string GetText(int r, int c)
        {
            if (sheet != null)
            {
                var cell = sheet.Cells[r, c];
                //try
                //{
                //    cell.Calculate();
                //}
                //catch
                //{
                //    return "";
                //}

                return cell.Text;
            }
            return "";
        }

        public void SetValue(int r, int c, object value)
        {
            try
            {
                if (sheet != null)
                {
                    sheet.SetValue(r, c, value);
                }
            }
            catch (Exception e)
            {
                Logger.Sheets.LogException(this, e);
            }
        }

        public void SetFormula(int r, int c, string value)
        {
            try
            {
                if (sheet != null)
                {
                    sheet.Cells[r, c].Formula = value;
                }
            }
            catch (Exception e)
            {
                Logger.Sheets.LogException(this, e);
            }
        }

        public IEnumerable<string> GetRowText(int i)
        {
            if (sheet != null)
            {
                ExcelRange row = sheet.Cells[i, 1, i, 256];
                foreach (ExcelRangeBase cell in row)
                {
                    yield return cell.Text;
                }
            }
        }

        public IEnumerable<string> GetColumnText(int i)
        {
            if (sheet != null)
            {
                ExcelRange row = sheet.Cells[1, i, 256, i];
                foreach (ExcelRangeBase cell in row)
                {
                    yield return cell.Text;
                }
            }
        }

        public IEnumerable<string> GetColumnFormula(int i)
        {
            if (sheet != null)
            {
                ExcelRange row = sheet.Cells[1, i, 256, i];
                foreach (ExcelRangeBase cell in row)
                {
                    yield return cell.Formula;
                }
            }
        }

        public bool Calculate()
        {
            try
            {
                if (sheet != null)
                {
                    Logger.Sheets.Log(this, "Calculate");
                    sheet.Calculate();
                    return true;
                }
            }
            catch (Exception e)
            {
                Logger.Sheets.LogException(this, e);
            }
            return false;
        }

        public bool Save()
        {
            try
            {
                if (package != null)
                {
                    package.Save();
                    return true;
                }
                return false;
            }
            catch (Exception e)
            {
                Logger.Sheets.LogException(this, e);
                return false;
            }
        }
    }
}
