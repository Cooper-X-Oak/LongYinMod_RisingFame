# P0 低风险减肝 / 交互提效包研究

更新时间：`2026-03-25`

## 目的

这份文档是围绕 `P0：低风险减肝 / 交互提效包` 做的定向研究。

目标不是继续扩功能，而是回答三个更实际的问题：

1. 如果继续做 P0，哪些管线最值得挂
2. 哪些位置适合做“真实生效”
3. 哪些位置适合做“原生提示 / 轻量提效”，且不明显增加运行负担

这份文档默认遵守当前项目的稳定性原则：

- 不改成复杂框架
- 不引入运行时对象扫描
- 不注入额外 Unity 组件
- 不依赖高频轮询之外的新重逻辑
- 优先复用原生控制器与原生单例

## 研究基线

程序集来源如下。

路径表述说明：

- 下文统一以游戏根目录为基准，记作：`LongYinLiZhiZhuan/BepInEx/interop/Assembly-CSharp.dll`

研究方式：

- 读取 interop 元数据
- 提取类型名、方法名、`NativeMethodInfoPtr_...` 字段
- 结合当前插件已有稳定 Hook，推定上游流程与原生提示管线

说明：

- 下文中“已确认”表示方法名 / 签名在 interop 里直接可见
- “推定调用链”表示按控制器职责和命名关系整理出的业务路径，适合拿来做二次验证与定点实现

<a id="p0-target-split"></a>
## P0 的推荐目标拆分

P0 最适合拆成三层：

1. 底层真实入账层：保证真的加到数值上
2. 业务流程层：做主角流程提效、减少重复确认、补充结算节点
3. 原生提示层：让玩家感知到功能已生效

这三层不要混用职责：

- 真值仍然尽量守在 `HeroData` / `BookWriterData`
- 流程提效看 `ReadBookController` / `StudySkillController` / `PlotController` / `BuildingUIController`
- 提示优先用 `InfoController` / `GameController` / `SpeShowController`

## 一、P0 真实入账层

这部分已经基本明确，而且也是当前最稳的基础。

### 已确认的底层真实入账点

- 武学读书经验：`HeroData.AddSkillBookExp(float, KungfuSkillLvData, bool)`
- 武学实战经验：
  - `HeroData.AddSkillFightExp(float, KungfuSkillLvData, bool)`
  - `HeroData.BattleChangeSkillFightExp(float, KungfuSkillLvData, bool)`
- 技艺经验：`HeroData.ChangeLivingSkillExp(int, float, bool)`
- 好感：`HeroData.ChangeFavor(float, bool, float, float, bool)`
- 门派&官府功绩：
  - `HeroData.ChangeForceContribution(float, bool, int)`
  - `HeroData.ChangeGovernContribution(float, bool)`
- 抄书：
  - `BookWriterData.GetEachDayWorkPercent()`
  - `BookWriterData.GetMoneyCost()`
  - `BookWriterData.GetTotalTimeCost()`

### 这一层适合做什么

- 倍率修正
- 独立乘区
- 全路径覆盖
- 主角与 NPC 同时生效的全局设计

### 这一层不适合做什么

- 玩家专用逻辑
- 复杂提示
- 交互分支控制

结论：

- P0 只要还强调“稳定 + 少漏路径”，真实入账层就不应该轻易挪走

## 二、P0 业务流程层

这层的价值不是替代底层入账，而是：

- 找到更接近玩家体感的结算点
- 复用原生的确认、自动、结束流程
- 为未来“只影响主角流程”的分支预留路线

<a id="p0-read-book"></a>
## 三、读书流程研究

### 已确认方法

#### ReadBookController

- `ChangeTotalExp(float)`
- `FinishRead()`
- `GetReadExp(float)`
- `StartReadBook(HeroData, ItemData, bool, bool)`
- `SureStartReadBook()`
- `RealStartReadBook()`
- `AutoReadBook()`

