using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Arkon.Core
{
    /// <summary>
    /// Data counterpart of combat/weapon_data.gd. Artwork is supplied by the
    /// consumer; this definition contains gameplay and projectile parameters.
    /// </summary>
    public sealed class WeaponDefinition
    {
        public string Id { get; }
        public string DisplayName { get; }
        public int Damage { get; }
        public float FireInterval { get; }
        public float MaxDistance { get; }
        public float ProjectileSpeed { get; }
        public float ProjectileFps { get; }
        public Vec2 ProjectileScale { get; }
        public bool ProjectileSpawnMuzzle { get; }
        public bool ConsumesAmmo { get; }
        public bool Melee { get; }
        public float ActionRange { get; }
        public IReadOnlyList<float> SpreadAngles { get; }
        public int Pellets => SpreadAngles.Count;

        public WeaponDefinition(string id, int damage = 100, float fireInterval = .28f,
            float maxDistance = 650, float projectileSpeed = 650,
            bool consumesAmmo = true, bool melee = false, float actionRange = 32,
            IEnumerable<float> spreadAngles = null, string displayName = null,
            float projectileFps = 14, Vec2? projectileScale = null,
            bool projectileSpawnMuzzle = true)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A weapon requires an ID.", nameof(id));
            if (!Scalar.IsFinite(fireInterval) || !Scalar.IsFinite(maxDistance)
                || !Scalar.IsFinite(projectileSpeed) || !Scalar.IsFinite(actionRange)
                || !Scalar.IsFinite(projectileFps))
                throw new ArgumentException("Weapon values must be finite.");
            var angles = new List<float>(spreadAngles ?? new[] { 0f });
            foreach (var angle in angles)
                if (!Scalar.IsFinite(angle)) throw new ArgumentException("Spread angles must be finite.", nameof(spreadAngles));
            Id = id;
            DisplayName = displayName ?? id;
            Damage = damage;
            FireInterval = fireInterval;
            MaxDistance = maxDistance;
            ProjectileSpeed = projectileSpeed;
            ProjectileFps = projectileFps;
            ProjectileScale = projectileScale ?? new Vec2(1.1f, 1.1f);
            ProjectileSpawnMuzzle = projectileSpawnMuzzle;
            ConsumesAmmo = consumesAmmo;
            Melee = melee;
            ActionRange = actionRange;
            // world.gd actually emits one projectile per spread angle. This is
            // authoritative even though the original Resource also had pellets.
            SpreadAngles = new ReadOnlyCollection<float>(angles);
        }
    }

    public static class WeaponCatalog
    {
        public const string PistolId = "pistol";
        public const string ShotgunId = "shotgun";
        public const string BatId = "bat";

        // Exact released Level 3 values from topdown/weapons/*.tres, rather
        // than WeaponData's unused .18-second default.
        public static WeaponDefinition Pistol { get; } = new WeaponDefinition(
            PistolId, displayName: "ПИСТОЛЕТ");
        public static WeaponDefinition Shotgun { get; } = new WeaponDefinition(
            ShotgunId, fireInterval: .72f,
            spreadAngles: new[] { -.12f, -.06f, 0, .06f, .12f }, displayName: "ДРОБОВИК");
        public static WeaponDefinition Bat { get; } = new WeaponDefinition(
            BatId, fireInterval: .38f, maxDistance: 64, projectileSpeed: 0,
            consumesAmmo: false, melee: true, actionRange: 48,
            displayName: "БИТА", projectileFps: 0, projectileScale: new Vec2(1, 1),
            projectileSpawnMuzzle: false);

        public static IReadOnlyDictionary<string, WeaponDefinition> Default { get; } =
            new ReadOnlyDictionary<string, WeaponDefinition>(
                new Dictionary<string, WeaponDefinition>(StringComparer.Ordinal)
                {
                    [PistolId] = Pistol,
                    [ShotgunId] = Shotgun,
                    [BatId] = Bat
                });

        public static bool TryGet(string id, out WeaponDefinition definition)
        {
            definition = null;
            return id != null && Default.TryGetValue(id, out definition);
        }
    }
}
