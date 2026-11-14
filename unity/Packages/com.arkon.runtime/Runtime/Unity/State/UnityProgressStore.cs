using System;
using System.IO;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    public static class UnityProgressStore
    {
        public static void Save(ProgressModel progress, string path)
            => AtomicTextFile.Write(path, JsonUtility.ToJson(progress.Capture(), true));
        public static bool TryLoad(ProgressModel progress, string path, out string error)
        {
            try { return progress.TryApply(JsonUtility.FromJson<ProgressDocument>(File.ReadAllText(path)), out error); }
            catch (Exception e) when (e is IOException || e is ArgumentException || e is UnauthorizedAccessException)
            { error = e.Message; return false; }
        }
    }
}
