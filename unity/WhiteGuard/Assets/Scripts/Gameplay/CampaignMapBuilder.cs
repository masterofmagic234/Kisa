using System;
using Arkon.Core;
using Arkon.Unity;
using UnityEngine;

namespace WhiteGuard.Gameplay
{
    /// <summary>Builds native colliders/actors from authored book locations.</summary>
    internal sealed class CampaignMapBuilder
    {
        private readonly CampaignGameLoop owner;
        private readonly Transform root;
        public CampaignMapBuilder(CampaignGameLoop owner, Transform root) { this.owner = owner; this.root = root; }

        public TopdownWorld2D Build(CampaignLevel level, Camera camera)
        {
            var host = Make("Campaign combat world");
            var world = host.AddComponent<TopdownWorld2D>();
            world.Transitioning = true; world.CombatPaused = true;
            world.BuildNavigationOnStart = false; world.RowsPerSlice = 12;
            world.WorldSizePixels = new Vector2(level.width, level.height);
            owner.AssignWorld(world);
            foreach (var data in Safe(level.floors)) Rectangle(data, "floor", false);
            foreach (var data in Safe(level.walls)) Rectangle(data, "wall", true);
            foreach (var data in Safe(level.props)) Rectangle(data, "prop", data.solid);
            Boundary(level.width, level.height);
            Player(level, world, camera);
            foreach (var data in Safe(level.doors)) Door(data, world);
            foreach (var data in Safe(level.enemies)) Enemy(data, world);
            foreach (var data in Safe(level.pickups)) Pickup(data, world);
            foreach (var data in Safe(level.actors)) Actor(data, world);
            foreach (var data in Safe(level.interactions))
            {
                var value = Make("Interaction " + data.id); At(value, data.x, data.y);
                owner.EmitEntity(new CampaignEntity(value, "interaction", data.id, data.style ?? data.action,
                    PointBounds(data.x, data.y, 20, 20)));
            }
            if (level.exit != null)
            {
                var exit = Make("Extraction point"); At(exit, level.exit.x + level.exit.w / 2, level.exit.y + level.exit.h / 2);
                owner.EmitEntity(new CampaignEntity(exit, "exit", level.exit.id ?? "exit", "exit", level.exit));
            }
            Physics2D.SyncTransforms();
            world.BindTarget(owner.Player); world.BuildNavigation(new Vector2(level.width, level.height));
            return world;
        }

        private void Boundary(float width, float height)
        {
            Rectangle(new CampaignRect { id = "border_w", x = -18, y = -18, w = 18, h = height + 36, style = "boundary" }, "wall", true);
            Rectangle(new CampaignRect { id = "border_e", x = width, y = -18, w = 18, h = height + 36, style = "boundary" }, "wall", true);
            Rectangle(new CampaignRect { id = "border_n", x = 0, y = -18, w = width, h = 18, style = "boundary" }, "wall", true);
            Rectangle(new CampaignRect { id = "border_s", x = 0, y = height, w = width, h = 18, style = "boundary" }, "wall", true);
        }

        private void Rectangle(CampaignRect data, string kind, bool solid)
        {
            var value = Make(kind + " " + data.id); At(value, data.x + data.w / 2, data.y + data.h / 2);
            if (solid)
            {
                value.layer = ArkonCoordinates.WallsLayer;
                var collider = value.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(data.w, data.h) / ArkonCoordinates.PixelsPerUnit;
            }
            owner.EmitEntity(new CampaignEntity(value, kind, data.id, data.style, data));
        }

        private void Player(CampaignLevel level, TopdownWorld2D world, Camera camera)
        {
            var value = Make("Кирилл Мазур", false); At(value, level.spawn.x, level.spawn.y);
            var actor = value.AddComponent<TopdownActor2D>();
            actor.AssignedWorld = world.GetComponent<Physics2DPort>();
            actor.InfiniteAmmo = false; actor.ReadKeyboardMouse = true; actor.CombatEnabled = level.combat;
            actor.AimCamera = camera;
            value.SetActive(true);
            var weapon = string.IsNullOrEmpty(level.startWeapon) ? "pistol" : level.startWeapon;
            if (weapon != "pistol") actor.Weapons.Equip(weapon, weapon == "shotgun" ? 6 : 0);
            owner.AssignPlayer(actor);
            owner.EmitEntity(new CampaignEntity(value, "player", "mazur", "mazur", PointBounds(level.spawn.x, level.spawn.y, 28, 28)));
        }

