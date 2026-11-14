using System;

namespace Arkon.Core
{
    /// <summary>Public race_state.gd and race_queries.gd. Mission and ranking remain distinct.</summary>
    public sealed class RaceState
    {
        public float Countdown = 3;
        public bool RaceStarted, RaceFinished, MissionComplete, MissionFailed;
        public int Lap, TotalLaps, Position = 1, RacerCount;
        public float LapTime, BestLap = -1, RaceTime, LastLapTime, PlayerProgress, MessageTime;
        public float[] AiProgress = Array.Empty<float>();
        public int Hp = 100, Ammo, Acorns, AcornsTotal;
        public void Setup(int laps, int racers) { TotalLaps = laps; RacerCount = racers; AiProgress = new float[Math.Max(0, racers - 1)]; }
        public void ResetRace()
        {
            Countdown = 3; RaceStarted = RaceFinished = MissionComplete = MissionFailed = false;
            Lap = 0; LapTime = RaceTime = LastLapTime = 0; BestLap = -1; Position = 1; PlayerProgress = 0;
            Array.Clear(AiProgress, 0, AiProgress.Length); MessageTime = 0; Hp = 100; Ammo = Acorns = 0;
            // Source reset intentionally retains TotalLaps, RacerCount and AcornsTotal.
        }
    }
    public static class RaceQueries
    {
        public static bool CanControl(RaceState state) => state.RaceStarted && !state.RaceFinished && !state.MissionComplete && !state.MissionFailed;
        public static bool IsActive(RaceState state) => CanControl(state);
        public static bool IsFinished(RaceState state) => state.RaceFinished || state.MissionComplete || state.MissionFailed;
        public static bool IsLastLap(RaceState state) => state.Lap >= state.TotalLaps - 1;
    }

    /// <summary>Canonical segment/lap/grid math from race_car_controller.gd, including corrected contact ownership.</summary>
    public sealed class RaceRouteState
    {
        public readonly int TrackSize;
        public int SegmentIndex { get; private set; }
        public float SegmentProgress { get; private set; }
        public int LastSegmentIndex { get; private set; } = -1;
        public int Lap { get; private set; }
        public int GridIndex { get; private set; }
        public float GridWorldZOffset { get; private set; }
        public float Progress => (float)Lap * TrackSize + SegmentIndex + SegmentProgress;
        public float PhysicalProgress => Progress - GridWorldZOffset / RaceCalibration.SegmentHeight;
        public float WorldZ => Progress * RaceCalibration.SegmentHeight - GridWorldZOffset;
        public RaceRouteState(int trackSize)
        {
            if (trackSize <= 0) throw new ArgumentOutOfRangeException(nameof(trackSize));
            TrackSize = trackSize;
        }
        public void PlaceOnGrid(int slot)
        {
            // Source place_on_grid owns placement only; the race owner creates/reset cars.
            GridIndex = slot; SegmentIndex = 0; SegmentProgress = 0;
            GridWorldZOffset = slot * RaceCalibration.GridSpacing;
        }
        public void AdvanceSegments(float advance)
        {
            SegmentProgress += advance;
            while (SegmentProgress >= 1)
            {
                SegmentProgress -= 1; LastSegmentIndex = SegmentIndex; SegmentIndex++;
                if (SegmentIndex >= TrackSize) { SegmentIndex = 0; Lap++; }
            }
            while (SegmentProgress < 0)
            {
                SegmentProgress += 1; LastSegmentIndex = SegmentIndex; SegmentIndex--;
                if (SegmentIndex < 0) { SegmentIndex = TrackSize - 1; Lap--; }
            }
        }
        public void SetFromPhysicalWorldZ(float correctedWorldZ)
        {
            var canonical = (correctedWorldZ + GridWorldZOffset) / RaceCalibration.SegmentHeight;
            var floor = (int)Math.Floor(canonical);
            Lap = (int)Math.Floor(canonical / TrackSize);
            SegmentIndex = RaceNumerics.PosMod(floor, TrackSize);
            SegmentProgress = canonical - (float)Math.Floor(canonical);
        }
    }
}
