namespace BounceBash.Core;

/// <summary>
/// Game modes supported in Bounce Bash (GDD v0.9.5 Section 1.2.4 - 1.2.6):
/// - SinglePlayer: 1 player solo climb, continuous auto-scrolling camera, touch Elixir at Temple to win.
/// - PvEBot: 1 human player vs 1 Bot AI on local machine, shared leader camera, 3 lives, King of the Hill GoalZone.
/// - PvPDefault: 2-4 LAN players, shared leader camera, 3 lives, 5s GoalZone hold.
/// - PvPQuickMatch: 2-4 LAN players, 3-minute timer with altitude & hold time tiebreaker.
/// - LocalTwoPlayerTest: 2 players sharing 1 keyboard (P1: A/D/Space, P2: J/L/I).
/// </summary>
public enum GameMode
{
    SinglePlayer = 0,
    PvPDefault = 1,
    PvPQuickMatch = 2,
    LocalTwoPlayerTest = 3,
    PvEBot = 4
}

/// <summary>
/// Gameplay state machine states defined in GDD Section 1.3.1.
/// </summary>
public enum PlayerState
{
    GroundedIdle = 0,
    GroundedMove = 1,
    Charging = 2,
    Airborne = 3,
    Landing = 4
}

public enum CharacterGender
{
    Male = 0,
    Female = 1
}

public enum SectorTheme
{
    Jungle = 0,
    Rock = 1,
    Temple = 2
}

/// <summary>
/// Modular combination order defined in GDD Section 1.4.1:
/// Combination A: Sector 1 = Jungle (Tier 0) -> Sector 2 = Rock (Tier 1) -> Sector 3 = Temple (Final)
/// Combination B: Sector 1 = Rock (Tier 0) -> Sector 2 = Jungle (Tier 1) -> Sector 3 = Temple (Final)
/// </summary>
public enum MapCombination
{
    Random = 0,
    CombinationA_JungleThenRock = 1,
    CombinationB_RockThenJungle = 2
}

public enum ItemEffectType
{
    None = 0,
    KineticNullifier = 1,
    DirectionLock = 2
}
