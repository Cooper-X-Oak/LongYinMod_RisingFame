# 拍卖刷新断点研究（v1.8.15）

更新时间：`2026-03-27`

> 说明：这份文档保留的是 `v1.8.15` 阶段为了判断拍卖 reroll 为什么“看起来动了、其实没重掷”而整理出来的断点研究与探针清单。  
> 它更偏“研究过程记录”，不是当前稳定实现的最终口径。  
> 如果你想先看已经收口的结论与稳定方案，请优先阅读：[一键刷新功能逆向与实现说明](./refresh-implementation-notes.md) 和 [拍卖刷新复盘与工程教训](./auction-refresh-retrospective.md)。

## 1. 问题一句话

为什么历史上可用的拍卖刷新，在当前环境下会卡在 `ShowAuctionItem` 之后，无法继续进入 `GenerateAuctionItem`，从而不生成新的 `ItemListData`？

## 2. 当前结论状态

当前仓库内的静态研究和最小探针已经补齐，但运行态结论仍然需要一次自然点击和一次插件链路验证日志来收口。

已确认的事实：

- 当前补丁只显式挂载一个 `GenerateAuctionItem` 签名：
  `PlotController.GenerateAuctionItem(ItemListData, float, List<int>, int)`
- 当前补丁只显式挂载一个 `ShowAuctionItem` 签名：
  `PlotController.ShowAuctionItem()`
- 当前显示层快照逻辑已经确认真实展品 icon 位于 `ChoosePanel/ChooseItemList/Viewport/Content/...`
- 当前 reroll 主流程是“reset display -> close choose panel -> ensure open”，并不是直接生成新池

尚未在当前版本运行态中被证实的点：

- 原生手点“查看展品”时，是否还会进入当前签名的 `GenerateAuctionItem`
- 如果不会，真实生成入口是否已经迁移或改签名
- 如果会，插件链路缺了哪一个前置状态

## 3. 判断标准

- `Alt+R` 被按到，不算刷新成功
- `ShowAuctionItem` 被调用，不算刷新成功
- 只有以下任一成立才算 reroll 成功：
  - 出现 `GenerateAuctionItem pre/post`
  - 新 `ItemListData` 被生成，并能证实替换旧结果
- `ChoosePanel` 里看到展品，不代表底层池已刷新
- 仅 UI 变化不记为“部分成功”

## 4. 前置校验

进入运行态验证前，先看启动日志：

1. `PlotController.GenerateAuctionItem` patch 没有出现 `[SKIP]` 或 `[FAIL]`
2. 启动时会额外打印 `PlotController.ShowAuctionItem` 和 `PlotController.GenerateAuctionItem` 的所有重载
3. 启动时会额外打印 `AuctionController.StartAuctionRound/StartAreaAuction/RestartAuction` 以及 `ItemListController.SetItemList/RefreshItemList/SetItemListData` 的所有重载
4. 如果启动日志里看到了多个 `GenerateAuctionItem` 重载，而当前 patch 只挂了一个，就不能再把“没日志”直接解释成“没调用”

说明：

- 这一步已经在插件里实现，启动时会输出 `Method overload PlotController.GenerateAuctionItem#...`
- 目的就是确认当前环境下到底有哪些签名存在

## 5. 历史正常链路

1. `nowSinglePlot.choices` 中存在 `callFuc=ShowAuctionItem` 的 choice
2. 原生点击“查看展品”调用 `PlotController.ShowAuctionItem()`
3. `ShowAuctionItem()` 内部进入 `GenerateAuctionItem(...)`
4. `GenerateAuctionItem(...)` 生成新的 `ItemListData`
5. `ChoosePanel` 根据该数据生成 `ItemIconController`
6. 日志出现 `GenerateAuctionItem pre/post`

## 6. 当前失效链路

1. 插件路径会触发 `ShowAuctionItem`
2. 现有日志可见 `ShowAuctionItem pre/post pool=null`
3. 没有看到 `GenerateAuctionItem pre/post`
4. `ChoosePanel` 仍可继续显示展品 icon，但 `tempPlotShop` 和 `plotItemGrid` 为空
5. 因此更符合“生成层未触发或未命中真实入口”，而不是“UI 没关好”

## 7. 原生点击 vs 插件调用对照

| 场景                       | 是否进入 ShowAuctionItem | 是否进入 GenerateAuctionItem | ChoosePanel 展品是否来自新池子 |
| ------------------------ | -------------------- | ------------------------ | --------------------- |
| A 原生点击“查看展品”             | 待运行态验证               | 待运行态验证                   | 待运行态验证                |
| B 插件链路触发 ShowAuctionItem | 是，已有日志               | 当前未见日志                   | 当前证据更像旧列表继续显示         |

