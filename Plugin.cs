using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace RisingFame;

[BepInPlugin("com.luoxu.longyin.risingfame", "RisingFame - MingYangTianXia", "1.8.5")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log = null!;

    // ---- Mod ON/OFF ----
    internal static bool Enabled = true;

    // Input debounce to avoid rapid toggle spam.
    static float _nextToggleTime;
    static int _lastPollFrame = -1;

    // Cached auction generation context captured from the game's native path.
    static ItemListData? _lastAuctionTargetList;
    static float _lastAuctionShopLv;
    static Il2CppSystem.Collections.Generic.List<int>? _lastAuctionItemTypeLimit;
    static int _lastAuctionItemNum;
    static int _auctionGenerateVersion;
    static bool _pendingAuctionReroll;

    // Exp multiplier: rank1=x3.0, each rank +0.5 (rank6=x5.5)
    internal static float GetExpMultiplier(HeroData hero)
    {
        if (!Enabled) return 1f;
        return Math.Max(3.0f + 0.5f * hero.heroForceLv, 1f);
    }

    // Living skill exp multiplier: rank1=x2.0, each rank +0.5 (rank6=x4.5)
    internal static float GetLivingSkillExpMultiplier(HeroData hero)
    {
        if (!Enabled) return 1f;
        return Math.Max(2.0f + 0.5f * hero.heroForceLv, 1f);
    }

    // Favor multiplier: rank1=x1.5, each rank +0.5 (rank6=x4.0)
    internal static float GetFavorMultiplier(HeroData hero)
    {
        if (!Enabled) return 1f;
        return Math.Max(1.5f + 0.5f * hero.heroForceLv, 1f);
    }

    // ---- Beep via Windows API ----
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool Beep(uint freq, uint duration);

    public override void Load()
    {
        Log = base.Log;

        var harmony = new Harmony("com.luoxu.longyin.risingfame");

        // ReadBook exp rate (for UI/theoretical display consistency)
        TryPatch(harmony, AccessTools.Method(typeof(HeroData), "GetBookExpRate", new[] { typeof(KungfuSkillLvData) }),
            postfix: new HarmonyMethod(typeof(ExpPatches), nameof(ExpPatches.MultiplyExpRate)),
            name: "HeroData.GetBookExpRate");

        // Study/battle fight exp rate (for UI/theoretical display consistency)
        TryPatch(harmony, AccessTools.Method(typeof(HeroData), "GetFightExpRate", new[] { typeof(KungfuSkillLvData) }),
            postfix: new HarmonyMethod(typeof(ExpPatches), nameof(ExpPatches.MultiplyExpRate)),
            name: "HeroData.GetFightExpRate");

        // ReadBook real gain
        TryPatch(harmony, AccessTools.Method(typeof(HeroData), "AddSkillBookExp",
                new[] { typeof(float), typeof(KungfuSkillLvData), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(FightExpPatches), nameof(FightExpPatches.AddSkillBookExp_Pre)),
            name: "HeroData.AddSkillBookExp");

        // Combat/fight exp
        TryPatch(harmony, AccessTools.Method(typeof(HeroData), "AddSkillFightExp",
                new[] { typeof(float), typeof(KungfuSkillLvData), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(FightExpPatches), nameof(FightExpPatches.AddSkillFightExp_Pre)),
            name: "HeroData.AddSkillFightExp");

        // Some combat paths use this entrypoint instead of AddSkillFightExp
        TryPatch(harmony, AccessTools.Method(typeof(HeroData), "BattleChangeSkillFightExp",
                new[] { typeof(float), typeof(KungfuSkillLvData), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(FightExpPatches), nameof(FightExpPatches.BattleChangeSkillFightExp_Pre)),
            name: "HeroData.BattleChangeSkillFightExp");

        // Living skill real gain
        TryPatch(harmony, AccessTools.Method(typeof(HeroData), "ChangeLivingSkillExp",
                new[] { typeof(int), typeof(float), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(LivingSkillExpPatches), nameof(LivingSkillExpPatches.ChangeLivingSkillExp_Pre)),
            name: "HeroData.ChangeLivingSkillExp");

        // Favor
        TryPatch(harmony, AccessTools.Method(typeof(HeroData), "ChangeFavor",
                new[] { typeof(float), typeof(bool), typeof(float), typeof(float), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(FavorPatches), nameof(FavorPatches.ChangeFavor_Pre)),
            name: "HeroData.ChangeFavor");

        // Force contribution (inner + outer)
        TryPatch(harmony, AccessTools.Method(typeof(HeroData), "ChangeForceContribution",
                new[] { typeof(float), typeof(bool), typeof(int) }),
            prefix: new HarmonyMethod(typeof(ContributionPatches), nameof(ContributionPatches.ChangeForceContribution_Pre)),
            name: "HeroData.ChangeForceContribution");

        // Govern contribution
        TryPatch(harmony, AccessTools.Method(typeof(HeroData), "ChangeGovernContribution",
                new[] { typeof(float), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(ContributionPatches), nameof(ContributionPatches.ChangeGovernContribution_Pre)),
            name: "HeroData.ChangeGovernContribution");

        // Book writing: speed x10, cost /10
        TryPatch(harmony, AccessTools.Method(typeof(BookWriterData), "GetEachDayWorkPercent"),
            postfix: new HarmonyMethod(typeof(BookWriterPatches), nameof(BookWriterPatches.GetEachDayWorkPercent_Post)),
            name: "BookWriterData.GetEachDayWorkPercent");

        TryPatch(harmony, AccessTools.Method(typeof(BookWriterData), "GetMoneyCost"),
            postfix: new HarmonyMethod(typeof(BookWriterPatches), nameof(BookWriterPatches.GetMoneyCost_Post)),
            name: "BookWriterData.GetMoneyCost");

        TryPatch(harmony, AccessTools.Method(typeof(BookWriterData), "GetTotalTimeCost"),
            postfix: new HarmonyMethod(typeof(BookWriterPatches), nameof(BookWriterPatches.GetTotalTimeCost_Post)),
            name: "BookWriterData.GetTotalTimeCost");

        // Input polling (stable path on this game build)
        TryPatch(harmony, AccessTools.Method(typeof(Camera), "FireOnPostRender", new[] { typeof(Camera) }),
            postfix: new HarmonyMethod(typeof(Plugin), nameof(PollInput)),
            name: "Camera.FireOnPostRender (input)");

        TryPatch(harmony, AccessTools.Method(typeof(PlotController), "GenerateAuctionItem",
                new[] { typeof(ItemListData), typeof(float), typeof(Il2CppSystem.Collections.Generic.List<int>), typeof(int) }),
            prefix: new HarmonyMethod(typeof(AuctionTracePatches), nameof(AuctionTracePatches.GenerateAuctionItem_Pre)),
            postfix: new HarmonyMethod(typeof(AuctionTracePatches), nameof(AuctionTracePatches.GenerateAuctionItem_Post)),
            name: "PlotController.GenerateAuctionItem");

        TryPatch(harmony, AccessTools.Method(typeof(PlotController), "ShowAuctionItem", Type.EmptyTypes),
            prefix: new HarmonyMethod(typeof(AuctionTracePatches), nameof(AuctionTracePatches.ShowAuctionItem_Pre)),
            postfix: new HarmonyMethod(typeof(AuctionTracePatches), nameof(AuctionTracePatches.ShowAuctionItem_Post)),
            name: "PlotController.ShowAuctionItem");

        Log.LogInfo("RisingFame v1.8.5 loaded. Mod ON. Press '=' to toggle. Press Alt+R to refresh current panel.");
        Log.LogInfo("Martial exp: rank1 x3.0, +0.5/rank. Living exp: rank1 x2.0, +0.5/rank. Favor: rank1 x1.5, +0.5/rank. Contribution enabled. BookWrite: speed x10, cost/time /10. Quick refresh: breakthrough / special enhance / enhance / craft / auction reroll arm.");
    }

    void TryPatch(Harmony harmony, System.Reflection.MethodInfo? target,
        HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, string name = "")
    {
        if (target == null) { Log.LogWarning($"[SKIP] {name} - not found"); return; }
        try { harmony.Patch(target, prefix: prefix, postfix: postfix); }
        catch (Exception ex) { Log.LogError($"[FAIL] {name}: {ex.Message}"); }
    }

    public static void PollInput()
    {
        try
        {
            int frame = Time.frameCount;
            if (frame == _lastPollFrame) return;
            _lastPollFrame = frame;

            if (Time.unscaledTime < _nextToggleTime) return;

            bool togglePressed = Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadEquals);
            if (togglePressed)
            {
                _nextToggleTime = Time.unscaledTime + 0.2f;
                Enabled = !Enabled;

                Log.LogInfo($"[RisingFame] {(Enabled ? "ON" : "OFF")}");

                QueueBeep(enabled: Enabled);
                return;
            }

            if (!Enabled) return;

            bool altHeld = Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt);
            if (!altHeld) return;

            bool refreshPressed = Input.GetKeyDown(KeyCode.R);
            if (!refreshPressed) return;

            _nextToggleTime = Time.unscaledTime + 0.15f;

            string result = TryQuickRefresh();
            Log.LogInfo($"[RisingFame] {result}");
            QueueRefreshBeep(result.StartsWith("Refresh: ", StringComparison.Ordinal));
        }
        catch { }
    }

    static void QueueBeep(bool enabled)
    {
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (enabled)
                {
                    Beep(1200, 80);
                    Beep(1600, 80);
                }
                else
                {
                    Beep(800, 80);
                    Beep(400, 80);
                }
            }
            catch { }
        });
    }

    static void QueueRefreshBeep(bool success)
    {
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (success)
                {
                    Beep(1500, 70);
                }
                else
                {
                    Beep(500, 90);
                }
            }
            catch { }
        });
    }

    static string TryQuickRefresh()
    {
        try
        {
            if (TryRefreshSpecialEnhance())
                return "Refresh: special enhance choices";

            if (TryRefreshBreakThrough())
                return "Refresh: breakthrough choices";

            if (TryRefreshEnhance())
                return "Refresh: enhance ui";

            if (TryRefreshCraft())
                return "Refresh: craft choices";

            if (TryRefreshAuction())
                return "Refresh: auction reroll armed";
        }
        catch (Exception ex)
        {
            return $"Refresh failed: {ex.Message}";
        }

        PlotController? plot = PlotController.Instance;
        if (plot != null && IsPanelActive(plot.plotPanel))
        {
            LogAuctionPlotContext(plot);
        }

        LogRefreshContext();

        return "Refresh skipped: no supported panel";
    }

    static bool TryRefreshSpecialEnhance()
    {
        SpeEnhanceEquipController? instance = SpeEnhanceEquipController.Instance;
        if (instance == null) return false;
        if (!IsPanelActive(instance.speEnhanceEquipUI)) return false;
        if (!instance.CanEnhance()) return false;

        instance.ClearAllChoice();
        instance.GenerateChoice();
        instance.RefreshEnhanceButtonState();
        return true;
    }

    static bool TryRefreshBreakThrough()
    {
        BreakThroughController? instance = BreakThroughController.Instance;
        if (instance == null) return false;
        if (!IsPanelActive(instance.breakThroughPanel)) return false;
        if (instance.targetSkill == null) return false;

        instance.StartShowBreakChoice();
        instance.RefreshExtraRateInfo();
        return true;
    }

    static bool TryRefreshAuction()
    {
        PlotController? plot = PlotController.Instance;
        if (plot == null || !IsAuctionPlotActive(plot))
            return false;

        if (FindAuctionChoice(plot, "ShowAuctionItem") != null)
        {
            _pendingAuctionReroll = true;
            Log.LogInfo("[RisingFame] Auction reroll armed. Click native '查看展品' to apply.");
            return true;
        }

        LogAuctionPlotContext(plot);
        return false;
    }

    static bool TryRefreshCraft()
    {
        CraftUIController? instance = CraftUIController.Instance;
        if (instance == null) return false;
        if (!IsPanelActive(instance.creaftUIPanel)) return false;
        if (instance.craftResultList == null || instance.craftResultList.Count <= 0) return false;

        string before = GetItemListSummary(instance.craftResultList);

        instance.CraftButtonClicked();
        string after = GetItemListSummary(instance.craftResultList);

        Log.LogInfo($"[RisingFame] Craft refresh results {before} -> {after}");
        return !string.Equals(before, after, StringComparison.Ordinal);
    }

    static bool TryRefreshAuctionByChoice(PlotController plot, AuctionController? auction)
    {
        SinglePlotChoiceData? choice = FindAuctionChoice(plot, "ShowAuctionItem");
        if (choice == null) return false;

        string beforePool = GetItemListSummary(plot.tempPlotShop);
        string beforeCached = GetItemListSummary(_lastAuctionTargetList);
        string beforeShown = auction != null ? GetItemListSummary(auction.auctionItemList) : "null";
        int beforeGenerateVersion = _auctionGenerateVersion;

        PrepareAuctionChoiceContext(plot, choice);
        ResetPlotItemDisplay(plot);

        if (!TryInvokePlotChoiceCall(plot, choice))
            return false;

        string afterPool = GetItemListSummary(plot.tempPlotShop);
        string afterCached = GetItemListSummary(_lastAuctionTargetList);
        string afterShown = auction != null ? GetItemListSummary(auction.auctionItemList) : "null";
        bool generated = beforeGenerateVersion != _auctionGenerateVersion;

        if (generated
            || !string.Equals(beforePool, afterPool, StringComparison.Ordinal)
            || !string.Equals(beforeCached, afterCached, StringComparison.Ordinal)
            || !string.Equals(beforeShown, afterShown, StringComparison.Ordinal))
        {
            Log.LogInfo($"[RisingFame] Auction choice refresh pool {beforePool} -> {afterPool} | cached {beforeCached} -> {afterCached} | shown {beforeShown} -> {afterShown} | generated={generated}");
            return true;
        }

        return false;
    }

    static void ResetPlotItemDisplay(PlotController plot)
    {
        int childCountBefore = GetPlotItemGridChildCount(plot);
        Log.LogInfo($"[RisingFame] Plot item reset begin children={childCountBefore}");

        try
        {
            plot.HidePlotItem();
        }
        catch { }

        try
        {
            plot.ClearPlotItem();
        }
        catch { }

        try
        {
            plot.plotInteractItem = null!;
        }
        catch { }

        try
        {
            plot.plotInteractItemTempRecord = null!;
        }
        catch { }

        try
        {
            if (plot.plotItemGrid != null)
            {
                Transform grid = plot.plotItemGrid.transform;
                int removed = 0;
                for (int i = grid.childCount - 1; i >= 0; i--)
                {
                    Transform child = grid.GetChild(i);
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
                    removed++;
                }

                Log.LogInfo($"[RisingFame] Plot item reset removed={removed}");
            }
        }
        catch (Exception ex)
        {
            Log.LogInfo($"[RisingFame] Plot item reset destroy error: {ex.Message}");
        }

        int childCountAfter = GetPlotItemGridChildCount(plot);
        Log.LogInfo($"[RisingFame] Plot item reset children {childCountBefore} -> {childCountAfter}");
    }

    static int GetPlotItemGridChildCount(PlotController plot)
    {
        try
        {
            return plot.plotItemGrid != null ? plot.plotItemGrid.transform.childCount : 0;
        }
        catch
        {
            return -1;
        }
    }

    static bool TryRefreshAuctionByCachedGenerate(PlotController plot, AuctionController? auction)
    {
        if (_lastAuctionTargetList == null) return false;

        ItemListData targetList = plot.tempPlotShop ?? _lastAuctionTargetList;
        string beforePool = GetItemListSummary(targetList);
        string beforeShown = auction != null ? GetItemListSummary(auction.auctionItemList) : "null";

        if (!ReferenceEquals(plot.tempPlotShop, targetList))
            TrySetMember(plot, "tempPlotShop", targetList);

        ClearItemList(targetList);
        if (auction != null && auction.auctionItemList != null && !ReferenceEquals(auction.auctionItemList, targetList))
            ClearItemList(auction.auctionItemList);

        plot.GenerateAuctionItem(targetList, _lastAuctionShopLv, CloneIntList(_lastAuctionItemTypeLimit), _lastAuctionItemNum);

        SinglePlotChoiceData? choice = FindAuctionChoice(plot, "ShowAuctionItem");
        if (choice != null)
        {
            PrepareAuctionChoiceContext(plot, choice);
            TryInvokePlotChoiceCall(plot, choice);
        }
        else
        {
            plot.ShowAuctionItem();
        }

        string afterPool = GetItemListSummary(targetList);
        string afterShown = auction != null ? GetItemListSummary(auction.auctionItemList) : "null";

        if (!string.Equals(beforePool, afterPool, StringComparison.Ordinal)
            || !string.Equals(beforeShown, afterShown, StringComparison.Ordinal))
        {
            Log.LogInfo($"[RisingFame] Auction cached refresh pool {beforePool} -> {afterPool} | shown {beforeShown} -> {afterShown}");
            return true;
        }

        return false;
    }

    static bool TryRefreshEnhance()
    {
        EnhanceUIController? instance = EnhanceUIController.Instance;
        if (instance == null) return false;
        if (!IsPanelActive(instance.enhanceUIPanel)) return false;
        if (instance.targetBuilding == null) return false;

        instance.HideEnhanceUI();
        instance.OpenEnhanceUI(instance.enhanceType, instance.targetBuilding, instance.useMoney);
        return true;
    }

    static bool IsAuctionPanelActive(AuctionController instance)
    {
        return IsPanelActive(instance.auctionPanel)
            || IsPanelActive(instance.playerAuctionUI)
            || IsPanelActive(instance.startAuctionButton)
            || IsPanelActive(instance.talkPanel)
            || IsPanelActive(instance.skipButton);
    }

    static bool IsAuctionPlotActive(PlotController instance)
    {
        if (!IsPanelActive(instance.plotPanel)) return false;

        return instance.tempPlotShop != null
            || ContainsAuctionToken(instance.nowPlot?.plotCallFuc)
            || ContainsAuctionToken(instance.nowPlot?.plotName)
            || ContainsAuctionToken(instance.nowSinglePlot?.clickCallFuc)
            || ContainsAuctionToken(instance.nowPlotText)
            || HasAuctionChoice(instance.nowSinglePlot);
    }

    static bool ContainsAuctionToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;

        return value.Contains("Auction", StringComparison.OrdinalIgnoreCase)
            || value.Contains("ShowAuctionItem", StringComparison.OrdinalIgnoreCase)
            || value.Contains("StartAreaAuction", StringComparison.OrdinalIgnoreCase)
            || value.Contains("StartAuctionRound", StringComparison.OrdinalIgnoreCase)
            || value.Contains("SellAuctionItem", StringComparison.OrdinalIgnoreCase);
    }
    static bool HasAuctionChoice(SinglePlotData? plot)
    {
        if (plot?.choices == null) return false;

        for (int i = 0; i < plot.choices.Count; i++)
        {
            SinglePlotChoiceData choice = plot.choices[i];
            if (ContainsAuctionToken(choice.choiceText)
                || ContainsAuctionToken(choice.callFuc)
                || ContainsAuctionToken(choice.callParam))
            {
                return true;
            }
        }

        return false;
    }

    static SinglePlotChoiceData? FindAuctionChoice(PlotController plot, string callFuc)
    {
        if (plot.nowSinglePlot?.choices == null) return null;

        for (int i = 0; i < plot.nowSinglePlot.choices.Count; i++)
        {
            SinglePlotChoiceData choice = plot.nowSinglePlot.choices[i];
            if (string.Equals(choice.callFuc, callFuc, StringComparison.OrdinalIgnoreCase))
                return choice;
        }

        return null;
    }

    static void PrepareAuctionChoiceContext(PlotController plot, SinglePlotChoiceData choice)
    {
        bool nowSet = TrySetMember(plot, "nowChoice", choice);
        bool newSet = TrySetMember(plot, "newChoice", choice);
        Log.LogInfo($"[RisingFame] Auction choice context nowChoice={nowSet} newChoice={newSet} call={choice.callFuc} param={choice.callParam}");
    }

    static bool TryInvokePlotChoiceCall(PlotController plot, SinglePlotChoiceData choice)
    {
        string callFuc = choice.callFuc ?? string.Empty;
        if (string.IsNullOrWhiteSpace(callFuc)) return false;

        MethodInfo? noArg = AccessTools.Method(typeof(PlotController), callFuc, Type.EmptyTypes);
        if (noArg != null)
        {
            noArg.Invoke(plot, null);
            return true;
        }

        MethodInfo? oneArg = AccessTools.Method(typeof(PlotController), callFuc, new[] { typeof(string) });
        if (oneArg != null)
        {
            oneArg.Invoke(plot, new object?[] { choice.callParam ?? string.Empty });
            return true;
        }

        return false;
    }

    static bool TrySetMember(object target, string name, object? value)
    {
        Type type = target.GetType();

        try
        {
            PropertyInfo? property = AccessTools.Property(type, name);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return true;
            }

            FieldInfo? field = AccessTools.Field(type, name);
            if (field != null)
            {
                field.SetValue(target, value);
                return true;
            }
        }
        catch { }

        return false;
    }

    static void ClearItemList(ItemListData? list)
    {
        if (list?.allItem == null) return;
        list.allItem.Clear();
    }

    static void ClearItemList(Il2CppSystem.Collections.Generic.List<ItemData>? items)
    {
        if (items == null) return;
        items.Clear();
    }

    static Il2CppSystem.Collections.Generic.List<int> CloneIntList(Il2CppSystem.Collections.Generic.List<int>? source)
    {
        var clone = new Il2CppSystem.Collections.Generic.List<int>();
        if (source == null) return clone;

        for (int i = 0; i < source.Count; i++)
            clone.Add(source[i]);

        return clone;
    }

    internal static void CacheAuctionGenerate(ItemListData targetList, float shopLv, Il2CppSystem.Collections.Generic.List<int>? itemTypeLimit, int itemNum)
    {
        _lastAuctionTargetList = targetList;
        _lastAuctionShopLv = shopLv;
        _lastAuctionItemTypeLimit = CloneIntList(itemTypeLimit);
        _lastAuctionItemNum = itemNum;
    }

    internal static void MarkAuctionGenerated()
    {
        _auctionGenerateVersion++;
        if (_pendingAuctionReroll)
        {
            _pendingAuctionReroll = false;
            Log.LogInfo("[RisingFame] Auction reroll applied by native exhibit view.");
        }
    }

    internal static void PreparePendingAuctionReroll(PlotController plot)
    {
        if (!_pendingAuctionReroll) return;

        string beforeCached = GetItemListSummary(_lastAuctionTargetList);
        string beforeTemp = GetItemListSummary(plot.tempPlotShop);

        if (_lastAuctionTargetList != null)
            ClearItemList(_lastAuctionTargetList);

        if (plot.tempPlotShop != null && !ReferenceEquals(plot.tempPlotShop, _lastAuctionTargetList))
            ClearItemList(plot.tempPlotShop);

        try
        {
            plot.plotInteractItem = null!;
        }
        catch { }

        try
        {
            plot.plotInteractItemTempRecord = null!;
        }
        catch { }

        Log.LogInfo($"[RisingFame] Auction reroll prepare cached {beforeCached} -> {GetItemListSummary(_lastAuctionTargetList)} | temp {beforeTemp} -> {GetItemListSummary(plot.tempPlotShop)}");
    }

    internal static string GetAuctionChoiceContext(PlotController plot)
    {
        string nowChoice = TryGetChoiceCall(plot, "nowChoice");
        string newChoice = TryGetChoiceCall(plot, "newChoice");
        return $"nowChoice={nowChoice} newChoice={newChoice}";
    }

    static string TryGetChoiceCall(PlotController plot, string memberName)
    {
        try
        {
            Type type = plot.GetType();
            object? value = null;

            PropertyInfo? property = AccessTools.Property(type, memberName);
            if (property != null && property.CanRead)
                value = property.GetValue(plot);

            if (value == null)
            {
                FieldInfo? field = AccessTools.Field(type, memberName);
                if (field != null)
                    value = field.GetValue(plot);
            }

            if (value is SinglePlotChoiceData choice)
                return choice.callFuc ?? "null";
        }
        catch { }

        return "null";
    }

    internal static string GetItemListSummary(ItemListData? list)
    {
        if (list == null || list.allItem == null) return "null";
        return GetItemListSummary(list.allItem);
    }

    internal static string GetItemListSummary(Il2CppSystem.Collections.Generic.List<ItemData>? items)
    {
        if (items == null) return "null";

        int count = items.Count;
        if (count <= 0) return "count=0";

        int show = Math.Min(count, 6);
        string[] parts = new string[show];
        for (int i = 0; i < show; i++)
        {
            ItemData item = items[i];
            parts[i] = $"{item.itemID}:{item.name}";
        }

        return $"count={count} [{string.Join(" | ", parts)}{(count > show ? " | ..." : string.Empty)}]";
    }

    static void LogAuctionPlotContext(PlotController plot)
    {
        string plotName = plot.nowPlot?.plotName ?? "null";
        string plotCall = plot.nowPlot?.plotCallFuc ?? "null";
        string clickCall = plot.nowSinglePlot?.clickCallFuc ?? "null";
        int choiceCount = plot.nowSinglePlot?.choices?.Count ?? 0;

        Log.LogInfo($"[RisingFame] Auction plot unresolved. plot={plotName}, plotCall={plotCall}, clickCall={clickCall}, pool={GetItemListSummary(plot.tempPlotShop)}");
        Log.LogInfo($"[RisingFame] Auction plot choice ctx {GetAuctionChoiceContext(plot)}");

        if (plot.nowSinglePlot?.choices == null) return;

        for (int i = 0; i < plot.nowSinglePlot.choices.Count; i++)
        {
            SinglePlotChoiceData choice = plot.nowSinglePlot.choices[i];
            Log.LogInfo($"[RisingFame] Auction choice[{i + 1}/{choiceCount}] text={choice.choiceText} call={choice.callFuc} param={choice.callParam}");
        }
    }

    static void LogRefreshContext()
    {
        AuctionController? auction = AuctionController.Instance;
        if (auction != null)
        {
            Log.LogInfo(
                $"[RisingFame] Auction ctx panel={IsPanelActive(auction.auctionPanel)} playerUI={IsPanelActive(auction.playerAuctionUI)} startBtn={IsPanelActive(auction.startAuctionButton)} talk={IsPanelActive(auction.talkPanel)} skip={IsPanelActive(auction.skipButton)} shown={GetItemListSummary(auction.auctionItemList)}");
        }

        PlotController? plot = PlotController.Instance;
        if (plot != null)
        {
            bool hasPool = plot.tempPlotShop != null;
            bool plotPanel = IsPanelActive(plot.plotPanel);
            if (hasPool || plotPanel || ContainsAuctionToken(plot.nowPlot?.plotName) || ContainsAuctionToken(plot.nowPlotText))
            {
                Log.LogInfo(
                    $"[RisingFame] Plot ctx panel={plotPanel} pool={GetItemListSummary(plot.tempPlotShop)} plot={plot.nowPlot?.plotName ?? "null"} plotCall={plot.nowPlot?.plotCallFuc ?? "null"} clickCall={plot.nowSinglePlot?.clickCallFuc ?? "null"}");
            }
        }
    }

    static bool IsPanelActive(GameObject? panel)
    {
        return panel != null && panel.activeInHierarchy;
    }
}