#### ReadBookTextController

- `ReadText()`
- `ChangeExpRate(float, bool)`
- `ReadSameColumn()`
- `ReadSameRow()`

#### BuildingUIController / PlotController

- `BuildingUIController.ChooseReadBook()`
- `PlotController.ChooseReadBook(string)`
- `PlotController.ReadBookChoosen()`
- `PlotController.RealStartReadBook()`
- `PlotController.PlotGetReadTotalExp()`

### 推定调用链

推定读书主链大致是：

`BuildingUIController.ChooseReadBook()`
-> `PlotController.ChooseReadBook(string)`
-> `PlotController.ReadBookChoosen()`
-> `ReadBookController.StartReadBook(HeroData, ItemData, bool, bool)`
-> `ReadBookController.SureStartReadBook()`
-> `ReadBookController.RealStartReadBook()`
-> `ReadBookTextController.ReadText()` / `ChangeExpRate(float, bool)`
-> `ReadBookController.GetReadExp(float)` / `ChangeTotalExp(float)`
-> `ReadBookController.FinishRead()`
-> `PlotController.PlotGetReadTotalExp()`
-> `HeroData.AddSkillBookExp(...)`

### 对 P0 的意义

适合的低风险点：

- `ReadBookController.FinishRead()`：适合做一次性结算提示
- `ReadBookController.AutoReadBook()`：说明原生已经存在自动路径，未来做轻量交互提效时可以优先研究复用，而不是自己模拟输入
- `ReadBookController.StartReadBook(...)` / `SureStartReadBook()`：适合研究“少一步确认”的低侵入改法

不建议优先碰的点：

- `ReadBookTextController.ChangeExpRate(float, bool)`
- `ReadBookTextController.ReadText()`

原因：

- 这些更像读书过程中的高频表现层
- 拿它们做核心逻辑更容易带来额外调用频率和兼容风险

## 四、武学修炼流程研究

### 已确认方法

#### StudySkillController

- `GetPracticeExpRate(KungfuSkillLvData)`
- `StartStudySkill(StudySkillType, KungfuSkillLvData, string, AreaBuildingData, bool)`
- `SureStartStudySkill()`
- `RealStartStudySkill()`
- `AutoStudySkill()`
- `PlayerStudySkill()`
- `FinishStudySkill(float)`

#### 具体小游戏控制器

- `StudyAttackSkillController.StartStudyFightSkill(KungfuSkillLvData)`
- `StudyAttackSkillController.FinishStudyFightSkill()`
- `StudyDodgeSkillController.StartStudyDodgeSkill(KungfuSkillLvData)`
- `StudyDodgeSkillController.FinishStudyDodgeSkill()`
- `StudyInternalSkillController.StartStudyInternalSkill(KungfuSkillLvData)`
- `StudyInternalSkillController.FinishStudyInternalSkill()`
- `StudyUniqueSkillController.StartStudyUniqueSkill(KungfuSkillLvData)`
- `StudyUniqueSkillController.FinishStudyUniqueSkill()`

#### 建筑与剧情入口

- `BuildingUIController.StudyFightSelf()`
- `BuildingUIController.StudyFightOther()`
- `BuildingUIController.StudyInternalSelf()`
- `PlotController.StudyFightOther(string)`
- `PlotController.StudyFightSelfFinish(string)`
- `PlotController.GetStudyFightExp(string)`
- `PlotController.StudyFightWithNPC()`
- `PlotController.StudyFightWithNPCSure()`

### 推定调用链

推定主链大致是：

`BuildingUIController.StudyFightSelf()` / `StudyFightOther()` / `StudyInternalSelf()`
-> `StudySkillController.StartStudySkill(StudySkillType, KungfuSkillLvData, string, AreaBuildingData, bool)`
-> `StudySkillController.SureStartStudySkill()`
-> `StudySkillController.RealStartStudySkill()`
-> 对应小游戏控制器 `StartStudy...`
-> 对应小游戏控制器 `FinishStudy...`
-> `StudySkillController.FinishStudySkill(float)`
-> `PlotController.GetStudyFightExp(string)` / `StudyFightSelfFinish(string)`
-> `HeroData.AddSkillFightExp(...)` 或 `BattleChangeSkillFightExp(...)`

