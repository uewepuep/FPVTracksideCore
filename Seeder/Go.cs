using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SeederEngine
{
    /// <summary>
    /// Small helpers that reproduce Go semantics the engine depends on, so the
    /// port behaves identically to the AUFPV platform's Go code:
    ///  - Go's append-to-nil yields nil when nothing is appended (persisted as JSON null),
    ///  - sort.SliceStable is a STABLE sort driven by a less() function
    ///    (List.Sort in .NET is unstable),
    ///  - %q quoting in error messages.
    /// </summary>
    internal static class Go
    {
        /// <summary>append([]string(nil), src...) — null when src is null or empty.</summary>
        public static List<T> Copy<T>(IEnumerable<T> src)
        {
            if (src == null)
            {
                return null;
            }
            List<T> list = new List<T>(src);
            if (list.Count == 0)
            {
                return null;
            }
            return list;
        }

        /// <summary>append(dst, vals...) — allocates when dst is null and vals non-empty.</summary>
        public static List<T> Append<T>(List<T> dst, IEnumerable<T> vals)
        {
            if (vals == null)
            {
                return dst;
            }
            foreach (T v in vals)
            {
                if (dst == null)
                {
                    dst = new List<T>();
                }
                dst.Add(v);
            }
            return dst;
        }

        public static List<T> Append<T>(List<T> dst, T val)
        {
            if (dst == null)
            {
                dst = new List<T>();
            }
            dst.Add(val);
            return dst;
        }

        /// <summary>s[:0] — keeps nil-ness: null stays null, otherwise an empty list.</summary>
        public static List<T> Truncate<T>(List<T> src)
        {
            if (src == null)
            {
                return null;
            }
            return new List<T>();
        }

        public static int Len<T>(ICollection<T> c)
        {
            return c == null ? 0 : c.Count;
        }

        public static IEnumerable<T> Iter<T>(IEnumerable<T> c)
        {
            if (c == null)
            {
                return Array.Empty<T>();
            }
            return c;
        }

        /// <summary>
        /// sort.SliceStable equivalent: a stable merge sort using a Go-style less(a, b).
        /// Sorts in place.
        /// </summary>
        public static void SortStable<T>(List<T> list, Func<T, T, bool> less)
        {
            if (list == null || list.Count < 2)
            {
                return;
            }
            T[] a = list.ToArray();
            T[] tmp = new T[a.Length];
            MergeSort(a, tmp, 0, a.Length, less);
            for (int i = 0; i < a.Length; i++)
            {
                list[i] = a[i];
            }
        }

        private static void MergeSort<T>(T[] a, T[] tmp, int lo, int hi, Func<T, T, bool> less)
        {
            if (hi - lo < 2)
            {
                return;
            }
            int mid = (lo + hi) / 2;
            MergeSort(a, tmp, lo, mid, less);
            MergeSort(a, tmp, mid, hi, less);
            int i = lo, j = mid, k = lo;
            while (i < mid && j < hi)
            {
                // Take from the right only when it is strictly less — keeps equal
                // elements in their original order.
                if (less(a[j], a[i]))
                {
                    tmp[k++] = a[j++];
                }
                else
                {
                    tmp[k++] = a[i++];
                }
            }
            while (i < mid)
            {
                tmp[k++] = a[i++];
            }
            while (j < hi)
            {
                tmp[k++] = a[j++];
            }
            for (int x = lo; x < hi; x++)
            {
                a[x] = tmp[x];
            }
        }

        /// <summary>Go's %q (strconv.Quote) for the plain strings pilot names and ids are.</summary>
        public static string Quote(string s)
        {
            if (s == null)
            {
                s = "";
            }
            StringBuilder sb = new StringBuilder();
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\a': sb.Append("\\a"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\v': sb.Append("\\v"); break;
                    default:
                        if (c < 0x20 || c == 0x7f)
                        {
                            sb.Append("\\x");
                            sb.Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            sb.Append(c);
                        }
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>strconv.Atoi: returns false on anything Go would reject.</summary>
        public static bool Atoi(string s, out int n)
        {
            n = 0;
            if (string.IsNullOrEmpty(s))
            {
                return false;
            }
            int i = 0;
            bool neg = false;
            if (s[0] == '+' || s[0] == '-')
            {
                neg = s[0] == '-';
                i = 1;
                if (s.Length == 1)
                {
                    return false;
                }
            }
            long v = 0;
            for (; i < s.Length; i++)
            {
                char c = s[i];
                if (c < '0' || c > '9')
                {
                    n = 0;
                    return false;
                }
                v = v * 10 + (c - '0');
                if (v > int.MaxValue + 1L)
                {
                    n = 0;
                    return false;
                }
            }
            if (neg)
            {
                v = -v;
            }
            if (v > int.MaxValue || v < int.MinValue)
            {
                n = 0;
                return false;
            }
            n = (int)v;
            return true;
        }

        /// <summary>
        /// fmt.Sscanf(s, "%d", &n): parses an optional sign and leading decimal digits;
        /// leaves n at 0 when there are none.
        /// </summary>
        public static int ScanInt(string s)
        {
            if (string.IsNullOrEmpty(s))
            {
                return 0;
            }
            int i = 0;
            // Sscanf skips leading spaces.
            while (i < s.Length && s[i] == ' ')
            {
                i++;
            }
            bool neg = false;
            if (i < s.Length && (s[i] == '+' || s[i] == '-'))
            {
                neg = s[i] == '-';
                i++;
            }
            int start = i;
            long v = 0;
            while (i < s.Length && s[i] >= '0' && s[i] <= '9')
            {
                v = v * 10 + (s[i] - '0');
                if (v > int.MaxValue)
                {
                    return 0;
                }
                i++;
            }
            if (i == start)
            {
                return 0;
            }
            return (int)(neg ? -v : v);
        }

        public static string Itoa(int n)
        {
            return n.ToString(CultureInfo.InvariantCulture);
        }

        public static bool SameSlice(List<string> a, List<string> b)
        {
            int la = Len(a), lb = Len(b);
            if (la != lb)
            {
                return false;
            }
            for (int i = 0; i < la; i++)
            {
                if (a[i] != b[i])
                {
                    return false;
                }
            }
            return true;
        }

        public static int MinInt(int a, int b)
        {
            return a < b ? a : b;
        }

        public static int MaxInt(int a, int b)
        {
            return a > b ? a : b;
        }

        public static int CeilDiv(int a, int b)
        {
            if (b == 0)
            {
                return 0;
            }
            return (a + b - 1) / b;
        }

        /// <summary>strings.TrimSpace (Unicode white space, as Go defines it).</summary>
        public static string TrimSpace(string s)
        {
            if (s == null)
            {
                return "";
            }
            int start = 0, end = s.Length;
            while (start < end && IsGoSpace(s[start]))
            {
                start++;
            }
            while (end > start && IsGoSpace(s[end - 1]))
            {
                end--;
            }
            return s.Substring(start, end - start);
        }

        private static bool IsGoSpace(char c)
        {
            switch (c)
            {
                case '\t':
                case '\n':
                case '\v':
                case '\f':
                case '\r':
                case ' ':
                case '\u0085':
                case ' ':
                    return true;
            }
            if (c < 0x80)
            {
                return false;
            }
            return char.IsWhiteSpace(c);
        }
    }
}