[HarmonyPatch]
static class ExpPatches
{
    public static void MultiplyExpRate(HeroData __instance, ref float __result)
    {
        if (__result <= 0f) return;
        float m = Plugin.GetExpMultiplier(__instance);
        if (m <= 1f) return;
        __result *= m;
    }
}

[HarmonyPatch]
static class FightExpPatches
{
    public static void AddSkillBookExp_Pre(HeroData __instance, ref float __0)
    {
        if (__0 <= 0f) return;
        float m = Plugin.GetExpMultiplier(__instance);
        if (m <= 1f) return;
        __0 *= m;
    }

    public static void AddSkillFightExp_Pre(HeroData __instance, ref float __0)
    {
        if (__0 <= 0f) return;
        float m = Plugin.GetExpMultiplier(__instance);
        if (m <= 1f) return;
        __0 *= m;
    }

    public static void BattleChangeSkillFightExp_Pre(HeroData __instance, ref float __0)
    {
        if (__0 <= 0f) return;
        float m = Plugin.GetExpMultiplier(__instance);
        if (m <= 1f) return;
        __0 *= m;
    }
}

[HarmonyPatch]
static class LivingSkillExpPatches
{
    public static void ChangeLivingSkillExp_Pre(HeroData __instance, ref float __1)
    {
        if (__1 <= 0f) return;
        float m = Plugin.GetLivingSkillExpMultiplier(__instance);
        if (m <= 1f) return;
        __1 *= m;
    }
}

