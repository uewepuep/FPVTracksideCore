using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using mshtml;

namespace Web2BMP
{
    public class WebsiteToBitmap
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public string URL { get; set; }

        public FileInfo CSSFile { get; private set; }

        public const int ScrollbarWidth = 17;

        public bool CropScrollbars { get; set; }

        private bool hasRunMysteryFix;

        public WebsiteToBitmap(string url, FileInfo cssFile, int width, int height)
        {
            CSSFile = cssFile;
            Width = width;
            Height = height;

            URL = url;
            CropScrollbars = true;
        }


        public void ChangeSize(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public Bitmap Generate()
        {
            Bitmap bitmap = new Bitmap(Width, Height);
            using (WebBrowser browser = new WebBrowser())
            {
                browser.ScriptErrorsSuppressed = true;

                if (!hasRunMysteryFix)
                {
                    hasRunMysteryFix = true;
                    MysteriousIEFix(browser);
                }

                int width = Width;
                int height = Height;
                if (CropScrollbars)
                {
                    width += ScrollbarWidth;
                    height += ScrollbarWidth;
                }

                bool done = false;

                browser.ClientSize = new System.Drawing.Size(width, height);
                browser.DocumentCompleted += (object sender, WebBrowserDocumentCompletedEventArgs e) =>
                {
                    UpdateBitmap(browser, bitmap);
                    done = true;
                };

                // Reload the content..
                browser.Navigate(URL);
                while (browser.ReadyState != WebBrowserReadyState.Complete && !done)
                {
                    Thread.Sleep(100);
                    Application.DoEvents();
                }

                return bitmap;
            }
        }

        private static void MysteriousIEFix(WebBrowser browser)
        {
            int BrowserVer, RegVal;

            // get the installed IE version
            BrowserVer = browser.Version.Major;

            // set the appropriate IE version
            if (BrowserVer >= 11)
                RegVal = 11001;
            else if (BrowserVer == 10)
                RegVal = 10001;
            else if (BrowserVer == 9)
                RegVal = 9999;
            else if (BrowserVer == 8)
                RegVal = 8888;
            else
                RegVal = 7000;

            // set the actual key
            using (RegistryKey Key = Registry.CurrentUser.CreateSubKey(@"SOFTWARE\Microsoft\Internet Explorer\Main\FeatureControl\FEATURE_BROWSER_EMULATION", RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (Key.GetValue(System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".exe") == null)
                    Key.SetValue(System.Diagnostics.Process.GetCurrentProcess().ProcessName + ".exe", RegVal, RegistryValueKind.DWord);
            }

        }

        private void UpdateBitmap(WebBrowser browser, Bitmap bitmap)
        {
            browser.ScrollBarsEnabled = false;
            CSSFile.Refresh();
            if (CSSFile != null && CSSFile.Exists)
            {
                IHTMLDocument2 currentDocument = (IHTMLDocument2)browser.Document.DomDocument;

                int length = currentDocument.styleSheets.length;
                IHTMLStyleSheet styleSheet = currentDocument.createStyleSheet(@"", length + 1);
                styleSheet.cssText = File.ReadAllText(CSSFile.FullName);
            }

            // Capture 
            browser.DrawToBitmap(bitmap, browser.Bounds);
        }
    }
}
