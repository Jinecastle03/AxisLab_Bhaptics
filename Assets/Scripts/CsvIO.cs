using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace AxisLabHaptics
{
    public static class CsvIO
    {
        // motorPairs: "3:0.7|4:0.3"
        private static string MotorsToString(MotorWeight[] motors)
        {
            if (motors == null || motors.Length == 0) return "";
            return string.Join("|", motors.Select(m => $"{m.index}:{m.weight.ToString("G9", CultureInfo.InvariantCulture)}"));
        }

        private static MotorWeight[] StringToMotors(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return Array.Empty<MotorWeight>();

            var parts = s.Split('|');
            var arr = new MotorWeight[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                var kv = parts[i].Split(':');
                if (kv.Length != 2) continue;

                int idx = int.Parse(kv[0], CultureInfo.InvariantCulture);
                float w = float.Parse(kv[1], CultureInfo.InvariantCulture);
                arr[i] = new MotorWeight { index = idx, weight = w };
            }
            return arr;
        }

        public static void ExportTraceToCsv(HapticTraceAsset trace, string filePath)
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));

            var sb = new StringBuilder();
            sb.AppendLine("time,x,y,z,baseIntensity,sigma,motorPairs");

            foreach (var p in trace.points)
            {
                string line =
                    p.time.ToString("G9", CultureInfo.InvariantCulture) + "," +
                    p.pos.x.ToString("G9", CultureInfo.InvariantCulture) + "," +
                    p.pos.y.ToString("G9", CultureInfo.InvariantCulture) + "," +
                    p.pos.z.ToString("G9", CultureInfo.InvariantCulture) + "," +
                    p.baseIntensity.ToString("G9", CultureInfo.InvariantCulture) + "," +
                    p.sigma.ToString("G9", CultureInfo.InvariantCulture) + "," +
                    MotorsToString(p.motors);

                sb.AppendLine(line);
            }

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"CSV Exported: {filePath}");
        }

        public static void ImportTraceFromCsv(HapticTraceAsset trace, string filePath, bool clearFirst = true)
        {
            if (trace == null) throw new ArgumentNullException(nameof(trace));
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);

            var lines = File.ReadAllLines(filePath, Encoding.UTF8);
            if (lines.Length <= 1) return;

            if (clearFirst) trace.points.Clear();

            for (int i = 1; i < lines.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;

                // split by comma, motorPairs is last column (no commas inside)
                var cols = lines[i].Split(',');
                if (cols.Length < 7) continue;

                float time = float.Parse(cols[0], CultureInfo.InvariantCulture);
                float x = float.Parse(cols[1], CultureInfo.InvariantCulture);
                float y = float.Parse(cols[2], CultureInfo.InvariantCulture);
                float z = float.Parse(cols[3], CultureInfo.InvariantCulture);
                float baseI = float.Parse(cols[4], CultureInfo.InvariantCulture);
                float sigma = float.Parse(cols[5], CultureInfo.InvariantCulture);
                string motorPairs = cols[6];

                var tp = new TracePoint
                {
                    time = time,
                    pos = new Vector3(x, y, z),
                    baseIntensity = baseI,
                    sigma = sigma,
                    motors = StringToMotors(motorPairs)
                };

                trace.points.Add(tp);
            }

#if UNITY_EDITOR
            UnityEditor.EditorUtility.SetDirty(trace);
#endif
            Debug.Log($"CSV Imported: {filePath} (points: {trace.points.Count})");
        }
    }
}
