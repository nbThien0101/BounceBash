using System;
using System.Collections.Generic;
using BounceBash.Core;
using Godot;

namespace BounceBash.Level;

/// <summary>
/// Builds and validates the 3-sector vertical climb from the 5-module pool (GDD Section 1.4.1-1.4.4 & T10):
/// Pool of 5 modules:
/// - Jungle_Tier0 (A1: Rừng - Lối rễ / Nhập môn)
/// - Jungle_Tier1 (A2: Rừng - Vòm tán / Thử thách)
/// - Rock_Tier0   (B1: Đá cổ - Hành lang vọng / Nhập môn)
/// - Rock_Tier1   (B2: Đá cổ - Giếng đôi / Thử thách)
/// - Temple_Final (Sector 3: Đền cổ - Đỉnh tranh bảo vật)
///
/// Combination A: Jungle_Tier0 -> Rock_Tier1 -> Temple_Final
/// Combination B: Rock_Tier0   -> Jungle_Tier1 -> Temple_Final
/// </summary>
public partial class MapAssembler : Node2D
{
    public List<SectorModule> ActiveModules { get; } = [];
    public List<PlatformCandidate> AllRespawnPlatforms { get; } = [];
    public List<ItemAnchorCandidate> AllItemAnchors { get; } = [];

    public Vector2[] SpawnPositions { get; private set; } = new Vector2[4];
    public Vector2 GoalZonePosition { get; private set; }
    public float TowerTopY { get; private set; } = -SectorModule.SectorHeight * 3.0f;
    public MapCombination ActiveCombination { get; private set; }

    public void BuildTower(int seed, MapCombination requestedCombination)
    {
        foreach (Node child in GetChildren())
        {
            child.QueueFree();
        }
        ActiveModules.Clear();
        AllRespawnPlatforms.Clear();
        AllItemAnchors.Clear();

        var rng = new Random(seed);
        ActiveCombination = requestedCombination switch
        {
            MapCombination.CombinationA_JungleThenRock => MapCombination.CombinationA_JungleThenRock,
            MapCombination.CombinationB_RockThenJungle => MapCombination.CombinationB_RockThenJungle,
            _ => rng.Next(2) == 0
                ? MapCombination.CombinationA_JungleThenRock
                : MapCombination.CombinationB_RockThenJungle
        };

        string[] moduleOrder = ActiveCombination == MapCombination.CombinationA_JungleThenRock
            ? ["Jungle_Tier0", "Rock_Tier1", "Temple_Final"]
            : ["Rock_Tier0", "Jungle_Tier1", "Temple_Final"];

        for (int sectorIdx = 0; sectorIdx < 3; sectorIdx++)
        {
            float sectorBaseY = -sectorIdx * SectorModule.SectorHeight;
            SectorModule module = CreateModuleVariant(moduleOrder[sectorIdx], sectorIdx, new Vector2(0f, sectorBaseY));
            AddChild(module);
            ActiveModules.Add(module);

            AllRespawnPlatforms.AddRange(module.RespawnPlatforms);
            AllItemAnchors.AddRange(module.ItemAnchors);

            if (sectorIdx == 2)
            {
                GoalZonePosition = module.GoalPedestalGlobal;
            }
        }

        TowerTopY = -3.0f * SectorModule.SectorHeight;

        // Starting Camp spawn positions inside Sector 1 (GDD 1.4.1: Starting camp is inside Sector 1)
        SpawnPositions =
        [
            new Vector2(260f, -32f),
            new Vector2(300f, -32f),
            new Vector2(340f, -32f),
            new Vector2(380f, -32f)
        ];
    }

    private static SectorModule CreateModuleVariant(string moduleId, int sectorIndex, Vector2 basePos)
    {
        var mod = new SectorModule
        {
            Name = $"Sector_{sectorIndex + 1}_{moduleId}",
            ModuleId = moduleId,
            SectorIndex = sectorIndex,
            Position = basePos
        };

        // Outer boundary bounce walls on Left (x=0..32) and Right (x=608..640)
        mod.AddBounceWall(16f, -SectorModule.SectorHeight, 32f, SectorModule.SectorHeight);
        mod.AddBounceWall(624f, -SectorModule.SectorHeight, 32f, SectorModule.SectorHeight);

        switch (moduleId)
        {
            case "Jungle_Tier0":
                BuildJungleTier0(mod);
                break;
            case "Jungle_Tier1":
                BuildJungleTier1(mod);
                break;
            case "Rock_Tier0":
                BuildRockTier0(mod);
                break;
            case "Rock_Tier1":
                BuildRockTier1(mod);
                break;
            case "Temple_Final":
            default:
                BuildTempleFinal(mod);
                break;
        }

        return mod;
    }

