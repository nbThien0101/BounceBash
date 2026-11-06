using System.Collections.Generic;
using BounceBash.Core;
using Godot;

namespace BounceBash.Level;

public readonly record struct PlatformCandidate(
    Vector2 TopCenterGlobal,
    float Width,
    int SectorIndex,
    string ModuleId);

public readonly record struct ItemAnchorCandidate(
    Vector2 GlobalPos,
    ItemEffectType PreferredType,
    int LockedDirectionVariant,
    string ModuleId);

/// <summary>
/// Represents one vertical Sector module in the 3-sector match structure (GDD Section 1.4.1-1.4.4).
/// Standardized dimensions: 640px wide x 720px tall (2 viewport heights per sector).
/// Standardized Entry (bottom X = 320) and Exit (top X = 320) so modules can be swapped
/// in Combination A (Jungle0 -> Rock1 -> Temple) or Combination B (Rock0 -> Jungle1 -> Temple).
/// </summary>
public partial class SectorModule : Node2D
{
    public const float SectorWidth = 640.0f;
    public const float SectorHeight = 720.0f;
    public const float OuterWallThickness = 32.0f;

    public string ModuleId { get; set; } = "Jungle_Tier0";
    public string DisplayTitle { get; set; } = "A1 · Rừng - Lối rễ (Nhập môn)";
    public SectorTheme Theme { get; set; } = SectorTheme.Jungle;
    public int DifficultyTier { get; set; } // 0 = Sector 1, 1 = Sector 2, 2 = Sector 3 (Temple)
    public int SectorIndex { get; set; }

    public Vector2 EntryLocal => new(SectorWidth * 0.5f, -24.0f);
    public Vector2 ExitLocal => new(SectorWidth * 0.5f, -SectorHeight + 24.0f);

    public List<PlatformCandidate> RespawnPlatforms { get; } = [];
    public List<ItemAnchorCandidate> ItemAnchors { get; } = [];
    public Vector2 GoalPedestalGlobal { get; private set; }

    private readonly List<(Rect2 Rect, bool IsBounceWall)> _geometryBlocks = [];
    private Texture2D? _tilesetTexture;

    public override void _Ready()
    {
        if (ResourceLoader.Exists("res://Assets/Sprites/Tilesets/sector_tileset.png"))
        {
            _tilesetTexture = GD.Load<Texture2D>("res://Assets/Sprites/Tilesets/sector_tileset.png");
        }
    }

