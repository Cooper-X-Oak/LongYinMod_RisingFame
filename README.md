<div align="center">

# 名扬天下 RisingFame

《龙吟立志传》BepInEx 插件。当前版本聚焦稳定、低负担的核心增强：倍率修正、开关，以及低风险的一键刷新。

[![Release](https://img.shields.io/github/v/release/Cooper-X-Oak/LongYinMod_RisingFame?label=release)](https://github.com/Cooper-X-Oak/LongYinMod_RisingFame/releases/latest)
[![License](https://img.shields.io/github/license/Cooper-X-Oak/LongYinMod_RisingFame)](https://github.com/Cooper-X-Oak/LongYinMod_RisingFame/blob/main/LICENSE)
![Platform](https://img.shields.io/badge/platform-Windows%20x64-2ea44f)
![Runtime](https://img.shields.io/badge/runtime-Unity%20IL2CPP-ffb000)
![BepInEx](https://img.shields.io/badge/BepInEx-6.0.0--be.755%2B3fab71a-6f42c1)

[下载发布版](https://github.com/Cooper-X-Oak/LongYinMod_RisingFame/releases/latest) | [更新记录](./CHANGELOG.md) | [贡献说明](./CONTRIBUTING.md) | [问题反馈](https://github.com/Cooper-X-Oak/LongYinMod_RisingFame/issues/new/choose)

![RisingFame cover](./assets/cover.svg)

</div>

## 演示

- 刷新功能测试视频：[刷新测试.mp4](./assets/刷新测试.mp4)
- 技术实现说明：[doc/refresh-implementation-notes.md](./doc/refresh-implementation-notes.md)

## 功能

| 模块        | 规则                                                                                     |
| --------- | -------------------------------------------------------------------------------------- |
| 开关        | `=` 切换 ON / OFF，带蜂鸣与简短日志                                                               |
| 一键刷新      | `Alt+R`，当前支持：突破词条、神兵 / 特殊强化词条、拍卖重开                                                     |
| 武学经验      | 起始 `x3.0`，每升 1 阶 `+0.5`，公式：`max(1.0, 3.0 + 0.5 * heroForceLv)`                         |
| 技艺经验      | 起始 `x2.0`，每升 1 阶 `+0.5`，公式：`max(1.0, 2.0 + 0.5 * heroForceLv)`                         |
| 好感增长      | 起始 `x1.5`，每升 1 阶 `+0.5`，仅放大正向变化，公式：`max(1.0, 1.5 + 0.5 * heroForceLv)`                 |
| 门派 / 官府功绩 | 外门派与官府：`(heroForceLv + 1) + fame / 1000`；本门派：`((heroForceLv + 1) + fame / 1000) * 0.5` |
| 抄书        | 进度 `x10`，花费 `/10`，时间 `/10`                                                             |

## 安装

1. 下载并解压固定版本：`BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip`
2. 解压到 `LongYinLiZhiZhuan.exe` 所在目录。
3. 先不要放其他 mod，手动启动一次游戏，确认生成 `BepInEx/LogOutput.log`。
4. 将 `RisingFame.dll` 放入 `BepInEx/plugins/`。
5. 再次启动游戏，进游戏后按 `=` 测试开关。

## 支持边界

| 项目      | 说明                                       |
| ------- | ---------------------------------------- |
| 平台      | `Windows x64`                            |
| 游戏版本    | `Steam 版《龙吟立志传》`                         |
| 运行时     | `Unity IL2CPP`                           |
| BepInEx | `6.0.0-be.755+3fab71a`                   |
| 安装方式    | 先单独验证 `BepInEx` 能启动，再放入 `RisingFame.dll` |

除以上组合外，其他环境与混装方式不在当前支持范围内。

## 说明

- 武学经验与技艺经验的倍率，对走同一入账路径的 NPC 同样生效。
- 当前版本优先“真入账 + 低风险”，不追求大而全。
- `Alt+R` 是上下文触发，只调用当前已打开界面的原生控制器方法，不做后台扫描。
- 这次刷新功能的逆向定位、失败原因和最终实现路径，已经整理到 [doc/refresh-implementation-notes.md](./doc/refresh-implementation-notes.md)。

## QA

### 1. 装完 BepInEx 就打不开

先不要放 `RisingFame.dll`。如果只装固定版本的 BepInEx 仍然打不开，问题通常在 BepInEx 环境本身：版本错误、架构错误，或目录里混有旧文件。

### 2. 装完 DLL 后不生效或进不去

先确认 `BepInEx/LogOutput.log` 里有插件加载信息，并确保 `BepInEx/plugins/` 里只有当前版本的 `RisingFame.dll`，不要和其他 mod 混装测试。

### 3. 反馈问题时请提供

1. `BepInEx/LogOutput.log`
2. 游戏根目录截图
3. 你下载的 `BepInEx` 压缩包完整文件名

## 文档

- [逆向总览：原生 Hook / 管线研究](./doc/native-hook-research.md)
- [P0 提效研究：低风险减肝 / 交互提效包](./doc/p0-qol-pipe-research.md)
- [需求侧研究：Steam / B 站 Mod 需求分析](./doc/mod-demand-proposals.md)
- [刷新功能技术说明：Alt+R 逆向与实现](./doc/refresh-implementation-notes.md)

## License

MIT