判定分叉：

- 若 A 能进、B 不能进：插件缺失前置状态
- 若 A、B 都不能进：当前版本真实生成链路迁移，不再走该方法
- 若 A、B 都能进但 B 不更新：生成成功，但显示层桥接没有接上

## 8. 真实显示层位置

当前仓库中的快照逻辑和现有日志都指向同一个位置：

- `Canvas/ChoosePanel/ChoosePanelRoot/ChooseItemList/Viewport/Content/...`
- 实际图标对象类型是 `ItemIconController`

这解释了为什么在 `AuctionPanel` 和 `plotItemGrid` 为空时，界面上仍然能看到展品。

## 9. 真实数据层候选

按当前优先级排序：

1. `ChooseController.itemList` / `ItemListController` 内部绑定的 `ItemListData`
2. `ChooseController` 内部缓存的选择项数据
3. `AuctionController.auctionItemList`
4. `PlotController.tempPlotShop`

当前倾向判断：

- `tempPlotShop` 更像历史源数据位，但在当前失败日志中经常为 `null`
- `auctionItemList` 更像显示缓存，不足以证明“重新生成”
- 最值得补证的是 `ChooseController.itemList` 或 `ItemListController` 内部实际绑定对象

## 10. 静态逆向结果

### 10.1 当前 patch 明确挂载的方法

- `PlotController.GenerateAuctionItem(ItemListData, float, Il2CppSystem.Collections.Generic.List<int>, int)`
- `PlotController.ShowAuctionItem()`
- `ChooseController.HideChoosePanel()`
- `ChooseController.UnshowChoosePanel()`

### 10.2 当前插件如何识别拍卖 reroll

- `Alt+R` 会把 reroll 置为 pending，并启动一个低频时序：
  `ResetDisplay -> ChooseClose -> EnsureOpen`
- 插件会尝试恢复 `nowChoice/newChoice`
- 插件不会把“看见 icon”当成成功
- 真正的成功条件仍然是 `_auctionGenerateVersion` 增长，或者明确看到新池替换

### 10.3 当前已加的最小探针

当前代码里已经补上下面这些一次性、低频探针：

- 启动时打印 `ShowAuctionItem` / `GenerateAuctionItem` 的实际重载列表
- 启动时打印 `StartAuctionRound/StartAreaAuction/RestartAuction` 与 `SetItemList/RefreshItemList/SetItemListData` 的重载
- `Alt+I`：
  - 生成一次 `auction_probe_id=natural_###`
  - 打印拍卖 UI 快照
  - 用于接着手点一次原生“查看展品”
- `Alt+Shift+I`：
  - 同样生成 `natural_###`
  - 并在首次命中 `GenerateAuctionItem pre` 时额外打印一次调用栈
- `Alt+R`：
  - 生成一次 `auction_probe_id=plugin_###`
  - 在插件链路 reopen 前后打印 `nowChoice/newChoice/tempPlotShop/auctionStep/choosePanel/iconCount`
- `ShowAuctionItem pre/post`：
  - 会携带当前 probe id
  - 会记录 `generateSeen`
- `GenerateAuctionItem pre/post`：
  - 会携带当前 probe id
  - 便于直接判断 natural / plugin 两条链路是否命中同一入口
- `AuctionController.StartAuctionRound/StartAreaAuction/RestartAuction`：
  - 会在 probe 激活期间记录 `auction pre/post`
  - 用于确认真实生成是否迁移到更早的拍卖入口
- `ItemListController.SetItemList/RefreshItemList/SetItemListData`：
  - 会在 probe 激活期间记录 `itemlist pre/post`
  - 用于确认 ChoosePanel 的真实数据桥接点

## 11. 动态验证步骤

### 11.1 启动后先看日志

必须先确认两件事：

1. 没有 `[SKIP] PlotController.GenerateAuctionItem` 或 `[FAIL] PlotController.GenerateAuctionItem`
2. 启动日志里已经打印出 `Method overload PlotController.GenerateAuctionItem#...`

### 11.2 验证原生点击链路

步骤：

1. 进入拍卖场景，但先不要点“查看展品”
2. 按一次 `Alt+I`
3. 如果你怀疑真实生成入口已经迁移，改按 `Alt+Shift+I`
4. 只手点一次原生“查看展品”
5. 在日志中检索刚刚的 `auction_probe_id=natural_###`

期待看到的关键字段：

