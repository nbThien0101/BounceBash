# Bounce Bash — The Adventure to the Forbidden Temple (Godot 4.7 C#)

> **Phiên bản:** `v0.1` (Cập nhật ngày 07/11/2026)  
> **Nền tảng:** Godot Engine 4.7 Mono (`net8.0`, C# SDK 4.7.2)  
> **Tài liệu tham chiếu:** *Bounce Bash Research & Game Design Document (GDD v0.9.5)*

---

## 1. Tổng quan Dự án & Cập nhật Phiên bản v0.1

Phiên bản **v0.1** mang đến các nâng cấp toàn diện về hệ thống chế độ chơi, cơ chế tụ lực nhảy tuần hoàn, trí tuệ nhân tạo Bot AI PvE, tính năng sinh tồn nhiều người chơi (Multiplayer Elimination), cùng cơ chế tạo lại bản đồ (Regenerate Map).

### 1.1. Các chế độ chơi cốt lõi (GDD Slide 06 & 19)
1. **Solo (1 Người chơi - Tự leo tháp):**
   - Người chơi đơn độc chinh phục 3 tầng tháp (Sector 1: Rừng rậm, Sector 2: Núi đá, Sector 3: Đền cổ).
   - Camera auto-scroll cuộn lên liên tục với tốc độ tăng dần theo từng sector.
   - Chạm vào Lọ thuốc trường sinh (Elixir) trên đỉnh đền để giành chiến thắng.
2. **PvE (Đấu Bot AI):**
   - 1 người chơi đối đầu 1 Bot AI trực tiếp trên cùng bản đồ leo tháp.
   - Camera dùng chung bám theo người dẫn đầu (Dynamic Leader-Follow).
   - Mỗi bên có 3 lượt sống; tranh giành bảo vật King of the Hill tại GoalZone (giữ vùng đếm lùi 5 giây để thắng).
   - Bot AI (`BotController.cs`) chạy trực tiếp cục bộ bằng thuật toán định tuyến waypoint bệ leo, tự căn lực nhảy và né chướng ngại.
3. **PvP (Mạng LAN 2–4 người chơi):**
   - Kết nối mạng nội bộ Host-Authoritative qua `ENetMultiplayerPeer` (cổng mặc định `7777`).
   - Truyền xung lực va chạm người-người trong không trung, nhặt vật phẩm hiệu ứng (Kinetic Nullifier, Direction Lock).
   - Tranh chấp GoalZone: Đồng hồ tạm dừng khi có từ 2 người trở lên cùng đứng trong vùng đích.
4. **Local 2-Player Test:**
   - 2 người chơi thi đấu đối kháng trực tiếp trên cùng một bàn phím (P1: Phím Mũi tên + Enter; P2: Phím WASD + Space).

---

### 1.2. Cơ chế Tụ lực Nhảy Tuần hoàn (Ping-Pong Min ↔ Max)
- **Chu kỳ tuần hoàn (GDD Slide 09 & 20):** Khi giữ phím nhảy, thanh lực tăng dần từ `Min (0%)` $\to$ `Max (100%)` rồi giảm ngược lại từ `Max` $\to$ `Min` liên tục.
- **Tính hợp lệ tại mọi thời điểm:** Người chơi có thể nhả phím ở bất kỳ thời điểm nào trong pha tăng hoặc pha giảm, lực nhảy sẽ lấy chính xác giá trị tại thời điểm nhả phím.
- **Phản hồi âm thanh & thị giác:**
  - Thanh HUD hiển thị biểu tượng chiều tụ lực (`▲ TĂNG` / `▼ GIẢM`).
  - Âm thanh SFX tụ lực (`sfx_charge.wav`) tự động tăng/giảm cao độ (pitch modulation) theo nhịp lực.

---

### 1.3. Cải tiến Multiplayer Elimination & Quản lý Trận đấu
- **Cơ chế Sinh tồn Cuối cùng (Last-Man-Standing):**
  - Trận đấu PvP nhiều người chơi (2-4 người) **chỉ kết thúc khi còn đúng 1 người chơi sống sót** (hoặc hòa nếu tất cả cùng rơi khỏi màn hình).
  - Ví dụ: Trong trận 3 người, nếu 1 người rơi khỏi khung hình quá số lần cho phép (hết 3 mạng) $\to$ **2 người chơi còn lại vẫn tiếp tục thi đấu**.
- **Thông báo Loại trừ & Chế độ Quan sát (Spectator):**
  - Khi một người chơi bị loại, HUD hiển thị banner thông báo loại trừ cho toàn bộ phòng (`P... ĐÃ BỊ LOẠI! CÒN ... NGƯỜI CHƠI`), đồng thời đồng bộ qua RPC `RpcEliminationNotice`.
  - Người chơi bị loại chuyển sang chế độ khán giả với thông báo theo dõi trận đấu.
- **Hệ thống Camera cứu hộ (Dynamic Camera Fall Recovery):**
  - Khắc phục triệt để lỗi camera kẹt trên cao khi người dẫn đầu bị loại: Camera tự động hạ mượt mà xuống người sống sót cao nhất hiện tại, bảo đảm những người leo phía dưới không bị kéo chết oan do rơi khỏi khung hình rỗng.
- **Tùy chọn Chơi lại sau trận (End Game Result Overlay):**
  - **`CHƠI LẠI (GIỮ MAP)`**: Tái đấu ngay trên hạt giống (seed) bản đồ hiện tại để phục thù cùng layout bệ leo.
  - **`TẠO MAP MỚI (REGEN)`** (Phím tắt `R`): Sinh seed ngẫu nhiên mới, lắp ráp tháp leo hoàn toàn mới và tự động đồng bộ seed cho toàn bộ Client trong phòng LAN qua `HostBroadcastStartMatch`.
  - **`← VỀ MENU CHÍNH`**: Dọn dẹp trạng thái và quay về sảnh Lobby.
  - Đồng bộ bảng kết quả qua RPC `RpcMatchEnd` cho máy Client.

---

## 2. Vấn đề Blocking Cần Giải Quyết (Known Blocking Issue) & Đề xuất Hướng Xử Lý

> [!WARNING]
> ### Vấn đề Blocking v0.1: Không thể đẩy người chơi đang đứng yên (Standing Player Cannot Be Pushed)
> 
> * **Hiện tượng (Symptom):**  
>   Khi một người chơi đang đứng yên trên bệ (`Grounded`, vận tốc $v_x = 0$), người chơi khác khi đi bộ (`Walking` / kinematic movement) hoặc áp sát vào sẽ bị chặn lại như đâm vào một bức tường bất động; người đi tới không thể đẩy người đang đứng yên dịch chuyển hay rơi khỏi mép bệ.
> 
> * **Nguyên nhân kỹ thuật (Technical Root Cause):**  
>   Bộ xử lý va chạm [`CollisionResolver.cs`](Scripts/Physics/CollisionResolver.cs) hiện chỉ xử lý truyền xung lực (impulse transfer) khi hai nhân vật có vận tốc va chạm lớn trong không trung hoặc trong pha nhảy nảy bật. Khi cả hai ở trên mặt đất (`IsOnFloor()`), chuyển động sử dụng `MoveAndSlide()` của Godot mặc định đối xử với thân `CharacterBody2D` khác như chướng ngại vật cố định nếu không có cơ chế truyền lực ngang (horizontal push displacement).
> 
> * **Đề xuất Giải pháp (Proposed Resolution for Next Sprint):**  
>   1. **Cơ chế Đẩy ngang trên mặt đất (Ground Shove / Horizontal Kinematic Push):**  
>      Bổ sung kiểm tra va chạm mặt đất trong `PlayerController.PhysicsProcess`: Nếu nhân vật A đang di chuyển vào nhân vật B đang đứng yên trên cùng mặt sàn, áp dụng một lực đẩy ngang tỷ lệ thuận với vận tốc của A (có trừ lực ma sát bệ bám):  
>      $$v_{push} = v_{walker} \times \alpha_{push} \quad (\text{với } \alpha_{push} \approx 0.6 \sim 0.8)$$
>   2. **Trượt khỏi mép bệ (Ledge Pushing):**  
>      Cho phép người đứng yên bị đẩy dịch chuyển dọc theo bề mặt bệ; nếu bị đẩy vượt ra khỏi rìa bệ (`!IsOnFloor()`), người đó sẽ rơi xuống không trung và phải tìm cách nhảy bám lại hoặc mất mạng.
>   3. **Độ ưu tiên truyền lực (Momentum Advantage):**  
>      Người đang có vận tốc chạy đà hoặc vừa tiếp đất có lực đẩy lớn hơn người đứng yên; nếu cả hai cùng đi ngược chiều nhau, người có khối lượng hoặc vận tốc cao hơn sẽ đẩy lùi người còn lại.

---

## 3. Cấu trúc Thư mục Dự án

```text
Bounce Bash/
├── project.godot                  # Cấu hình Godot 4.7 C# (Pixel Art Nearest 640x360 -> 1280x720)
├── BounceBash.csproj              # Cấu hình C# SDK (.NET 8.0, Godot.NET.Sdk/4.7.2)
├── BounceBash.sln                 # Solution Visual Studio / Rider
├── Play_BounceBash.bat            # Script khởi động nhanh game
├── Open_in_Godot_Editor.bat       # Script mở Godot Editor
├── Resources/
│   └── DefaultTuningConfig.tres   # Resource cấu hình 16+ thông số vật lý & gameplay
├── Scenes/
│   └── Main.tscn                  # Scene chính khởi chạy RoundManager
├── Assets/
│   ├── Sprites/
│   │   ├── Characters/            # Sprite 32x32 native Explorer (Idle, Move, Charge x South/East/West)
│   │   ├── Icons/                 # Icon Kinetic Nullifier & Direction Lock (Left/Straight/Right)
│   │   ├── Tilesets/              # Tileset 16x16 DB32 (Rừng rậm, Núi đá, Đền cổ)
│   │   └── Props/                 # Lọ thuốc trường sinh (Elixir), bẫy gai (Spikes)
│   └── Audio/                     # SFX (Charge, Jump, Bounce, Fall, Item, Victory)
└── Scripts/
    ├── Config/
    │   └── TuningConfig.cs        # Tham số vật lý, camera, GoalZone, respawn
    ├── Core/
    │   ├── GameEnums.cs           # Chế độ chơi (SinglePlayer, PvEBot, PvPDefault, PvPQuickMatch)
    │   ├── GameSession.cs         # Lưu phiên chơi, cấu hình nhân vật P1-P4, SFX pool
    │   └── RoundManager.cs        # Điều phối trận đấu, luật thắng thua, sinh tồn multiplayer, đổi map
    ├── Player/
    │   ├── PlayerController.cs    # State Machine, tụ lực Ping-Pong Min↔Max, va chạm vật lý
    │   └── BotController.cs       # Trí tuệ nhân tạo Bot AI cho chế độ PvE
    ├── Physics/
    │   └── CollisionResolver.cs   # Giải quyết va chạm truyền động lượng nảy bật
    ├── Level/
    │   ├── SectorModule.cs        # Cấu trúc module địa hình dọc chuẩn hóa
    │   └── MapAssembler.cs        # Ghép tháp ngẫu nhiên từ pool 5 biến thể (Tổ hợp A & B)
    ├── Camera/
    │   └── CameraRig.cs           # Camera cuộn tự do (Solo) & Camera cứu hộ bám leader (PvE/PvP)
    ├── Gameplay/
    │   ├── LivesRespawnSystem.cs  # Hệ thống 3 mạng sống, chọn bệ hồi sinh an toàn
    │   ├── ItemSystem.cs          # Quản lý nhặt & áp dụng hiệu ứng vật phẩm
    │   └── GoalZone.cs            # Vùng chiếm giữ bảo vật King of the Hill
    ├── Net/
    │   └── NetworkManager.cs      # Mạng LAN Host-Authoritative qua ENetMultiplayerPeer
    └── UI/
        ├── MainMenuUI.cs          # Menu chính, Submenu Một người (Solo/PvE), Lobby LAN
        └── GameHud.cs             # HUD trạng thái người chơi, thông báo loại trừ, overlay kết quả
```

---

## 4. Hướng dẫn Khởi chạy và Biên dịch

### 4.1. Chạy nhanh qua Batch File
- **Vào game trực tiếp:** Nhấp đúp vào `Play_BounceBash.bat`.
- **Mở giao diện Godot Editor:** Nhấp đúp vào `Open_in_Godot_Editor.bat`.

### 4.2. Chạy từ Dòng lệnh (PowerShell)
```powershell
# Chuyển vào thư mục dự án
cd "E:\Study\game\BounceBash"

# Biên dịch mã nguồn C# (.NET 8.0)
dotnet build BounceBash.csproj

# Khởi chạy game qua Godot Engine
& "E:\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path .
```

---

## 5. Nguồn Asset & Ghi công (Asset Provenance)

- **Sprite Nhân vật (`Male Explorer` & `Female Explorer`, 32×32 pixel):** Thiết kế và tinh chỉnh bằng PixelLab.ai, tham chiếu tỉ lệ từ *Greg 32×32 Character Sprite Pack* (PixelPigeon).
- **Môi trường & Bảng màu:** Bảng màu kinh điển **DawnBringer 32 (DB32)**, định hướng mỹ thuật từ *Open Pixel Project (OPP) Jungle & Temple*.
- **Vật lý nhảy & va chạm:** Tham khảo quy đổi hệ số từ *Jump King Parabole* (`qequ/jump-king-parabole`) quy chuẩn sang đơn vị `px/s` và `px/s²`.
- **Âm thanh:** *Kenney Impact Sounds*, *Character Grunts Library*, và thư viện SFX của *Bounce Bash Preview*.
