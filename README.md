# 名扬天下 RisingFame

龙吟立志传 BepInEx 插件（稳定精简版）。

当前版本：`v1.7.0`

## 30 秒快速安装

如果你只想用最短路径安装，请直接照下面做：

1. 下载并解压固定版本：
   `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip`
2. 将压缩包内容解压到 `LongYinLiZhiZhuan.exe` 所在目录。
3. 手动启动一次游戏，确认已生成 `BepInEx/LogOutput.log`。
4. 将 `RisingFame.dll` 放入 `BepInEx/plugins/`。
5. 再次手动启动游戏，进入游戏后按 `=` 测试开关。

如果你装完打不开，请不要先改配置，直接看下面的“受支持环境”和 “QA / 常见问题”。

## 受支持环境

为尽量降低“装了打不开”的概率，本项目只支持下面这一套固定环境：

- 平台：`Windows x64`
- 游戏渠道：`Steam 版 龙吟立志传`
- 游戏运行时：`Unity IL2CPP`
- BepInEx 固定版本：`BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip`
- mod 安装方式：先单独验证 `BepInEx` 能启动，再放入 `RisingFame.dll`

官方参考：

- [Installing BepInEx on Il2Cpp Unity](https://docs.bepinex.dev/v6.0.0-pre.1/articles/user_guide/installation/unity_il2cpp.html)
- [BepInEx Bleeding Edge #755](https://builds.bepinex.dev/projects/bepinex_be)

除以上环境外，其他安装方式和组合一律视为不受支持环境。

## 功能概览

- `=` 键开关插件（蜂鸣提示 + 简短日志）
- 读书经验倍率（动态）
- 实战经验倍率（动态）
- 技艺经验倍率（动态）
- 好感提升倍率（动态，仅正向变化）
- 门派/政务贡献倍率
- 抄书效率：进度 `x10`，花费 `/10`，时间显示 `/10`

## 倍率规则

### 读书 / 实战经验

角色等级倍率使用以下规则：

- Rank1: `x3.0`
- 每升 1 级：`+0.5`
- Rank6: `x5.5`

公式：`当前经验基础值 * max(1.0, 3.0 + 0.5 * heroForceLv)`

说明：

- 显示倍率（理论经验百分比）与实际入账使用同一套倍率规则。
- 实战经验同时覆盖多个入账路径，避免部分场景不生效。
- 当前实现不区分主角与 NPC：只要角色走到同一套武学经验入账路径，读书/实战经验倍率都会生效。
- 这意味着高难度下 NPC 的武学成长也会一起加快，属于当前版本的设计行为，而不是 bug。

### 技艺经验

角色等级倍率使用以下规则：

- Rank1: `x2.0`
- 每升 1 级：`+0.5`
- Rank6: `x4.5`

公式：`当前技艺经验基础值 * max(1.0, 2.0 + 0.5 * heroForceLv)`

说明：

- 当前优先修改真实入账点，确保技艺经验实际结算会放大。
- 当前实现同样不区分主角与 NPC：只要角色走到同一套技艺经验入账路径，倍率都会生效。

### 好感

角色等级倍率使用以下规则：

- Rank1: `x1.5`
- 每升 1 级：`+0.5`
- Rank6: `x4.0`

公式：`当前好感基础值 * max(1.0, 1.5 + 0.5 * heroForceLv)`（仅正向变化）

### 门派 / 政务贡献

- 外门与政务：`(heroForceLv + 1) + fame / 1000`
- 本门派：`((heroForceLv + 1) + fame / 1000) * 0.5`

## 玩家安装

### 唯一受支持的傻瓜安装方式

1. 下载并解压唯一受支持的 `BepInEx` 版本：
   `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip`
2. 将压缩包内容直接解压到游戏根目录，也就是 `LongYinLiZhiZhuan.exe` 所在目录。
3. 先不要放入任何其他 mod，也不要放入 `RisingFame.dll`。
4. 手动启动一次游戏，只验证 `BepInEx` 能否正常创建目录和日志。
5. 退出游戏后，确认已经生成：
   - `BepInEx/config/BepInEx.cfg`
   - `BepInEx/LogOutput.log`
6. 将 `RisingFame.dll` 放到：
   `BepInEx/plugins/`
7. 再次手动启动游戏，进入游戏后按 `=` 测试开关。

### 安装成功后，游戏目录应当包含

- `LongYinLiZhiZhuan.exe`
- `winhttp.dll`
- `doorstop_config.ini`
- `BepInEx/`
- `dotnet/`
- `BepInEx/plugins/RisingFame.dll`

## 支持边界

以下情况不属于受支持安装方式：

- 使用 `Mono` 版 `BepInEx`
- 使用 `x86` 版 `BepInEx`
- 使用除 `6.0.0-be.755+3fab71a` 之外的其他 `BepInEx` 版本
- 在已有旧版 `BepInEx` 的目录上直接混合覆盖
- 首次安装时同时混装其他 mod、调试工具或插件
- 手动修改 `doorstop_config.ini`
- 手动修改 `BepInEx.cfg`
- 使用作者本地开发脚本作为玩家安装方式

玩家只需要：

- 安装固定版本的 `BepInEx`
- 放入 `RisingFame.dll`
- 手动启动游戏测试

玩家不需要：

- 跑脚本
- 改配置
- 自己判断 `Mono / IL2CPP`
- 自己判断 `x86 / x64`

## QA / 常见问题

### Q1：装完 `BepInEx` 后，游戏直接打不开

先不要放 `RisingFame.dll`，只保留固定版本的 `BepInEx`：

- `BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.755+3fab71a.zip`

如果此时仍然打不开，问题不在本 mod，而在 `BepInEx` 环境本身。常见原因：

- 装成了 `Mono` 版
- 装成了 `x86` 版
- 混入了旧版 `BepInEx` 文件
- 游戏目录里残留其他 loader 或旧 mod 文件

建议处理方式：

1. 备份存档。
2. 清理游戏目录中的旧 `BepInEx`、`dotnet`、`winhttp.dll`、`doorstop_config.ini`。
3. 重新解压固定版本的 `BepInEx`。
4. 先只验证 `BepInEx` 能否独立启动。

### Q2：装完 `BepInEx` 能进游戏，但放入 `RisingFame.dll` 后打不开

请先确认 `BepInEx/LogOutput.log` 是否出现类似下面的加载信息：

- `Loading [RisingFame - MingYangTianXia 1.7.0]`
- `RisingFame v1.7.0 loaded. Mod ON. Press '=' to toggle.`

如果没有出现，优先检查：

- `RisingFame.dll` 是否放在 `BepInEx/plugins/`
- 是否还有其他 mod 一起混装
- 是否把旧版 `RisingFame.dll` 放进了别的插件目录

### Q3：按 `=` 只有蜂鸣，没有效果

请分别测试：

1. 按 `=` 是否会出现蜂鸣和 `ON/OFF` 切换日志。
2. 关闭状态下数值是否恢复原版。
3. 对应功能是否属于当前版本支持范围。

当前版本支持：

- 武学读书经验
- 武学实战经验
- 技艺经验
- 好感提升
- 门派/政务贡献
- 抄书效率与花费

### Q4：为什么 NPC 也会一起变强

这是当前版本的设计行为，不是 bug。

- 武学经验倍率对走同一套入账路径的角色生效
- 技艺经验倍率对走同一套入账路径的角色生效
- 因此主角与 NPC 都可能一起成长更快

这也是当前版本在高难度下维持一定平衡感的一部分。

### Q5：反馈问题时要提供什么

如果要反馈“打不开 / 不生效 / 很慢”，请一次性提供下面三样：

1. `BepInEx/LogOutput.log`
2. 游戏根目录截图
3. 你下载的 `BepInEx` 压缩包完整文件名

没有这三样信息时，很难判断是：

- `BepInEx` 安装问题
- 架构装错
- 环境混装
- mod 本身问题

### Q6：为什么不提供脚本安装

作者本地 `ps1` 脚本包含个人开发路径和本地环境假设，不适合作为公开安装方式。

因此公开发布只支持：

- 固定版本 `BepInEx`
- 手动解压
- 手动放入 `RisingFame.dll`

## 开发说明

仓库中的本地 `ps1` 脚本仅用于作者个人开发，不属于开源发布内容，也不是玩家安装方式的一部分。

如果你是玩家，请忽略任何脚本相关说明，只按照上面的“唯一受支持的傻瓜安装方式”安装。

## 稳定性说明

此版本为最小核心实现：

- 不使用运行时反射遍历
- 不注入额外 Unity 组件
- 不输出高频调试日志

重点是兼容性与启动稳定性。

## License

MIT
