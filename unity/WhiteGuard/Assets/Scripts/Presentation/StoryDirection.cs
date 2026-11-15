using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhiteGuard.Presentation
{
    [Serializable] public sealed class StoryDirectionData { public int schema; public StoryShot[] shots; }
    [Serializable] public sealed class StoryShot { public string cardId; public StoryBeat[] beats; }
    [Serializable] public sealed class StoryBeat
    {
        public float at;
        public float[] view;
        public string art, insetArt;
        public int quadrant;
        public float[] inset;
    }
    /// <summary>Authored cuts and detail inserts, kept separate from narration and the source pictures.</summary>
    public sealed class StoryDirection
    {
        private readonly Dictionary<string, StoryShot> shots = new Dictionary<string, StoryShot>(StringComparer.Ordinal);
        public StoryDirection()
        {
            var file = Resources.Load<TextAsset>("WhiteGuardCampaign/story-direction");
            if (!file) throw new InvalidOperationException("Не найдена разметка сюжетных сцен.");
            var data = JsonUtility.FromJson<StoryDirectionData>(file.text);
            if (data == null || data.schema != 1 || data.shots == null) throw new InvalidOperationException("Invalid story direction data.");
            foreach (var shot in data.shots)
            {
                if (shot == null || string.IsNullOrEmpty(shot.cardId) || shot.beats == null || shot.beats.Length == 0 || shots.ContainsKey(shot.cardId))
                    throw new InvalidOperationException("Invalid story shot.");
                var previous = -1f;
                foreach (var beat in shot.beats)
                {
                    if (beat == null || float.IsNaN(beat.at) || float.IsInfinity(beat.at) || beat.at < 0 || beat.at < previous)
                        throw new InvalidOperationException("Invalid story cut time.");
                    CheckRect(beat.view);
                    if (!string.IsNullOrEmpty(beat.insetArt)) CheckRect(beat.inset);
                    previous = beat.at;
                }
                shots.Add(shot.cardId, shot);
            }
        }
        private static void CheckRect(float[] rect)
        {
            if (rect == null || rect.Length != 4) throw new InvalidOperationException("A story view needs four coordinates.");
            foreach (var value in rect) if (float.IsNaN(value) || float.IsInfinity(value)) throw new InvalidOperationException("Non-finite story rectangle.");
            if (rect[0] < 0 || rect[1] < 0 || rect[2] <= 0 || rect[3] <= 0 || rect[0] + rect[2] > 1.00001f || rect[1] + rect[3] > 1.00001f)
                throw new InvalidOperationException("Story rectangle is outside its picture.");
        }
        public StoryBeat At(string id, float seconds)
        {
            if (id == null || !shots.TryGetValue(id, out var shot)) return null;
            var result = shot.beats[0];
            foreach (var beat in shot.beats) { if (beat.at > seconds) break; result = beat; }
            return result;
        }
    }
}
