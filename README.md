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

## [01] 一句话说明

- 这是一个以“稳定、低负担、真入账”为优先目标的《龙吟立志传》BepInEx 插件。
- 核心仍然是倍率修正、开关和兼容性控制，不是大而全功能包。
- `1.8` 版本新增了低风险的 `Alt+R` 上下文一键刷新。

## [02] 核心功能

| 模块        | 规则                                                                                     |
| --------- | -------------------------------------------------------------------------------------- |
| 开关        | `=` 切换 ON / OFF，带蜂鸣与简短日志                                                               |
| 一键刷新      | `Alt+R`，当前支持：突破词条、神兵 / 特殊强化词条、拍卖重开                                                     |
| 武学经验      | 起始 `x3.0`，每升 1 阶 `+0.5`，公式：`max(1.0, 3.0 + 0.5 * heroForceLv)`                         |
| 技艺经验      | 起始 `x2.0`，每升 1 阶 `+0.5`，公式：`max(1.0, 2.0 + 0.5 * heroForceLv)`                         |
| 好感增长      | 起始 `x1.5`，每升 1 阶 `+0.5`，仅放大正向变化，公式：`max(1.0, 1.5 + 0.5 * heroForceLv)`                 |
| 门派 / 官府功绩 | 外门派与官府：`(heroForceLv + 1) + fame / 1000`；本门派：`((heroForceLv + 1) + fame / 1000) * 0.5` |
| 抄书        | 进度 `x10`，花费 `/10`，时间 `/10`                                                             |

## [03] 1.8 新功能：一键刷新

`1.8` 开始新增 `Alt+R` 上下文一键刷新，只在当前已打开的支持界面中生效，目标是减少反复退出重进、反复刷词条的操作负担。

当前支持：

- 突破词条
- 神兵 / 特殊强化词条
- 拍卖重开

演示如下：

![一键刷新武学词条演示](./assets/refresh-demo.gif)

- 技术实现说明：[doc/refresh-implementation-notes.md](./doc/refresh-implementation-notes.md)

## [04] 安装

1. 下载并解压固定版本：`BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip`
2. 解压到 `LongYinLiZhiZhuan.exe` 所在目录。
3. 先不要放其他 mod，手动启动一次游戏，确认生成 `BepInEx/LogOutput.log`。
4. 将 `RisingFame.dll` 放入 `BepInEx/plugins/`。
5. 再次启动游戏，进游戏后按 `=` 测试开关。

## [05] 支持边界

| 项目      | 说明                                       |
| ------- | ---------------------------------------- |
| 平台      | `Windows x64`                            |
| 游戏版本    | `Steam 版《龙吟立志传》`                         |
| 运行时     | `Unity IL2CPP`                           |
| BepInEx | `6.0.0-be.755+3fab71a`                   |
| 安装方式    | 先单独验证 `BepInEx` 能启动，再放入 `RisingFame.dll` |

除以上组合外，其他环境与混装方式不在当前支持范围内。

## [06] 说明

- 武学经验与技艺经验的倍率，对走同一入账路径的 NPC 同样生效。
- 当前版本优先“真入账 + 低风险”，不追求大而全。
- `Alt+R` 是上下文触发，只调用当前已打开界面的原生控制器方法，不做后台扫描。
- 这次刷新功能的逆向定位、失败原因和最终实现路径，已经整理到 [doc/refresh-implementation-notes.md](./doc/refresh-implementation-notes.md)。

## [07] QA

### [07.1] 装完 BepInEx 就打不开

先不要放 `RisingFame.dll`。如果只装固定版本的 BepInEx 仍然打不开，问题通常在 BepInEx 环境本身：版本错误、架构错误，或目录里混有旧文件。

### [07.2] 装完 DLL 后不生效或进不去

先确认 `BepInEx/LogOutput.log` 里有插件加载信息，并确保 `BepInEx/plugins/` 里只有当前版本的 `RisingFame.dll`，不要和其他 mod 混装测试。

