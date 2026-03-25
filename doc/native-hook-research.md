# 原生 Hook / 管线研究

更新时间：`2026-03-25`

## 目的

这份文档用于整理《龙吟立志传》当前可见的原生实现管线，给后续功能扩展提供参考。

重点不是“哪里都能 Hook”，而是分清：

1. 哪些入口是真实入账层
2. 哪些入口是业务流程层
3. 哪些入口是原生 UI / 提示层

这样后续加功能时，可以按目标选点：

- 要全角色真实生效：优先底层入账点
- 要只影响玩家流程：优先业务流程点
- 要补原生提示：优先原生 UI 管线

## 研究方法

本次研究基于游戏 interop 目录中的代理程序集：

- `D:\Users\luoxu\game\steam\steamapps\common\LongYinLiZhiZhuan\BepInEx\interop\Assembly-CSharp.dll`

研究方式：

- 读取 interop 元数据
- 提取类型名、方法名、`NativeMethodInfoPtr_...` 字段签名
- 通过控制器、结果对象、UI prefab、单例入口拼接原生调用链

说明：

- 这不是完整逐行 IL 反编译结果
- 一部分结论来自方法名和签名推断
- 这份文档适合做“挂点地图”和“实现方向判断”
- 如果后续要做具体实现，仍然建议针对目标方法再做一次定向验证

## 当前插件的稳定核心挂点

当前 `Plugin.cs` 里，真实入账和核心功能的挂点仍然是对的：

- 武学读书经验：
  - `HeroData.AddSkillBookExp(float, KungfuSkillLvData, bool)`
- 武学实战经验：
  - `HeroData.AddSkillFightExp(float, KungfuSkillLvData, bool)`
  - `HeroData.BattleChangeSkillFightExp(float, KungfuSkillLvData, bool)`
- 武学显示倍率：
  - `HeroData.GetBookExpRate(KungfuSkillLvData)`
  - `HeroData.GetFightExpRate(KungfuSkillLvData)`
- 技艺经验：
  - `HeroData.ChangeLivingSkillExp(int, float, bool)`
- 好感：
  - `HeroData.ChangeFavor(float, bool, float, float, bool)`
- 门派&官府功绩：
  - `HeroData.ChangeForceContribution(float, bool, int)`
  - `HeroData.ChangeGovernContribution(float, bool)`
- 抄书：
  - `BookWriterData.GetEachDayWorkPercent()`
  - `BookWriterData.GetMoneyCost()`
  - `BookWriterData.GetTotalTimeCost()`

结论：

- 如果目标是“全角色真实入账生效”，当前核心选点仍然应该守在 `HeroData` / `BookWriterData` 这一层
- 如果只是追求显示一致性，`GetBookExpRate` / `GetFightExpRate` 可以看作可选层，不是核心入账层

## 一、武学经验管线

### 1. 底层真实入账

最底层、最通用、最适合做倍率 core 的入口：

- `HeroData.AddSkillBookExp(float, KungfuSkillLvData, bool)`
- `HeroData.AddSkillFightExp(float, KungfuSkillLvData, bool)`
- `HeroData.BattleChangeSkillFightExp(float, KungfuSkillLvData, bool)`

显示或理论倍率相关入口：

- `HeroData.GetBookExpRate(KungfuSkillLvData)`
- `HeroData.GetFightExpRate(KungfuSkillLvData)`

特点：

- 这层会覆盖主角和 NPC
- 只要角色走到同一套武学入账路径，就会一起吃到倍率
- 适合做“独立乘区”“不漏路径”的实现

### 2. 玩家流程层

偏玩家专用、玩法流程层的入口：

- `ReadBookController.GetReadExp(float)`
- `ReadBookController.ChangeTotalExp(float)`
- `ReadBookController.FinishRead()`
- `StudySkillController.FinishStudySkill(float)`
- `StudyAttackSkillController.FinishStudyFightSkill()`
- `StudyDodgeSkillController.FinishStudyDodgeSkill()`
- `StudyInternalSkillController.FinishStudyInternalSkill()`
- `StudyUniqueSkillController.FinishStudyUniqueSkill()`
- `PlotController.GetStudyFightExp(string)`
- `PlotController.PlotGetReadTotalExp()`

