# 一键刷新功能逆向与实现说明

更新时间：`2026-03-26`

## 目的

这份文档用于沉淀 `Alt+R` 一键刷新功能的逆向过程、失败点和最终稳定实现路径。

这次工作的目标很明确：

1. 不增加新的高频运行负担
2. 不引入组件注入和后台扫描
3. 只复用原生控制器已经存在的方法
4. 让刷新逻辑在当前打开的界面中直接生效

换句话说，这不是“做一个万能 UI 工具层”，而是做一个足够轻、足够稳、方便后续继续扩展的最小实现。

## 背景

`1.8` 版本引入了上下文快捷键：

- `=`：插件 ON / OFF
- `Alt+R`：尝试刷新当前支持的界面

当前确认支持的界面有三类：

- 突破词条
- 神兵 / 特殊强化词条
- 拍卖重开

插件的输入路径仍然沿用当前项目已验证稳定的方案：

- `Camera.FireOnPostRender` + `PollInput`
- 使用 `GetKeyDown`、防抖、单帧去重

这样做的原因是：

- 项目里已经证明这条路径比额外注入 `MonoBehaviour` 更稳
- 不需要新建组件，不引入场景生命周期风险
- 只有按键发生时才会进一步走刷新分支

## 初版失败现象

首版实现部署后，按键会有蜂鸣提示，但没有实际效果，日志里出现了典型告警：

- `AccessTools.Field: Could not find field for type SpeEnhanceEquipController and name speEnhanceEquipUI`
- `AccessTools.Field: Could not find field for type BreakThroughController and name breakThroughPanel`
- `AccessTools.Field: Could not find field for type AuctionController and name auctionPanel`
- `[RisingFame] Refresh skipped: no supported panel`

这说明两个事实：

1. 快捷键逻辑本身已经跑到了
2. 问题出在“如何识别当前界面”和“如何调用原生刷新入口”

也就是说，故障不在输入，不在热键，不在蜂鸣，而在反射目标选错了。

## 研究方法

这次定位没有继续靠猜字段名，而是直接对游戏的 interop 代理程序集做定向检查。

研究基线：

- `D:\Users\luoxu\game\steam\steamapps\common\LongYinLiZhiZhuan\BepInEx\interop\Assembly-CSharp.dll`

实际使用的方法：

1. 新建一个临时 `net8.0` 控制台工程
2. 直接引用游戏 interop 程序集
3. 补上 `Il2CppInterop.Runtime.dll`
4. 用 `typeof(...)` + 反射枚举控制器的属性、方法和签名
5. 只记录结论，不把临时探针工具提交进仓库

这样做的好处是：

- 避开了 IL2CPP 运行时上下文里“不好直接全局扫类型”的问题
- 可以得到比猜测更可靠的“真实属性 / 方法签名”
- 结论足够精确，后续实现可以改成强类型调用

## 已确认的原生控制器入口

### 1. `SpeEnhanceEquipController`

确认到的关键属性：

- `Instance`
- `speEnhanceEquipUI`

确认到的关键方法：

- `CanEnhance()`
- `ClearAllChoice()`
- `GenerateChoice()`
- `RefreshEnhanceButtonState()`

结论：

- 特殊强化界面可以通过 `Instance` 直接拿单例
- 当前界面是否打开，可以通过 `speEnhanceEquipUI.activeInHierarchy` 判断
- 刷新逻辑并不需要自己重建 UI，只要调用原生“清空 -> 重生成 -> 刷新按钮状态”这条链

### 2. `BreakThroughController`

确认到的关键属性：

- `Instance`
- `breakThroughPanel`
- `targetSkill`

确认到的关键方法：

- `StartShowBreakChoice()`
- `RefreshExtraRateInfo()`

结论：

- 突破界面是否有效，不仅要看面板是否打开，还要看 `targetSkill` 是否存在
- 这类刷新应直接复用原生“重新展示词条”的入口，而不是手写一个替代流程

### 3. `AuctionController`

确认到的关键属性：

- `Instance`
- `auctionPanel`
- `heroList`
- `playerSellItem`
- `endMatchCallPlot`
- `auctionDifficulty`
- `havePlayer`
- `auctionKeeper`

确认到的关键方法：

- `RestartAuction(List<HeroData>, ItemListData, ItemData, string, float, bool, string)`

这里最重要的发现有两个：

1. `RestartAuction(...)` 的第二个参数不是当前显示用的 `List<ItemData>`，而是 `ItemListData`
2. 控制器本身暴露的是 `auctionItemList : List<ItemData>`，不能直接拿来当重开参数

### 4. `PlotController`

确认到的关键属性：

- `Instance`
- `tempPlotShop`

结论：

- 当前拍卖流程使用的原始货池，实际更接近 `PlotController.Instance.tempPlotShop`
- 它的类型正好是 `ItemListData`
- 因此拍卖重开时，应把这个对象作为 `RestartAuction(...)` 的第二个参数传回原生流程

## 为什么初版会失败

这次问题本质上有三层。

### 1. 把 IL2CPP interop 暴露的属性，当成了普通字段

初版实现使用的是：

- `AccessTools.Field(type, "speEnhanceEquipUI")`
- `AccessTools.Field(type, "breakThroughPanel")`
- `AccessTools.Field(type, "auctionPanel")`

但对当前游戏这份 interop 代理来说，这些成员是属性，不是可直接反射到的普通字段。

因此：