### 对 P0 的意义

适合的低风险点：

- `StudySkillController.FinishStudySkill(float)`：适合做一次性提示或轻度修正
- `StudySkillController.AutoStudySkill()`：说明原生已有自动修炼能力，适合研究轻量提效
- `StudySkillController.SureStartStudySkill()`：适合研究减少一次确认

不建议优先碰的点：

- `StudyAttackSkillController.ChangeCombo()`
- `StudyDodgeSkillController.FinishButtonClicked()`
- `StudyInternalSkillController.ShowPoint()`
- `StudyUniqueSkillController.ChangeCombo()`

原因：

- 这些更靠近小游戏内过程或输入表现
- 对稳定版来说，收益没有底层结算与开始/结束节点高

## 五、技艺学习流程研究

### 已确认方法

#### BuildingUIController

- `StudyLivingSkill(string)`
- `StudyMaxLivingSkill(string)`

#### PlotController

- `StudyLivingSkillStart(string)`
- `StudyLivingSkillFinish(string)`
- `StudyMaxLivingSkillStart(string)`
- `StudyMaxLivingSkillFinish(string)`
- `StudyLivingSkillWithNPC()`
- `StudyLivingSkillWithNPCChoosen(string)`
- `StudyLivingSkillWithNPCSure(string)`
- `TeachLivingSkillWithNPC()`
- `TeachLivingSkillWithNPCChoosen(string)`
- `TeachLivingSkillWithNPCSure(string)`
- `StudyMaxLivingSkillWithNPC()`
- `StudyMaxLivingSkillWithNPCChoosen(string)`
- `StudyMaxLivingSkillWithNPCSure(string)`

### 推定调用链

推定主链大致是：

`BuildingUIController.StudyLivingSkill(string)` / `StudyMaxLivingSkill(string)`
-> `PlotController.StudyLivingSkillStart(string)` / `StudyMaxLivingSkillStart(string)`
-> `PlotController.StudyLivingSkillFinish(string)` / `StudyMaxLivingSkillFinish(string)`
-> `HeroData.ChangeLivingSkillExp(...)`

NPC 相关分支大致是：

`PlotController.StudyLivingSkillWithNPC()`
-> `StudyLivingSkillWithNPCChoosen(string)`
-> `StudyLivingSkillWithNPCSure(string)`
-> `HeroData.ChangeLivingSkillExp(...)`

以及：

`PlotController.TeachLivingSkillWithNPC()`
-> `TeachLivingSkillWithNPCChoosen(string)`
-> `TeachLivingSkillWithNPCSure(string)`
-> 技艺经验 / 技艺等级相关变更

### 对 P0 的意义

- 技艺这一块目前最稳的真值层仍然是 `HeroData.ChangeLivingSkillExp(...)`
- 但如果以后要做“只影响玩家学习流程”的版本，`PlotController.StudyLivingSkill...` 这一串是很值得继续下钻的
- 当前阶段不建议拿 NPC 教学链来做核心逻辑，比较适合后续做定向玩法扩展

## 六、门派 / 官府功绩流程研究

### 已确认方法

#### BuildingUIController

- `ShowContributionExchange()`
- `ShowGovernContribution()`
- `GiveTreasureToGovern()`
- `GovernmentClearBadFame()`

#### PlotController

- `ChangePlayerForceContribution(string)`
- `PlotChangeGovernContribution(string)`
- `GiveForceHeroContribution()`
- `GiveForceHeroContributionSure(string)`
- `AddFavorOtherForce(string)`
- `AddFavorOtherForceSure(string)`
- `SureExchangeOtherForceSkill(string)`