特点：

- 更像主角学习流程的结算点
- 更适合做“只影响玩家流程”的分支
- 不适合作为覆盖全局的底层倍率入口

### 3. 原生展示层

武学经验的原生展示管线很明确：

- `SpeShowController.Instance`
- `SpeShowController.ShowGetSkillExp(KungfuSkillLvData, float, int)`
- `SpeShowController.ShowGetSkillExp(KungfuSkillLvData, float, int, string)`
- `SkillExpShowPrefab`
- `GameObjectController.skillExpShowPrefab`

结论：

- 游戏自带“武学经验获得展示”这套原生 UI
- 如果未来要给武学经验做原生提示，这条线是最值得优先研究的

## 二、技艺经验管线

### 1. 底层真实入账

当前最稳的底层入口：

- `HeroData.ChangeLivingSkillExp(int, float, bool)`

相关能力与上限入口：

- `HeroData.GetLivingSkillExpMax(int)`
- `HeroData.ChangeLivingSkill(int, float, bool, bool)`
- `HeroData.ChangeMaxLivingSkill(int, int, bool)`
- `HeroData.GetMaxLivingSkill(int)`

特点：

- 这是当前最适合做真实技艺经验倍率的核心点
- 覆盖面比剧情 / 教学 / 建筑流程层更广

### 2. 玩家流程 / 业务层

建筑与剧情流程里，技艺经验相关入口非常多：

- `BuildingUIController.StudyLivingSkill(string)`
- `BuildingUIController.StudyMaxLivingSkill(string)`
- `PlotController.CheckLivingSkillStart()`
- `PlotController.CheckLivingSkillResult(string)`
- `PlotController.StudyLivingSkillStart(string)`
- `PlotController.StudyLivingSkillFinish(string)`
- `PlotController.StudyMaxLivingSkillStart(string)`
- `PlotController.StudyMaxLivingSkillFinish(string)`
- `PlotController.ChooseStudyLivingSkillWithNPC()`
- `PlotController.StudyLivingSkillWithNPC()`
- `PlotController.StudyLivingSkillWithNPCChoosen(string)`
- `PlotController.StudyLivingSkillWithNPCSure(string)`
- `PlotController.TeachLivingSkillWithNPC()`
- `PlotController.TeachLivingSkillWithNPCChoosen(string)`
- `PlotController.TeachLivingSkillWithNPCSure(string)`
- `PlotController.TeachMaxLivingSkillWithNPC()`
- `PlotController.TeachMaxLivingSkillWithNPCChoosen(string)`
- `PlotController.TeachMaxLivingSkillWithNPCSure(string)`
- `PlotController.ChangeLivingSkill(string)`
- `PlotController.ChangeLivingSkillExp(string)`
- `PlotController.ChangeMaxLivingSkill(string)`

特点：

- 这层更适合做“只改主角流程”“只改某些学习方式”的功能
- 如果以后想拆出“剧情技艺”和“通用技艺”两个分支，这一层很值得继续研究

### 3. 展示层现状

当前没有找到与 `SpeShowController.ShowGetSkillExp(...)` 对等的“技艺经验原生展示”入口。

已发现的相关对象：

- `SkillExpShowPrefab`
- `GameObjectController.skillExpShowPrefab`

但就现有签名来看，这套展示明显偏武学技能：

- `ShowGetSkillExp(KungfuSkillLvData, ...)`

结论：

- 技艺经验未来如果想补原生提示，优先考虑通用文本管线
- 不要先假设游戏里存在同级别的“技艺经验专用弹出卡”

## 三、好感管线

### 1. 底层真实变更

当前核心入口：

- `HeroData.ChangeFavor(float, bool, float, float, bool)`

相关能力与辅助方法：

- `HeroData.GetMaxFavor()`
- `HeroData.GetNatureFavorRate()`
- `HeroData.GetFavorRate()`
- `HeroData.SetFavor(...)`
- `HeroData.SetMeetFavor(...)`

### 2. 原生表现层

