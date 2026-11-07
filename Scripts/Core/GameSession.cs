using System;
using System.Collections.Generic;
using BounceBash.Config;
using Godot;

namespace BounceBash.Core;

public class PlayerSlotInfo
{
    public int SlotIndex { get; set; } // 0..3 -> P1..P4
    public int PeerId { get; set; } = 1;
    public string DisplayName { get; set; } = "P1";
    public Color TagColor { get; set; } = Colors.Red;
    public CharacterGender Gender { get; set; } = CharacterGender.Male;
    public bool IsOccupied { get; set; }
}

/// <summary>
/// Global session state autoload singleton (GDD Section 1.5.4, 1.6.3, 1.7.2).
/// Stores tuning config, player slot colors/genders, audio settings, and match parameters.
/// </summary>
public partial class GameSession : Node
{
    public static GameSession Instance { get; private set; } = null!;

    public TuningConfig Tuning { get; private set; } = new();

    public GameMode CurrentMode { get; set; } = GameMode.SinglePlayer;
    public MapCombination SelectedCombination { get; set; } = MapCombination.Random;
    public int CurrentMapSeed { get; set; } = 20261105;
    public bool ShowTrajectoryPreview { get; set; } = true;

    public float MusicVolume { get; set; } = 0.8f;
    public float SfxVolume { get; set; } = 0.85f;
    public float Brightness { get; set; } = 1.0f;

    // P1 = Red, P2 = Blue, P3 = Green, P4 = Yellow (GDD Section 1.5.4)
    public static readonly Color[] PlayerPalette =
    [
        new Color("e63946"), // P1 Red
        new Color("3a86ff"), // P2 Blue
        new Color("2a9d8f"), // P3 Green
        new Color("ffbe0b")  // P4 Yellow
    ];

    public List<PlayerSlotInfo> Slots { get; } = [];

    private readonly Dictionary<string, AudioStream> _sfxCache = [];
    private readonly List<AudioStreamPlayer> _sfxPool = [];

    public override void _Ready()
    {
        Instance = this;

        if (ResourceLoader.Exists("res://Resources/DefaultTuningConfig.tres"))
        {
            var loaded = GD.Load<TuningConfig>("res://Resources/DefaultTuningConfig.tres");
            if (loaded != null)
            {
                Tuning = loaded;
            }
        }

        ResetSlotsForSinglePlayer();
        InitAudioPool();
    }

    public void ResetSlotsForSinglePlayer(CharacterGender gender = CharacterGender.Male)
    {
        Slots.Clear();
        for (int i = 0; i < 4; i++)
        {
            Slots.Add(new PlayerSlotInfo
            {
                SlotIndex = i,
                PeerId = i == 0 ? 1 : 0,
                DisplayName = $"P{i + 1}",
                TagColor = PlayerPalette[i],
                Gender = i % 2 == 0 ? gender : CharacterGender.Female,
                IsOccupied = i == 0
            });
        }
    }

    public void SetupSlotsForPvE(CharacterGender playerGender = CharacterGender.Male, CharacterGender botGender = CharacterGender.Female)
    {
        Slots.Clear();
        for (int i = 0; i < 4; i++)
        {
            Slots.Add(new PlayerSlotInfo
            {
                SlotIndex = i,
                PeerId = i == 0 ? 1 : (i == 1 ? 99 : 0),
                DisplayName = i == 0 ? "P1" : (i == 1 ? "BOT" : $"P{i + 1}"),
                TagColor = PlayerPalette[i],
                Gender = i == 0 ? playerGender : botGender,
                IsOccupied = i < 2
            });
        }
    }

    public void SetupSlotsForLocalTwoPlayer(CharacterGender p1Gender = CharacterGender.Male, CharacterGender p2Gender = CharacterGender.Female)
    {
        Slots.Clear();
        for (int i = 0; i < 4; i++)
        {
            Slots.Add(new PlayerSlotInfo
            {
                SlotIndex = i,
                PeerId = i + 1,
                DisplayName = $"P{i + 1}",
                TagColor = PlayerPalette[i],
                Gender = i == 0 ? p1Gender : p2Gender,
                IsOccupied = i < 2
            });
        }
    }

    private void InitAudioPool()
    {
        string[] sfxNames = ["sfx_charge", "sfx_jump", "sfx_bounce", "sfx_fall", "sfx_item", "sfx_victory"];
        foreach (string name in sfxNames)
        {
            string path = $"res://Assets/Audio/{name}.wav";
            if (ResourceLoader.Exists(path))
            {
                _sfxCache[name] = GD.Load<AudioStream>(path);
            }
        }

        for (int i = 0; i < 6; i++)
        {
            var player = new AudioStreamPlayer();
            AddChild(player);
            _sfxPool.Add(player);
        }
    }

    public void PlaySfx(string name, float pitchScale = 1.0f, float volumeMultiplier = 1.0f)
    {
        if (SfxVolume <= 0.01f || !_sfxCache.TryGetValue(name, out var stream))
        {
            return;
        }

        foreach (var player in _sfxPool)
        {
            if (!player.Playing)
            {
                player.Stream = stream;
                player.PitchScale = Mathf.Clamp(pitchScale, 0.5f, 2.5f);
                float linear = Mathf.Clamp(SfxVolume * volumeMultiplier, 0.001f, 1.0f);
                player.VolumeDb = Mathf.LinearToDb(linear);
                player.Play();
                return;
            }
        }
    }
}
