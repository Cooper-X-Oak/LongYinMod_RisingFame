<div align="center">

# 名扬天下 RisingFame

龙吟立志传 BepInEx 插件。只保留最有效的倍率修正与稳定开关，优先兼容性、启动稳定性和低负担。

[![Release](https://img.shields.io/github/v/release/Cooper-X-Oak/LongYinMod_RisingFame?label=release)](https://github.com/Cooper-X-Oak/LongYinMod_RisingFame/releases/latest)
[![License](https://img.shields.io/github/license/Cooper-X-Oak/LongYinMod_RisingFame)](https://github.com/Cooper-X-Oak/LongYinMod_RisingFame/blob/main/LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-2ea44f)
![Runtime](https://img.shields.io/badge/runtime-Unity%20IL2CPP-ffb000)
![BepInEx](https://img.shields.io/badge/BepInEx-6.0.0--be.755%2B3fab71a-6f42c1)

[下载发布版](https://github.com/Cooper-X-Oak/LongYinMod_RisingFame/releases/latest) · [更新记录](./CHANGELOG.md) · [快速安装](#快速安装) · [问题反馈](https://github.com/Cooper-X-Oak/LongYinMod_RisingFame/issues/new/choose)

![RisingFame cover](./assets/cover.svg)

</div>

## 一句话说明

- `=` 键开关，带蜂鸣和简短日志
- 武学、技艺、好感、门派 / 政务贡献倍率修正
- 抄书效率调整：进度 `x10`，花费 `/10`，时间 `/10`
- 仅保留必要 Hook，优先保证真实入账与启动稳定性

## 快速安装

1. 下载并解压固定版本：`BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip`
2. 将内容解压到 `LongYinLiZhiZhuan.exe` 所在目录。
3. 先不要放其他 mod，手动启动一次游戏，确认生成 `BepInEx/LogOutput.log`。
4. 将 `RisingFame.dll` 放入 `BepInEx/plugins/`。
5. 再次启动游戏，进入游戏后按 `=` 测试开关。

## 支持环境

| 项目 | 说明 |
| --- | --- |
| 平台 | `Windows x64` |
| 游戏 | `Steam 版 龙吟立志传` |
| 运行时 | `Unity IL2CPP` |
| BepInEx | `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip` |
| 安装方式 | 先单独验证 `BepInEx` 能启动，再放入 `RisingFame.dll` |

除以上组合外，其他环境与混装方式不在支持范围内。

## 功能与倍率

| 模块 | 规则 |
| --- | --- |
| 武学读书 / 实战经验 | 起始 `x3.0`，Rank 每 `+1` 倍率 `+0.5`，公式：`max(1.0, 3.0 + 0.5 * heroForceLv)` |
| 技艺经验 | 起始 `x2.0`，Rank 每 `+1` 倍率 `+0.5`，公式：`max(1.0, 2.0 + 0.5 * heroForceLv)` |
| 好感 | 起始 `x1.5`，Rank 每 `+1` 倍率 `+0.5`，仅放大正向变化，公式：`max(1.0, 1.5 + 0.5 * heroForceLv)` |
| 门派 / 政务贡献 | 外门与政务：`(heroForceLv + 1) + fame / 1000`；本门派：`((heroForceLv + 1) + fame / 1000) * 0.5` |
| 抄书 | 进度 `x10`，花费 `/10`，时间 `/10` |
| 开关 | `=` 切换 ON / OFF，关闭后恢复原版行为 |

## 说明

- 武学 / 技艺经验倍率对走同一入账路径的 NPC 也会生效，这是当前版本设计行为。
- 当前实现不追求大而全，只保留有效且相对稳定的核心功能。

## QA

### 1. 装完 BepInEx 就打不开

先不要放 `RisingFame.dll`。如果只装固定版本 BepInEx 仍打不开，问题通常在 BepInEx 环境本身：版本装错、架构装错，或者目录里混有旧文件。

### 2. 装完 DLL 后不生效或进不去

先确认 `BepInEx/LogOutput.log` 里有插件加载信息，并确保 `BepInEx/plugins/` 里只有当前版本 `RisingFame.dll`，不要和其他 mod 一起混测。

### 3. 反馈问题时请提供

1. `BepInEx/LogOutput.log`
2. 游戏根目录截图
3. 你下载的 `BepInEx` 压缩包完整文件名

## License

MIT