好感相关的原生表现不是纯文本提示，而是更接近剧情 / 交互动画链：

- `HeroData.SetHeroFavorUI(GameObject, bool)`
- `PlotController.ChangeHeroFavor(string)`
- `PlotController.PlotChangeHeroFavor(HeroData, float, float, float, bool)`
- `PlotController.CheckHeroFavorAnim(HeroData, float)`

结论：

- 如果未来要给好感改动补原生表现，优先研究 `PlotController.CheckHeroFavorAnim`
- 如果只是要补低风险提示，也可以退到 `InfoController` 或 `GameController`

## 四、门派&官府功绩管线

### 1. 底层真实入账

当前最稳的底层入口：

- `HeroData.ChangeForceContribution(float, bool, int)`
- `HeroData.ChangeGovernContribution(float, bool)`

相关辅助方法：

- `HeroData.CheckOutForceContribution()`
- `HeroData.ClearContributionRecord()`
- `HeroData.GetUpgradeForceLvNeedContribution(...)`
- `HeroData.GetGovernUpgradeCost()`
- `HeroData.GetHornorUpgradeCost()`
- `HeroData.ChangeGovernLv()`
- `HeroData.ChangeHornorLv()`

### 2. 业务流程层

门派&官府功绩相关业务明显集中在 `PlotController`、`BuildingUIController`、`MeetingController`：

#### PlotController

- `PlotController.ChangePlayerForceContribution(string)`
- `PlotController.ChangeHeroForceContribution(string)`
- `PlotController.PlotChangeGovernContribution(string)`
- `PlotController.GiveForceHeroContribution()`
- `PlotController.GiveForceHeroContributionSure(string)`
- `PlotController.ChangeFavorToOtherForce(string)`
- `PlotController.ChangeFavorToOtherForceChoose(string)`
- `PlotController.AddFavorOtherForce(string)`
- `PlotController.AddFavorOtherForceTreasureChoose(string)`
- `PlotController.AddFavorOtherForceTreasureChoosen(string)`
- `PlotController.AddFavorOtherForceSure(string)`
- `PlotController.SureExchangeOtherForceSkill(string)`

#### BuildingUIController

- `BuildingUIController.ShowContributionExchange()`
- `BuildingUIController.ShowGovernLv()`
- `BuildingUIController.ShowHornorPlot()`
- `BuildingUIController.ShowGovernContribution()`
- `BuildingUIController.GovernmentClearBadFame()`
- `BuildingUIController.GiveTreasureToGovern()`

#### MeetingController

- `MeetingController.SetInfoText()`
- `MeetingController.SetInfoFocusText(string)`
- `MeetingController.ShowForceMission()`
- `MeetingController.lastMonthContributionUIPrefab`
- `MeetingController.lastMonthContributionUIs`
- `MeetingController.lastMonthContributionHeroList`

#### OtherForceContributionExchangeController

- `OtherForceContributionExchangeController.ShowExchangeUI(ForceData)`
- `OtherForceContributionExchangeController.RefreshExchangeUI()`
- `OtherForceContributionExchangeController.GetExchangeContributionCost(int, float)`

### 3. 界面刷新层

和人物详情界面相关的直接刷新入口：

- `HeroDetailController.RefreshForceContributionInfoData()`

结论：

- 如果目标是“真实拿到多少”，继续守 `HeroData`
- 如果目标是“某个门派 / 官府流程内的具体效果”，优先研究 `PlotController` / `BuildingUIController`
- 如果目标是“界面同步刷新”，再看 `HeroDetailController` / `MeetingController`

## 五、原生通知 / 消息管线

### 1. 信息栏 / 系统消息

这是最像“世界消息 / 系统记录 / 信息列表”的原生管线：

- `InfoController.Instance`
- `InfoController.AddInfo(InfoType, string)`
- `InfoController.AddInfo(InfoData)`
- `InfoController.AddInfoTab(string, string, string, string, float, float, Color)`
- `InfoController.RealAddInfo(...)`
- `InfoController.RealAddInfoTab(...)`
- `InfoController.AddMail(MailData)`
- `InfoController.BuildInfoList()`

相关数据结构：

