using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Tools;

namespace Web2BMP
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {

            string url;
            FileInfo cssfile;
            int width, height;
            string filename;

            if (args.Length == 5)
            {
                filename = args[0];
                url = args[1];
                cssfile = new FileInfo(args[2]);

                width = int.Parse(args[3]);
                height = int.Parse(args[4]);
            }
            else
            {
                filename = "out.bmp";
                url = "https://en.wikipedia.org/wiki/Main_Page";
                cssfile = new FileInfo("style.css");
                width = 800;
                height = 600;
            }

            WebsiteToBitmap websiteToBitmap = new WebsiteToBitmap(url, cssfile, width, height);
            using (Bitmap bmp = websiteToBitmap.Generate())
            {
                bmp.Save(filename);
            }
        }

    }
}