- `auction_probe_id=natural_### source=natural show pre`
- `auction_probe_id=natural_### source=natural generate pre`
- `auction_probe_id=natural_### source=natural generate post`

如果只看到：

- `show pre`
- `show post`
- 且 `generateSeen=false`

那么原生点击也没有进入当前命中的 `GenerateAuctionItem`。

### 11.3 验证插件链路

步骤：

1. 保持在同一个拍卖场景
2. 按一次 `Alt+R`
3. 检索最新的 `auction_probe_id=plugin_###`

期待看到的关键字段：

- `source=plugin phase=ensure pre`
- `source=plugin show pre`
- `source=plugin generate pre`
- `source=plugin generate post`

如果只看到：

- `plugin phase=... pre/post`
- `plugin show pre/post`
- 且没有 `generate pre/post`

那么插件确实只走到了展示层。

### 11.4 成功和失败各说明什么

成功：

- `GenerateAuctionItem pre/post` 出现
- `ItemListData` 摘要发生变化
- `_auctionGenerateVersion` 增加

失败：

- 原生和插件都没有进入当前签名的 `GenerateAuctionItem`
- 更符合“入口迁移”或“签名变化”
- 如果只有原生能进、插件不能进，则更符合“插件缺前置状态”

## 12. 必须回答的问题

当前探针就是为下面这些问题服务的，结题前必须明确回答：

1. 原生手点“查看展品”时，当前版本是否还会调用 `GenerateAuctionItem`？
2. 如果会，插件为什么没补齐同样的前置状态？
3. 如果不会，当前版本真实生成方法改成了什么？
4. `ChoosePanel` 中那批真实展品 icon 的数据源来自哪里？
5. 当前真正的展品数据层是 `ItemListData`、`List<ItemData>`，还是别的缓存结构？
6. 当前“展品显示层”和“展品生成层”之间的桥接方法是什么？
7. `ShowAuctionItem` 现在是生成入口，还是已经退化成“只展示既有列表”？
8. 当前最值得验证的 P0 点是哪个方法或字段？

## 13. P0 验证建议

### P0 点

`GenerateAuctionItem` 真实入口 / 有效签名

### 原因

它仍然是历史上的生成入口。若它没有在当前版本的真实链路里被调用，就不可能解释新的 `ItemListData` 如何生成。

### 当前最小可执行方案

- 启动后检查 overload 日志
- `Alt+I` + 原生点击一次“查看展品”
- `Alt+R` + 检查插件链路一次
- 只有必要时再用 `Alt+Shift+I` 打一次栈

### 成功时应该看到什么

- `GenerateAuctionItem pre/post`
- 新旧 `ItemListData` 摘要变化
- `_auctionGenerateVersion` 增长

### 失败时说明什么

- 生成入口迁移
- 或签名变化导致当前 patch 未命中
- 或 `ShowAuctionItem` 已不再承担生成职责

## 14. 禁止事项

- 不要把 `ChoosePanel` 当成真实随机池
- 不要因为看见 icon 变化就判定 reroll 成功
- 不要继续把“多等一帧 / 多补一次 close/open”当成主要研究方向
- 不要引入常驻扫描
- 不要引入高频日志
- 不要把实验代码混进稳定版主流程
- 不要自动结束游戏进程

## 15. 推荐检索关键词

- `ShowAuctionItem`
- `GenerateAuctionItem`
- `ChooseController`
- `ChooseItemList`
- `ItemIconController`
- `AuctionController`
- `PlotController`
- `SinglePlotChoiceData`
- `ItemListData`
- `ItemData`
- `StartAuctionRound`
- `StartAreaAuction`
- `RestartAuction`
- `SetItemList`
- `RefreshItemList`
- `SetItemListData`
- `ShowPlotItem`
- `ClearPlotItem`
- `HideChoosePanel`
- `UnshowChoosePanel`
- `plotInteractItem`
- `plotInteractItemTempRecord`
- `tempPlotShop`
- `nowChoice`
- `newChoice`
- `auction_probe_id=natural_`
- `auction_probe_id=plugin_`

## 16. 当前最保守的一句话结论

当前拍卖刷新失效，更符合以下判断：

`ShowAuctionItem` 被调用，但未进入或未命中当前有效的 `GenerateAuctionItem`；可能原因包括生成入口迁移、签名变化，或插件链路未补齐前置状态，因此新的 `ItemListData` 没有生成，而 `ChoosePanel` 继续显示既有列表。

## 17. 一句话验收标准

只有在日志中看到 `GenerateAuctionItem pre/post`，并确认新 `ItemListData` 生成且替换旧结果时，才算拍卖 reroll 成功。