- `InfoType`
  - `WorldInfo`
  - `ForceInfo`
  - `PersonalInfo`
  - `OtherPersonInfo`
- `InfoData`
  - `InfoData(InfoType, String)`
  - `InfoData(InfoType, TimeData, String)`
- `InfoTabData`
  - 带图标、颜色、声音、持续时间
- `MailData`
  - 带标题、正文、时间、重要性

相关展示层：

- `InfoTextList.Add(InfoData)`
- `InfoTextList.Add(int, string)`
- `InfoMenuController.ShowInfoMenu()`
- `MailIconController`
- `PopInfoTabController`

适合用途：

- 原生风格的非战斗提示
- 世界消息
- 势力消息
- 个人消息
- 长文本或带图标的提示

### 2. 轻量文本提示

这是当前最值得优先研究的通用轻提示管线：

- `GameController.Instance`
- `GameController.ShowTextOnMouse(string)`
- `GameController.ShowTextOnMouse(string, int)`
- `GameController.ShowTextOnMouse(string, Color)`
- `GameController.ShowTextOnMouse(string, int, Color)`
- `GameController.ShowTextAtPos(string, Vector3)`
- `GameController.ShowTextAtPos(string, Vector3, int, Color)`
- `GameController.ShowTextAtPos(string, Vector3, int, Color, Vector3, GameObject, Ease, string, string, string)`

相关 prefab：

- `GameObjectController.simpleTextPrefab`

适合用途：

- 鼠标旁提示
- 低侵入的轻量飘字
- 未来如果要补技艺经验提示，这条线很值得优先尝试

### 3. 战斗提示

战斗场景专用提示：

- `BattleController.AddInfoText(string, bool)`
- `BattleUnit.ShowTextOnHead(string, Color, int, Ease, string, string, string)`

适合用途：

- 战斗内临时信息
- 头顶飘字
- 技能 / 状态 / 额外结果提示

### 4. 武学经验专用展示

这条更像“获得武学经验 / 获得武学”的专用展示：

- `SpeShowController.Instance`
- `SpeShowController.ShowGetSkill(KungfuSkillLvData)`
- `SpeShowController.ShowGetSkill(KungfuSkillLvData, string)`
- `SpeShowController.ShowGetSkillExp(KungfuSkillLvData, float, int)`
- `SpeShowController.ShowGetSkillExp(KungfuSkillLvData, float, int, string)`
- `SpeShowController.ShowSkillLevelUpParticle(...)`

相关对象：

- `SkillExpShowPrefab`

适合用途：

- 武学经验变化时沿用原生展示
- 武学升级提示

限制：

- 当前签名是 `KungfuSkillLvData`
- 没看到对等的“技艺经验展示”方法

## 六、读书与抄书相关对象

### 1. ReadBookController

读书流程控制器：

- `ReadBookController.Instance`
- `ReadBookController.ShowReadBookPanel()`
- `ReadBookController.StartShowText()`
- `ReadBookController.ShowNearText(ReadBookTextController)`
- `ReadBookController.ChangeTotalExp(float)`
- `ReadBookController.GetTotalExp()`
- `ReadBookController.GetReadExp(float)`
- `ReadBookController.FinishRead()`
- `ReadBookController.StartReadBook(HeroData, ItemData, bool, bool)`
- `ReadBookController.SureStartReadBook()`
- `ReadBookController.RealStartReadBook()`
- `ReadBookController.AutoReadBook()`

相关字段：

- `readBookTextTypeDataBase`
- `readBookTextPrefab`
- `readTextExpIcon`
- `targetPracticeExpData`

### 2. ReadBookTextController

读书文字块表现层：

- `ReadBookTextController.SeeText()`
- `ReadBookTextController.ReadText()`
- `ReadBookTextController.ChangeExpRate(float, bool)`
- `ReadBookTextController.ReadSameColumn()`
- `ReadBookTextController.ReadSameRow()`

意义：

- 如果以后想做“读书阶段中途提示”而不是只在最终入账处放大，这条线值得研究

### 3. BookWriterData

抄书相关底层对象：