### [07.3] 反馈问题时请提供

1. `BepInEx/LogOutput.log`
2. 游戏根目录截图
3. 你下载的 `BepInEx` 压缩包完整文件名

## [08] 文档导航

这套文档现在按四层来组织：

- 需求侧：先回答“玩家要什么、版本应该往哪走”
- 技术侧：再回答“原生管线长什么样、挂点该放哪”
- 实现侧：最后回答“某个功能是怎么真正落地的”
- 资料层：保留抓取样本与原始素材，方便回溯

建议阅读顺序：

1. 先看需求侧，确认玩家到底在要什么
2. 再看原生管线，确认哪些点适合挂
3. 最后看具体功能实现，确认一条功能是怎么落地的

每篇文档最多展开 4 个可直达章节，点进去可以直接跳到对应部分。

### [08.1] 需求侧

#### [08.1.1] [社区需求与功能提案](./doc/mod-demand-proposals.md)

> 为什么有这篇：先把社区高频需求、版本路线和优先级讲清楚，避免功能一路加下去却偏离真正高价值方向。

- [需求归类](./doc/mod-demand-proposals.md#demand-categories)
- [提案优先级](./doc/mod-demand-proposals.md#demand-priority)
- [P0 细化定义](./doc/mod-demand-proposals.md#demand-p0-scope)
- [落地顺序](./doc/mod-demand-proposals.md#demand-roadmap)

#### [08.1.2] [Bilibili 需求二次分析](./doc/bilibili-demand-analysis.md)

> 为什么有这篇：把 B 站视频标题、字幕和评论区里的真实诉求再细化一轮，补足“提案层”之外的用户声音。

- [重点样本](./doc/bilibili-demand-analysis.md#bili-analysis-samples)
- [二次结论](./doc/bilibili-demand-analysis.md#bili-analysis-conclusion)
- [路线启发](./doc/bilibili-demand-analysis.md#bili-analysis-insights)
- [后续研究方向](./doc/bilibili-demand-analysis.md#bili-analysis-next)

### [08.2] 技术侧

#### [08.2.1] [原生 Hook / 管线研究](./doc/native-hook-research.md)

> 为什么有这篇：把原生实现拆成真实入账层、流程层和展示层，后续加功能时不再靠猜。

- [稳定核心挂点](./doc/native-hook-research.md#native-core-hooks)
- [武学经验管线](./doc/native-hook-research.md#native-fight-pipeline)
- [原生通知管线](./doc/native-hook-research.md#native-notify-pipeline)
- [挂点建议](./doc/native-hook-research.md#native-hook-suggestions)

#### [08.2.2] [P0 低风险减肝 / 交互提效包研究](./doc/p0-qol-pipe-research.md)

> 为什么有这篇：把“低风险、低负担”的做法单独收敛出来，避免后续实现一上来就走重逻辑和高风险路线。

- [目标拆分](./doc/p0-qol-pipe-research.md#p0-target-split)
- [读书流程研究](./doc/p0-qol-pipe-research.md#p0-read-book)
- [原生提示管线](./doc/p0-qol-pipe-research.md#p0-native-feedback)
- [实现优先级建议](./doc/p0-qol-pipe-research.md#p0-priority)

### [08.3] 实现侧

#### [08.3.1] [一键刷新功能逆向与实现说明](./doc/refresh-implementation-notes.md)

> 为什么有这篇：把 `1.8` 一键刷新从踩坑、逆向到最终落地完整记下来，方便以后复用这套方法做别的功能。

- [初版失败现象](./doc/refresh-implementation-notes.md#refresh-first-failure)
- [原生控制器入口](./doc/refresh-implementation-notes.md#refresh-native-controllers)
- [最终实现路径](./doc/refresh-implementation-notes.md#refresh-final-path)
- [未来启发](./doc/refresh-implementation-notes.md#refresh-future)

### [08.4] 资料层

- 原始抓取资料包见：[doc/bilibili/README.md](./doc/bilibili/README.md)

## License

MIT