#### MeetingController

- `SetInfoText()`
- `SetInfoFocusText(string)`
- `ShowForceMission()`

#### OtherForceContributionExchangeController

- `ShowExchangeUI(ForceData)`
- `RefreshExchangeUI()`
- `GetExchangeContributionCost(int, float)`

#### HeroDetailController

- `RefreshForceContributionInfoData()`

### 推定调用链

门派 / 官府功绩业务大致可以拆成三类：

#### 1. 真实值变更

- `HeroData.ChangeForceContribution(...)`
- `HeroData.ChangeGovernContribution(...)`

#### 2. 业务事件 / 建筑 / 剧情流程

- `PlotController.ChangePlayerForceContribution(string)`
- `PlotController.PlotChangeGovernContribution(string)`
- `PlotController.GiveForceHeroContribution...`
- `PlotController.AddFavorOtherForce...`
- `PlotController.SureExchangeOtherForceSkill(string)`
- `BuildingUIController.ShowContributionExchange()`
- `BuildingUIController.ShowGovernContribution()`

#### 3. 界面刷新与信息展示

- `MeetingController.SetInfoText()`
- `MeetingController.SetInfoFocusText(string)`
- `MeetingController.ShowForceMission()`
- `HeroDetailController.RefreshForceContributionInfoData()`
- `OtherForceContributionExchangeController.RefreshExchangeUI()`

### 对 P0 的意义

适合的低风险点：

- 真值仍然守 `HeroData`
- 如果以后要补原生提示或界面同步刷新，可以优先试：
  - `MeetingController.SetInfoText()`
  - `HeroDetailController.RefreshForceContributionInfoData()`
  - `OtherForceContributionExchangeController.RefreshExchangeUI()`

重要结论：

- 这一块完全可以拆成“真实入账”和“界面刷新”两个层次，不必把所有功绩逻辑都塞进剧情控制器里

<a id="p0-native-feedback"></a>
## 七、P0 原生提示管线

这是 P0 很值得继续研究的一层，因为它可以明显增强玩家感知，但通常不需要重逻辑。

### 1. 系统消息 / 信息栏

#### 已确认方法

- `InfoController.AddInfo(InfoType, string)`
- `InfoController.AddInfo(InfoData)`
- `InfoController.RealAddInfo(InfoData)`
- `InfoController.AddInfoTab(string, string, string, string, float, float, Color)`
- `InfoController.RealAddInfoTab(InfoTabData)`
- `InfoController.AddMail(MailData)`

#### 已确认相关类型

- `InfoType`
  - `WorldInfo`
  - `ForceInfo`
  - `PersonalInfo`
  - `OtherPersonInfo`
- `InfoData(InfoType, string)`
- `InfoData(InfoType, TimeData, string)`
- `InfoTabData(string, string, string, string, float, float, Color)`
- `MailData(string, string, TimeData, bool, bool)`

#### 适合用途

- 低频但重要的系统提示
- 门派 / 官府 / 个人消息
- 结果记录型通知

### 2. 轻量飘字 / 鼠标旁提示

#### 已确认方法

- `GameController.ShowTextOnMouse(string)`
- `GameController.ShowTextOnMouse(string, int)`
- `GameController.ShowTextOnMouse(string, Color)`
- `GameController.ShowTextOnMouse(string, int, Color)`
- `GameController.ShowTextAtPos(string, Vector3)`
- `GameController.ShowTextAtPos(string, Vector3, int, Color)`
- `GameController.ShowTextAtPos(string, Vector3, int, Color, Vector3, GameObject, Ease, string, string, string)`

#### 适合用途

- 简短提示
- 低侵入反馈
- 不想打断流程时的增强提示

### 3. 武学经验专用展示

#### 已确认方法