- `BookWriterData.GetWorkResult()`
- `BookWriterData.CanStartWork()`
- `BookWriterData.HaveMoney()`
- `BookWriterData.GetMoneyCost()`
- `BookWriterData.GetTotalTimeCost()`
- `BookWriterData.GetEachDayWorkPercent()`
- `BookWriterData.HaveEnoughSkill()`
- `BookWriterData.GetMinSkillLv()`
- `BookWriterData.GetSkillChangeRate()`

意义：

- 当前抄书功能继续守在 `BookWriterData` 这一层是对的
- 这层已经足够做效率 / 花费 / 时间类功能

## 七、控制器单例入口

这些控制器都存在 `Instance` 入口，后续如果要走原生流程，不一定需要自己找场景对象：

- `InfoController.Instance`
- `SpeShowController.Instance`
- `GameController.Instance`
- `GameObjectController.Instance`
- `PlotController.Instance`
- `ReadBookController.Instance`
- `StudySkillController.Instance`
- `BuildingUIController.Instance`
- `MeetingController.Instance`
- `HeroDetailController.Instance`

这点很重要：

- 未来如果要做原生 UI 通知，优先考虑控制器单例
- 不要优先走注入组件、场景扫描、运行时找对象这种高风险路线

## 八、后续功能的挂点建议

### 场景 A：要全角色真实生效

推荐继续守在底层：

- 武学：`HeroData.AddSkillBookExp` / `AddSkillFightExp` / `BattleChangeSkillFightExp`
- 技艺：`HeroData.ChangeLivingSkillExp`
- 好感：`HeroData.ChangeFavor`
- 功绩：`HeroData.ChangeForceContribution` / `ChangeGovernContribution`

理由：

- 覆盖面最全
- 不容易漏路径
- 主角和 NPC 一起生效

### 场景 B：要只影响玩家流程

推荐优先研究上游流程：

- 武学：
  - `ReadBookController`
  - `StudySkillController`
  - `StudyAttackSkillController`
  - `StudyInternalSkillController`
  - `PlotController.GetStudyFightExp`
- 技艺：
  - `BuildingUIController.StudyLivingSkill`
  - `PlotController.StudyLivingSkill...`
  - `PlotController.TeachLivingSkillWithNPC...`

理由：

- 更偏玩家学习流程
- 更容易避免 NPC 一起吃到效果

### 场景 C：要补原生提示，但尽量低风险

建议优先级：

1. `InfoController.AddInfo`
2. `GameController.ShowTextOnMouse` / `ShowTextAtPos`
3. `SpeShowController.ShowGetSkillExp`
4. `PlotController.CheckHeroFavorAnim`

理由：

- `InfoController` 最像系统消息，侵入性低
- `GameController` 最像轻量通用提示
- `SpeShowController` 很适合武学经验，但不适合泛用到所有系统
- `PlotController.CheckHeroFavorAnim` 更像好感专用表现

### 场景 D：要只刷新界面，不改真实值

可研究对象：

- `HeroDetailController.RefreshForceContributionInfoData()`
- `BuildingUIController.ShowGovernContribution()`
- `MeetingController.SetInfoText()`
- `InfoController.BuildInfoList()`

提醒：

- 这层不适合拿来做核心逻辑
- 更适合作为真实入账后的附加刷新

## 九、当前建议避免的方向

基于当前项目稳定性目标，仍然建议避免：

- 运行时大范围反射扫描
- 注入额外 Unity 组件
- 高频日志
- 用 UI 刷新方法代替真实入账逻辑
- 用剧情控制器替代全局底层入账点

## 十、当前最值得继续深挖的方向

如果下一轮还要继续研究，建议优先做这两条：

1. `PlotController` 的剧情业务地图
   
   - 重点梳理：
     - 武学读书 / 实战
     - 技艺学习 / 传授
     - 好感变化
     - 门派&官府功绩变化
   - 目标：
     - 找到“只影响玩家流程”的更细分入口

2. 原生提示方案评估
   
   - 对比：
     - `InfoController`
     - `GameController`
     - `SpeShowController`
   - 目标：
     - 选出未来版本里最稳的一条通知方案