    /// <summary>
    /// A1 · Rừng - Lối rễ (Nhập môn) — GDD Figure 1.4-02 Left:
    /// Wide starting camp platform, teaches A/D walk, charge jump, and first wall bounce.
    /// Max vertical gap between consecutive platforms is <= 115px (well within max jump height 148px).
    /// </summary>
    private static void BuildJungleTier0(SectorModule mod)
    {
        mod.DisplayTitle = "A1 · Rừng - Lối rễ (Nhập môn)";
        mod.Theme = SectorTheme.Jungle;
        mod.DifficultyTier = 0;

        // Entry / Starting Camp wide floor
        mod.AddPlatform(320f, -32f, 340f, 24f);
        // Step 1: Left & Right wide root ledges
        mod.AddPlatform(165f, -125f, 140f, 16f);
        mod.AddPlatform(475f, -155f, 140f, 16f);
        // Step 2: Center trunk platform + Right bounce wall teaching first bank shot
        mod.AddPlatform(310f, -250f, 130f, 16f);
        mod.AddBounceWall(560f, -390f, 24f, 150f);
        // Step 3: Upper-left resting bough + right ambush ledge (GDD Fig 1.4-02)
        mod.AddPlatform(175f, -350f, 145f, 16f);
        mod.AddPlatform(455f, -435f, 120f, 16f);
        mod.AddItemCandidate(465f, -460f, ItemEffectType.KineticNullifier);
        // Step 4: Upper-left approach & standardized Exit platform at X=320
        mod.AddPlatform(205f, -535f, 135f, 16f);
        mod.AddPlatform(430f, -620f, 125f, 16f);
        mod.AddPlatform(320f, -700f, 150f, 16f);
    }

    /// <summary>
    /// A2 · Rừng - Vòm tán (Thử thách) — GDD Figure 1.4-02 Right:
    /// Central bounce column dividing 2 lanes, off-route Buff branch on left, ambush ledge on top-left.
    /// </summary>
    private static void BuildJungleTier1(SectorModule mod)
    {
        mod.DisplayTitle = "A2 · Rừng - Vòm tán (Thử thách)";
        mod.Theme = SectorTheme.Jungle;
        mod.DifficultyTier = 1;

        // Standardized Entry floor receiving jump from Sector 1 Exit
        mod.AddPlatform(320f, -32f, 160f, 16f);

        // Central vertical bounce column splitting Left and Right lanes (GDD Fig 1.4-02 A2)
        mod.AddBounceWall(320f, -310f, 28f, 210f);

        // Right main climb lane + Left off-route branch
        mod.AddPlatform(485f, -125f, 120f, 16f);
        mod.AddPlatform(155f, -150f, 120f, 16f);
        mod.AddPlatform(460f, -235f, 110f, 16f);

        // Center bridge above the splitting column
        mod.AddPlatform(310f, -330f, 115f, 16f);

        // Off-route Left Buff branch ("nhánh buff lệch tuyến" in GDD Fig 1.4-02)
        mod.AddPlatform(125f, -385f, 100f, 16f);
        mod.AddItemCandidate(125f, -410f, ItemEffectType.KineticNullifier);

        // Right upper climb + Top-left ambush ledge
        mod.AddPlatform(485f, -430f, 115f, 16f);
        mod.AddPlatform(200f, -520f, 115f, 16f);
        mod.AddItemCandidate(200f, -545f, ItemEffectType.DirectionLock, -1);

        mod.AddPlatform(440f, -610f, 115f, 16f);
        // Standardized Exit platform at X=320
        mod.AddPlatform(320f, -700f, 150f, 16f);
    }

    /// <summary>
    /// B1 · Đá cổ - Hành lang vọng (Nhập môn) — GDD Figure 1.4-03 Left:
    /// Alternating stone ledges + overhead bounce ceiling ("trần nảy: nhảy thẳng sẽ dội đầu").
    /// </summary>
    private static void BuildRockTier0(SectorModule mod)
    {
        mod.DisplayTitle = "B1 · Đá cổ - Hành lang vọng (Nhập môn)";
        mod.Theme = SectorTheme.Rock;
        mod.DifficultyTier = 0;

        // Entry / Starting Camp wide floor
        mod.AddPlatform(320f, -32f, 340f, 24f);

        // Left & Right wide stone ledges
        mod.AddPlatform(155f, -130f, 135f, 16f);
        mod.AddPlatform(485f, -130f, 135f, 16f);

        // Overhead bounce ceiling block in the middle (teaches angled jumping around ceilings - GDD Fig 1.4-03 B1)
        mod.AddBounceWall(260f, -255f, 130f, 24f);
        mod.AddPlatform(320f, -230f, 135f, 16f);

        // Mid-section alternating ruins
        mod.AddPlatform(150f, -335f, 125f, 16f);
        mod.AddPlatform(475f, -360f, 125f, 16f);
        mod.AddItemCandidate(475f, -385f, ItemEffectType.KineticNullifier);

        // Upper bounce wall & ambush ledge
        mod.AddBounceWall(105f, -540f, 24f, 150f);
        mod.AddPlatform(275f, -465f, 125f, 16f);
        mod.AddPlatform(460f, -565f, 125f, 16f);
        mod.AddPlatform(220f, -625f, 120f, 16f);

        // Standardized Exit platform at X=320
        mod.AddPlatform(320f, -700f, 150f, 16f);
    }

