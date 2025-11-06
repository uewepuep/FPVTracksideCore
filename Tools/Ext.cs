using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Tools
{
    public static class Ext
    {
        public static string ToLogFormat(this DateTime dateTime)
        {
            string format = dateTime.Hour.ToString("00") + ":" + dateTime.Minute.ToString("00") + ":" + dateTime.Second.ToString("00") + "." + dateTime.Millisecond.ToString("000");
            return format;
        }

        public static IEnumerable<Microsoft.Xna.Framework.Color> XNA(this IEnumerable<ToolColor> colors)
        {
            return colors.Select(c => c.XNA);
        }

        public static IEnumerable<Microsoft.Xna.Framework.Color> XNAPreMultiplied(this IEnumerable<ToolColor> colors)
        {
            return colors.Select(c => c.XNAPreMultiplied);
        }

        private static DateTime start = DateTime.Now;
        public static T GetFromCurrentTime<T>(this IEnumerable<T> things, float intervalSeconds)
        {
            int length = things.Count();
            if (length == 0)
            {
                return default(T);
            }

            float intervals = (float)(DateTime.Now - start).TotalSeconds / intervalSeconds;
            int index = ((int)intervals) % length;

            T result = things.GetAtIndex(index);
            if (result.Equals(default(T)))
            {
                result = things.LastOrDefault();
            }
            return result;
        }

        public static T GetAtIndex<T>(this IEnumerable<T> things, int index)
        {
            int i = 0;
            foreach (T t in things)
            {
                if (i == index)
                {
                    return t;
                }
                i++;
            }
            return default(T);
        }


        private static Random random;

        public static T Random<T>(this IEnumerable<T> things)
        {
            if (random == null)
            {
                random = new Random();
            }

            int r = random.Next(0, things.Count());
            int i = 0;
            foreach (T t in things)
            {
                if (i == r)
                {
                    return t;
                }
                i++;
            }
            return things.FirstOrDefault();
        }

        public static IEnumerable<T> Randomise<T>(this IEnumerable<T> things)
        {
            if (random == null)
            {
                random = new Random();
            }

            List<T> list = things.ToList();

            while (list.Count > 0)
            {
                T next = list.Random();
                list.Remove(next);
                yield return next;
            }
        }

        public static Size GetSize(this Texture2D texture)
        {
            return new Size(texture.Width, texture.Height);
        }

        public static IEnumerable<IEnumerable<T>> Split<T>(this IEnumerable<T> list, int parts)
        {
            return list.Select((item, index) => new { index, item })
                         .GroupBy(x => x.index % parts)
                         .Select(x => x.Select(y => y.item));
        }

        public static IEnumerable<Type> GetBaseTypes(this Type type)
        {
            if (type == null)
                yield break;

            foreach (Type t in GetBaseTypes(type.BaseType))
            {
                yield return t;
            }
            yield return type;
        }

        public static string ReplaceCommas(this string csv, char with = '.')
        {
            return csv.Replace(',', with);
        }

        public static IEnumerable<Keys> GetKeys(this IEnumerable<ShortcutKey> shortcutKeys)
        {
            foreach (ShortcutKey shortcutKey in shortcutKeys.Where(r => r != null))
            {
                foreach (Keys key in shortcutKey.InvolvedKeys)
                {
                    yield return key;
                }
            }
        }

        public static string ToHex(this Color color)
        {
            return "#" + color.R.ToString("XX") + color.G.ToString("XX") + color.B.ToString("XX");
        }

        public static string ToCSV(this string[][] table)
        {
            return string.Join("\r\n", table.Select(line => string.Join(",", line.Select(i => i.NoControlCharacters()))));
        }

        public static string ToTSV(this string[][] table)
        {
            return string.Join("\r\n", table.Select(line => string.Join("\t", line.Select(i => i.NoControlCharacters()))));
        }

        public static string NoControlCharacters(this string text)
        {
            string clean = "";
            foreach (char c in text)
            {
                if (c < 32)
                    continue;
                if (c >= 127 && c < 160)
                    continue;

                clean += c;
            }
            return clean;
        }

        public static string AsciiOnly(this string text)
        {
            string clean = "";
            foreach (char c in text)
            {
                if (c < 32)
                    continue;
                if (c >= 127 && c < 160)
                    continue;

                if (c >= 255)
                    continue;

                clean += c;
            }
            return clean;
        }

        public static string NoExtension(this FileInfo file)
        {
            if (file.FullName.EndsWith(file.Extension))
            {
                return file.FullName.Substring(0, file.FullName.Length - file.Extension.Length);
            }

            return file.FullName;
        }

        public static string ToString(this double? d, string f)
        {
            if (d == null)
                return "null";
            return d.Value.ToString(f);
        }

        public static string ToASCII(this string input)
        {
            StringBuilder sb = new StringBuilder(input.Length);
            foreach (char c in input)
            {
                if ((int)c > 127)
                    continue;
                if ((int)c < 32)  
                    continue;
                sb.Append(c);
            }

            return sb.ToString();
        }

        public static void SetValue<K, V>(this Dictionary<K,V> dict, K key, V value)
        {
            if (dict.ContainsKey(key))
                dict[key] = value;
            else
                dict.Add(key, value);
        }
    }

    public class DateOnlyAttribute : Attribute
    {
    }
}
