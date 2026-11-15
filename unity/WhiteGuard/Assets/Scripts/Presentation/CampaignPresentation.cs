using System;
using System.Collections.Generic;
using Arkon.Core;
using Arkon.Unity;
using WhiteGuard.Gameplay;
using UnityEngine;

namespace WhiteGuard.Presentation
{
    /// <summary>Draws actual authored scene objects and native projectile leases with the game art.</summary>
    public sealed class CampaignPresentation : MonoBehaviour
    {
        private CampaignGameLoop loop;
        private CampaignArt art;
        private readonly List<ActorVisual> actors = new List<ActorVisual>();
        private readonly List<ProjectileView> projectileViews = new List<ProjectileView>(64);
        private readonly Dictionary<long, LineRenderer> lines = new Dictionary<long, LineRenderer>();
        private readonly Queue<LineRenderer> availableLines = new Queue<LineRenderer>();
        private readonly HashSet<long> liveTokens = new HashSet<long>();
        private readonly List<long> retired = new List<long>();
        private Material lineMaterial;

        public void Configure(CampaignGameLoop game, CampaignArt images)
        { loop = game; art = images; game.EntityCreated += Add; lineMaterial = new Material(Shader.Find("Sprites/Default")); }

        private SpriteRenderer Image(Transform parent, string id, int order, Vector2 size, bool tiled = false)
        {
            var value = new GameObject("Image " + id); value.transform.SetParent(parent, false);
            var renderer = value.AddComponent<SpriteRenderer>();
            renderer.sprite = art.Sprite(id, tiled ? 400 : 100); renderer.sortingOrder = order;
            if (!renderer.sprite) { Debug.LogError("Missing campaign sprite: " + id, this); return renderer; }
            if (tiled) { renderer.drawMode = SpriteDrawMode.Tiled; renderer.size = size; }
            else
            {
                var bounds = renderer.sprite.bounds.size;
                var scale = Mathf.Min(size.x / Mathf.Max(.001f, bounds.x), size.y / Mathf.Max(.001f, bounds.y));
                renderer.transform.localScale = new Vector3(scale, scale, 1);
            }
            return renderer;
        }

        private static string MaterialId(string style)
        {
            switch (style ?? "")
            {
                case "asphalt": return "road";
                case "courtyard": case "sidewalk": return "pavement";
                case "forest": case "jungle_floor": return "jungle";
                case "earth": return "dirt";
                case "mud": return "dirt";
                case "tile": case "ceramic": return "tiles";
                case "timber": return "wood";
                case "boundary": return "plaster";
                default: return style;
            }
        }
        private string Existing(string id, string fallback) => art.Contains(id) ? id : fallback;
        private string Surface(string style, string fallback)
        {
            var id = "material_" + MaterialId(style);
            return art.Contains(id) ? id : "material_" + fallback;
        }
        private void Add(CampaignEntity entity)
        {
            if (!entity.Object || entity.Kind == "passenger") return;
            var root = entity.Object.transform;
            var size = new Vector2(entity.Bounds.w, entity.Bounds.h) / ArkonCoordinates.PixelsPerUnit;
            if (entity.Kind == "floor" || entity.Kind == "wall")
            {
                var material = art.ResolveStyle(entity.Kind == "floor" ? "floors" : "walls", entity.Style,
                    Surface(entity.Style, entity.Kind == "floor" ? "concrete" : "plaster"));
                Image(root, material, entity.Kind == "floor" ? -200 : 1200 + Mathf.RoundToInt(entity.Bounds.y + entity.Bounds.h), size, true);
                return;
            }
            if (entity.Kind == "door")
            {
                var door = entity.Object.GetComponent<DoorPort2D>();
                if (door && door.LeafPivot)
                {
                    var image = Image(door.LeafPivot, "material_wood", 1200 + Mathf.RoundToInt(entity.Bounds.y), new Vector2(.32f, .075f), true);
                    image.transform.localPosition = new Vector3(.16f, 0, 0);
                    image.color = new Color(.63f, .53f, .35f, 1);
                }
                return;
            }
            if (entity.Kind == "player" || entity.Kind == "enemy" || entity.Kind == "actor" || entity.Kind == "vehicle")
            {
                var identity = ActorIdentity(entity);
                var actorRoot = new GameObject("Visual"); actorRoot.transform.SetParent(root, false);
                var first = identity + (art.Contains(identity + "_idle") ? "_idle" : "");
                var visualSize = size * (entity.Kind == "vehicle" ? 1 : identity == "schoolgirl" ? 1.05f : 1.4f);
                var renderer = Image(actorRoot.transform, first, 1300, visualSize);
                var shadow = Image(actorRoot.transform, first, 1299, visualSize * 1.04f);
                shadow.color = new Color(0, 0, 0, .28f); shadow.transform.localPosition = new Vector3(.025f, -.035f, 0);
                var actor = new ActorVisual { Root = root, Pivot = actorRoot.transform, Image = renderer, Shadow = shadow, Identity = identity,
                    Player = entity.Object.GetComponent<TopdownActor2D>(), Enemy = entity.Object.GetComponent<EnemyPort2D>(), Npc = entity.Object.GetComponent<CampaignNpc>(), Size = visualSize, LastPosition = root.position,
                    StepPhase = Mathf.Abs(entity.Object.GetInstanceID() % 97) * .019f };
                if (actor.Player) actor.Player.VisualPivot = actor.Pivot;
                if (actor.Enemy) actor.Enemy.VisualPivot = actor.Pivot;
                if (actor.Npc) actor.Npc.VisualPivot = actor.Pivot;
                actors.Add(actor); return;
            }
            if (entity.Kind == "prop" || entity.Kind == "pickup")
            {
                var key = art.ResolveStyle("props", entity.Style, PropIdentity(entity.Style));
                Image(root, Existing(key, "crate"), 1100 + Mathf.RoundToInt(entity.Bounds.y + entity.Bounds.h * .5f), size);
            }
        }

