using System;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity.Intro
{
    public enum IntroPlaybackState { Title, Playing, Finished, Error }

    /// <summary>
    /// Unity adapter for the existing White Guard opening. PNGs, text, MP3s,
    /// cuts and cues are input data, not regenerated content. This is an engine
    /// migration of the restored staging, not a new approved direction.
    /// Actual Unity lifecycle, import, audio and GPU tests are still required.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class IntroPlayer : MonoBehaviour
    {
        [SerializeField] private string resourcePrefix = "WhiteGuardIntro";
        private IntroBookData _book;
        private IntroStageData _stage;
        private IntroStageRenderer _renderer;
        private AudioClip[] _clips;
        private AudioClip _roadClip;
        private AudioSource _voice, _road;
        private readonly CueTrack _cues = new CueTrack();
        private double[] _offsets;
        private double _lastDecoded, _controlsUntil, _loadDeadline;
        private bool _loading, _completionRaised, _observedVoice;
        private bool _restartAfterDisable;
        private int _resumeVoiceSample, _resumeRoadSample, _lastRoadSample;
        private int _voiceStartFrame;
        private Vector3 _mouse;
        private Font _bodyFont, _titleFont;
        private GUIStyle _body, _title, _small, _button, _caption;

        public event Action<bool> Finished;
        public event Action<bool> PauseChanged;
        public IntroPlaybackState State { get; private set; } = IntroPlaybackState.Title;
        public bool Ready { get; private set; }
        public bool Paused { get; private set; }
        public bool Muted { get; private set; }
        public bool CaptionsEnabled { get; private set; }
        public int BlockIndex { get; private set; } = -1;
        public int CompletionCount { get; private set; }
        public string Caption { get; private set; } = "";
        public string LastError { get; private set; }
        public double GlobalSeconds => BlockIndex >= 0 && _offsets != null ? _offsets[BlockIndex] + _lastDecoded : 0;
        public double VoiceDuration => _book == null ? 0 : _book.duration;
        public IntroBookData Book => _book;
        public IntroStageData Storyboard => _stage;

        private void Awake()
        {
            _voice = gameObject.AddComponent<AudioSource>();
            _road = gameObject.AddComponent<AudioSource>();
            foreach (var source in new[] { _voice, _road })
            { source.playOnAwake = false; source.spatialBlend = 0; source.ignoreListenerPause = false; }
            _voice.loop = false; _road.loop = true;
            Configure(resourcePrefix);
        }

        /// <summary>
        /// Configure before playback. An active sequence must finish or be skipped
        /// before replacement; a failed asynchronous candidate never replaces a
        /// currently playing voice. Audio may still need background preload.
        /// </summary>
        public bool Configure(string prefix)
        {
            if (State == IntroPlaybackState.Playing)
            { LastError = "Finish or skip the active intro before reconfiguration."; return false; }
            try
            {
                if (string.IsNullOrWhiteSpace(prefix)) throw new ArgumentException("An intro Resources prefix is required.");
                prefix = prefix.Trim('/');
                var bookAsset = Resources.Load<TextAsset>(prefix + "/intro");
                var stageAsset = Resources.Load<TextAsset>(prefix + "/intro_shots");
                if (bookAsset == null || stageAsset == null) throw new InvalidOperationException("Missing intro JSON in Resources/" + prefix);
                var book = JsonUtility.FromJson<IntroBookData>(bookAsset.text);
                var stage = JsonUtility.FromJson<IntroStageData>(IntroJsonDefaults.WithLayerAlpha(stageAsset.text));
                if (!IntroDataValidation.Validate(book, stage, out var error)) throw new InvalidOperationException(error);
                var clips = new AudioClip[book.blocks.Length]; var offsets = new double[clips.Length];
                double elapsed = 0;
                for (var i = 0; i < clips.Length; i++)
                {
                    offsets[i] = elapsed; elapsed += book.blocks[i].duration;
                    clips[i] = Resources.Load<AudioClip>(MediaKey(prefix, book.blocks[i].audio));
                    if (clips[i] == null) throw new InvalidOperationException("Missing narration clip: " + book.blocks[i].audio);
                    clips[i].LoadAudioData();
                }
                var road = Resources.Load<AudioClip>(prefix + "/road-ambience");
                if (road == null) throw new InvalidOperationException("Missing road ambience.");
                road.LoadAudioData();
                var renderer = new IntroStageRenderer(stage, path => Resources.Load<Texture2D>(MediaKey(prefix, path)));
                StopSources(); _renderer?.Dispose(); _renderer = renderer;
                resourcePrefix = prefix; _book = book; _stage = stage; _clips = clips; _offsets = offsets; _roadClip = road;
                _bodyFont = Resources.Load<Font>(prefix + "/fonts/Manrope-Variable");
                _titleFont = Resources.Load<Font>(prefix + "/fonts/Forum-Regular");
                _body = null; _title = null; _small = null; _button = null; _caption = null;
                Ready = false; Paused = false; State = IntroPlaybackState.Title; BlockIndex = -1;
                _lastDecoded = 0; Caption = ""; LastError = null;
                _restartAfterDisable = false; _lastRoadSample = 0;
                CaptionsEnabled = stage.captions_default || Muted; _completionRaised = false;
                _loading = true; _loadDeadline = Time.realtimeSinceStartupAsDouble + 30;
                CheckAudioReady(); ApplyMute();
                return State != IntroPlaybackState.Error;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                Debug.LogError("White Guard intro configuration failed: " + LastError, this);
                // A synchronous validation error retains the preceding loaded data.
                if (_book == null) EnterError(LastError);
                return false;
            }
        }

        private static string MediaKey(string prefix, string sourcePath)
        {
            const string sourceRoot = "res://assets/intro/";
            if (sourcePath == null || !sourcePath.StartsWith(sourceRoot, StringComparison.Ordinal))
                throw new InvalidOperationException("Asset is outside the preserved intro media directory.");
            var relative = sourcePath.Substring(sourceRoot.Length);
            var extension = relative.LastIndexOf('.');
            return prefix + "/" + (extension >= 0 ? relative.Substring(0, extension) : relative);
        }

        private void CheckAudioReady()
        {
            if (!_loading) return;
            var loaded = CheckClip(_roadClip);
            foreach (var clip in _clips) loaded &= CheckClip(clip);
            if (State == IntroPlaybackState.Error) return;
            if (loaded) { Ready = true; _loading = false; }
            else if (Time.realtimeSinceStartupAsDouble > _loadDeadline) EnterError("Timed out loading narration audio.");
        }

        private bool CheckClip(AudioClip clip)
        {
            if (clip == null || clip.loadState == AudioDataLoadState.Failed)
            { EnterError("Unity could not decode an intro audio clip."); return false; }
            return clip.loadState == AudioDataLoadState.Loaded && clip.samples > 0 && clip.frequency > 0;
        }

        public void StartPlayback()
        {
            // The scene/pause owner must release a global pause before starting
            // this player. Playing an AudioSource while timeScale=0 consumes it.
            if (!Ready || Time.timeScale <= 0 || AudioListener.pause) return;
            State = IntroPlaybackState.Playing; Paused = false; _completionRaised = false;
            _restartAfterDisable = false; _lastRoadSample = 0;
            RevealControls(); _road.clip = _roadClip; _road.timeSamples = 0; _road.Play();
            ApplyMute(); PlayBlock(0); PauseChanged?.Invoke(false);
        }

        public void PlayBlock(int index)
        {
            if (!Ready || State != IntroPlaybackState.Playing || index < 0 || index >= _clips.Length) return;
            var block = _book.blocks[index]; var cues = new CaptionCue[block.timed_captions.Length];
            for (var i = 0; i < cues.Length; i++)
            { var cue = block.timed_captions[i]; cues[i] = new CaptionCue { Start = cue.start, End = cue.end, Text = cue.text }; }
            _cues.Configure(cues); _voice.Stop(); BlockIndex = index; _lastDecoded = 0;
            _voice.clip = _clips[index]; _voice.timeSamples = 0; _voice.Play();
            _voiceStartFrame = Time.frameCount; _observedVoice = false;
            if (Paused) _voice.Pause();
            UpdateCaption();
        }

        public void SetPaused(bool value)
        {
            if (State != IntroPlaybackState.Playing || Paused == value) return;
            if (!value && (Time.timeScale <= 0 || AudioListener.pause)) return;
            if (value)
            {
                ReadDecodedPosition(); _voice.Pause(); _road.Pause();
            }
            else
            {
                if (_restartAfterDisable)
                {
                    _voice.clip = _clips[BlockIndex]; _voice.Play();
                    _voice.timeSamples = Math.Max(0, Math.Min(_voice.clip.samples - 1, _resumeVoiceSample));
                    _road.clip = _roadClip; _road.Play();
                    _road.timeSamples = Math.Max(0, Math.Min(_roadClip.samples - 1, _resumeRoadSample));
                    _restartAfterDisable = false;
                    // This is a deliberately resumed existing voice, not a fresh
                    // resource load. Seeking its final sample may end immediately.
                    _observedVoice = true;
                }
                else { _voice.UnPause(); _road.UnPause(); }
                _voiceStartFrame = Time.frameCount;
            }
            Paused = value; RevealControls(); PauseChanged?.Invoke(value);
        }

        public void ToggleMute()
        {
            Muted = !Muted;
            if (Muted) CaptionsEnabled = true;
            ApplyMute(); RevealControls();
        }

        public void ToggleCaptions()
        {
            if (Muted && CaptionsEnabled) return;
            CaptionsEnabled = !CaptionsEnabled; RevealControls();
        }

        public void Skip() => Complete(true);

        public void SeekBlock(double seconds)
        {
            if (State != IntroPlaybackState.Playing || BlockIndex < 0 || _voice.clip == null) return;
            var clip = _voice.clip;
            var previouslyPlaying = _voice.isPlaying || _observedVoice;
            seconds = Math.Max(0, Math.Min(seconds, _book.blocks[BlockIndex].duration));
            var sample = (int)Math.Min(clip.samples - 1L, Math.Max(0L, (long)Math.Round(seconds * clip.frequency)));
            _voice.timeSamples = sample;
            _observedVoice = previouslyPlaying;
            _lastDecoded = Math.Min((double)sample / clip.frequency, _book.blocks[BlockIndex].duration - .0001);
            UpdateCaption();
        }

        private void Update()
        {
            CheckAudioReady();
            if (Input.mousePosition != _mouse || Input.touchCount > 0) { _mouse = Input.mousePosition; RevealControls(); }
            if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
            {
                if (State == IntroPlaybackState.Playing) SetPaused(!Paused);
                else StartPlayback();
            }
            if (Input.GetKeyDown(KeyCode.Escape) && State == IntroPlaybackState.Playing) SetPaused(true);
            if (State != IntroPlaybackState.Playing) return;
            // Time.timeScale does not pause AudioSource; never let a world pause
            // consume the monologue. Resuming remains an explicit user action.
            if (!Paused && (Time.timeScale <= 0 || AudioListener.pause)) SetPaused(true);
            if (Paused) return;
            if (_voice.isPlaying) { _observedVoice = true; ReadDecodedPosition(); UpdateCaption(); }
            else if (Time.frameCount > _voiceStartFrame + 2)
            {
                if (!_observedVoice) { EnterError("Narration did not enter the playing state."); return; }
                if (BlockIndex + 1 < _clips.Length) PlayBlock(BlockIndex + 1);
                else Complete(false);
            }
        }

        private void ReadDecodedPosition()
        {
            if (BlockIndex < 0 || _voice.clip == null || !_voice.isPlaying) return;
            // Decoder/sample clock, never accumulated render deltaTime.
            _lastDecoded = Math.Max(0, Math.Min((double)_voice.timeSamples / _voice.clip.frequency,
                _book.blocks[BlockIndex].duration - .0001));
            if (_road.isPlaying) _lastRoadSample = _road.timeSamples;
        }

        private void UpdateCaption()
        {
            var cue = _cues.At(_lastDecoded); Caption = cue == null ? "" : cue.Text;
        }

        private void Complete(bool skipped)
        {
            if (State != IntroPlaybackState.Playing || _completionRaised) return;
            _completionRaised = true; State = IntroPlaybackState.Finished; Paused = false;
            StopSources(); CompletionCount++; PauseChanged?.Invoke(false);
            Finished?.Invoke(skipped);
        }

        private void ApplyMute()
        { if (_voice != null) _voice.mute = Muted; if (_road != null) { _road.mute = Muted; _road.volume = .04466836f; } }
        private void StopSources() { if (_voice != null) _voice.Stop(); if (_road != null) _road.Stop(); }
        private void RevealControls() => _controlsUntil = Time.realtimeSinceStartupAsDouble + (_stage == null ? 2.25 : _stage.controls_idle_seconds);
        private void EnterError(string error)
        {
            LastError = error; _loading = false; Ready = false; State = IntroPlaybackState.Error; Paused = false;
            StopSources(); Debug.LogError("White Guard intro: " + error, this);
        }
        private void OnApplicationFocus(bool focused) { if (!focused) SetPaused(true); }
        private void OnApplicationPause(bool paused) { if (paused) SetPaused(true); }
        private void OnDisable()
        {
            if (State != IntroPlaybackState.Playing || BlockIndex < 0) return;
            ReadDecodedPosition();
            _resumeVoiceSample = _voice.isPlaying ? _voice.timeSamples :
                (int)Math.Round(_lastDecoded * _clips[BlockIndex].frequency);
            _resumeRoadSample = _road.isPlaying ? _road.timeSamples : _lastRoadSample;
            SetPaused(true);
            // Disabling a whole GameObject stops its AudioSources, unlike just
            // pausing a source. Explicit resume must restart at the saved sample.
            _restartAfterDisable = true;
        }
        private void OnDestroy()
        {
            StopSources(); _renderer?.Dispose();
            if (_voice != null) Destroy(_voice); if (_road != null) Destroy(_road);
        }

        private void OnGUI()
        {
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var oldMatrix = GUI.matrix; var oldColor = GUI.color; var oldEnabled = GUI.enabled;
            IntroStageRenderer.Solid(new Rect(0, 0, Screen.width, Screen.height), Color.black);
            var viewport = IntroStageGeometry.Viewport(Screen.width, Screen.height);
            GUI.matrix = Matrix4x4.TRS(new Vector3((float)viewport.X, (float)viewport.Y, 0), Quaternion.identity,
                new Vector3((float)viewport.Width / 1280, (float)viewport.Height / 720, 1));
            GUI.BeginGroup(new Rect(0, 0, 1280, 720));
            try
            {
                EnsureStyles();
                var shade = State == IntroPlaybackState.Title ? .42f : State == IntroPlaybackState.Finished ? .24f : 1;
                _renderer?.Draw(GlobalSeconds, new Color(shade, shade, shade, 1), _bodyFont);
                GUI.color = Color.white;
                if (State == IntroPlaybackState.Error)
                {
                    Card("", "ВСТУПЛЕНИЕ НЕДОСТУПНО", "Не удалось загрузить файлы просмотра.", "Повторить загрузку", () => Configure(resourcePrefix), true);
                }
                else if (State == IntroPlaybackState.Title)
                    Card("АЛЕКСАНДР БУШКОВ", "БЕЛАЯ ГВАРДИЯ", "Мысли Кирилла Мазура", "Начать", StartPlayback, Ready);
                else if (State == IntroPlaybackState.Finished)
                    Card("ГЛАВА ПЕРВАЯ", "КОРТЕЖ", "Мужичка того недреманная стража взяла…", "Повторить вступление", StartPlayback, Ready);
                else DrawPlayingUi();
            }
            finally { GUI.EndGroup(); GUI.matrix = oldMatrix; GUI.color = oldColor; GUI.enabled = oldEnabled; }
        }

        private void DrawPlayingUi()
        {
            if (CaptionsEnabled && !string.IsNullOrEmpty(Caption))
            {
                _caption.fontSize = 22;
                while (_caption.fontSize > 14 && _caption.CalcHeight(new GUIContent(Caption), 1022) > _caption.lineHeight * 2 + 2) _caption.fontSize--;
                var h = Math.Max(_caption.lineHeight, _caption.CalcHeight(new GUIContent(Caption), 1022));
                IntroStageRenderer.Solid(new Rect(105, 700 - h - 24, 1070, h + 24), new Color(.025f, .03f, .025f, .72f));
                GUI.Label(new Rect(129, 700 - h - 12, 1022, h + 2), Caption, _caption);
            }
            if (!Paused && Time.realtimeSinceStartupAsDouble >= _controlsUntil) return;
            if (GUI.Button(new Rect(694, 20, 140, 38), Paused ? "Продолжить" : "Пауза", _button)) SetPaused(!Paused);
            if (GUI.Button(new Rect(842, 20, 140, 38), CaptionsEnabled ? "Субтитры: вкл" : "Субтитры", _button)) ToggleCaptions();
            if (GUI.Button(new Rect(990, 20, 124, 38), Muted ? "Звук: выкл" : "Звук: вкл", _button)) ToggleMute();
            if (GUI.Button(new Rect(1122, 20, 134, 38), "Пропустить", _button)) Skip();
            var progress = _book == null ? 0 : (float)Math.Max(0, Math.Min(1, GlobalSeconds / _book.duration));
            IntroStageRenderer.Solid(new Rect(0, 717, 1280, 3), new Color32(32, 39, 31, 255));
            IntroStageRenderer.Solid(new Rect(0, 717, 1280 * progress, 3), new Color32(192, 164, 109, 255));
        }

        private void Card(string kicker, string title, string subtitle, string action, Action callback, bool enabled)
        {
            GUI.Label(new Rect(80, 225, 1120, 32), kicker, _small);
            GUI.Label(new Rect(40, 273, 1200, 90), title, _title);
            GUI.Label(new Rect(80, 378, 1120, 38), subtitle, _body);
            var wasEnabled = GUI.enabled; GUI.enabled = enabled;
            if (GUI.Button(new Rect(460, 443, 360, 52), action, _button)) callback();
            GUI.enabled = wasEnabled;
        }

        private void EnsureStyles()
        {
            if (_body != null) return;
            var ink = new Color32(238, 231, 213, 255);
            _body = new GUIStyle(GUI.skin.label) { font = _titleFont, fontSize = 20, alignment = TextAnchor.MiddleCenter };
            _body.normal.textColor = ink;
            _title = new GUIStyle(_body) { fontSize = 64 };
            _small = new GUIStyle(_body) { fontSize = 16 };
            _button = new GUIStyle(GUI.skin.button) { font = _bodyFont, fontSize = 14, alignment = TextAnchor.MiddleCenter };
            _button.normal.textColor = ink;
            _caption = new GUIStyle(GUI.skin.label) { font = _bodyFont, fontSize = 22, wordWrap = true, alignment = TextAnchor.UpperLeft, richText = false };
            _caption.normal.textColor = ink;
        }
    }
}
