using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Arkon.Core;
using Arkon.Unity;
using WhiteGuard.Gameplay;
using UnityEngine;

namespace WhiteGuard.Presentation
{
    /// <summary>The player-facing campaign UI. It displays real loop state, inventory and story cards.</summary>
    public sealed class CampaignUI : MonoBehaviour
    {
        private CampaignGameLoop loop;
        private CampaignArt art;
        private Action<bool> opening;
        private Func<bool> openingVisible;
        private NarrativeCard[] cards;
        private Action narrativeComplete;
        private int cardIndex;
        private double cardSince, noticeUntil;
        private string notice = "", startupError = "";
        private bool settings, credits, narrativeMenu;
        private bool journal;
        private bool confirmNew;
        private bool levelSelect, resumeAfterLevelSelect;
        private Vector2 levelScroll;
        private string levelSelectError = "";
        private Vector2 journalScroll;
        private AudioSource narration;
        private bool voiceComplete, voiceStarted, voiceObserved, voicePaused;
        private int voiceStartFrame;
        private int shortcutFrame = -1;
        private StoryDirection direction;
        private float storySeconds;
        private GUIStyle title, heading, body, small, objective, button, caption, number;
        private readonly Color ink = new Color32(240, 231, 208, 255);
        private readonly Color gold = new Color32(195, 164, 101, 255);
        private readonly Color panel = new Color32(18, 25, 22, 242);