- `SpeShowController.ShowGetSkill(KungfuSkillLvData)`
- `SpeShowController.ShowGetSkill(KungfuSkillLvData, string)`
- `SpeShowController.ShowGetSkillExp(KungfuSkillLvData, float, int)`
- `SpeShowController.ShowGetSkillExp(KungfuSkillLvData, float, int, string)`

#### 适合用途

- 武学经验结算
- 武学升级提醒
- 沿用原生技能经验展示风格

#### 限制

- 当前签名明显是武学专用
- 不适合作为“技艺经验统一提示管线”直接复用

## 八、P0 最适合优先尝试的方案

### 方案 A：真值继续守底层，提示挂在结算节点

这是当前最稳的一条路：

- 真实倍率继续守在 `HeroData` / `BookWriterData`
- 结算或完成时，在上层控制器补一次轻量提示

推荐组合：

- 读书：`ReadBookController.FinishRead()` + `GameController.ShowTextOnMouse(...)`
- 武学修炼：`StudySkillController.FinishStudySkill(float)` + `SpeShowController.ShowGetSkillExp(...)` 或 `GameController.ShowTextOnMouse(...)`
- 技艺：`PlotController.StudyLivingSkillFinish(string)` + `GameController.ShowTextOnMouse(...)`
- 功绩：`PlotController.ChangePlayerForceContribution(string)` / `PlotController.PlotChangeGovernContribution(string)` + `InfoController.AddInfo(...)`

优点：

- 玩家感知强
- 不需要把逻辑搬离底层
- 较容易控制调用频率

### 方案 B：轻量交互提效优先复用原生 `Auto...` / `Sure...`

当前已经确认存在：

- `ReadBookController.AutoReadBook()`
- `StudySkillController.AutoStudySkill()`
- `ReadBookController.SureStartReadBook()`
- `StudySkillController.SureStartStudySkill()`

这意味着：

- 如果未来要做“少点一次确认”“更直接开始”“一键自动学习”类功能，优先研究复用这些原生路径
- 不建议自己做输入模拟、场景扫描按钮、额外状态机

### 方案 C：想做玩家专用版本时，再上移挂点

如果以后你希望：

- 主角吃倍率
- NPC 不吃倍率
- 某些收益只影响玩家流程

那就应当把一部分实现往上移到：

- `ReadBookController`
- `StudySkillController`
- `BuildingUIController`
- `PlotController`

但这不是当前稳定版的优先路线。

## 九、当前应避免的方向

为了守住 P0 的“低风险”，当前仍然建议避免：

- 在 `ReadBookTextController` 这类高频表现层做核心逻辑
- 在各类小游戏控制器的过程方法里做复杂倍率
- 运行时找场景对象、扫对象、注入组件
- 用 `PlotController` 这种超大类直接承接所有真值逻辑
- 为提示系统自建一套大 UI

<a id="p0-priority"></a>
## 十、实现优先级建议

如果后续真要继续做 P0 版本迭代，建议顺序是：

1. 守住现有底层真实入账 Hook
2. 读书 / 修炼 / 技艺 / 功绩补原生提示方案
3. 研究 `Auto...` / `Sure...` 做交互提效
4. 最后再考虑是否拆出“仅主角流程版”

## 结论

围绕 `P0：低风险减肝 / 交互提效包`，当前最值得继续利用的原生管线已经比较清楚：

- 真值层：`HeroData` / `BookWriterData`
- 流程层：`ReadBookController` / `StudySkillController` / `BuildingUIController` / `PlotController`
- 提示层：`InfoController` / `GameController` / `SpeShowController`

换句话说，后续如果继续扩 P0，最稳的做法不是“加更多逻辑”，而是：

- 继续让真值守在底层
- 把玩家感知补在结算点
- 把交互提效建立在游戏原生控制器现成的 `Auto...`、`Sure...`、`Finish...` 之上

这条路线最符合当前项目的稳定性目标，也最有机会在不明显增加负担的前提下，继续做出玩家能立刻感觉到变化的版本。
