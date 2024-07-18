using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using System;
using System.Collections.Generic;
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

            return things.GetAtIndex(index);
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
            return things.LastOrDefault();
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
    }

    public class DateOnlyAttribute : Attribute
    {
    }
}