[HarmonyPatch]
static class FavorPatches
{
    public static void ChangeFavor_Pre(HeroData __instance, ref float __0)
    {
        if (__0 <= 0f) return;
        float m = Plugin.GetFavorMultiplier(__instance);
        if (m <= 1f) return;
        __0 *= m;
    }
}

[HarmonyPatch]
static class ContributionPatches
{
    public static void ChangeForceContribution_Pre(HeroData __instance, ref float __0, bool __1, int __2)
    {
        if (__0 <= 0f || !__1 || !Plugin.Enabled) return;
        try
        {
            int lv = __instance.heroForceLv + 1; // 1-6 (武者~宗师)
            float fame = __instance.fame;
            bool isInner = (__2 == __instance.belongForceID);
            float rate = isInner ? (lv + fame / 1000f) * 0.5f : lv + fame / 1000f;
            __0 = Math.Max(__0, 1f) * Math.Max(rate, 1f);
        }
        catch { }
    }

    public static void ChangeGovernContribution_Pre(HeroData __instance, ref float __0, bool __1)
    {
        if (__0 <= 0f || !__1 || !Plugin.Enabled) return;
        try
        {
            int lv = __instance.heroForceLv + 1;
            float fame = __instance.fame;
            float rate = lv + fame / 1000f;
            __0 = Math.Max(__0, 1f) * Math.Max(rate, 1f);
        }
        catch { }
    }
}