        public void Configure(CampaignGameLoop game, CampaignArt images, TextAsset source, Action<bool> showOpening, Func<bool> isOpeningVisible)
        {
            loop = game; art = images; opening = showOpening; openingVisible = isOpeningVisible;
            game.NarrativeRequested += Narrative;
            game.Notice += Notice;
            game.EntityCreated += Entity;
            game.StateChanged += StateChanged;
            AudioListener.volume = PlayerPrefs.GetFloat("WhiteGuard.MasterVolume", .8f);
            QualitySettings.vSyncCount = PlayerPrefs.GetInt("WhiteGuard.VSync", 1);
            narration = gameObject.AddComponent<AudioSource>(); narration.playOnAwake = false; narration.spatialBlend = 0;
            direction = new StoryDirection();
            var voiceFile = Resources.Load<TextAsset>("WhiteGuardCampaign/Voice/voice-manifest");
            var manifest = voiceFile ? JsonUtility.FromJson<VoiceManifest>(voiceFile.text) : null;
            if (manifest == null || !source || game.Campaign == null || manifest.schema != 1 || manifest.status != "complete"
                || manifest.campaignSHA256 != Hash(source.text) || manifest.cards == null)
                throw new InvalidOperationException("Озвучка не соответствует версии кампании.");
            var recordings = new Dictionary<string, VoiceCard>(StringComparer.Ordinal);
            foreach (var entry in manifest.cards)
            {
                if (entry == null || string.IsNullOrEmpty(entry.cardId) || recordings.ContainsKey(entry.cardId))
                    throw new InvalidOperationException("Повтор или пустая запись в списке озвучки.");
                recordings.Add(entry.cardId, entry);
            }
            var count = 0;
            foreach (var level in game.Campaign.levels)
                foreach (var sequence in new[] { level.before, level.after })
                    foreach (var card in sequence ?? Array.Empty<NarrativeCard>())
                    {
                        if (!recordings.TryGetValue(card.id, out var entry) || entry.textSHA256 != Hash(card.text ?? "")
                            || entry.resource != "WhiteGuardCampaign/Voice/" + card.id)
                            throw new InvalidOperationException("Не найдена соответствующая тексту запись: " + card.id);
                        count++;
                    }
            voiceComplete = count > 0 && count == manifest.totalCards && count == manifest.generatedCards && count == recordings.Count;
            if (!voiceComplete) throw new InvalidOperationException("Список озвучки кампании неполон.");
        }
        public void SetStartupError(string error) { startupError = error ?? ""; }
        private void Entity(CampaignEntity entity)
        {
            if (entity.Kind != "player" || !entity.Object) return;
            var actor = entity.Object.GetComponent<TopdownActor2D>();
            if (actor) actor.FireInputAllowedQuery = () => this && loop && loop.State == CampaignGameState.Playing && !levelSelect && !PointerOverPause();
        }
        private void StateChanged()
        {
            // Continue can finish loading while this modal is already open.
            // Gate the first playable frame, including AI, before returning to the loop.
            if (levelSelect && loop && loop.State == CampaignGameState.Playing)
            { resumeAfterLevelSelect = true; loop.Pause(true); }
        }
        private bool PointerOverPause()
        {
            var viewport = IntroStageGeometry.Viewport(Screen.width, Screen.height);
            if (viewport.Width <= 0) return true;
            var scale = viewport.Width / 1280;
            var x = (Input.mousePosition.x - viewport.X) / scale;
            var y = (Screen.height - Input.mousePosition.y - viewport.Y) / scale;
            return x >= 1156 && x <= 1256 && y >= 18 && y <= 62;
        }
        private void Narrative(NarrativeCard[] values, Action complete)
        {
            cards = values ?? Array.Empty<NarrativeCard>(); cardIndex = 0; narrativeComplete = complete;
            narrativeMenu = false; storySeconds = 0; cardSince = Time.realtimeSinceStartupAsDouble;
            StartVoice();
            if (cards.Length == 0) FinishNarrative();
        }
        private void Notice(string text) { notice = text ?? ""; noticeUntil = Time.realtimeSinceStartupAsDouble + Math.Max(4.5, notice.Length * .055); }
        private void Update()
        {
            if (!loop || openingVisible?.Invoke() == true) return;
            if (levelSelect)
            {
                if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.F8)) CloseLevelSelect();
                UpdateVoice(); return;
            }
            if (Input.GetKeyDown(KeyCode.F8)) { ShowLevelSelect(); UpdateVoice(); return; }
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                if (journal) { journal = false; loop.Pause(false); return; }
                if (confirmNew) { confirmNew = false; return; }
                if (settings || credits) { settings = credits = false; return; }
                if (loop.State == CampaignGameState.Playing) loop.Pause(true);
                else if (loop.State == CampaignGameState.Paused) loop.Pause(false);
                else if (loop.State == CampaignGameState.Narrative) narrativeMenu = !narrativeMenu;
            }
            if (Input.GetKeyDown(KeyCode.J) && (journal || loop.State == CampaignGameState.Playing))
            { journal = !journal; loop.Pause(journal); }
            if (loop.State == CampaignGameState.Narrative && !narrativeMenu && !settings && !credits &&
                (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return)))
            { shortcutFrame = Time.frameCount; NextCard(); UpdateVoice(); return; }
            if (loop.State == CampaignGameState.Failed && Input.GetKeyDown(KeyCode.R)) loop.Retry();
            if (loop.State == CampaignGameState.Debrief && Input.GetKeyDown(KeyCode.Return)) loop.Advance();
            UpdateVoice();
        }
        private void OnApplicationFocus(bool focused) { if (!focused && loop && loop.State == CampaignGameState.Playing) loop.Pause(true); }
        private void OnApplicationPause(bool paused) { if (paused && loop && loop.State == CampaignGameState.Playing) loop.Pause(true); }

        private void OnGUI()
        {
            if (openingVisible?.Invoke() == true || Screen.width <= 0 || Screen.height <= 0) return;
            // Update owns these story shortcuts; a focused IMGUI button must not consume them a second time.
            if ((shortcutFrame == Time.frameCount || (loop && loop.State == CampaignGameState.Narrative && !narrativeMenu && !settings && !credits && !levelSelect))
                && Event.current.isKey && (Event.current.keyCode == KeyCode.Space || Event.current.keyCode == KeyCode.Return))
                Event.current.Use();
            var previousMatrix = GUI.matrix; var previousColor = GUI.color; var previousEnabled = GUI.enabled;
            if (!loop || loop.State == CampaignGameState.Menu || loop.State == CampaignGameState.Loading ||
                loop.State == CampaignGameState.Narrative || loop.State == CampaignGameState.Finished)
            {
                GUI.matrix = Matrix4x4.identity;
                Solid(new Rect(0, 0, Screen.width, Screen.height), new Color32(18, 25, 22, 255));
            }
            var viewport = IntroStageGeometry.Viewport(Screen.width, Screen.height);
            GUI.matrix = Matrix4x4.TRS(new Vector3((float)viewport.X, (float)viewport.Y, 0), Quaternion.identity,
                new Vector3((float)viewport.Width / 1280, (float)viewport.Height / 720, 1));
            GUI.BeginGroup(new Rect(0, 0, 1280, 720));
            try
            {
                Styles(); GUI.color = Color.white;
                if (!string.IsNullOrEmpty(startupError)) { ErrorScreen(); return; }
                if (!loop || art == null) { Solid(new Rect(0, 0, 1280, 720), panel); GUI.Label(new Rect(80, 325, 1120, 50), "Подготовка…", heading); return; }
                GUI.enabled = previousEnabled && !settings && !credits && !journal && !confirmNew && !levelSelect;
                switch (loop.State)
                {
                    case CampaignGameState.Menu: Menu(); break;
                    case CampaignGameState.Loading: Loading(); break;
                    case CampaignGameState.Narrative: Story(); break;
                    case CampaignGameState.Playing: Hud(); break;
                    case CampaignGameState.Paused: Hud(); Pause(); break;
                    case CampaignGameState.Failed: Hud(); Failure(); break;
                    case CampaignGameState.Debrief: Debrief(); break;
                    case CampaignGameState.Finished: Ending(); break;
                }
                GUI.enabled = previousEnabled;
                if (levelSelect) LevelSelect(); else if (settings) Settings(); else if (credits) Credits(); else if (journal) Journal(); else if (confirmNew) ConfirmNew();
                if (loop.State == CampaignGameState.Playing && !settings && !credits && !journal && !levelSelect
                    && Time.realtimeSinceStartupAsDouble < noticeUntil && !string.IsNullOrEmpty(notice))
                {
                    var height = Mathf.Clamp(small.CalcHeight(new GUIContent(notice), 678) + 25, 58, 180);
                    Solid(new Rect(280, 620 - height, 720, height), panel);
                    GUI.Label(new Rect(301, 631 - height, 678, height - 20), notice, small);
                }
            }
            finally { GUI.EndGroup(); GUI.matrix = previousMatrix; GUI.color = previousColor; GUI.enabled = previousEnabled; }
        }
        private void Styles()
        {
            if (title != null) return;
            var display = art?.Display; var font = art?.Body;
            title = new GUIStyle(GUI.skin.label) { font = display, fontSize = 83, richText = false };
            title.normal.textColor = ink;
            heading = new GUIStyle(title) { fontSize = 38, wordWrap = true };
            body = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18, wordWrap = true, richText = false };
            body.normal.textColor = ink;
            small = new GUIStyle(body) { fontSize = 14 };
            objective = new GUIStyle(body) { fontSize = 20 };
            button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 18, alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(18, 14, 8, 8), border = new RectOffset(0, 0, 0, 0) };
            button.normal.background = Texture2D.whiteTexture; button.hover.background = Texture2D.whiteTexture;
            button.active.background = Texture2D.whiteTexture;
            button.normal.textColor = ink; button.hover.textColor = Color.white; button.active.textColor = ink;
            caption = new GUIStyle(body) { fontSize = 23, wordWrap = true };
            number = new GUIStyle(heading) { fontSize = 29, alignment = TextAnchor.MiddleRight };
        }
        private bool Button(Rect bounds, string text, bool enabled = true)
        {
            var old = GUI.enabled; var background = GUI.backgroundColor;
            GUI.enabled = old && enabled; GUI.backgroundColor = new Color(.16f, .21f, .18f, .96f);
            var pressed = GUI.Button(bounds, text, button);
            GUI.backgroundColor = background; GUI.enabled = old; return pressed;
        }
        public static void Solid(Rect bounds, Color color)
        { var before = GUI.color; GUI.color = color; GUI.DrawTexture(bounds, Texture2D.whiteTexture); GUI.color = before; }
        private void Picture(string resource, int quadrant, Color tint, float[] view = null)
        {
            var image = art?.Story(resource); if (!image) { Solid(new Rect(0, 0, 1280, 720), new Color32(33, 39, 31, 255)); return; }
            var x = 0f; var y = 0f; var width = (float)image.width; var height = (float)image.height;
            if (quadrant >= 0 && quadrant < 4)
            { width *= .5f; height *= .5f; x = quadrant % 2 * width; y = quadrant / 2 * height; }
            if (view != null && view.Length == 4)
            { x += view[0] * width; y += view[1] * height; width *= view[2]; height *= view[3]; }
            var cropHeight = width * 720 / 1280;
            if (cropHeight <= height) { y += (height - cropHeight) * .5f; height = cropHeight; }
            else { var next = height * 1280 / 720; x += (width - next) * .5f; width = next; }
            var color = GUI.color; GUI.color = tint;
            GUI.DrawTextureWithTexCoords(new Rect(0, 0, 1280, 720), image,
                new Rect(x / image.width, 1 - (y + height) / image.height, width / image.width, height / image.height), true);
            GUI.color = color;
        }
        private void Menu()
        {
            Picture("WhiteGuardArt/Story/story-atlas-a", 0, new Color(.53f, .53f, .48f, 1));
            Solid(new Rect(0, 0, 620, 720), new Color(.035f, .06f, .047f, .86f));
            GUI.Label(new Rect(70, 66, 550, 32), "АЛЕКСАНДР БУШКОВ  /  ПИРАНЬЯ", small);
            GUI.Label(new Rect(65, 118, 580, 190), "БЕЛАЯ\nГВАРДИЯ", title);
            Solid(new Rect(72, 328, 100, 3), gold);
            GUI.Label(new Rect(72, 348, 495, 55), "Чужая страна. Чужой приказ. Свои люди.", body);
            var y = 424f;
            if (loop.SaveAvailable) { if (Button(new Rect(72, y, 385, 42), "Продолжить")) loop.ContinueSaved(); y += 49; }
            if (Button(new Rect(72, y, 385, 42), "Новая игра")) { if (loop.SaveAvailable) confirmNew = true; else opening?.Invoke(true); } y += 49;
            if (Button(new Rect(72, y, 385, 42), "Выбор уровня  ·  F8")) ShowLevelSelect(); y += 49;
            if (Button(new Rect(72, y, 185, 42), "Вступление")) opening?.Invoke(false);
            if (Button(new Rect(268, y, 189, 42), "Настройки")) settings = true; y += 49;
            if (Button(new Rect(72, y, 185, 42), "Авторы")) credits = true;
            if (Button(new Rect(268, y, 189, 42), "Выход")) Application.Quit();
            if (!string.IsNullOrEmpty(loop.LastError))
            {
                Solid(new Rect(650, 507, 550, 130), panel);
                GUI.Label(new Rect(671, 523, 507, 105), "Сохранение не удалось открыть или записать. Проверьте свободное место. Можно начать новую игру.", body);
            }
            GUI.Label(new Rect(72, 684, 780, 24), "WASD — движение   ·   Мышь — прицел   ·   E — действие", small);
        }
        private void Loading()
        {
            Solid(new Rect(0, 0, 1280, 720), panel);
            GUI.Label(new Rect(80, 254, 1120, 60), loop.Level?.title ?? "Белая гвардия", heading);
            GUI.Label(new Rect(82, 326, 1116, 34), "Подготовка маршрута…", body);
            Solid(new Rect(82, 393, 1116, 3), new Color(.19f, .24f, .2f, 1));
            Solid(new Rect(82, 393, 1116 * Mathf.Clamp01(loop.LoadingProgress), 3), gold);
            if (Button(new Rect(82, 449, 240, 42), "В меню")) loop.ReturnToMenu();
        }
        public void ShowLevelSelect()
        {
            if (!loop || loop.Campaign == null || !string.IsNullOrEmpty(startupError) || levelSelect) return;
            resumeAfterLevelSelect = loop.State == CampaignGameState.Playing;
            if (resumeAfterLevelSelect) loop.Pause(true);
            settings = credits = journal = confirmNew = false;
            levelSelect = true; levelSelectError = "";
            if (narration && voiceStarted && !voicePaused) { narration.Pause(); voicePaused = true; }
        }
        private void CloseLevelSelect()
        {
            levelSelect = false;
            if (resumeAfterLevelSelect && loop && loop.State == CampaignGameState.Paused) loop.Pause(false);
            resumeAfterLevelSelect = false;
        }
        private void LevelSelect()
        {
            Shade(); Solid(new Rect(220, 42, 840, 638), panel);
            GUI.Label(new Rect(255, 68, 770, 60), "Выбор уровня", heading);
            GUI.Label(new Rect(257, 132, 766, 48), string.IsNullOrEmpty(levelSelectError)
                ? "Режим отладки. Глава начнётся заново.\nПрогресс основной кампании сохранится." : levelSelectError, small);
            var levels = loop.Campaign.levels;
            levelScroll = GUI.BeginScrollView(new Rect(250, 192, 780, 392), levelScroll,
                new Rect(0, 0, 752, Mathf.Max(392, levels.Length * 46)));
            string chosen = null;
            for (var i = 0; i < levels.Length; i++)
                if (Button(new Rect(5, i * 46, 730, 41), (i + 1).ToString("00") + "   " + levels[i].title)) chosen = levels[i].id;
            GUI.EndScrollView();
            if (Button(new Rect(255, 615, 770, 43), "Назад  ·  F8 / Esc")) CloseLevelSelect();
            if (chosen != null)
            {
                if (!loop.StartDebugLevel(chosen))
                { levelSelectError = "Не удалось сохранить текущую игру. Проверьте свободное место и попробуйте ещё раз."; return; }
                levelSelect = resumeAfterLevelSelect = false;
                cards = null; narrativeComplete = null; narrativeMenu = false; notice = ""; StopVoice();
            }
        }
        private void Hud()
        {
            if (loop.Level == null || !loop.Player) return;
            Solid(new Rect(22, 18, 750, 101), new Color(.035f, .06f, .047f, .88f));
            GUI.Label(new Rect(39, 29, 680, 25), (loop.Level.chapter + "  /  " + loop.Level.title).ToUpperInvariant(), small);
            var text = loop.Progress != null && loop.Progress.AllObjectivesComplete ? "К месту выхода" : loop.Progress?.ActiveText;
            GUI.Label(new Rect(39, 59, 710, 55), text ?? "", objective);
            if (Button(new Rect(1160, 18, 96, 42), "Пауза")) loop.Pause(true);
            if (loop.IsDebugSession)
                GUI.Label(new Rect(970, 75, 286, 28), "ОТЛАДКА  ·  F8 — УРОВНИ", small);
            var health = loop.Player.GetComponent<DamageReceiver2D>().Health;
            Solid(new Rect(22, 631, 323, 65), panel);
            GUI.Label(new Rect(39, 640, 196, 24), "КИРИЛЛ МАЗУР", small);
            Bar(new Rect(39, 674, 208, 5), health.Current / (float)Math.Max(1, health.Maximum), health.Current > 30 ? gold : new Color(.75f, .25f, .18f, 1));
            GUI.Label(new Rect(250, 638, 78, 40), health.Current.ToString(), number);
            var weapon = loop.Player.Weapons;
            Solid(new Rect(961, 631, 295, 65), panel);
            var weaponName = weapon.CurrentId == "pistol" ? "СЛУЖЕБНОЕ ОРУЖИЕ" : weapon.CurrentId == "shotgun" ? "ДРОБОВИК" : "РУКОПАШНАЯ";
            if (loop.Level.combat)
            {
                GUI.Label(new Rect(978, 640, 247, 24), weaponName, small);
                GUI.Label(new Rect(1160, 663, 76, 27), weapon.CurrentDefinition.ConsumesAmmo ? (weapon.InfiniteAmmo ? "∞" : weapon.Ammo.ToString()) : "—", number);
                GUI.Label(new Rect(978, 673, 186, 20), "1 / 2 / 3   ·   Q — бросок", small);
            }
            else
            {
                GUI.Label(new Rect(978, 640, 247, 24), "ОРУЖИЕ УБРАНО", small);
                GUI.Label(new Rect(978, 670, 247, 24), "E — действие  ·  J — заметки", small);
            }
            var actorY = 138f;
            foreach (var actor in loop.Actors)
            {
                if (!actor || !actor.Definition.required || actor.Definition.role == "passenger") continue;
                Solid(new Rect(22, actorY, 227, 45), new Color(.035f, .06f, .047f, .8f));
                GUI.Label(new Rect(34, actorY + 5, 202, 24), actor.Definition.name, small);
                Bar(new Rect(34, actorY + 33, 201, 3), actor.Health.Current / (float)Math.Max(1, actor.Health.Maximum), gold);
                actorY += 52;
            }
            var hint = loop.InteractionHint;
            if (string.IsNullOrEmpty(hint) && loop.World)
            {
                switch (loop.World.QueryAction(loop.Player).Kind)
                {
                    case TopdownActionKind.Door: hint = "Открыть дверь"; break;
                    case TopdownActionKind.Pickup: hint = "Подобрать оружие или предмет"; break;
                    case TopdownActionKind.Finish: hint = "Добить противника"; break;
                }
            }
            if (!string.IsNullOrEmpty(hint) && loop.State == CampaignGameState.Playing)
            {
                if (Time.realtimeSinceStartupAsDouble >= noticeUntil || string.IsNullOrEmpty(notice))
                {
                    Solid(new Rect(330, 556, 620, 48), panel);
                    GUI.Label(new Rect(349, 567, 580, 31), "E  ·  " + hint, body);
                }
            }
            if (loop.State == CampaignGameState.Playing) ObjectiveBeacon();
        }
        private static void Bar(Rect area, float value, Color color)
        { Solid(area, new Color(.25f, .29f, .25f, 1)); area.width *= Mathf.Clamp01(value); Solid(area, color); }

        private void ObjectiveBeacon()
        {
            if (loop.Level == null || loop.Progress == null || !loop.Player || !loop.Player.AimCamera) return;
            var found = false; var target = Vector2.zero;
            if (loop.Progress.AllObjectivesComplete && loop.Level.exit != null)
            { target = Center(loop.Level.exit); found = true; }
            else foreach (var goal in loop.Level.objectives ?? Array.Empty<CampaignObjective>())
            {
                if (loop.Progress.IsCompleted(goal.id) || !loop.Progress.PrerequisitesMet(goal.requires) || goal.type == "protect") continue;
                if (goal.type == "interact")
                {
                    foreach (var interaction in loop.Level.interactions ?? Array.Empty<CampaignInteraction>())
                        if (interaction.id == goal.target) { target = new Vector2(interaction.x, interaction.y); found = true; break; }
                }
                else if (goal.type == "reach" || goal.type == "escort" || goal.type == "survive")
                {
                    var regionId = !string.IsNullOrEmpty(goal.region) ? goal.region : goal.type == "reach" ? goal.target : "";
                    foreach (var region in loop.Level.regions ?? Array.Empty<CampaignRect>())
                        if (region.id == regionId) { target = Center(region); found = true; break; }
                    if (!found && loop.Level.exit != null && loop.Level.exit.id == regionId)
                    { target = Center(loop.Level.exit); found = true; }
                    if (!found && goal.type == "escort")
                    {
                        var actor = loop.FindActor(goal.target);
                        var route = actor ? actor.Definition.route : null;
                        if (route != null && route.Length > 0) { target = new Vector2(route[route.Length - 1].x, route[route.Length - 1].y); found = true; }
                    }
                }
                break; // No hidden enemy radar: a clear objective uses the readable task text.
            }
            if (!found || Vec2.Distance(loop.Player.Position, new Vec2(target.x, target.y)) < 28) return;
            var screen = loop.Player.AimCamera.WorldToScreenPoint(ArkonCoordinates.ToUnity(new Vec2(target.x, target.y)));
            var viewport = IntroStageGeometry.Viewport(Screen.width, Screen.height); var scale = viewport.Width / 1280;
            if (scale <= 0) return;
            var x = Mathf.Clamp((float)((screen.x - viewport.X) / scale), 275, 1210);
            var y = Mathf.Clamp((float)((Screen.height - screen.y - viewport.Y) / scale), 148, 516);
            var color = new Color(gold.r, gold.g, gold.b, .65f + Mathf.Sin(Time.time * 3) * .2f);
            Solid(new Rect(x - 10, y - 10, 7, 2), color); Solid(new Rect(x - 10, y - 10, 2, 7), color);
            Solid(new Rect(x + 3, y + 8, 7, 2), color); Solid(new Rect(x + 8, y + 3, 2, 7), color);
            GUI.Label(new Rect(x - 30, y + 15, 90, 22), loop.Progress.AllObjectivesComplete ? "ВЫХОД" : "ЦЕЛЬ", small);
        }
        private static Vector2 Center(CampaignRect area) => new Vector2(area.x + area.w * .5f, area.y + area.h * .5f);

        private void Story()
        {
            if (cards == null || cardIndex >= cards.Length) { Solid(new Rect(0, 0, 1280, 720), panel); return; }
            var card = cards[cardIndex];
            var shot = direction?.At(card.id, storySeconds);
            Picture(!string.IsNullOrEmpty(shot?.art) ? shot.art : card.art,
                !string.IsNullOrEmpty(shot?.art) ? shot.quadrant : card.quadrant, Color.white, shot?.view);
            if (!string.IsNullOrEmpty(shot?.insetArt)) StoryInset(shot);
            var textHeight = caption.CalcHeight(new GUIContent(card.text ?? ""), 1090);
            var panelHeight = Mathf.Clamp(textHeight + 124, 190, 330);
            Solid(new Rect(0, 720 - panelHeight, 1280, panelHeight), new Color(.035f, .055f, .04f, .96f));
            GUI.Label(new Rect(70, 735 - panelHeight, 1040, 28), (card.speaker ?? "Мазур").ToUpperInvariant(), small);
            GUI.Label(new Rect(70, 772 - panelHeight, 1090, textHeight + 8), card.text ?? "", caption);
            GUI.Label(new Rect(70, 24, 1120, 35), loop.Level?.title?.ToUpperInvariant() ?? "", small);
            GUI.Label(new Rect(70, 681, 320, 25), (cardIndex + 1) + " / " + cards.Length, small);
            if (Button(new Rect(952, 661, 258, 40), cardIndex + 1 == cards.Length ? "Продолжить  →" : "Дальше  →", CanAdvanceCard() && !narrativeMenu)) NextCard();
            if (Button(new Rect(1016, 20, 195, 35), "Пропустить сцену", !narrativeMenu)) FinishNarrative();
            if (narrativeMenu)
            {
                Shade(); GUI.Label(new Rect(405, 238, 560, 70), "Пауза", heading);
                if (Button(new Rect(405, 333, 470, 48), "Продолжить")) narrativeMenu = false;
                if (Button(new Rect(405, 391, 470, 48), "Выбор уровня  ·  F8")) ShowLevelSelect();
                if (Button(new Rect(405, 449, 470, 48), "Настройки")) settings = true;
                if (Button(new Rect(405, 507, 470, 48), "В меню")) { narrativeMenu = false; cards = null; narrativeComplete = null; StopVoice(); loop.ReturnToMenu(); }
            }
        }
        private bool CanAdvanceCard() => cards != null && cardIndex < cards.Length && Time.realtimeSinceStartupAsDouble - cardSince >= .35;
        private void NextCard()
        {
            if (!CanAdvanceCard()) return;
            cardIndex++; storySeconds = 0; cardSince = Time.realtimeSinceStartupAsDouble;
            if (cardIndex >= cards.Length) FinishNarrative();
            else StartVoice();
        }
        private void FinishNarrative()
        {
            var done = narrativeComplete; narrativeComplete = null; cards = null; narrativeMenu = false; StopVoice(); done?.Invoke();
        }
        private void Shade() => Solid(new Rect(0, 0, 1280, 720), new Color(0, .02f, .01f, .78f));
        private void Pause()
        {
            Shade(); GUI.Label(new Rect(405, 170, 600, 65), "Пауза", heading);
            if (Button(new Rect(405, 265, 470, 48), "Продолжить")) loop.Pause(false);
            if (Button(new Rect(405, 323, 470, 48), "С контрольной точки")) loop.Retry();
            if (Button(new Rect(405, 381, 470, 48), "Выбор уровня  ·  F8")) ShowLevelSelect();
            if (Button(new Rect(405, 439, 470, 48), "Настройки")) settings = true;
            if (Button(new Rect(405, 497, 470, 48), "Сохранить и выйти в меню")) loop.ReturnToMenu();
        }
        private void Failure()
        {
            Shade(); GUI.Label(new Rect(250, 204, 900, 66), "ПОПЫТКА НЕ УДАЛАСЬ", heading);
            GUI.Label(new Rect(253, 289, 774, 90), loop.FailureReason, body);
            if (Button(new Rect(253, 411, 482, 49), "R  ·  С контрольной точки")) loop.Retry();
            if (Button(new Rect(253, 470, 482, 49), "Начать главу заново")) loop.RestartLevel();
            if (Button(new Rect(253, 529, 482, 49), "В меню")) loop.ReturnToMenu();
        }
        private void Debrief()
        {
            Shade(); GUI.Label(new Rect(250, 212, 1000, 65), loop.Level?.title ?? "", heading);
            GUI.Label(new Rect(253, 307, 774, 58), "Задача выполнена. Этот участок пути пройден.", body);
            if (Button(new Rect(253, 414, 580, 50), "Продолжить  →")) loop.Advance();
            if (Button(new Rect(253, 474, 580, 45), "В меню")) loop.ReturnToMenu();
        }
        private void Ending()
        {
            Picture("WhiteGuardArt/Story/story-atlas-c", 0, new Color(.55f, .55f, .52f, 1)); Shade();
            GUI.Label(new Rect(180, 146, 1000, 70), "ЗА ВОРОТАМИ", heading);
            GUI.Label(new Rect(183, 247, 850, 105), "Приказ закончился. Люди остались живы.\nКое-что после этой ночи так и осталось за кадром.", caption);
            GUI.Label(new Rect(183, 381, 860, 35), "История Кирилла Мазура завершена.", body);
            if (Button(new Rect(183, 469, 420, 46), "Авторы")) credits = true;
            if (Button(new Rect(183, 525, 420, 46), "В главное меню")) loop.ReturnToMenu();
        }
        private void Settings()
        {
            Shade(); Solid(new Rect(350, 140, 580, 460), panel);
            GUI.Label(new Rect(385, 175, 510, 60), "Настройки", heading);
            GUI.Label(new Rect(385, 261, 510, 30), "Громкость", body);
            var volume = GUI.HorizontalSlider(new Rect(385, 313, 510, 28), AudioListener.volume, 0, 1);
            if (Math.Abs(volume - AudioListener.volume) > .001f) { AudioListener.volume = volume; PlayerPrefs.SetFloat("WhiteGuard.MasterVolume", volume); }
            var vsync = GUI.Toggle(new Rect(385, 362, 510, 32), QualitySettings.vSyncCount > 0, " Вертикальная синхронизация", body);
            if (vsync != (QualitySettings.vSyncCount > 0)) { QualitySettings.vSyncCount = vsync ? 1 : 0; PlayerPrefs.SetInt("WhiteGuard.VSync", vsync ? 1 : 0); }
            GUI.Label(new Rect(385, 421, 510, 82), "WASD — движение · Shift — бег\nЛКМ — огонь · E — действие · Q — бросок\n1/2/3 — оружие · Esc — пауза\nF8 — выбор уровня", small);
            if (Button(new Rect(385, 525, 510, 44), "Назад")) { settings = false; PlayerPrefs.Save(); }
        }
        private void ConfirmNew()
        {
            Shade(); Solid(new Rect(305, 205, 670, 310), panel);
            GUI.Label(new Rect(340, 236, 600, 61), "Новая история", heading);
            GUI.Label(new Rect(342, 314, 596, 76), "Начать заново? Текущая контрольная точка будет заменена.", body);
            if (Button(new Rect(342, 421, 285, 47), "Начать заново")) { confirmNew = false; opening?.Invoke(true); }
            if (Button(new Rect(644, 421, 294, 47), "Отмена")) confirmNew = false;
        }
        private void Journal()
        {
            Shade(); Solid(new Rect(190, 80, 900, 570), panel);
            GUI.Label(new Rect(225, 108, 830, 58), "Заметки Мазура", heading);
            GUI.Label(new Rect(227, 176, 826, 28), loop.Level?.title ?? "", small);
            var total = 25f; var count = 0;
            foreach (var note in loop.Level?.interactions ?? Array.Empty<CampaignInteraction>())
                if (loop.HasInteracted(note.id) && !string.IsNullOrWhiteSpace(note.body))
                { total += body.CalcHeight(new GUIContent(note.body), 780) + 66; count++; }
            journalScroll = GUI.BeginScrollView(new Rect(225, 223, 830, 330), journalScroll, new Rect(0, 0, 800, Mathf.Max(total, 325)));
            var y = 8f;
            if (count == 0) GUI.Label(new Rect(5, y, 780, 100), "Здесь останутся наблюдения и важные детали этого участка пути.", body);
            foreach (var note in loop.Level?.interactions ?? Array.Empty<CampaignInteraction>())
            {
                if (!loop.HasInteracted(note.id) || string.IsNullOrWhiteSpace(note.body)) continue;
                GUI.Label(new Rect(5, y, 780, 28), note.text, small); y += 31;
                var height = body.CalcHeight(new GUIContent(note.body), 780);
                GUI.Label(new Rect(5, y, 780, height + 5), note.body, body); y += height + 35;
            }
            GUI.EndScrollView();
            if (Button(new Rect(225, 581, 830, 43), "Вернуться  ·  J")) { journal = false; loop.Pause(false); }
        }
        private void Credits()
        {
            Shade(); Solid(new Rect(260, 115, 760, 515), panel);
            GUI.Label(new Rect(300, 153, 680, 60), "Белая гвардия", heading);
            GUI.Label(new Rect(302, 247, 676, 260),
                "По роману Александра Бушкова\nЦикл «Пиранья»\n\nИгровая постановка — история Кирилла Мазура.\nОснова — наш движок и материалы Acorn Hunter.\nОригинальное вступление, новые рисунки и игровые ассеты.\n\nСпасибо за прохождение.", body);
            if (Button(new Rect(302, 550, 676, 44), "Назад")) credits = false;
        }
        private void ErrorScreen()
        {
            Solid(new Rect(0, 0, 1280, 720), panel);
            GUI.Label(new Rect(80, 230, 1120, 60), "Не удалось открыть игру", heading);
            GUI.Label(new Rect(82, 329, 1116, 180), startupError, body);
        }
        private void OnDestroy()
        {
            StopVoice();
            if (!loop) return;
            loop.NarrativeRequested -= Narrative; loop.Notice -= Notice; loop.EntityCreated -= Entity; loop.StateChanged -= StateChanged;
        }

        private void StartVoice()
        {
            StopVoice();
            if (!voiceComplete || !narration || cards == null || cardIndex >= cards.Length) return;
            narration.clip = Resources.Load<AudioClip>("WhiteGuardCampaign/Voice/" + cards[cardIndex].id);
            if (narration.clip) narration.clip.LoadAudioData();
        }
        private void StopVoice()
        {
            if (narration) { narration.Stop(); narration.clip = null; }
            voiceStarted = voiceObserved = voicePaused = false;
        }
        private void UpdateVoice()
        {
            if (!loop || loop.State != CampaignGameState.Narrative || cards == null || cardIndex >= cards.Length) { StopVoice(); return; }
            var shouldPause = narrativeMenu || levelSelect || settings || credits || AudioListener.pause || Time.timeScale <= 0;
            if (shouldPause)
            {
                if (voiceStarted && !voicePaused) { narration.Pause(); voicePaused = true; }
                return;
            }
            storySeconds += Time.unscaledDeltaTime;
            if (!narration || !narration.clip) return;
            if (!voiceStarted)
            {
                if (narration.clip.loadState != AudioDataLoadState.Loaded) return;
                narration.Play(); voiceStarted = true; voiceStartFrame = Time.frameCount;
            }
            else if (voicePaused) { narration.UnPause(); voicePaused = false; }
            if (narration.isPlaying)
            {
                voiceObserved = true;
                storySeconds = narration.timeSamples / (float)Math.Max(1, narration.clip.frequency);
            }
            else if (voiceObserved && Time.frameCount > voiceStartFrame + 2
                && Time.realtimeSinceStartupAsDouble - cardSince >= Mathf.Max(.9f, cards[cardIndex].hold)) NextCard();
        }
        [Serializable] private sealed class VoiceManifest
        { public int schema = 0, totalCards = 0, generatedCards = 0; public string status = "", campaignSHA256 = ""; public VoiceCard[] cards = null; }
        [Serializable] private sealed class VoiceCard
        { public string cardId = "", textSHA256 = "", resource = ""; }
        private static string Hash(string text)
        {
            using var hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", "").ToLowerInvariant();
        }

        private void StoryInset(StoryBeat beat)
        {
            var region = art.Region(beat.insetArt); var texture = art.Texture(beat.insetArt);
            if (region == null || !texture || beat.inset == null || beat.inset.Length != 4) return;
            var box = new Rect(beat.inset[0] * 1280, beat.inset[1] * 720, beat.inset[2] * 1280, beat.inset[3] * 720);
            Solid(new Rect(box.x - 5, box.y - 5, box.width + 10, box.height + 10), new Color32(21, 28, 23, 255));
            Solid(box, new Color32(215, 204, 174, 255));
            var available = new Rect(box.x + 12, box.y + 12, box.width - 24, box.height - 24);
            var scale = Mathf.Min(available.width / region.w, available.height / region.h);
            var image = new Rect(available.center.x - region.w * scale / 2, available.center.y - region.h * scale / 2, region.w * scale, region.h * scale);
            GUI.DrawTextureWithTexCoords(image, texture, new Rect(region.x / (float)texture.width,
                1 - (region.y + region.h) / (float)texture.height, region.w / (float)texture.width, region.h / (float)texture.height), true);
        }
    }
}