- HarmonyX 日志会持续报 `Could not find field`
- 面板活跃检测永远失败
- 结果就是逻辑最终落到 `Refresh skipped: no supported panel`

### 2. 拍卖重开参数顺序曾经写错

初版里拍卖分支曾把：

- `auctionKeeper`
- `auctionDifficulty`
- `havePlayer`
- `endMatchCallPlot`

这几个参数的位置传错。

即使界面检测正确，参数顺序不对也会让原生方法无法得到正确上下文。

### 3. 拍卖数据源类型也选错了

`AuctionController.auctionItemList` 是当前显示用的 `List<ItemData>`，而 `RestartAuction(...)` 需要的是 `ItemListData`。

这不是简单的类型转换问题，而是“当前显示缓存”与“流程源数据”不是同一层对象。

最终确认后，正确的原始数据来源是：

- `PlotController.Instance.tempPlotShop`

## 最终实现路径

### 1. 输入层

仍然保持轻量：

- `Camera.FireOnPostRender` 后置调用 `PollInput`
- `Alt` 按住 + `R` 单次触发
- 防抖 + 单帧去重

结论：

- 刷新逻辑只在用户按键时进入
- 平时没有后台巡检或额外场景扫描

### 2. 特殊强化刷新

最终逻辑：

1. `SpeEnhanceEquipController.Instance != null`
2. `instance.speEnhanceEquipUI.activeInHierarchy`
3. `instance.CanEnhance()`
4. 依次调用：
   - `ClearAllChoice()`
   - `GenerateChoice()`
   - `RefreshEnhanceButtonState()`

优点：

- 完全复用原生控制器已有方法
- 不额外生成临时对象
- 逻辑非常接近玩家自己重新打开界面的原生结果

### 3. 突破词条刷新

最终逻辑：

1. `BreakThroughController.Instance != null`
2. `instance.breakThroughPanel.activeInHierarchy`
3. `instance.targetSkill != null`
4. 调用：
   - `StartShowBreakChoice()`
   - `RefreshExtraRateInfo()`

优点：

- 复用了原生词条展示入口
- 刷新后还能同步额外概率 / 加成信息

### 4. 拍卖重开

最终逻辑：

1. `AuctionController.Instance != null`
2. `instance.auctionPanel.activeInHierarchy`
3. `PlotController.Instance != null`
4. `PlotController.Instance.tempPlotShop != null`
5. 调用：

```csharp
instance.RestartAuction(
    instance.heroList,
    plot.tempPlotShop,
    instance.playerSellItem,
    instance.endMatchCallPlot,
    instance.auctionDifficulty,
    instance.havePlayer,
    instance.auctionKeeper);
```

优点：

- 直接回到原生拍卖重开方法
- 参数类型、顺序和来源都与控制器真实签名一致
- 不需要自己重写拍卖流程

## 当前实现为什么足够轻

这次实现刻意避免了几个过去容易引发兼容风险的方向：

- 不新增 Harmony 高频 Patch
- 不往场景里注入自定义组件
- 不做后台扫描查找界面
- 不做反射式“盲猜路径”重建流程
- 不额外维护复杂状态机

目前的刷新系统只做三件事：

1. 等用户按键
2. 判断当前是否处在支持界面
3. 调用对应原生控制器的现成方法

因此它的运行成本几乎全部集中在“按键瞬间”，不会在平时持续吞性能。

## 对未来功能的启发

这次逆向得到的经验，对后续功能扩展很有价值。

### 1. IL2CPP 项目里，优先检查“属性暴露”而不是猜字段

很多 interop 代理类会把游戏对象暴露成属性：

- `Instance`
- `xxxPanel`
- `xxxUI`
- `targetSkill`

如果上来就用 `AccessTools.Field(...)` 猜，很容易在日志里踩一地告警。

### 2. 优先找“控制器已有业务方法”

比起自己重建逻辑，更稳的做法通常是：

- 找单例控制器
- 找它已经存在的业务方法
- 用原生数据重新走一次原生流程

这也是这次 `Alt+R` 最终能稳定落地的关键。

### 3. 拍卖 / 刷新类功能要特别注意“显示缓存”和“源数据”分层

`List<ItemData>` 和 `ItemListData` 看起来都像“物品列表”，但职责完全不同。

后续如果做：

- 黑市刷新
- 商店刷新
- 藏经阁刷新
- 锻造 / 重铸类刷新

都要优先先分清：

- 当前 UI 上显示的缓存是谁
- 真正驱动流程重开的源数据是谁

### 4. 原生提示层仍然值得继续研究

这次刷新功能只做了蜂鸣和简短日志，没有接入更丰富的原生提示。

如果后续要做“更像游戏原生体验”的版本，建议继续研究：

- `InfoController`
- `SpeShowController`
- `WorldNews` / 世界消息相关管线

这部分可与现有文档联动参考：

- `doc/native-hook-research.md`
- `doc/p0-qol-pipe-research.md`

## 结论

`Alt+R` 这次能稳定落地，核心并不是“多写了多少代码”，而是把调用点找对了。

最终结论可以概括成三句话：

1. 特殊强化、突破、拍卖都存在可复用的原生控制器入口
2. IL2CPP interop 下要优先按真实属性 / 方法签名做强类型调用
3. 只在按键瞬间触发原生流程，才符合这个项目当前的稳定性原则

这套方法后续可以继续复用到更多“低风险提效”功能上，但每次都应该坚持同一个原则：

- 先确认真实原生入口
- 再决定是否值得接入
- 最后才写代码
