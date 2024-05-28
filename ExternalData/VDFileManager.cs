using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using RaceLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ExternalData
{
    public class VDFileManager
    {
        public const float TransScale = 200;
        public const float QuartScale = 1000;

        private static JsonSerializerSettings JsonSerializerSettings = new JsonSerializerSettings() { Formatting = Formatting.None, MissingMemberHandling = MissingMemberHandling.Ignore };


        private static Dictionary<int, TrackElement.ElementTypes> mapping = new Dictionary<int, TrackElement.ElementTypes>()
        {
            { 285, TrackElement.ElementTypes.Gate },
            { 286, TrackElement.ElementTypes.Dive },
            { 88, TrackElement.ElementTypes.Dive },
            { 170, TrackElement.ElementTypes.Flag },
            { 2231, TrackElement.ElementTypes.Flag },
        };

        public static RaceLib.Track LoadTrk(string filename)
        {
            string encrypted = File.ReadAllText(filename);
            string decrypted = VDDecrypter.Decrypt(encrypted, VDDecrypter.DecryptKey);

            int len = decrypted.Length;
            int len2 = encrypted.Length;

            int nameStart = decrypted.IndexOf("\n");
            int start = decrypted.IndexOf("{");
            if (start == -1) 
            {
                return null;
            }

            int end = decrypted.LastIndexOf("}") + 1;

            string name = decrypted.Substring(nameStart, start - 2);

            string data = decrypted.Substring(start, end - start);

            VDFormat result = JsonConvert.DeserializeObject<VDFormat>(data, JsonSerializerSettings);

            Track track = new Track();
            track.Name = name;

            List<TrackElement> elements = new List<TrackElement>();
            elements.AddRange(GetTrackElements(result.gates));

            if (result.barriers != null)
            {
                foreach (TrackElement element in GetTrackElements(result.barriers))
                {
                    element.Decorative = true;
                    elements.Add(element);
                }
            }
            

            track.TrackElements = elements.Where(r => r.ElementType != TrackElement.ElementTypes.Invalid).ToArray();

            return track;
        }

        public static void SaveTrk(RaceLib.Track track, string filename)
        {
            VDFormat vDFormat = new VDFormat();
            vDFormat.gates = GetVDGates(track.TrackElements).ToArray();

            string serialised = JsonConvert.SerializeObject(vDFormat, JsonSerializerSettings);

            Regex rgx = new Regex("[^a-zA-Z0-9 -]");
            string name = rgx.Replace(track.Name, "");

            string output = "16\n" + name + "\n" + serialised;

            string encrypted = VDDecrypter.Encrypt(output, VDDecrypter.DecryptKey);

            File.WriteAllText(filename, encrypted);
        }

        private static int GetPrefabType(TrackElement.ElementTypes type)
        {
            foreach (var kvp in mapping)
            {
                if (type == kvp.Value)
                    return kvp.Key;
            }

            return -1;
        }

        private static TrackElement.ElementTypes GetElementType(int prefab)
        {
            if (mapping.TryGetValue(prefab, out var elementType))
            { 
                return elementType; 
            }

            return TrackElement.ElementTypes.Invalid;
        }


        private static IEnumerable<VDGate> GetVDGates(IEnumerable<TrackElement> trackElements)
        {
            bool first = true;

            int count = 0;

            foreach (TrackElement trackElement in trackElements)
            {
                VDGate vdgate = new VDGate();
                vdgate.start = vdgate.finish = first;
                first = false;
                vdgate.prefab = GetPrefabType(trackElement.ElementType);

                Vector3 t = trackElement.Position * TransScale;

                vdgate.trans.pos[0] = -(int)t.X;
                vdgate.trans.pos[1] = (int)t.Y;
                vdgate.trans.pos[2] = (int)t.Z;

                float rotation = trackElement.Rotation;

                if (trackElement.ElementType == TrackElement.ElementTypes.Flag)
                {
                    rotation -= 90;
                }

                Quaternion q = Quaternion.CreateFromAxisAngle(Vector3.Up, rotation);

                vdgate.trans.rot[0] = (int)(q.W * QuartScale);
                vdgate.trans.rot[1] = (int)(q.X * QuartScale);
                vdgate.trans.rot[2] = (int)(q.Y * QuartScale);
                vdgate.trans.rot[3] = (int)(q.Z * QuartScale);

                vdgate.trans.scale[0] = 100;
                vdgate.trans.scale[1] = 100;
                vdgate.trans.scale[2] = 100;

                vdgate.gate = count;
                count++;
                yield return vdgate;
            }
        }

        private static IEnumerable<TrackElement> GetTrackElements(IEnumerable<VDGate> vdgates)
        {
            Vector3 offset = Vector3.Zero;

            foreach (VDGate vg in vdgates.OrderBy(r => r.gate)) 
            {
                TrackElement tr = new TrackElement();
                tr.ElementType = GetElementType(vg.prefab);

                tr.Position = new Vector3(-vg.trans.pos[0], vg.trans.pos[1], vg.trans.pos[2]);
                tr.Position /= TransScale; // VD weird units?.

                if (offset == Vector3.Zero) 
                {
                    offset = tr.Position;
                }

                tr.Position -= offset;

                // Quaternion also seems to be 1000x
                Quaternion qr = new Quaternion(vg.trans.rot[0] / QuartScale, vg.trans.rot[1] / QuartScale, vg.trans.rot[2] / QuartScale, vg.trans.rot[3] / QuartScale);

                Vector3 output = Vector3.Transform(Vector3.Forward, qr);

                output.Y = 0;

                float dot = Vector3.Dot(Vector3.Forward, output);

                tr.Rotation = MathHelper.ToDegrees((float)Math.Acos(dot));

                if (tr.ElementType == TrackElement.ElementTypes.Flag)
                {
                    tr.Rotation += 90;
                }

                if (float.IsNaN(tr.Rotation))
                {
                    tr.Rotation = 0;
                }

                yield return tr;
            }
        }
    }

    internal class VDFormat
    {
        public VDGate[] gates { get; set; }
        public VDGate[] barriers { get; set; }

        public VDFormat()
        {
            gates = new VDGate[0];
            barriers = new VDGate[0];
        }
    }

    internal class VDGate
    {
        public int prefab { get; set; }
        public transform trans { get; set; }
        public int gate { get; set; }
        public bool start { get; set; }
        public bool finish { get; set; }

        public VDGate()
        {
            trans = new transform();
        }
    }

    internal class transform
    {
        public int[] pos { get; set; }
        public int[] rot { get; set; }
        public int[] scale { get; set; }

        public transform()
        {
            pos = new int[3];
            rot = new int[4];
            scale = new int[3] { 100, 100, 100 };

        }
    }

    internal class VDDecrypter
    {
        public const string DecodeKey = "Bat Cave Games";
        public const string DecryptKey = "Velocidrone";

        public static string Encrypt(string input, string key)
        {
            key = key.Replace(" ", "").Substring(0, 8);
            RijndaelManaged rijndaelManaged = new RijndaelManaged
            {
                Key = Encoding.UTF8.GetBytes(key + Reverse(key)),
                Mode = CipherMode.ECB,
                BlockSize = 128,
                Padding = PaddingMode.PKCS7
            };
            ICryptoTransform transform = rijndaelManaged.CreateEncryptor(rijndaelManaged.Key, null);
            string result;
            using (MemoryStream memoryStream = new MemoryStream())
            {
                using (CryptoStream cryptoStream = new CryptoStream(memoryStream, transform, CryptoStreamMode.Write))
                {
                    using (StreamWriter streamWriter = new StreamWriter(cryptoStream))
                    {
                        streamWriter.Write(input);
                        streamWriter.Flush();
                    }
                    result = Convert.ToBase64String(memoryStream.ToArray());
                }
            }
            return result;
        }

        public static string Decrypt(string nmbdgcbekck, string cipher)
        {
            cipher = cipher.Replace(" ", "").Substring(0, 8);
            byte[] buffer = Convert.FromBase64String(nmbdgcbekck);
            string s = cipher + Reverse(cipher);

            RijndaelManaged rijndaelManaged = new RijndaelManaged
            {
                Key = Encoding.UTF8.GetBytes(s),
                Mode = CipherMode.ECB,
                BlockSize = 128,
                Padding = PaddingMode.PKCS7
            };
            ICryptoTransform transform = rijndaelManaged.CreateDecryptor(rijndaelManaged.Key, rijndaelManaged.IV);
            string result;
            using (MemoryStream memoryStream = new MemoryStream(buffer))
            {
                using (CryptoStream cryptoStream = new CryptoStream(memoryStream, transform, CryptoStreamMode.Read))
                {
                    using (StreamReader streamReader = new StreamReader(cryptoStream))
                    {
                        result = streamReader.ReadToEnd();
                    }
                }
            }
            return result;
        }

        public static string Reverse(string input)
        {
            char[] array = input.ToCharArray();
            Array.Reverse(array);
            return new string(array);
        }
    }
}
