using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Arkon.Core
{
    /// <summary>
    /// State port of topdown/weapon_component.gd. Each actor owns its catalog
    /// reserves and cooldown; changing weapon resets the cooldown as in source.
    /// </summary>
    public sealed class WeaponModel
    {
        private readonly Dictionary<string, WeaponDefinition> _catalog;
        private readonly Dictionary<string, int> _reserves;
        public IReadOnlyDictionary<string, int> Reserves { get; }
        public string CurrentId { get; private set; } = string.Empty;
        public WeaponDefinition CurrentDefinition { get; private set; }
        public int Ammo { get; private set; }
        public float Cooldown { get; private set; }
        public bool InfiniteAmmo { get; set; }
        public bool CanFire => CurrentDefinition != null && Cooldown <= 0
            && (!CurrentDefinition.ConsumesAmmo || InfiniteAmmo || Ammo > 0);
        public event Action<string, int> Changed;

        public WeaponModel(bool infiniteAmmo = true, string initialId = WeaponCatalog.PistolId,
            int initialAmmo = 12)
            : this(WeaponCatalog.Default, infiniteAmmo, initialId, initialAmmo) { }

        public WeaponModel(IReadOnlyDictionary<string, WeaponDefinition> catalog,
            bool infiniteAmmo = true, string initialId = WeaponCatalog.PistolId, int initialAmmo = 12)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            _catalog = new Dictionary<string, WeaponDefinition>(StringComparer.Ordinal);
            _reserves = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in catalog)
            {
                if (string.IsNullOrEmpty(entry.Key) || entry.Value == null)
                    throw new ArgumentException("A catalog entry must have an ID and definition.", nameof(catalog));
                _catalog.Add(entry.Key, entry.Value);
                if (entry.Value.ConsumesAmmo) _reserves[entry.Key] = 0;
            }
            Reserves = new ReadOnlyDictionary<string, int>(_reserves);
            InfiniteAmmo = infiniteAmmo;
            if (!Equip(initialId, initialAmmo))
                throw new ArgumentException("The initial weapon is absent from the catalog.", nameof(initialId));
        }

        public int GetReserve(string id) => id != null && _reserves.TryGetValue(id, out var amount) ? amount : 0;

        public bool Equip(string id, int additionalAmmo = 0)
        {
            if (id == null || !_catalog.TryGetValue(id, out var definition)) return false;
            CurrentId = id;
            CurrentDefinition = definition;
            if (definition.ConsumesAmmo)
            {
                Ammo = InfiniteAmmo ? -1 : (int)Math.Max(0, Math.Min(int.MaxValue,
                    (long)GetReserve(id) + Math.Max(additionalAmmo, 0)));
                _reserves[id] = Ammo;
            }
            else Ammo = 0;
            Cooldown = 0;
            Changed?.Invoke(CurrentId, Ammo);
            return true;
        }

        public void Tick(float delta)
        {
            if (!Scalar.IsFinite(delta) || delta <= 0) return;
            Cooldown = Math.Max(0, Cooldown - delta);
        }

        public bool ConsumeShot()
        {
            if (!CanFire) return false;
            if (CurrentDefinition.ConsumesAmmo && !InfiniteAmmo)
            {
                Ammo = Math.Max(Ammo - 1, 0);
                _reserves[CurrentId] = Ammo;
            }
            Cooldown = Math.Max(CurrentDefinition.FireInterval, .01f);
            Changed?.Invoke(CurrentId, Ammo);
            return true;
        }
    }
}
