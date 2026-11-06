# Bounce Bash — The Adventure to the Forbidden Temple (Godot 4.7 C#)

Bản khởi tạo dự án (Initial Project) bám sát **GDD v0.9.2 (CO3045)** được chuyển đổi từ Unity C# sang **Godot 4.7 Mono (C# / .NET 8.0)**.

## 1. Cấu trúc Thư mục Dự án

```text
Bounce Bash/
├── project.godot                  # Cấu hình Godot 4.7 C# (Nearest-neighbor, 640x360 -> 1280x720, 60Hz)
├── BounceBash.csproj              # Cấu hình C# SDK (Godot.NET.Sdk/4.7.2, net8.0)
├── BounceBash.sln                 # Solution C#
├── nuget.config                   # Nguồn NuGet cục bộ từ Godot_mono.app & nuget.org
├── Resources/
│   └── DefaultTuningConfig.tres   # Resource cấu hình thông số vật lý & cân bằng (thay cho ScriptableObject)
├── Scenes/
│   └── Main.tscn                  # Scene chính khởi chạy RoundManager
├── Assets/
│   ├── Sprites/
│   │   ├── Characters/            # Sprite 32x32 native Male & Female Explorer (Idle, Move, Charge x South/East/West)
│   │   ├── Icons/                 # Biểu tượng hiệu ứng Kinetic Nullifier & Direction Lock (Trái/Thẳng/Phải)
│   │   ├── Tilesets/              # Tileset 16x16 bảng màu DB32 cho 3 chủ đề: Rừng, Núi đá, Đền cổ
│   │   └── Props/                 # Lọ thuốc trường sinh (Elixir), bẫy gai (Spikes)
│   └── Audio/                     # SFX mẫu (Charge, Jump, Bounce, Fall, Item, Victory)
└── Scripts/
    ├── Config/
    │   └── TuningConfig.cs        # Quản lý 16+ thông số vật lý, camera, respawn, trận đấu (Mục 1.3.2)
    ├── Core/
    │   ├── GameEnums.cs           # Enum trạng thái, chế độ chơi, tổ hợp map
    │   ├── GameSession.cs         # Autoload lưu cấu hình phiên chơi, P1-P4 slot, SFX pool
    │   └── RoundManager.cs        # Điều phối luồng ván đấu (Menu -> Ghép Map -> Đang chơi -> Kết quả)
    ├── Player/
    │   └── PlayerController.cs    # State Machine 5 trạng thái, nhảy tích lực Jump King, đối kháng vật lý
    ├── Physics/
    │   └── CollisionResolver.cs   # Giải xung va chạm người-người & bật tường (tránh double-impulse)
    ├── Level/
    │   ├── SectorModule.cs        # Cấu trúc 1 module dọc (Entry/Exit chuẩn hóa, bệ hồi sinh, điểm đặt item)
    │   └── MapAssembler.cs        # Ghép 3 Sector từ pool 5 biến thể (Tổ hợp A & Tổ hợp B)
    ├── Camera/
    │   └── CameraRig.cs           # Camera auto-scroll (1 người) & Camera chung bám người cao nhất (PvP)
    ├── Gameplay/
    │   ├── LivesRespawnSystem.cs  # Hệ thống 3 lượt sống & chọn bệ hồi sinh động Main/Aux
    │   ├── ItemSystem.cs          # Quản lý Kinetic Nullifier (unique) & Direction Lock
    │   └── GoalZone.cs            # Vùng tranh bảo vật King of the Hill (Đếm lùi cá nhân / Tạm dừng khi tranh chấp)
    ├── Net/
    │   └── NetworkManager.cs      # Mạng LAN Host-Authoritative qua ENetMultiplayerPeer (cổng mặc định 7777)
    └── UI/
        ├── MainMenuUI.cs          # Đầy đủ 5 màn hình giao diện theo đúng phác thảo GDD 1.6.3
        └── GameHud.cs             # HUD trong trận (P1-P4, lượt sống, lực nhảy, icon hiệu ứng, đồng hồ đích)
```

## 2. Cách Mở và Chạy Dự án

1. Mở **Godot 4.7 Mono** (`/Users/nguyenbaothien/Downloads/Godot_mono.app`).
2. Chọn **Import** -> Trỏ tới thư mục `/Users/nguyenbaothien/Documents/bachkhoa/261/ltgame/Bounce Bash/project.godot`.
3. Nhấn **Build** (biểu tượng cái búa ở góc trên bên phải) hoặc nhấn **F5 (Run Project)**.
   - Hoặc chạy trực tiếp từ Terminal:
     ```bash
     export PATH="$HOME/.dotnet:$PATH"
     dotnet build BounceBash.csproj
     /Users/nguyenbaothien/Downloads/Godot_mono.app/Contents/MacOS/Godot --path .
     ```

## 3. Nguồn Asset & Ghi công (Asset Provenance — Phụ lục B GDD)

- **Nhân vật (`Male Explorer` & `Female Explorer`, 32×32 native):** Phát triển/chỉnh sửa bằng **PixelLab.ai** [7], đối chiếu tỉ lệ tham khảo từ *Greg 32×32 Character Sprite Pack* của PixelPigeon [5] (không sử dụng trực tiếp sprite Greg gốc).
- **Môi trường & Bảng màu:** Tham khảo định hướng *Open Pixel Project (OPP) Jungle & Temple* [6] và bảng màu **DawnBringer 32 (DB32)**.
- **Tham khảo Vật lý nhảy:** Quy đổi hệ số từ bản mod *Jump King Parabole* (`qequ/jump-king-parabole`) [4] từ đơn vị `per-frame (60Hz)` sang `px/s` và `px/s²`.
- **Tham khảo Âm thanh:** *Kenney Impact Sounds* [9], *Character Grunts Library* [8], *Bounce Bash SFX Preview v0.8* [10], và *Marma Epic Adventure Orchestral* [11].
