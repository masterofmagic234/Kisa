using UnityEngine;

namespace WhiteGuard.Gameplay
{
    /// <summary>Presentation metadata for real scene objects created from the campaign map.</summary>
    public readonly struct CampaignEntity
    {
        public readonly GameObject Object;
        public readonly string Kind, Id, Style;
        public readonly CampaignRect Bounds;
        public readonly float Angle;
        public CampaignEntity(GameObject value, string kind, string id, string style, CampaignRect bounds, float angle = 0)
        { Object = value; Kind = kind; Id = id; Style = style; Bounds = bounds; Angle = angle; }
    }
}
