using System;
using System.Collections.Generic;
using UnityEngine;

namespace WhiteGuard.Presentation
{
    [Serializable] public sealed class CampaignArtManifest { public int schema; public CampaignArtAtlas[] atlases; public CampaignStyleMappings styleMappings; }
    [Serializable] public sealed class CampaignStyleMappings { public CampaignStyleAlias[] props, floors, walls; }
    [Serializable] public sealed class CampaignStyleAlias { public string style, entryId; }
    [Serializable] public sealed class CampaignArtAtlas
    { public string resource; public int width, height, columns, rows; public CampaignArtRegion[] entries; }
    [Serializable] public sealed class CampaignArtRegion
    { public string id; public int x, y, w, h; public float facingClockwiseDegrees; }

    /// <summary>Uses the generated image assets; crop rectangles are texture coordinates, not replacement drawings.</summary>
    public sealed class CampaignArt : IDisposable
    {
        private readonly Dictionary<string, CampaignArtRegion> regions = new Dictionary<string, CampaignArtRegion>(StringComparer.Ordinal);
        private readonly Dictionary<string, Texture2D> textures = new Dictionary<string, Texture2D>(StringComparer.Ordinal);
        private readonly Dictionary<string, Sprite> sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
        private CampaignStyleMappings styleMappings;
        public Font Body { get; }
        public Font Display { get; }
        public CampaignArt()
        {
            Body = Resources.Load<Font>("WhiteGuardIntro/fonts/Manrope-Variable");
            Display = Resources.Load<Font>("WhiteGuardIntro/fonts/Forum-Regular");
            if (!Body || !Display) throw new InvalidOperationException("Не загружены шрифты игры.");
            var file = Resources.Load<TextAsset>("WhiteGuardArt/art-manifest");
            if (!file) throw new InvalidOperationException("Не найден набор графики игры.");
            var manifest = JsonUtility.FromJson<CampaignArtManifest>(file.text);
            if (manifest == null || manifest.schema != 1 || manifest.atlases == null) throw new InvalidOperationException("Неверный каталог графики.");
            styleMappings = manifest.styleMappings;
            foreach (var atlas in manifest.atlases)
            {
                var texture = Resources.Load<Texture2D>(atlas.resource);
                if (!texture || texture.width != atlas.width || texture.height != atlas.height)
                    throw new InvalidOperationException("Не удалось импортировать изображение: " + atlas.resource);
                foreach (var entry in atlas.entries ?? Array.Empty<CampaignArtRegion>())
                {
                    if (entry.x < 0 || entry.y < 0 || entry.w <= 0 || entry.h <= 0 || entry.x + entry.w > texture.width || entry.y + entry.h > texture.height)
                        throw new InvalidOperationException("Неверный регион изображения: " + entry.id);
                    if (regions.ContainsKey(entry.id)) throw new InvalidOperationException("Повтор изображения: " + entry.id);
                    regions.Add(entry.id, entry); textures.Add(entry.id, texture);
                }
            }
        }
        public bool Contains(string id) => !string.IsNullOrEmpty(id) && regions.ContainsKey(id);
        public CampaignArtRegion Region(string id) => regions.TryGetValue(id, out var value) ? value : null;
        public Texture2D Texture(string id) => textures.TryGetValue(id, out var value) ? value : null;
        public string ResolveStyle(string group, string style, string fallback)
        {
            var aliases = group == "floors" ? styleMappings?.floors : group == "walls" ? styleMappings?.walls : styleMappings?.props;
            foreach (var alias in aliases ?? Array.Empty<CampaignStyleAlias>())
                if (alias.style == style && Contains(alias.entryId)) return alias.entryId;
            return Contains(style) ? style : fallback;
        }
        public Sprite Sprite(string id, float pixelsPerUnit = 100)
        {
            if (!Contains(id)) return null;
            var key = id + "@" + pixelsPerUnit.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (sprites.TryGetValue(key, out var existing)) return existing;
            var area = regions[id]; var texture = textures[id];
            var sprite = UnityEngine.Sprite.Create(texture, new Rect(area.x, texture.height - area.y - area.h, area.w, area.h),
                new Vector2(.5f, .5f), pixelsPerUnit, 0, SpriteMeshType.FullRect);
            sprite.name = id; sprites.Add(key, sprite); return sprite;
        }
        public Texture2D Story(string key) => string.IsNullOrEmpty(key) ? null : Resources.Load<Texture2D>(key);
        public void Dispose() { foreach (var sprite in sprites.Values) if (sprite) UnityEngine.Object.Destroy(sprite); sprites.Clear(); }
    }
}
