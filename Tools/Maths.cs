using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Tools
{
    public enum RectangleAlignment
    {
        TopLeft, TopCenter, TopRight,
        CenterLeft, Center, CenterRight,
        BottomLeft, BottomCenter, BottomRight
    }

    public enum FitType
    {
        FitBoth,
        FitHorizontal,
        FitVertical
    }

    public static class Maths
    {
        public static int CountLines(string str)
        {
            if (str == null)
                throw new ArgumentNullException("str");
            if (str == string.Empty)
                return 1;
            int index = -1;
            int count = 0;
            while (-1 != (index = str.IndexOf('\n', index + 1)))
                count++;

            return count + 1;
        }

        public static int NextPowerOfTwo(int size)
        {
            int nextSizeUp = 2;
            while (nextSizeUp < size) nextSizeUp *= 2;
            return nextSizeUp;
        }

        public static Rectangle Center(Rectangle needle, Rectangle haystack)
        {
            Point c = haystack.Center;
            return new Rectangle(c.X - (needle.Width / 2), c.Y - (needle.Height / 2), needle.Width, needle.Height);
        }

        public static Rectangle FitBoxMaintainAspectRatio(Rectangle outer, float aspectRatio, RectangleAlignment alignment, FitType fitType)
        {
            Rectangle innerBounds = new Rectangle(0, 0, (int)(aspectRatio * 1000000), 1000000);
            return FitBoxMaintainAspectRatio(outer, innerBounds, alignment, fitType);
        }

        public static Rectangle FitBoxMaintainAspectRatio(Rectangle outer, Rectangle inner, RectangleAlignment alignment, FitType fitType)
        {
            float factor = ScaleFactor(outer, inner, fitType);
            return FitBoxMaintainAspectRatio(outer, inner, factor, alignment);
        }

        public static Rectangle FitBoxMaintainAspectRatio(Rectangle outer, Rectangle inner, float factor, RectangleAlignment alignment)
        {
            Rectangle output = new Rectangle(0, 0, (int)(inner.Width * factor), (int)(inner.Height * factor));

            switch (alignment)
            {
                case RectangleAlignment.TopLeft:
                    output.X = outer.X;
                    output.Y = outer.Y;
                    break;

                case RectangleAlignment.TopCenter:
                    output.X = outer.X + ((outer.Width - output.Width) / 2);
                    output.Y = outer.Y;
                    break;

                case RectangleAlignment.TopRight:
                    output.X = outer.Right - output.Width;
                    output.Y = outer.Y;
                    break;

                case RectangleAlignment.CenterLeft:
                    output.X = outer.X;
                    output.Y = outer.Y + ((outer.Height - output.Height) / 2);
                    break;

                case RectangleAlignment.Center:
                    output.X = outer.X + ((outer.Width - output.Width) / 2);
                    output.Y = outer.Y + ((outer.Height - output.Height) / 2);
                    break;

                case RectangleAlignment.CenterRight:
                    output.X = outer.Right - output.Width;
                    output.Y = outer.Y + ((outer.Height - output.Height) / 2);
                    break;



                case RectangleAlignment.BottomLeft:
                    output.X = outer.X;
                    output.Y = outer.Bottom - output.Height;
                    break;

                case RectangleAlignment.BottomCenter:
                    output.X = outer.X + ((outer.Width - output.Width) / 2);
                    output.Y = outer.Bottom - output.Height;
                    break;

                case RectangleAlignment.BottomRight:
                    output.X = outer.Right - output.Width;
                    output.Y = outer.Bottom - output.Height;
                    break;

                default:
                    throw new NotImplementedException();
            }

            return output;
        }

        public static float ScaleFactor(Rectangle outer, Rectangle inner, FitType fitType)
        {
            float hfactor = (float)outer.Height / (float)inner.Height;
            float wfactor = (float)outer.Width / (float)inner.Width;

            switch (fitType)
            {
                case FitType.FitHorizontal:
                    return wfactor;
                case FitType.FitVertical:
                    return hfactor;

                case FitType.FitBoth:
                default:
                    return Math.Min(hfactor, wfactor);
            }
        }

        public static string CamelCaseToHuman(this string input)
        {
            string output = input.Replace("_", " ");

            output = Regex.Replace(output, @"([a-z])([A-Z0-9]+)", "$1 $2").Trim();
            //output = Regex.Replace(output, @"([^ ])([A-Z0-9]*)([A-Z0-9])([a-z])", "$1$2 $3$4").Trim();

            return output;
        }

        public static string AutoAcronym(this string input)
        {
            string output = input.Replace("_", " ");

            output = Regex.Replace(output, @"([^A-Z0-9]+)", "").Trim();

            return output;
        }

        public static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            int[,] d = new int[n + 1, m + 1];

            // Step 1
            if (n == 0)
            {
                return m;
            }

            if (m == 0)
            {
                return n;
            }

            // Step 2
            for (int i = 0; i <= n; d[i, 0] = i++)
            {
            }

            for (int j = 0; j <= m; d[0, j] = j++)
            {
            }

            // Step 3
            for (int i = 1; i <= n; i++)
            {
                //Step 4
                for (int j = 1; j <= m; j++)
                {
                    // Step 5
                    int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;

                    // Step 6
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }
            // Step 7
            return d[n, m];
        }

        public static void GenerateRandomNumbers()
        {
            Random r = new Random();

            List<int> list = new List<int>();

            for (int i = 0; i < 10; i++)
            {
                list.Add(r.Next(0, 10));
            }
            Console.WriteLine(string.Join(", ", list.Select(l => l.ToString())));
        }

        public static string MakeCSVLine(params string[] parameters)
        {
            return string.Join(",", parameters.Select(l => l.ReplaceCommas())) + "\n";
        }

        public static Rectangle Flip(this Rectangle src, int sourceHeight)
        {
            src.Y = sourceHeight - src.Y;
            src.Height = -src.Height;
            return src;
        }

        public static Rectangle Mirror(this Rectangle src, int sourceWidth)
        {
            src.X = sourceWidth - src.X;
            src.Width = -src.Width;
            return src;
        }
    }
}