    /// <summary>
    /// B2 · Đá cổ - Giếng đôi (Thử thách) — GDD Figure 1.4-03 Right:
    /// Twin bounce walls ("zíc-zắc giữa hai tường nảy") + Debuff pocket behind left wall ("túi debuff sau tường").
    /// </summary>
    private static void BuildRockTier1(SectorModule mod)
    {
        mod.DisplayTitle = "B2 · Đá cổ - Giếng đôi (Thử thách)";
        mod.Theme = SectorTheme.Rock;
        mod.DifficultyTier = 1;

        // Standardized Entry platform
        mod.AddPlatform(320f, -32f, 160f, 16f);

        // Twin bounce walls creating the narrow well shaft (GDD Fig 1.4-03 B2)
        mod.AddBounceWall(215f, -360f, 24f, 240f);
        mod.AddBounceWall(435f, -360f, 24f, 240f);

        // Step stones inside & around the twin well
        mod.AddPlatform(355f, -125f, 105f, 16f);
        mod.AddPlatform(285f, -225f, 105f, 16f);
        mod.AddPlatform(355f, -325f, 105f, 16f);

        // Left pocket behind the bounce wall ("túi debuff sau tường" in GDD Fig 1.4-03 B2)
        mod.AddPlatform(115f, -280f, 95f, 16f);
        mod.AddItemCandidate(115f, -305f, ItemEffectType.DirectionLock, 1);

        // Upper ledges exiting the twin well
        mod.AddPlatform(275f, -430f, 115f, 16f);
        mod.AddPlatform(485f, -515f, 115f, 16f);
        mod.AddItemCandidate(485f, -540f, ItemEffectType.KineticNullifier);
        mod.AddPlatform(195f, -605f, 115f, 16f);

        // Standardized Exit platform at X=320
        mod.AddPlatform(320f, -700f, 150f, 16f);
    }

    /// <summary>
    /// Sector 3 · Đền Cổ Bậc Cuối (GoalZone King of the Hill) — GDD Section 1.4.2 & 1.4.4:
    /// Columns, high-pressure final climb, side recovery ledges so players knocked off GoalZone
    /// always have a fair return path without softlocking, and the central Temple Pedestal with the Elixir.
    /// </summary>
    private static void BuildTempleFinal(SectorModule mod)
    {
        mod.DisplayTitle = "C1 · Đền Cổ - Đỉnh Tranh Đoạt Bảo Vật";
        mod.Theme = SectorTheme.Temple;
        mod.DifficultyTier = 2;

        // Standardized Entry platform
        mod.AddPlatform(320f, -32f, 160f, 16f);

        // Lower temple columns & twin approach
        mod.AddPlatform(165f, -125f, 115f, 16f);
        mod.AddPlatform(475f, -125f, 115f, 16f);
        mod.AddBounceWall(320f, -250f, 28f, 120f);

        mod.AddPlatform(235f, -225f, 105f, 16f);
        mod.AddPlatform(405f, -225f, 105f, 16f);
        mod.AddItemCandidate(165f, -150f, ItemEffectType.DirectionLock, 0);

        // Mid sanctuary platform + side bounce pillars
        mod.AddPlatform(320f, -325f, 150f, 16f);
        mod.AddBounceWall(120f, -520f, 24f, 170f);
        mod.AddBounceWall(520f, -520f, 24f, 170f);

        // Recovery return steps on both sides of the Temple Sanctuary (GDD 1.4.2: knocked-off players have a fair return route)
        mod.AddPlatform(185f, -425f, 105f, 16f);
        mod.AddPlatform(455f, -425f, 105f, 16f);
        mod.AddItemCandidate(455f, -450f, ItemEffectType.KineticNullifier);

        mod.AddPlatform(205f, -520f, 100f, 16f);
        mod.AddPlatform(435f, -520f, 100f, 16f);

        // Final GoalZone Pedestal (wide enough for King-of-the-Hill contesting, open on sides for knock-offs - GDD 1.4.4)
        mod.AddPlatform(320f, -600f, 170f, 20f);
        mod.SetGoalPedestalLocal(new Vector2(320f, -600f));

        // Temple roof cap so camera stops cleanly with GoalZone fully visible inside view
        mod.AddBounceWall(320f, -720f, 576f, 20f);
    }
}
