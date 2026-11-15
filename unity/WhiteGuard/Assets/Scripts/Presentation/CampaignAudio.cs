using System;
using Arkon.Core;
using Arkon.Unity;
using WhiteGuard.Gameplay;
using UnityEngine;

namespace WhiteGuard.Presentation
{
    /// <summary>Original procedural score and Foley, driven by real shots/impacts and gameplay events.</summary>
    public sealed class CampaignAudio : MonoBehaviour
    {
        private CampaignGameLoop loop;
        private Func<bool> openingVisible;
        private AudioSource score;
        private readonly AudioSource[] voices = new AudioSource[10];
        private AudioClip shot, impact, glass, step, cue, scoreClip;
        private ProjectilePool2D boundPool;
        private int nextVoice;
        private float stepClock;
        private bool previousListenerPause;

        public void Configure(CampaignGameLoop game, Func<bool> isOpeningVisible)
        {
            loop = game; openingVisible = isOpeningVisible; previousListenerPause = AudioListener.pause;
            for (var i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false; voices[i].spatialBlend = 0; voices[i].volume = .38f;
            }
            shot = Percussion("Rifle report", .24f, 121, 47, .8f);
            impact = Percussion("Concrete impact", .09f, 1700, 29, .4f);
            glass = Percussion("Glass fragments", .37f, 6200, 331, .46f);
            step = Percussion("Footstep", .095f, 190, 701, .24f);
            cue = Percussion("Radio click", .075f, 1300, 31, .14f);
            scoreClip = Score(); score = gameObject.AddComponent<AudioSource>();
            score.clip = scoreClip; score.loop = true; score.playOnAwake = false; score.spatialBlend = 0; score.volume = .075f; score.Play();
            loop.Feedback += Feedback;
            loop.LevelCompleted += Completed;
        }
        private void Update()
        {
            if (!loop || !score) return;
            var target = openingVisible?.Invoke() == true ? 0 : loop.State == CampaignGameState.Playing ? .095f : .055f;
            score.volume = Mathf.MoveTowards(score.volume, target, Time.unscaledDeltaTime * .13f);
            var pool = loop.World ? loop.World.GetComponent<ProjectilePool2D>() : null;
            if (pool != boundPool)
            {
                if (boundPool) boundPool.ProjectileImpacted -= Impact;
                boundPool = pool;
                if (pool) pool.ProjectileImpacted += Impact;
            }
            if (!loop.Player || !loop.GameplayActive) { stepClock = 0; return; }
            var at = loop.Player.transform.position;
            var moving = loop.Player.Motor.Velocity.LengthSquared > 4;
            stepClock -= Time.deltaTime;
            if (moving && stepClock <= 0) { Play(step, .32f, UnityEngine.Random.Range(.9f, 1.1f), at); stepClock = .29f; }
        }
        private void Impact(ProjectileView value, ProjectileImpact hit)
        { Play(value.Appearance == ProjectileAppearance.ThrownItem ? glass : impact, .37f, 1, ArkonCoordinates.ToUnity(hit.Position)); }
        private void Feedback(string id, Vec2 at)
        {
            if (id == "player_shot") Play(shot, .65f, loop.Player && loop.Player.Weapons.CurrentId == "shotgun" ? .73f : .98f, ArkonCoordinates.ToUnity(at));
            else if (id == "enemy_shot" || id == "ally_shot") Play(shot, .48f, UnityEngine.Random.Range(.91f, 1.04f), ArkonCoordinates.ToUnity(at));
            else if (id == "pickup" || id == "rescue" || id == "objective" || id == "radio") Play(cue, .5f, 1, ArkonCoordinates.ToUnity(at));
            else if (id == "door" || id == "slam") Play(impact, .55f, .4f, ArkonCoordinates.ToUnity(at));
            else if (id == "player_hurt") Play(step, .8f, .57f, ArkonCoordinates.ToUnity(at));
            else if (id == "swing") Play(step, .35f, 1.65f, ArkonCoordinates.ToUnity(at));
        }
        private void Completed(CampaignLevel value) { Play(cue, .6f, .8f, Vector3.zero); }
        private void Play(AudioClip clip, float volume, float pitch, Vector3 at)
        {
            var source = voices[nextVoice++ % voices.Length]; source.Stop(); source.clip = clip;
            source.pitch = pitch;
            var offset = loop && loop.Player ? at.x - loop.Player.transform.position.x : 0;
            source.panStereo = Mathf.Clamp(offset * .09f, -.6f, .6f); source.volume = volume * .38f; source.Play();
        }
        private static AudioClip Percussion(string name, float duration, float frequency, uint seed, float level)
        {
            const int rate = 22050; var samples = new float[Mathf.CeilToInt(duration * rate)];
            var random = seed; var smooth = 0f;
            for (var i = 0; i < samples.Length; i++)
            {
                random = unchecked(random * 1664525 + 1013904223);
                var noise = ((random >> 8) / 8388607.5f - 1);
                smooth += (noise - smooth) * Mathf.Clamp01(frequency / rate * 3);
                var time = i / (float)rate; var envelope = Mathf.Exp(-time / (duration * .19f)) * Mathf.Min(1, time * 5000);
                var body = Mathf.Sin(2 * Mathf.PI * frequency * time * (1 - time * .35f));
                samples[i] = Mathf.Clamp((smooth * .8f + body * .2f) * envelope * level, -.9f, .9f);
            }
            var clip = AudioClip.Create(name, samples.Length, 1, rate, false); clip.SetData(samples, 0); return clip;
        }
        private static AudioClip Score()
        {
            const int rate = 22050; const double tempo = 72; var duration = 32 * 60 / tempo;
            var samples = new float[(int)(duration * rate)];
            var roots = new[] { 73.4162, 65.4064, 58.2705, 65.4064 }; uint seed = 47191;
            var smooth = 0.0;
            for (var i = 0; i < samples.Length; i++)
            {
                var time = i / (double)rate; var beat = time * tempo / 60; var bar = (int)(beat / 4);
                var root = roots[bar / 2 % roots.Length]; var pulse = beat % 1;
                var bass = Math.Sin(2 * Math.PI * root * time + .7 * Math.Sin(2 * Math.PI * root * 2 * time)) * Math.Exp(-pulse * 3.4);
                var pad = Math.Sin(2 * Math.PI * root * 2 * time) + .45 * Math.Sin(2 * Math.PI * root * 2.997 * time)
                    + .25 * Math.Sin(2 * Math.PI * root * 3.5636 * time);
                seed = unchecked(seed * 1664525 + 1013904223); var noise = (seed >> 8) / 8388607.5 - 1;
                smooth += (noise - smooth) * .025;
                var rhythm = Math.Exp(-(beat * 2 % 1) * 45) * noise * .075 + Math.Exp(-pulse * 19) * Math.Sin(2 * Math.PI * 47 * time) * .12;
                var loopEnvelope = Math.Min(1, Math.Min(time, duration - time) * 3);
                samples[i] = (float)((bass * .17 + pad * .075 + smooth * .08 + rhythm) * loopEnvelope);
            }
            var clip = AudioClip.Create("White Guard — night watch", samples.Length, 1, rate, false); clip.SetData(samples, 0); return clip;
        }
        private void OnApplicationFocus(bool focus) { AudioListener.pause = !focus; }
        private void OnDestroy()
        {
            if (loop) { loop.Feedback -= Feedback; loop.LevelCompleted -= Completed; }
            if (boundPool) boundPool.ProjectileImpacted -= Impact;
            foreach (var clip in new[] { shot, impact, glass, step, cue, scoreClip }) if (clip) Destroy(clip);
            AudioListener.pause = previousListenerPause;
        }
    }
}