    public void AddPlatform(float centerX, float topY, float width, float height = 16.0f, bool registerRespawn = true)
    {
        var body = new StaticBody2D
        {
            Position = new Vector2(centerX, topY + height * 0.5f)
        };
        body.AddToGroup("StablePlatform");

        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(width, height) }
        };
        body.AddChild(shape);
        AddChild(body);

        var rect = new Rect2(centerX - width * 0.5f, topY, width, height);
        _geometryBlocks.Add((rect, false));

        if (registerRespawn && width >= 48.0f)
        {
            RespawnPlatforms.Add(new PlatformCandidate(
                GlobalPosition + new Vector2(centerX, topY),
                width,
                SectorIndex,
                ModuleId));
        }

        QueueRedraw();
    }

    public void AddBounceWall(float centerX, float topY, float width, float height)
    {
        var body = new StaticBody2D
        {
            Position = new Vector2(centerX, topY + height * 0.5f)
        };
        body.AddToGroup("BounceWall");

        var shape = new CollisionShape2D
        {
            Shape = new RectangleShape2D { Size = new Vector2(width, height) }
        };
        body.AddChild(shape);
        AddChild(body);

        var rect = new Rect2(centerX - width * 0.5f, topY, width, height);
        _geometryBlocks.Add((rect, true));
        QueueRedraw();
    }

    public void AddItemCandidate(float x, float y, ItemEffectType type, int lockedDir = 0)
    {
        ItemAnchors.Add(new ItemAnchorCandidate(
            GlobalPosition + new Vector2(x, y),
            type,
            lockedDir,
            ModuleId));
    }

    public void SetGoalPedestalLocal(Vector2 localPos)
    {
        GoalPedestalGlobal = GlobalPosition + localPos;
    }

    public override void _Draw()
    {
        // 1. Draw atmospheric background band for this sector (DB32 inspired dark palette so 32x32 characters pop - GDD 1.4.4 vii)
        Color bgColor = Theme switch
        {
            SectorTheme.Jungle => new Color("112318"),
            SectorTheme.Rock => new Color("161925"),
            SectorTheme.Temple => new Color("22161c"),
            _ => new Color("111926")
        };
        DrawRect(new Rect2(0f, -SectorHeight, SectorWidth, SectorHeight), bgColor, true);

        // Subtle decorative background pillars / foliage silhouettes (never occluding feet or platform edges - GDD 1.4.3)
        Color silhouetteCol = Theme switch
        {
            SectorTheme.Jungle => new Color(0.12f, 0.22f, 0.16f, 0.45f),
            SectorTheme.Rock => new Color(0.15f, 0.18f, 0.26f, 0.45f),
            _ => new Color(0.24f, 0.16f, 0.20f, 0.45f)
        };
        for (int i = 0; i < 4; i++)
        {
            float bx = 90f + i * 145f;
            DrawRect(new Rect2(bx, -SectorHeight + 40f, 28f, SectorHeight - 80f), silhouetteCol, true);
        }

        // Sector header watermark
        if (ThemeDB.FallbackFont != null)
        {
            DrawString(
                ThemeDB.FallbackFont,
                new Vector2(44f, -SectorHeight + 24f),
                $"SECTOR {SectorIndex + 1}: {DisplayTitle.ToUpperInvariant()}",
                HorizontalAlignment.Left,
                -1f,
                11,
                new Color(0.85f, 0.88f, 0.92f, 0.45f));
        }

        // 2. Draw all platforms and bounce walls using the DB32 16x16 tileset or high-contrast pixel blocks
        int themeBaseCol = Theme switch
        {
            SectorTheme.Jungle => 0,
            SectorTheme.Rock => 2,
            SectorTheme.Temple => 4,
            _ => 0
        };

        foreach (var (rect, isBounceWall) in _geometryBlocks)
        {
            if (_tilesetTexture != null)
            {
                int colIdx = themeBaseCol + (isBounceWall ? 1 : 0);
                Rect2 srcRect = new(colIdx * 16, 0, 16, 16);

                int tilesX = Mathf.Max(1, Mathf.RoundToInt(rect.Size.X / 16f));
                int tilesY = Mathf.Max(1, Mathf.RoundToInt(rect.Size.Y / 16f));
                for (int ty = 0; ty < tilesY; ty++)
                {
                    for (int tx = 0; tx < tilesX; tx++)
                    {
                        Rect2 dst = new(
                            rect.Position.X + tx * 16f,
                            rect.Position.Y + ty * 16f,
                            Mathf.Min(16f, rect.End.X - (rect.Position.X + tx * 16f)),
                            Mathf.Min(16f, rect.End.Y - (rect.Position.Y + ty * 16f)));
                        DrawTextureRectRegion(_tilesetTexture, dst, srcRect);
                    }
                }
            }

            // Crisp readable border highlight (bright top edge for stable platforms, bright side edges for BounceWalls - GDD 1.4.3)
            if (!isBounceWall)
            {
                Color topEdge = Theme switch
                {
                    SectorTheme.Jungle => new Color("99e550"),
                    SectorTheme.Rock => new Color("cbdbfc"),
                    _ => new Color("fbf236")
                };
                DrawLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y), topEdge, 2.0f);
            }
            else
            {
                Color bounceEdge = Theme switch
                {
                    SectorTheme.Jungle => new Color("d9a066"),
                    SectorTheme.Rock => new Color("639bff"),
                    _ => new Color("fbf236")
                };
                DrawRect(rect, bounceEdge, false, 1.5f);
            }
        }
    }
}
