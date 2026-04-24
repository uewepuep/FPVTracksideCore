using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace Timing.Aruco
{
    public class ArucoCalibration
    {
        public double[,] Mtx { get; }
        public double[] Dist { get; }
        public int ReferenceWidth { get; }
        public int ReferenceHeight { get; }

        private ArucoCalibration(double[,] mtx, double[] dist, int width, int height)
        {
            Mtx = mtx;
            Dist = dist;
            ReferenceWidth = width;
            ReferenceHeight = height;
        }

        public static ArucoCalibration TryLoad(string path)
        {
            return TryLoad(path, out _);
        }

        public static ArucoCalibration TryLoad(string path, out string errorReason)
        {
            errorReason = null;
            try
            {
                if (!File.Exists(path))
                {
                    errorReason = "file not found";
                    return null;
                }

                JObject root = JObject.Parse(File.ReadAllText(path));

                JArray mtxArr = root["mtx"] as JArray;
                JArray distArr = root["dist"] as JArray;
                JArray resArr = root["resolution"] as JArray;

                if (mtxArr == null || mtxArr.Count != 3)
                {
                    errorReason = "mtx missing or not 3x3";
                    return null;
                }

                double[,] mtx = new double[3, 3];
                for (int r = 0; r < 3; r++)
                {
                    JArray row = mtxArr[r] as JArray;
                    if (row == null || row.Count != 3)
                    {
                        errorReason = "mtx row " + r + " not length 3";
                        return null;
                    }
                    for (int c = 0; c < 3; c++)
                        mtx[r, c] = (double)row[c];
                }

                JArray distRow = distArr;
                if (distArr != null && distArr.Count > 0 && distArr[0] is JArray inner)
                    distRow = inner;
                if (distRow == null)
                {
                    errorReason = "dist missing";
                    return null;
                }

                double[] dist = new double[distRow.Count];
                for (int i = 0; i < distRow.Count; i++)
                    dist[i] = (double)distRow[i];

                int refH = 720;
                int refW = 960;
                if (resArr != null && resArr.Count >= 2)
                {
                    refH = (int)resArr[0];
                    refW = (int)resArr[1];
                }

                return new ArucoCalibration(mtx, dist, refW, refH);
            }
            catch (Exception ex)
            {
                errorReason = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }
    }
}
