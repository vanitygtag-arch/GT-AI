using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace GorillaTagAI
{
    // Passive, local-only learner. It does not read game objects or issue input itself.
    // A separately verified game-observation adapter must submit samples to this class.
    internal sealed class LearningModule
    {
        private readonly string _dataDirectory;
        private readonly string _samplesPath;
        private readonly Dictionary<string, PatternSummary> _patterns = new Dictionary<string, PatternSummary>();
        private int _observationCount;

        internal int ObservationCount => _observationCount;
        internal int PatternCount => _patterns.Count;
        internal string Status { get; private set; } = "Waiting for observations";
        internal string DataFilePath => _samplesPath;

        internal LearningModule(string configDirectory)
        {
            _dataDirectory = Path.Combine(configDirectory, "GorillaTagAI");
            _samplesPath = Path.Combine(_dataDirectory, "observations.csv");
            Directory.CreateDirectory(_dataDirectory);
            LoadExistingSummaries();
        }

        internal bool IsLearningAllowed(bool aiEnabled, bool learningEnabled)
        {
            return aiEnabled && learningEnabled;
        }

        internal void PauseStatus()
        {
            if (_observationCount == 0) Status = "Paused (AI or Learning is OFF)";
            else Status = "Saved data available; learning paused";
        }

        // Call only from a verified observation adapter, on the Unity main thread.
        // sourceKind should be "Self" or "ConsentingTarget"; do not pass player names/IDs.
        internal bool RecordObservation(
            bool aiEnabled,
            bool learningEnabled,
            string sourceKind,
            string movementLabel,
            float speed,
            float verticalSpeed,
            bool grounded)
        {
            if (!IsLearningAllowed(aiEnabled, learningEnabled))
            {
                Status = "Paused (AI or Learning is OFF)";
                return false;
            }

            if (sourceKind != "Self" && sourceKind != "ConsentingTarget")
            {
                Status = "Rejected observation: unknown source";
                return false;
            }

            string label = SanitizeLabel(movementLabel);
            if (label.Length == 0) label = "Unclassified";
            if (float.IsNaN(speed) || float.IsInfinity(speed) ||
                float.IsNaN(verticalSpeed) || float.IsInfinity(verticalSpeed))
            {
                Status = "Rejected observation: invalid numeric data";
                return false;
            }

            string key = label + "|" + (grounded ? "Grounded" : "Airborne");
            PatternSummary summary;
            if (!_patterns.TryGetValue(key, out summary))
            {
                summary = new PatternSummary(label, grounded);
                _patterns.Add(key, summary);
            }
            try
            {
                if (!File.Exists(_samplesPath))
                {
                    File.AppendAllText(_samplesPath,
                        "utc,source,movement,grounded,speed,vertical_speed" + Environment.NewLine,
                        new UTF8Encoding(false));
                }

                string row = string.Join(",", new string[]
                {
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    sourceKind,
                    Csv(label),
                    grounded ? "true" : "false",
                    speed.ToString("R", CultureInfo.InvariantCulture),
                    verticalSpeed.ToString("R", CultureInfo.InvariantCulture)
                }) + Environment.NewLine;
                File.AppendAllText(_samplesPath, row, new UTF8Encoding(false));
                summary.Add(speed, verticalSpeed);
                _observationCount++;
                Status = "Recording observations";
                return true;
            }
            catch (Exception ex)
            {
                Status = "Storage error: " + ex.GetType().Name;
                return false;
            }
        }

        internal string GetSummary(int maxLines)
        {
            if (_patterns.Count == 0) return "No learned patterns yet.";
            return string.Join("\n", _patterns.Values
                .OrderByDescending(p => p.Count)
                .Take(Math.Max(1, maxLines))
                .Select(p => p.Label + " / " + (p.Grounded ? "grounded" : "airborne") +
                    ": " + p.Count.ToString(CultureInfo.InvariantCulture) +
                    " samples, avg speed " + p.AverageSpeed.ToString("0.00", CultureInfo.InvariantCulture) +
                    ", avg vertical speed " + p.AverageVerticalSpeed.ToString("0.00", CultureInfo.InvariantCulture)));
        }

        private void LoadExistingSummaries()
        {
            if (!File.Exists(_samplesPath)) return;
            try
            {
                string[] lines = File.ReadAllLines(_samplesPath);
                for (int i = 1; i < lines.Length; i++)
                {
                    string[] fields = ParseCsvLine(lines[i]);
                    if (fields.Length < 6) continue;
                    bool grounded;
                    float speed;
                    float verticalSpeed;
                    if (!bool.TryParse(fields[3], out grounded) ||
                        !float.TryParse(fields[4], NumberStyles.Float, CultureInfo.InvariantCulture, out speed) ||
                        !float.TryParse(fields[5], NumberStyles.Float, CultureInfo.InvariantCulture, out verticalSpeed))
                        continue;
                    string label = SanitizeLabel(fields[2]);
                    string key = label + "|" + (grounded ? "Grounded" : "Airborne");
                    PatternSummary summary;
                    if (!_patterns.TryGetValue(key, out summary))
                    {
                        summary = new PatternSummary(label, grounded);
                        _patterns.Add(key, summary);
                    }
                    summary.Add(speed, verticalSpeed);
                    _observationCount++;
                }
                Status = _observationCount > 0 ? "Loaded saved observations" : "Waiting for observations";
            }
            catch
            {
                Status = "Could not read saved observations";
            }
        }

        private static string SanitizeLabel(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            string cleaned = value.Trim().Replace("\r", " ").Replace("\n", " ").Replace(",", " ").Replace("|", " ");
            return cleaned.Length > 32 ? cleaned.Substring(0, 32) : cleaned;
        }

        private static string Csv(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string[] ParseCsvLine(string line)
        {
            List<string> fields = new List<string>();
            StringBuilder current = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else quoted = !quoted;
                }
                else if (c == ',' && !quoted)
                {
                    fields.Add(current.ToString());
                    current.Length = 0;
                }
                else current.Append(c);
            }
            fields.Add(current.ToString());
            return fields.ToArray();
        }

        private sealed class PatternSummary
        {
            internal readonly string Label;
            internal readonly bool Grounded;
            internal int Count { get; private set; }
            private double _speedTotal;
            private double _verticalTotal;
            internal double AverageSpeed => Count == 0 ? 0 : _speedTotal / Count;
            internal double AverageVerticalSpeed => Count == 0 ? 0 : _verticalTotal / Count;

            internal PatternSummary(string label, bool grounded)
            {
                Label = label;
                Grounded = grounded;
            }

            internal void Add(float speed, float verticalSpeed)
            {
                Count++;
                _speedTotal += speed;
                _verticalTotal += verticalSpeed;
            }
        }
    }
}