[HarmonyPatch]
static class BookWriterPatches
{
    // Speed up book writing: each day progress x10
    public static void GetEachDayWorkPercent_Post(ref float __result)
    {
        if (!Plugin.Enabled) return;
        __result *= 10f;
    }

    // Reduce money cost to 1/10
    public static void GetMoneyCost_Post(ref int __result)
    {
        if (!Plugin.Enabled) return;
        __result = Math.Max(__result / 10, 1);
    }

    // Reduce time cost display to 1/10
    public static void GetTotalTimeCost_Post(ref int __result)
    {
        if (!Plugin.Enabled) return;
        __result = Math.Max(__result / 10, 1);
    }
}

[HarmonyPatch]
static class AuctionTracePatches
{
    public static void GenerateAuctionItem_Pre(PlotController __instance, ItemListData __0, float __1, Il2CppSystem.Collections.Generic.List<int> __2, int __3)
    {
        Plugin.CacheAuctionGenerate(__0, __1, __2, __3);
        Plugin.Log.LogInfo($"[RisingFame] Auction generate pre pool={Plugin.GetItemListSummary(__0)} shopLv={__1:0.##} itemNum={__3} ctx={Plugin.GetAuctionChoiceContext(__instance)}");
    }

    public static void GenerateAuctionItem_Post(PlotController __instance, ItemListData __0, float __1, Il2CppSystem.Collections.Generic.List<int> __2, int __3)
    {
        Plugin.MarkAuctionGenerated();
        Plugin.Log.LogInfo($"[RisingFame] Auction generate post pool={Plugin.GetItemListSummary(__0)} shopLv={__1:0.##} itemNum={__3} ctx={Plugin.GetAuctionChoiceContext(__instance)}");
    }

    public static void ShowAuctionItem_Pre(PlotController __instance)
    {
        Plugin.PreparePendingAuctionReroll(__instance);
        Plugin.Log.LogInfo($"[RisingFame] ShowAuctionItem pre pool={Plugin.GetItemListSummary(__instance.tempPlotShop)} ctx={Plugin.GetAuctionChoiceContext(__instance)}");
    }

    public static void ShowAuctionItem_Post(PlotController __instance)
    {
        Plugin.Log.LogInfo($"[RisingFame] ShowAuctionItem post pool={Plugin.GetItemListSummary(__instance.tempPlotShop)} ctx={Plugin.GetAuctionChoiceContext(__instance)}");
    }
}

