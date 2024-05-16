using Microsoft.Xna.Framework;
using Newtonsoft.Json;
using RaceLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ExternalData
{
    public class VDFileManager
    {
        public static RaceLib.Track LoadTrk(string filename)
        {
            string encrypted = File.ReadAllText(filename);
            string decrypted = VDDecrypter.Decrypt(encrypted, VDDecrypter.DecryptKey);

            int nameStart = decrypted.IndexOf("\n");
            int start = decrypted.IndexOf("{");
            if (start == -1) 
            {
                return null;
            }

            string name = decrypted.Substring(nameStart, start - 1);

            string data = decrypted.Substring(start);

            VDFormat result = JsonConvert.DeserializeObject<VDFormat>(data);

            Track track = new Track();
            track.Name = name;

            track.TrackElements = GetTrackElements(result.gates).ToArray();

            return track;
        }

        private static IEnumerable<TrackElement> GetTrackElements(IEnumerable<VDGate> vdgates)
        {
            Vector3 offset = Vector3.Zero;

            foreach (VDGate vg in vdgates.OrderBy(r => r.gate)) 
            {
                TrackElement tr = new TrackElement();
                switch (vg.prefab)
                {
                    case 285:
                        tr.ElementType = TrackElement.ElementTypes.Gate;
                        break;
                    case 88:
                        tr.ElementType = TrackElement.ElementTypes.Dive;
                        break;
                    case 170:
                        tr.ElementType = TrackElement.ElementTypes.Flag;
                        break;
#if DEBUG
                    default:
                        throw new NotImplementedException();
#endif
                }

                tr.Position = new Vector3(vg.trans.pos[0], vg.trans.pos[1], vg.trans.pos[2]);
                tr.Position /= 200; // VD weird units?.

                if (offset == Vector3.Zero) 
                {
                    offset = tr.Position;
                }

                tr.Position -= offset;

                // Quaternion also seems to be 1000x
                Quaternion qr = new Quaternion(vg.trans.rot[0] / 1000.0f, vg.trans.rot[1] / 1000.0f, vg.trans.rot[2] / 1000.0f, vg.trans.rot[3] / 1000.0f);

                Vector3 output = Vector3.Transform(Vector3.Forward, qr);

                output.Y = 0;

                float dot = Vector3.Dot(Vector3.Forward, output);

                tr.Rotation = MathHelper.ToDegrees((float)Math.Acos(dot));

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
    }

    internal class VDGate
    {
        public int prefab { get; set; }
        public transform trans { get; set; }
        public int gate { get; set; }
        public bool start { get; set; }
        public bool finish { get; set; }
    }

    internal class transform
    {
        public int[] pos { get; set; }
        public int[] rot { get; set; }
        public int[] scale { get; set; }
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
