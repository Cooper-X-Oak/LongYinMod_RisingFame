# 名扬天下 RisingFame

龙胤立志传 (LongYinLiZhiZhuan) 的 BepInEx Mod，通过动态倍率减少重复劳动，让你专注于剧情和策略。

A BepInEx mod for 龙胤立志传 that dynamically scales progression rates based on hero rank, reducing grind while preserving game balance.

## Features

### Dynamic Exp & Favor Multiplier — `2 × heroRank`

| Rank | Title | Multiplier |
|------|-------|------------|
| 1 | 武者 | x2 |
| 2 | 游侠 | x4 |
| 3 | 豪杰 | x6 |
| 4 | 大侠 | x8 |
| 5 | 名家 | x10 |
| 6 | 宗师 | x12 |

Applies to:
- **ReadBook Exp** (读书经验) — `HeroData.GetBookExpRate` postfix
- **Fight Exp** (实战经验) — `HeroData.AddSkillFightExp` prefix
- **Favor** (好感度) — `HeroData.ChangeFavor` prefix (positive gains only)

### Dynamic Contribution Multiplier — `heroRank + fame/1000`

| Type | Formula | Example (Rank 3, Fame 2000) |
|------|---------|----------------------------|
| Inner Force (内门派) | `(rank + fame/1000) × 0.5` | x2.5 |
| Outer Force (外门派) | `rank + fame/1000` | x5.0 |
| Govern (治理) | `rank + fame/1000` | x5.0 |

Applies to:
- **Force Contribution** (门派功绩) — `HeroData.ChangeForceContribution` prefix
- **Govern Contribution** (治理功绩) — `HeroData.ChangeGovernContribution` prefix

### Toggle

Press **`=`** to toggle the mod ON/OFF at any time.
- ON: ascending beep (1200→1600 Hz)
- OFF: descending beep (800→400 Hz)

Mod is **ON by default** when the game starts.

## Requirements

- 龙胤立志传 (Steam)
- [BepInEx 6 IL2CPP (bleeding edge)](https://builds.bepinex.dev/projects/bepinex_be) — build 755 or later
- Windows (uses kernel32 Beep for audio feedback)

## Installation

### From Release

1. Download `RisingFame.dll` from [Releases](../../releases)
2. Copy to `<game>/BepInEx/plugins/`
3. Launch the game

### Build from Source

```powershell
# Auto-downloads .NET SDK 8.0 to .tools/ and builds
.\build.ps1

# Copy DLL to game plugins folder
.\install-to-game.ps1
```

Or manually:

```bash
dotnet build -c Release
# Output: bin/Release/net6.0/RisingFame.dll
```

> **Note**: Building requires the game's BepInEx interop DLLs. Update `GameInteropDir` in the `.csproj` if your game is installed in a different path.

## Technical Details

- **Framework**: BepInEx 6.0.0-be.755 + Harmony
- **Runtime**: .NET 6.0 (CoreCLR) on Unity 2020.3.48f1c1 IL2CPP
- **Input**: Polled via `Camera.FireOnPostRender` Harmony postfix with `Time.frameCount` deduplication
- **No disk I/O on hotkey**: Multiplier state is held in memory only — avoids `ConfigEntry` write freezes

## License

MIT