        private string ActorIdentity(CampaignEntity entity)
        {
            if (entity.Kind == "player") return "mazur";
            if (entity.Kind == "vehicle") return entity.Style == "mercedes" || entity.Style == "president" ? "president_car" : Existing(entity.Style, "bus");
            var style = entity.Style ?? "";
            if (art.Contains(style + "_idle")) return style;
            if (style.Contains("natal"))
            {
                var npc = entity.Object.GetComponent<CampaignNpc>();
                return npc && npc.Definition.role == "civilian" ? "natalie_formal" : "natalie";
            }
            if (style.Contains("tatian")) return "tatiana";
            if (style.Contains("lavrik")) return "lavrik";
            if (style.Contains("leon")) return "leon";
            if (style.Contains("prince") || style.Contains("akinf")) return "prince";
            if (style.Contains("irina")) return "irina";
            if (entity.Kind == "enemy") return style.Contains("contract") ? "contractor" : style.Contains("guard") ? "guard" : "militia";
            return "civilian_female";
        }
        private static string PropIdentity(string style)
        {
            switch (style ?? "")
            {
                case "pistol": case "shotgun": case "bat": case "bottle": return "weapons";
                case "table": return "desk";
                case "car": case "mercedes": case "president": return "president_car";
                case "plant": return "fern_pot";
                case "tree": return "palm";
                case "bush": return "shrub";
                case "generator": return "radio";
                default: return style;
            }
        }

