using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Arkon.Core
{
    [Serializable]
    public sealed class CaptionCue
    {
        public double Start;
        public double End;
        public string Text;

        public CaptionCue() { }
        public CaptionCue(double start, double end, string text)
        {
            Start = start;
            End = end;
            Text = text;
        }

        internal CaptionCue Copy() => new CaptionCue(Start, End, Text);
    }

    /// <summary>
    /// Port of presentation/cinematic_cues.gd. Queries decoded sound time;
    /// there is deliberately no frame-delta clock or forward-only cursor.
    /// </summary>
    public sealed class CueTrack
    {
        private List<CaptionCue> _cues = new List<CaptionCue>();
        public int Count => _cues.Count;

        public bool Configure(IEnumerable<CaptionCue> records)
        {
            if (records == null) return false;
            var next = new List<CaptionCue>();
            var previous = -1.0;
            foreach (var row in records)
            {
                // Source cue validation preserves ordering and permits an empty track.
                // Reject non-finite external values rather than installing an unusable clock.
                if (row == null || !Finite(row.Start) || !Finite(row.End)
                    || row.Start < previous || row.End < row.Start) return false;
                next.Add(row.Copy());
                previous = row.Start;
            }
            _cues = next;
            return true;
        }

        public CaptionCue At(double decodedSeconds)
        {
            foreach (var cue in _cues)
                if (decodedSeconds >= cue.Start && decodedSeconds < cue.End)
                    return cue.Copy();
            return null;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    [Serializable]
    public sealed class ReaderPanel
    {
        public string Text;
        public double MinimumHold;

        public ReaderPanel() { }
        public ReaderPanel(string text, double minimumHold = 0)
        {
            Text = text;
            MinimumHold = minimumHold;
        }

        internal ReaderPanel Copy() => new ReaderPanel(Text, MinimumHold);
    }

    /// <summary>
    /// Port of narrative/illustrated_sequence.gd. The owner commits completion
    /// facts; this reader does not write progress or keep actor references.
    /// </summary>
    public sealed class ReaderSequence
    {
        private List<ReaderPanel> _panels = new List<ReaderPanel>();
        private Dictionary<string, bool> _facts = new Dictionary<string, bool>();

        public bool Active { get; private set; }
        public string SequenceId { get; private set; } = string.Empty;
        public int Index { get; private set; } = -1;
        public double HeldSeconds { get; private set; }
        public int PanelCount => _panels.Count;
        public ReaderPanel CurrentPanel => Active && Index >= 0 && Index < _panels.Count
            ? _panels[Index].Copy() : null;

        public event Action<ReaderPanel, int, int> PanelChanged;
        public event Action<string, IReadOnlyDictionary<string, bool>, bool> Finished;

        public bool Play(string id, IEnumerable<ReaderPanel> panels,
            IReadOnlyDictionary<string, bool> facts = null)
        {
            if (string.IsNullOrEmpty(id) || panels == null) return false;
            var nextPanels = new List<ReaderPanel>();
            foreach (var panel in panels)
            {
                if (panel == null || string.IsNullOrEmpty(panel.Text)
                    || double.IsNaN(panel.MinimumHold) || double.IsInfinity(panel.MinimumHold)
                    || panel.MinimumHold < 0) return false;
                nextPanels.Add(panel.Copy());
            }
            if (nextPanels.Count == 0) return false;
            var nextFacts = CopyFacts(facts);

            // Validation and copying precede all changes to an active sequence.
            SequenceId = id;
            _panels = nextPanels;
            _facts = nextFacts;
            Index = 0;
            Active = true;
            Show();
            return true;
        }

        public void Tick(double deltaSeconds, bool paused = false)
        {
            if (Active && !paused && deltaSeconds > 0
                && !double.IsNaN(deltaSeconds) && !double.IsInfinity(deltaSeconds))
                HeldSeconds += deltaSeconds;
        }

        public bool Advance()
        {
            if (!Active || HeldSeconds < _panels[Index].MinimumHold) return false;
            Index++;
            if (Index >= _panels.Count) Complete(false);
            else Show();
            return true;
        }

        public bool Skip()
        {
            if (!Active) return false;
            Complete(true);
            return true;
        }

        private void Show()
        {
            HeldSeconds = 0;
            PanelChanged?.Invoke(_panels[Index].Copy(), Index, _panels.Count);
        }

        private void Complete(bool skipped)
        {
            var id = SequenceId;
            var outcome = new ReadOnlyDictionary<string, bool>(CopyFacts(_facts));
            // Clear active state before notifications; a callback can safely start
            // the next sequence without a late write from this one clobbering it.
            Active = false;
            Finished?.Invoke(id, outcome, skipped);
        }

        private static Dictionary<string, bool> CopyFacts(IReadOnlyDictionary<string, bool> facts)
        {
            var copy = new Dictionary<string, bool>();
            if (facts != null)
                foreach (var fact in facts) copy.Add(fact.Key, fact.Value);
            return copy;
        }
    }
}
