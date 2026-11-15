using System;
using Arkon.Core;

namespace WhiteGuard.Gameplay
{
    [Serializable] public sealed class CampaignSave
    {
        public int schema = 1;
        public string campaign, level, contentHash;
        public string[] completedLevels = Array.Empty<string>();
        public string[] facts = Array.Empty<string>();
        public int deaths, kills;
        public float totalSeconds;
        public CampaignCheckpoint checkpoint;
    }

    [Serializable] public sealed class CampaignCheckpoint
    {
        public string level;
        public CampaignProgressSnapshot progress;
        public float playerX, playerY;
        public int health;
        public HealthState playerHealth;
        public WeaponState inventory;
        public bool bottle;
        public string[] interacted = Array.Empty<string>();
        public string[] consumedPickups = Array.Empty<string>();
        public CampaignActorState[] actors = Array.Empty<CampaignActorState>();
        public CampaignEnemyState[] enemies = Array.Empty<CampaignEnemyState>();
        public CampaignDoorState[] doors = Array.Empty<CampaignDoorState>();
        public string[] neutralized = Array.Empty<string>();
        public CampaignPickup[] activePickups = Array.Empty<CampaignPickup>();
    }

    [Serializable] public sealed class CampaignActorState
    {
        public string id;
        public float x, y;
        public int health, routeIndex;
        public HealthState healthState;
        public bool freed, carried;
    }

    [Serializable] public sealed class CampaignEnemyState
    {
        public string id;
        public float x, y;
        public bool dead;
    }

    [Serializable] public sealed class CampaignDoorState
    {
        public string id;
        public bool open, locked;
    }
}