        private void LateUpdate()
        {
            for (var i = actors.Count - 1; i >= 0; i--)
            {
                var actor = actors[i]; if (!actor.Root || !actor.Image) { actors.RemoveAt(i); continue; }
                if ((actor.Root.position - actor.LastPosition).sqrMagnitude > .000001f) actor.LastMovedAt = Time.time;
                var moved = actor.Player ? actor.Player.Motor.Velocity.LengthSquared > 4 :
                    actor.Enemy && actor.Enemy.Model != null ? actor.Enemy.Model.Velocity.LengthSquared > 4 : Time.time - actor.LastMovedAt < .085f;
                actor.LastPosition = actor.Root.position;
                var dead = actor.Enemy && actor.Enemy.Model != null && actor.Enemy.Model.State == EnemyState.Dead
                    || actor.Npc && actor.Npc.Health.Dead || actor.Player && actor.Player.Motor.Dead;
                var armed = !actor.Player || actor.Player.CombatEnabled;
                var leftStep = Mathf.FloorToInt((Time.time + actor.StepPhase) * 8) % 2 == 0;
                var suffix = !armed && art.Contains(actor.Identity + "_unarmed") ? "_unarmed" : moved && !dead ? (leftStep ? "_walk_left" : "_walk_right") : "_idle";
                var id = art.Contains(actor.Identity + suffix) ? actor.Identity + suffix : actor.Identity;
                if (actor.Player && armed && art.Contains(actor.Identity + "_" + actor.Player.Weapons.CurrentId))
                {
                    var weaponPose = actor.Identity + "_" + actor.Player.Weapons.CurrentId;
                    var walkingPose = weaponPose + (moved && !dead ? (leftStep ? "_walk_left" : "_walk_right") : "_idle");
                    id = art.Contains(walkingPose) ? walkingPose : weaponPose;
                }
                else if (actor.Enemy && actor.Enemy.Kind != EnemyKind.Gunman && art.Contains(actor.Identity + "_bat"))
                    id = actor.Identity + "_bat";
                if (dead && art.Contains(actor.Identity + "_down")) id = actor.Identity + "_down";
                if (actor.Npc && actor.Npc.Definition.role == "hostage" && !actor.Npc.Freed && art.Contains(actor.Identity + "_crouch"))
                    id = actor.Identity + "_crouch";
                var carrying = false;
                if (actor.Player && art.Contains("mazur_carry"))
                    foreach (var companion in loop.Actors) if (companion && companion.Carried && companion.Alive) { carrying = true; break; }
                if (carrying) id = "mazur_carry";
                var representedByCarrier = actor.Npc && actor.Npc.Carried && art.Contains("mazur_carry");
                actor.Image.enabled = !representedByCarrier; actor.Shadow.enabled = !representedByCarrier;
                if (art.Contains(id))
                {
                    var sprite = art.Sprite(id);
                    actor.Image.sprite = sprite; actor.Shadow.sprite = sprite;
                    var desired = actor.Size * (carrying ? 1.4f : 1);
                    var scale = Mathf.Min(desired.x / Mathf.Max(.001f, sprite.bounds.size.x), desired.y / Mathf.Max(.001f, sprite.bounds.size.y));
                    actor.Image.transform.localScale = new Vector3(scale, scale, 1);
                    actor.Shadow.transform.localScale = new Vector3(scale * 1.04f, scale * 1.04f, 1);
                    actor.Image.transform.localPosition = new Vector3(0, actor.Player && !armed && moved ? Mathf.Sin(Time.time * 16) * .005f : 0, 0);
                    var offset = art.Region(id).facingClockwiseDegrees;
                    actor.Image.transform.localRotation = Quaternion.Euler(0, 0, offset);
                    actor.Shadow.transform.localRotation = actor.Image.transform.localRotation;
                }
                if (actor.Npc) actor.Pivot.localRotation = Quaternion.Euler(0, 0, -actor.Npc.FacingRadians * Mathf.Rad2Deg);
                var source = ArkonCoordinates.ToSource(actor.Root.position);
                actor.Image.sortingOrder = 1300 + Mathf.RoundToInt(source.Y);
                actor.Shadow.sortingOrder = actor.Image.sortingOrder - 1;
                actor.Image.color = dead ? new Color(.48f, .43f, .39f, .85f) : Color.white;
                if (dead && !art.Contains(actor.Identity + "_down")) actor.Pivot.localRotation = Quaternion.Euler(0, 0, 85);
                else if (actor.Enemy && actor.Enemy.Model != null && actor.Enemy.Model.State == EnemyState.Stunned)
                    actor.Image.color = new Color(1, .78f, .38f, 1);
            }
            Projectiles();
        }
        private void Projectiles()
        {
            liveTokens.Clear();
            var pool = loop && loop.World ? loop.World.GetComponent<ProjectilePool2D>() : null;
            projectileViews.Clear(); if (pool) pool.CopyVisible(projectileViews);
            foreach (var shot in projectileViews)
            {
                liveTokens.Add(shot.Token);
                if (!lines.TryGetValue(shot.Token, out var line))
                {
                    line = availableLines.Count > 0 ? availableLines.Dequeue() : NewLine();
                    line.gameObject.SetActive(true); lines.Add(shot.Token, line);
                }
                var head = ArkonCoordinates.ToUnity(shot.Position);
                var tail = ArkonCoordinates.ToUnity(shot.Position - shot.Direction * (shot.Appearance == ProjectileAppearance.ThrownItem ? 5 : 16));
                var color = shot.Appearance == ProjectileAppearance.EnemyRound ? new Color(1, .48f, .25f, 1) : new Color(1, .91f, .68f, 1);
                line.startColor = new Color(color.r, color.g, color.b, .15f); line.endColor = color;
                line.SetPosition(0, tail); line.SetPosition(1, head);
            }
            retired.Clear(); foreach (var pair in lines) if (!liveTokens.Contains(pair.Key)) retired.Add(pair.Key);
            foreach (var token in retired) { var line = lines[token]; line.gameObject.SetActive(false); availableLines.Enqueue(line); lines.Remove(token); }
        }
        private LineRenderer NewLine()
        {
            var value = new GameObject("Projectile trace"); value.transform.SetParent(transform, false);
            var line = value.AddComponent<LineRenderer>(); line.sharedMaterial = lineMaterial; line.positionCount = 2;
            line.useWorldSpace = true; line.startWidth = .009f; line.endWidth = .018f; line.sortingOrder = 5000;
            return line;
        }
        private void OnDestroy()
        { if (loop) loop.EntityCreated -= Add; if (lineMaterial) Destroy(lineMaterial); }

        private sealed class ActorVisual
        {
            public Transform Root, Pivot;
            public SpriteRenderer Image, Shadow;
            public string Identity;
            public TopdownActor2D Player;
            public EnemyPort2D Enemy;
            public CampaignNpc Npc;
            public Vector2 Size;
            public Vector3 LastPosition;
            public float LastMovedAt = -1, StepPhase;
        }
    }
}
