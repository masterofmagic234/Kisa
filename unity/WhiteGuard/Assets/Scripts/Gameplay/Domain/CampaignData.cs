using System;

namespace WhiteGuard.Gameplay
{
    public static class CampaignActorDimensions
    {
        public const float HumanHitboxRadius = 7;
        public const float CarriedOffset = 5;
        public const float CarriedBodyRadius = HumanHitboxRadius + CarriedOffset;
    }

    [Serializable] public sealed class CampaignData
    {
        public int schema;
        public string id, title, firstLevel;
        public CampaignLevel[] levels = Array.Empty<CampaignLevel>();
    }

    [Serializable] public sealed class CampaignLevel
    {
        public string id, title, subtitle, environment, chapter, startWeapon;
        public float width, height;
        public bool combat = true;
        public CampaignPoint spawn;
        public CampaignRect exit;
        public NarrativeCard[] before = Array.Empty<NarrativeCard>();
        public NarrativeCard[] after = Array.Empty<NarrativeCard>();
        public CampaignRect[] floors = Array.Empty<CampaignRect>();
        public CampaignRect[] walls = Array.Empty<CampaignRect>();
        public CampaignRect[] props = Array.Empty<CampaignRect>();
        public CampaignRect[] regions = Array.Empty<CampaignRect>();
        public CampaignDoor[] doors = Array.Empty<CampaignDoor>();
        public CampaignEnemy[] enemies = Array.Empty<CampaignEnemy>();
        public CampaignPickup[] pickups = Array.Empty<CampaignPickup>();
        public CampaignActor[] actors = Array.Empty<CampaignActor>();
        public CampaignInteraction[] interactions = Array.Empty<CampaignInteraction>();
        public CampaignObjective[] objectives = Array.Empty<CampaignObjective>();
    }

    [Serializable] public sealed class CampaignPoint
    {
        public float x, y;
        public CampaignPoint() { }
        public CampaignPoint(float x, float y) { this.x = x; this.y = y; }
    }

    [Serializable] public sealed class CampaignRect
    {
        public string id, style;
        public float x, y, w, h;
        public bool solid;
        public bool Contains(float px, float py) => px >= x && py >= y && px <= x + w && py <= y + h;
    }

    [Serializable] public sealed class CampaignDoor
    {
        public string id, style;
        public float x, y, angle;
        public bool locked;
    }

    [Serializable] public sealed class CampaignEnemy
    {
        public string id, group, kind, style, activeAfterObjective;
        public float x, y;
    }

    [Serializable] public sealed class CampaignPickup
    {
        public string id, kind, style;
        public float x, y;
    }

    [Serializable] public sealed class CampaignActor
    {
        public string id, name, role, style, transport;
        public float x, y, speed;
        public int health;
        public bool required;
        public CampaignPoint[] route = Array.Empty<CampaignPoint>();
    }

    [Serializable] public sealed class CampaignInteraction
    {
        public string id, text, body, action, target, region, style;
        public float x, y, radius;
        public bool checkpoint;
        public string[] requires = Array.Empty<string>();
        public string[] grants = Array.Empty<string>();
    }

    [Serializable] public sealed class CampaignObjective
    {
        public string id, text, type, target, region;
        public float amount;
        public string[] requires = Array.Empty<string>();
    }

    [Serializable] public sealed class NarrativeCard
    {
        public string id, speaker, text, art;
        public int quadrant;
        public float hold;
    }
}
