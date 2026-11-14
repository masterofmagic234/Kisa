using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Arkon.Core
{
    [Serializable]
    public sealed class FactRecord
    {
        public string key = "";
        public string kind = "bool";
        public string value = "false";
    }

    [Serializable]
    public sealed class ProgressDocument
    {
        public int schema = 1;
        public string current = "";
        public string[] completed = Array.Empty<string>();
        public FactRecord[] facts = Array.Empty<FactRecord>();
    }

    // Data only; neither actors nor UnityEngine.Object references enter a save.
    // The native Unity DTO supports primitive story facts. Importing arbitrary
    // nested Godot ConfigFile Variants is deliberately not claimed by this port.
    public sealed class ProgressModel
    {
        private readonly List<string> completed = new List<string>();
        private readonly Dictionary<string, object> facts = new Dictionary<string, object>();
        public string CurrentLevel { get; set; } = "";
        public IReadOnlyList<string> CompletedLevels => completed.AsReadOnly();
        public IReadOnlyDictionary<string, object> Facts => facts;

        public bool IsCompleted(string id) => completed.Contains(id);
        public bool IsUnlocked(string predecessor = "") => string.IsNullOrEmpty(predecessor) || IsCompleted(predecessor);
        public void MarkCompleted(string id)
        {
            if (!string.IsNullOrEmpty(id) && !completed.Contains(id)) completed.Add(id);
        }
        public void ResetRun() { completed.Clear(); facts.Clear(); CurrentLevel = ""; }
        public void SetFact(string key, object value)
        {
            if (string.IsNullOrEmpty(key) || !(value is bool || value is string || value is int || value is double || value is float))
                throw new ArgumentException("A story fact must have a key and a primitive serializable value.");
            if (value is double d && (double.IsNaN(d) || double.IsInfinity(d)) || value is float f && !Scalar.IsFinite(f))
                throw new ArgumentException("A numeric story fact must be finite.");
            facts[key] = value;
        }
        public ProgressDocument Capture()
        {
            var records = new List<FactRecord>();
            foreach (var pair in facts)
            {
                var kind = pair.Value is bool ? "bool" : pair.Value is string ? "string" : "number";
                records.Add(new FactRecord { key = pair.Key, kind = kind,
                    value = pair.Value is bool b ? (b ? "true" : "false") : Convert.ToString(pair.Value, CultureInfo.InvariantCulture) });
            }
            records.Sort((a,b) => string.CompareOrdinal(a.key,b.key));
            return new ProgressDocument { current = CurrentLevel, completed = completed.ToArray(), facts = records.ToArray() };
        }
        public bool TryApply(ProgressDocument source, out string error)
        {
            error = "";
            if (source == null || source.schema != 1 || source.current == null || source.completed == null || source.facts == null)
            { error = "Unsupported or incomplete progress document."; return false; }
            var nextCompleted = new List<string>();
            var nextFacts = new Dictionary<string, object>();
            foreach (var id in source.completed)
            {
                if (id == null) { error = "A completed level ID is null."; return false; }
                if (!nextCompleted.Contains(id)) nextCompleted.Add(id);
            }
            foreach (var record in source.facts)
            {
                if (record == null || string.IsNullOrEmpty(record.key) || record.value == null || nextFacts.ContainsKey(record.key))
                { error = "Invalid or duplicate fact."; return false; }
                object value;
                if (record.kind == "bool" && bool.TryParse(record.value, out var boolean)) value = boolean;
                else if (record.kind == "string") value = record.value;
                else if (record.kind == "number" && double.TryParse(record.value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    && !double.IsNaN(number) && !double.IsInfinity(number)) value = number;
                else { error = "Unknown fact kind or invalid value."; return false; }
                nextFacts.Add(record.key, value);
            }
            // Validate the complete document before touching live progress.
            completed.Clear(); completed.AddRange(nextCompleted);
            facts.Clear(); foreach (var pair in nextFacts) facts.Add(pair.Key, pair.Value);
            CurrentLevel = source.current;
            return true;
        }
    }

    public static class AtomicTextFile
    {
        public static void Write(string path, string content)
        {
            var absolute = Path.GetFullPath(path); var directory = Path.GetDirectoryName(absolute);
            Directory.CreateDirectory(directory);
            var temporary = absolute + ".tmp";
            using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(content);
                file.Write(bytes, 0, bytes.Length); file.Flush(true);
            }
            // Never delete a previous valid save before replacement succeeds.
            if (File.Exists(absolute)) File.Replace(temporary, absolute, null);
            else File.Move(temporary, absolute);
        }
    }
}