        private void Door(CampaignDoor data, TopdownWorld2D world)
        {
            var value = Make("Door " + data.id, false); At(value, data.x, data.y);
            value.transform.rotation = Quaternion.Euler(0, 0, -data.angle);
            var door = value.AddComponent<DoorPort2D>(); door.AssignedWorld = world; door.Locked = data.locked;
            value.SetActive(true); owner.RegisterDoor(data, door);
            owner.EmitEntity(new CampaignEntity(value, "door", data.id, data.style ?? "door", PointBounds(data.x, data.y, 32, 6), data.angle));
        }

        private void Enemy(CampaignEnemy data, TopdownWorld2D world)
        {
            if (owner.IsNeutralized(data.id)) return;
            var value = Make("Enemy " + data.id, false); At(value, data.x, data.y);
            var enemy = value.AddComponent<EnemyPort2D>(); enemy.AssignedWorld = world;
            enemy.AIEnabled = string.IsNullOrEmpty(data.activeAfterObjective);
            enemy.Kind = data.kind == "melee" ? EnemyKind.Melee : data.kind == "butcher" ? EnemyKind.Butcher : EnemyKind.Gunman;
            enemy.SourceSenseSlot = StableSlot(data.id, 10); enemy.SourcePatrolSlot = StableSlot(data.id, 100);
            enemy.SourceDutySeed = StableSlot(data.id, 7);
            value.SetActive(true); owner.RegisterEnemy(data, enemy);
            owner.EmitEntity(new CampaignEntity(value, "enemy", data.id, data.style ?? data.kind, PointBounds(data.x, data.y, 28, 28)));
        }

        public void Pickup(CampaignPickup data, TopdownWorld2D world)
        {
            if (owner.IsPickupConsumed(data.id)) return;
            var value = Make("Pickup " + data.id, false); At(value, data.x, data.y);
            var item = value.AddComponent<PickupPort2D>(); item.AssignedWorld = world; item.Kind = data.kind;
            value.SetActive(true); owner.RegisterPickup(data, item);
            owner.EmitEntity(new CampaignEntity(value, "pickup", data.id, data.style ?? data.kind, PointBounds(data.x, data.y, 18, 18)));
        }

        private void Actor(CampaignActor data, TopdownWorld2D world)
        {
            var value = Make(data.name ?? data.id, false); At(value, data.x, data.y);
            var body = value.AddComponent<KinematicBodyPort2D>(); body.RadiusPixels = data.role == "vehicle" ? 18 : 7;
            value.AddComponent<ActorIdentity2D>();
            var health = value.AddComponent<DamageReceiver2D>();
            health.Maximum = data.health > 0 ? data.health : data.role == "vehicle" ? 240 : 100;
            health.Invulnerability = data.role == "vehicle" ? .08f : .24f;
            var actor = value.AddComponent<CampaignNpc>(); actor.Definition = data; actor.Owner = owner;
            value.SetActive(true); owner.RegisterActor(data, actor);
            owner.EmitEntity(new CampaignEntity(value, data.role == "vehicle" ? "vehicle" : data.role == "passenger" ? "passenger" : "actor", data.id,
                data.style ?? data.role, PointBounds(data.x, data.y, data.role == "vehicle" ? 72 : 28, data.role == "vehicle" ? 38 : 28)));
        }

        private GameObject Make(string name, bool active = true)
        {
            var value = new GameObject(name); value.SetActive(false);
            value.transform.SetParent(root, false); value.SetActive(active); return value;
        }
        internal static void At(GameObject value, float x, float y) => value.transform.position = ArkonCoordinates.ToUnity(new Vec2(x, y));
        internal static CampaignRect PointBounds(float x, float y, float width, float height) => new CampaignRect { x = x - width / 2, y = y - height / 2, w = width, h = height };
        private static T[] Safe<T>(T[] values) => values ?? Array.Empty<T>();
        private static int StableSlot(string text, int divisor)
        {
            uint hash = 2166136261;
            foreach (var c in text ?? "") hash = unchecked((hash ^ c) * 16777619);
            return (int)(hash % divisor);
        }
    }
}
