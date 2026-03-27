using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace RisingFame;

[BepInPlugin("com.luoxu.longyin.risingfame", "RisingFame - MingYangTianXia", "1.8.16")]
public class Plugin : BasePlugin
{
    const string PluginVersion = "1.8.16";
    const int BreakThroughFastRefreshFrames = 20;
    const float BreakThroughFastParticleDuration = 0.02f;
    internal static new ManualLogSource Log = null!;

    // ---- Mod ON/OFF ----
    internal static bool Enabled = true;

    // Input debounce to avoid rapid toggle spam.
    static float _nextToggleTime;
    static int _lastPollFrame = -1;
    enum AuctionRefreshStep
    {
        None,
        SingleShow,
        ResetDisplay,
        ChooseClose,
        EnsureOpen
    }
    internal enum AuctionProbeSource
    {
        None,
        Natural,
        Plugin
    }

    static int _pendingAuctionSequenceFrame = -1;
    static AuctionRefreshStep _pendingAuctionStep;
    static int _breakThroughFastRefreshUntilFrame = -1;

    // Cached auction generation context captured from the game's native path.
    static ItemListData? _lastAuctionTargetList;
    static float _lastAuctionShopLv;
    static Il2CppSystem.Collections.Generic.List<int>? _lastAuctionItemTypeLimit;
    static int _lastAuctionItemNum;
    static int _auctionGenerateVersion;
    static bool _pendingAuctionReroll;
    static bool _pendingAuctionAwaitChooseClose;
    static int _pendingAuctionEnsureOpenRetries;
    static int _auctionProbeNaturalSeq;
    static int _auctionProbePluginSeq;
    static bool _auctionProbeNaturalArmed;
    static bool _auctionProbePluginArmed;
    static string _auctionProbeNaturalId = string.Empty;
    static string _auctionProbePluginId = string.Empty;
    static AuctionProbeSource _auctionNextShowSource;
    static string _auctionProbeActiveId = string.Empty;
    static AuctionProbeSource _auctionProbeActiveSource;
    static int _auctionProbeActiveFrame = -1;
    static bool _auctionProbeGenerateSeen;
    static bool _auctionProbeStackArmed;
    static bool _auctionProbeStackEmitted;

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

        TryPatch(harmony, AccessTools.Method(typeof(ChooseController), "HideChoosePanel", Type.EmptyTypes),
            postfix: new HarmonyMethod(typeof(ChoosePanelTracePatches), nameof(ChoosePanelTracePatches.HideChoosePanel_Post)),
            name: "ChooseController.HideChoosePanel");

        TryPatch(harmony, AccessTools.Method(typeof(ChooseController), "UnshowChoosePanel", Type.EmptyTypes),
            postfix: new HarmonyMethod(typeof(ChoosePanelTracePatches), nameof(ChoosePanelTracePatches.UnshowChoosePanel_Post)),
            name: "ChooseController.UnshowChoosePanel");

        TryPatch(harmony, AccessTools.Method(typeof(BreakThroughController), "ShowItemParticle",
                new[] { typeof(GameObject), typeof(GameObject), typeof(float) }),
            prefix: new HarmonyMethod(typeof(BreakThroughTracePatches), nameof(BreakThroughTracePatches.ShowItemParticle3_Pre)),
            name: "BreakThroughController.ShowItemParticle/3");

        TryPatch(harmony, AccessTools.Method(typeof(BreakThroughController), "ShowItemParticle",
                new[] { typeof(GameObject), typeof(GameObject), typeof(float), typeof(float), typeof(int) }),
            prefix: new HarmonyMethod(typeof(BreakThroughTracePatches), nameof(BreakThroughTracePatches.ShowItemParticle5_Pre)),
            name: "BreakThroughController.ShowItemParticle/5");

        TryPatchAllOverloads(harmony, typeof(AuctionController), "StartAuctionRound",
            prefix: new HarmonyMethod(typeof(AuctionEntryTracePatches), nameof(AuctionEntryTracePatches.AuctionEntry_Pre)),
            postfix: new HarmonyMethod(typeof(AuctionEntryTracePatches), nameof(AuctionEntryTracePatches.AuctionEntry_Post)));
        TryPatchAllOverloads(harmony, typeof(AuctionController), "StartAreaAuction",
            prefix: new HarmonyMethod(typeof(AuctionEntryTracePatches), nameof(AuctionEntryTracePatches.AuctionEntry_Pre)),
            postfix: new HarmonyMethod(typeof(AuctionEntryTracePatches), nameof(AuctionEntryTracePatches.AuctionEntry_Post)));
        TryPatchAllOverloads(harmony, typeof(AuctionController), "RestartAuction",
            prefix: new HarmonyMethod(typeof(AuctionEntryTracePatches), nameof(AuctionEntryTracePatches.AuctionEntry_Pre)),
            postfix: new HarmonyMethod(typeof(AuctionEntryTracePatches), nameof(AuctionEntryTracePatches.AuctionEntry_Post)));

        TryPatchAllOverloads(harmony, typeof(ItemListController), "SetItemList",
            prefix: new HarmonyMethod(typeof(ItemListTracePatches), nameof(ItemListTracePatches.ItemList_Pre)),
            postfix: new HarmonyMethod(typeof(ItemListTracePatches), nameof(ItemListTracePatches.ItemList_Post)));
        TryPatchAllOverloads(harmony, typeof(ItemListController), "RefreshItemList",
            prefix: new HarmonyMethod(typeof(ItemListTracePatches), nameof(ItemListTracePatches.ItemList_Pre)),
            postfix: new HarmonyMethod(typeof(ItemListTracePatches), nameof(ItemListTracePatches.ItemList_Post)));
        TryPatchAllOverloads(harmony, typeof(ItemListController), "SetItemListData",
            prefix: new HarmonyMethod(typeof(ItemListTracePatches), nameof(ItemListTracePatches.ItemList_Pre)),
            postfix: new HarmonyMethod(typeof(ItemListTracePatches), nameof(ItemListTracePatches.ItemList_Post)));

        LogMethodOverloads(typeof(PlotController), "ShowAuctionItem");
        LogMethodOverloads(typeof(PlotController), "GenerateAuctionItem");
        LogMethodOverloads(typeof(AuctionController), "StartAuctionRound");
        LogMethodOverloads(typeof(AuctionController), "StartAreaAuction");
        LogMethodOverloads(typeof(AuctionController), "RestartAuction");
        LogMethodOverloads(typeof(ItemListController), "SetItemList");
        LogMethodOverloads(typeof(ItemListController), "RefreshItemList");
        LogMethodOverloads(typeof(ItemListController), "SetItemListData");

        Log.LogInfo($"RisingFame v{PluginVersion} loaded. Mod ON. Press '=' to toggle. Press Alt+R to refresh current panel. Press Alt+I to dump auction UI snapshot.");
        Log.LogInfo("Martial exp: rank1 x3.0, +0.5/rank. Living exp: rank1 x2.0, +0.5/rank. Favor: rank1 x1.5, +0.5/rank. Contribution enabled. BookWrite: speed x10, cost/time /10. Quick refresh: breakthrough / special enhance / enhance / craft / auction reroll arm.");
    }

    void TryPatch(Harmony harmony, System.Reflection.MethodInfo? target,
        HarmonyMethod? prefix = null, HarmonyMethod? postfix = null, string name = "")
    {
        if (target == null) { Log.LogWarning($"[SKIP] {name} - not found"); return; }
        try { harmony.Patch(target, prefix: prefix, postfix: postfix); }
        catch (Exception ex) { Log.LogError($"[FAIL] {name}: {ex.Message}"); }
    }

    void TryPatchAllOverloads(Harmony harmony, Type type, string methodName,
        HarmonyMethod? prefix = null, HarmonyMethod? postfix = null)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo[] methods = type.GetMethods(flags);
            int patched = 0;
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                patched++;
                try
                {
                    harmony.Patch(method, prefix: prefix, postfix: postfix);
                }
                catch (Exception ex)
                {
                    Log.LogError($"[FAIL] {type.Name}.{methodName} overload {DescribeMethodSignature(method)}: {ex.Message}");
                }
            }

            if (patched == 0)
                Log.LogWarning($"[SKIP] {type.Name}.{methodName} - not found");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[FAIL] {type.Name}.{methodName} patch scan failed: {ex.Message}");
        }
    }

    static void LogMethodOverloads(Type type, string methodName)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo[] methods = type.GetMethods(flags);
            int found = 0;
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    continue;

                found++;
                Log.LogInfo($"[RisingFame] Method overload {type.Name}.{methodName}#{found}: {DescribeMethodSignature(method)}");
            }

            if (found == 0)
                Log.LogWarning($"[RisingFame] Method overload {type.Name}.{methodName}: none");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"[RisingFame] Method overload {type.Name}.{methodName} inspect failed: {ex.Message}");
        }
    }

    static string DescribeMethodSignature(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length == 0)
            return $"{method.ReturnType.Name} {method.Name}()";

        string[] parts = new string[parameters.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo parameter = parameters[i];
            parts[i] = $"{parameter.ParameterType.Name} {parameter.Name}";
        }

        return $"{method.ReturnType.Name} {method.Name}({string.Join(", ", parts)})";
    }

    public static void PollInput()
    {
        try
        {
            int frame = Time.frameCount;
            if (frame == _lastPollFrame) return;
            _lastPollFrame = frame;

            TickPendingActions(frame);

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
            bool inspectPressed = Input.GetKeyDown(KeyCode.I);

            if (inspectPressed)
            {
                bool shiftHeld = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                ArmAuctionNaturalProbe(withStack: shiftHeld);
                DumpAuctionUiSnapshot();
                QueueRefreshBeep(success: true);
                return;
            }

            if (!refreshPressed) return;

            _nextToggleTime = Time.unscaledTime + 0.15f;

            string result = TryQuickRefresh();
            Log.LogInfo($"[RisingFame] {result}");
            QueueRefreshBeep(result.StartsWith("Refresh: ", StringComparison.Ordinal));
        }
        catch { }
    }

    static void TickPendingActions(int frame)
    {
        if (_pendingAuctionSequenceFrame < 0 || frame < _pendingAuctionSequenceFrame)
            return;

        AdvanceAuctionRefreshSequence(frame);
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

    static string NextProbeId(string prefix, ref int seq)
    {
        seq++;
        return $"{prefix}_{seq:000}";
    }

    static void ArmAuctionNaturalProbe(bool withStack)
    {
        _auctionProbeNaturalId = NextProbeId("natural", ref _auctionProbeNaturalSeq);
        _auctionProbeNaturalArmed = true;
        if (withStack)
        {
            _auctionProbeStackArmed = true;
            _auctionProbeStackEmitted = false;
        }

        Log.LogInfo($"[RisingFame] Auction probe armed. auction_probe_id={_auctionProbeNaturalId} source=natural{(withStack ? " stack=on" : string.Empty)}");
    }

    static void ArmAuctionPluginProbe()
    {
        _auctionProbePluginId = NextProbeId("plugin", ref _auctionProbePluginSeq);
        _auctionProbePluginArmed = true;
        Log.LogInfo($"[RisingFame] Auction probe armed. auction_probe_id={_auctionProbePluginId} source=plugin");
    }

    static void SetActiveAuctionProbe(string id, AuctionProbeSource source)
    {
        _auctionProbeActiveId = id;
        _auctionProbeActiveSource = source;
        _auctionProbeActiveFrame = Time.frameCount;
        _auctionProbeGenerateSeen = false;
    }

    static void ClearActiveAuctionProbe()
    {
        _auctionProbeActiveId = string.Empty;
        _auctionProbeActiveSource = AuctionProbeSource.None;
        _auctionProbeActiveFrame = -1;
        _auctionProbeGenerateSeen = false;
    }

    static bool IsActiveProbe(AuctionProbeSource source)
    {
        if (string.IsNullOrWhiteSpace(_auctionProbeActiveId)) return false;
        if (_auctionProbeActiveSource != source) return false;
        int delta = Time.frameCount - _auctionProbeActiveFrame;
        return delta >= 0 && delta <= 300;
    }

    static bool ShouldLogProbeAny()
    {
        return _auctionProbeNaturalArmed
            || _auctionProbePluginArmed
            || !string.IsNullOrWhiteSpace(_auctionProbeActiveId);
    }

    static bool TryActivateProbe(AuctionProbeSource source, string reason)
    {
        if (!string.IsNullOrWhiteSpace(_auctionProbeActiveId))
            return true;

        if (source == AuctionProbeSource.Plugin && _auctionProbePluginArmed)
        {
            string id = _auctionProbePluginId;
            _auctionProbePluginArmed = false;
            SetActiveAuctionProbe(id, AuctionProbeSource.Plugin);
            Log.LogInfo($"[RisingFame] auction_probe_id={id} source=plugin activatedBy={reason}");
            return true;
        }

        if (source == AuctionProbeSource.Natural && _auctionProbeNaturalArmed)
        {
            string id = _auctionProbeNaturalId;
            _auctionProbeNaturalArmed = false;
            SetActiveAuctionProbe(id, AuctionProbeSource.Natural);
            Log.LogInfo($"[RisingFame] auction_probe_id={id} source=natural activatedBy={reason}");
            return true;
        }

        return false;
    }

    static bool TryActivateProbeFromAny(string reason)
    {
        if (!string.IsNullOrWhiteSpace(_auctionProbeActiveId))
            return true;

        if (_auctionProbePluginArmed)
            return TryActivateProbe(AuctionProbeSource.Plugin, reason);

        if (_auctionProbeNaturalArmed)
            return TryActivateProbe(AuctionProbeSource.Natural, reason);

        return false;
    }

    internal static AuctionProbeSource ConsumeNextShowSource()
    {
        AuctionProbeSource source = _auctionNextShowSource;
        _auctionNextShowSource = AuctionProbeSource.None;
        return source;
    }

    static void MarkNextShowFromPlugin()
    {
        _auctionNextShowSource = AuctionProbeSource.Plugin;
    }

    static int CountActiveItemIcons()
    {
        try
        {
            ItemIconController[] icons = UnityEngine.Object.FindObjectsOfType<ItemIconController>(true);
            int activeCount = 0;
            for (int i = 0; i < icons.Length; i++)
            {
                ItemIconController? icon = icons[i];
                if (icon == null || icon.gameObject == null || !icon.gameObject.activeInHierarchy)
                    continue;

                activeCount++;
            }

            return activeCount;
        }
        catch
        {
            return -1;
        }
    }

    static void MaybeLogProbeStack(string probeId)
    {
        if (!_auctionProbeStackArmed || _auctionProbeStackEmitted) return;
        _auctionProbeStackEmitted = true;
        _auctionProbeStackArmed = false;
        string stack = TrimStack(Environment.StackTrace, 24);
        Log.LogInfo($"[RisingFame] auction_probe_id={probeId} stack={stack}");
    }

    static string TrimStack(string stack, int maxLines)
    {
        if (string.IsNullOrWhiteSpace(stack)) return "null";
        string[] lines = stack.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        int take = Math.Min(lines.Length, Math.Max(1, maxLines));
        return string.Join(" | ", lines, 0, take);
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

        int removed = ClearBreakThroughChoiceIcons(instance);
        ArmBreakThroughFastRefresh();
        instance.StartShowBreakChoice();
        instance.RefreshExtraRateInfo();
        Log.LogInfo($"[RisingFame] BreakThrough refresh cleared icons removed={removed}");
        return true;
    }

    static void ArmBreakThroughFastRefresh()
    {
        _breakThroughFastRefreshUntilFrame = Math.Max(_breakThroughFastRefreshUntilFrame, Time.frameCount + BreakThroughFastRefreshFrames);
    }

    internal static void AccelerateBreakThroughShowItemParticle(ref float duration)
    {
        if (!IsBreakThroughFastRefreshActive()) return;
        if (duration > BreakThroughFastParticleDuration)
            duration = BreakThroughFastParticleDuration;
    }

    internal static void AccelerateBreakThroughShowItemParticle(ref float durationA, ref float durationB)
    {
        if (!IsBreakThroughFastRefreshActive()) return;
        if (durationA > BreakThroughFastParticleDuration)
            durationA = BreakThroughFastParticleDuration;
        if (durationB > BreakThroughFastParticleDuration)
            durationB = BreakThroughFastParticleDuration;
    }

    static bool IsBreakThroughFastRefreshActive()
    {
        return _breakThroughFastRefreshUntilFrame >= 0
            && Time.frameCount <= _breakThroughFastRefreshUntilFrame;
    }

    static int ClearBreakThroughChoiceIcons(BreakThroughController instance)
    {
        System.Collections.Generic.List<Transform> slots = GetBreakThroughChoiceSlots(instance);
        int removed = 0;
        for (int i = 0; i < slots.Count; i++)
            removed += ClearBreakThroughChoiceIconsRecursive(slots[i]);
        return removed;
    }

    static System.Collections.Generic.List<Transform> GetBreakThroughChoiceSlots(BreakThroughController instance)
    {
        var results = new System.Collections.Generic.List<Transform>();
        TryCollectBreakThroughSlotTransforms(TryGetMember(instance, "breakThroughPos"), results);
        if (results.Count > 0)
            return results;

        Transform? iconRoot = FindChildTransformByName(instance.breakThroughPanel != null ? instance.breakThroughPanel.transform : null, "BreakThroughIconPos");
        if (iconRoot != null)
        {
            for (int i = 0; i < iconRoot.childCount; i++)
                results.Add(iconRoot.GetChild(i));
        }

        return results;
    }

    static void TryCollectBreakThroughSlotTransforms(object? value, System.Collections.Generic.List<Transform> results)
    {
        if (value == null) return;

        Transform? transform = TryResolveTransform(value);
        if (transform != null)
        {
            results.Add(transform);
            return;
        }

        if (value is string) return;

        if (value is System.Collections.IEnumerable enumerable)
        {
            foreach (object? item in enumerable)
                TryCollectBreakThroughSlotTransforms(item, results);
            return;
        }

        if (!TryGetCollectionCount(value, out int count)) return;

        for (int i = 0; i < count; i++)
            TryCollectBreakThroughSlotTransforms(TryGetCollectionItem(value, i), results);
    }

    static bool TryGetCollectionCount(object value, out int count)
    {
        count = 0;
        object? raw = TryGetMember(value, "Count") ?? TryGetMember(value, "Length");
        if (raw == null) return false;

        try
        {
            count = Convert.ToInt32(raw);
            return count >= 0;
        }
        catch
        {
            return false;
        }
    }

    static object? TryGetCollectionItem(object value, int index)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo? property = value.GetType().GetProperty("Item", flags);
            if (property != null)
                return property.GetValue(value, new object?[] { index });
        }
        catch { }

        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            MethodInfo? method = value.GetType().GetMethod("get_Item", flags, null, new[] { typeof(int) }, null);
            if (method != null)
                return method.Invoke(value, new object?[] { index });
        }
        catch { }

        return null;
    }

    static Transform? TryResolveTransform(object? value)
    {
        if (value is Transform transform) return transform;
        if (value is GameObject gameObject) return gameObject.transform;
        if (value is Component component) return component.transform;
        return null;
    }

    static Transform? FindChildTransformByName(Transform? root, string targetName)
    {
        if (root == null) return null;

        var stack = new System.Collections.Generic.Stack<Transform>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            Transform current = stack.Pop();
            if (string.Equals(current.name, targetName, StringComparison.OrdinalIgnoreCase))
                return current;

            for (int i = current.childCount - 1; i >= 0; i--)
                stack.Push(current.GetChild(i));
        }

        return null;
    }

    static int ClearBreakThroughChoiceIconsRecursive(Transform root)
    {
        int removed = 0;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            Transform child = root.GetChild(i);
            if (IsBreakThroughChoiceClone(child.gameObject))
            {
                UnityEngine.Object.DestroyImmediate(child.gameObject);
                removed++;
                continue;
            }

            removed += ClearBreakThroughChoiceIconsRecursive(child);
        }

        return removed;
    }

    static bool IsBreakThroughChoiceClone(GameObject gameObject)
    {
        return string.Equals(gameObject.name, "BreakThroughChoiceIcon(Clone)", StringComparison.Ordinal);
    }

    static bool TryRefreshAuction()
    {
        PlotController? plot = PlotController.Instance;
        if (plot == null || !IsAuctionPlotActive(plot))
            return false;

        if (FindAuctionChoice(plot, "ShowAuctionItem") != null)
        {
            _pendingAuctionReroll = true;
            ArmAuctionPluginProbe();
            bool exhibitVisible = IsAuctionExhibitVisible(plot);
            _pendingAuctionAwaitChooseClose = false;
            _pendingAuctionEnsureOpenRetries = 0;
            ScheduleAuctionRefreshStep(exhibitVisible ? AuctionRefreshStep.ResetDisplay : AuctionRefreshStep.EnsureOpen, Time.frameCount + (exhibitVisible ? 1 : 3));
            Log.LogInfo(exhibitVisible
                ? "[RisingFame] Auction reroll armed. Reset -> close -> ensure open."
                : "[RisingFame] Auction reroll armed. Ensure open shortly.");
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

        PlotController? plot = PlotController.Instance;
        if (plot == null) return false;

        string before = GetItemListSummary(instance.craftResultList);
        ClearItemList(instance.craftResultList);

        try
        {
            plot.FinishCraft();
            string after = GetItemListSummary(instance.craftResultList);
            Log.LogInfo($"[RisingFame] Craft refresh via FinishCraft {before} -> {after}");
            return !string.Equals(before, after, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            Log.LogInfo($"[RisingFame] Craft refresh via FinishCraft failed: {ex.Message}");
            return false;
        }
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

    static void DumpAuctionUiSnapshot()
    {
        try
        {
            Log.LogInfo("[RisingFame] ===== Auction UI Snapshot =====");

            AuctionController? auction = AuctionController.Instance;
            if (auction == null)
            {
                Log.LogInfo("[RisingFame] AuctionController = null");
            }
            else
            {
                Log.LogInfo(
                    $"[RisingFame] Auction state step={auction.auctionStep} items={GetItemListSummary(auction.auctionItemList)} icons={GetGameObjectListSummary(auction.auctionItemIconList)}");
                Log.LogInfo(
                    $"[RisingFame] Auction panels panel={DescribeGameObject(auction.auctionPanel)} playerUI={DescribeGameObject(auction.playerAuctionUI)} startBtn={DescribeGameObject(auction.startAuctionButton)} talk={DescribeGameObject(auction.talkPanel)} leave={DescribeGameObject(auction.leaveAuctionButton)} skip={DescribeGameObject(auction.skipButton)} tempObj={DescribeGameObject(auction.tempObj)}");
                Log.LogInfo(
                    $"[RisingFame] Auction prefab slot={DescribeGameObject(auction.auctionSlotPrefab)} slotParent={DescribeTransform(auction.auctionSlotPrefab != null ? auction.auctionSlotPrefab.transform.parent : null)}");

                GameObject? firstIcon = GetFirstGameObject(auction.auctionItemIconList);
                if (firstIcon != null)
                {
                    Transform? iconParent = firstIcon.transform.parent;
                    Log.LogInfo(
                        $"[RisingFame] Auction firstIcon={DescribeGameObject(firstIcon)} iconParent={DescribeTransform(iconParent)}");
                    DumpHierarchy(iconParent != null ? iconParent.gameObject : firstIcon, maxDepth: 2, maxChildren: 24, tag: "AuctionIconRoot");
                }

                if (auction.auctionPanel != null)
                    DumpHierarchy(auction.auctionPanel, maxDepth: 2, maxChildren: 24, tag: "AuctionPanel");

                if (auction.playerAuctionUI != null)
                    DumpHierarchy(auction.playerAuctionUI, maxDepth: 2, maxChildren: 24, tag: "PlayerAuctionUI");
            }

            DumpActiveItemIconControllers();
            DumpActiveItemListControllers();

            PlotController? plot = PlotController.Instance;
            if (plot == null)
            {
                Log.LogInfo("[RisingFame] PlotController = null");
            }
            else
            {
                Log.LogInfo(
                    $"[RisingFame] Plot state panel={DescribeGameObject(plot.plotPanel)} itemGrid={DescribeGameObject(plot.plotItemGrid)} tempShop={GetItemListSummary(plot.tempPlotShop)} item={(plot.plotInteractItem != null)} tempRecord={(plot.plotInteractItemTempRecord != null)} ctx={GetAuctionChoiceContext(plot)}");

                if (plot.plotItemGrid != null)
                    DumpHierarchy(plot.plotItemGrid, maxDepth: 2, maxChildren: 24, tag: "PlotItemGrid");
            }

            Log.LogInfo("[RisingFame] ===== Auction UI Snapshot End =====");
        }
        catch (Exception ex)
        {
            Log.LogInfo($"[RisingFame] Auction UI snapshot failed: {ex.Message}");
        }
    }

    static void DumpActiveItemIconControllers()
    {
        try
        {
            ItemIconController[] icons = UnityEngine.Object.FindObjectsOfType<ItemIconController>(true);
            int activeCount = 0;
            for (int i = 0; i < icons.Length; i++)
            {
                ItemIconController? icon = icons[i];
                if (icon == null || icon.gameObject == null || !icon.gameObject.activeInHierarchy)
                    continue;

                activeCount++;
                ItemData? item = null;
                try { item = icon.itemData; } catch { }
                string itemName = item != null ? $"{item.itemID}:{item.name}" : "null";
                Log.LogInfo(
                    $"[RisingFame] ActiveItemIcon path={GetTransformPath(icon.transform)} item={itemName} iconType={SafeToString(() => icon.itemIconType.ToString())} hideName={SafeToString(() => icon.hideItemName.ToString())} hideBox={SafeToString(() => icon.hideItemBox.ToString())}");
            }

            Log.LogInfo($"[RisingFame] ActiveItemIcon total={activeCount}");
        }
        catch (Exception ex)
        {
            Log.LogInfo($"[RisingFame] ActiveItemIcon dump failed: {ex.Message}");
        }
    }

    static void DumpActiveItemListControllers()
    {
        try
        {
            ItemListController[] lists = UnityEngine.Object.FindObjectsOfType<ItemListController>(true);
            int activeCount = 0;
            for (int i = 0; i < lists.Length; i++)
            {
                ItemListController? list = lists[i];
                if (list == null || list.gameObject == null || !list.gameObject.activeInHierarchy)
                    continue;

                activeCount++;
                Log.LogInfo($"[RisingFame] ActiveItemList path={GetTransformPath(list.transform)} comps={GetComponentSummary(list.gameObject)}");
                DumpHierarchy(list.gameObject, maxDepth: 2, maxChildren: 20, tag: "ActiveItemList");
            }

            Log.LogInfo($"[RisingFame] ActiveItemList total={activeCount}");
        }
        catch (Exception ex)
        {
            Log.LogInfo($"[RisingFame] ActiveItemList dump failed: {ex.Message}");
        }
    }

    static string GetGameObjectListSummary(Il2CppSystem.Collections.Generic.List<GameObject>? list)
    {
        if (list == null) return "null";

        var sb = new StringBuilder();
        sb.Append("count=").Append(list.Count);
        if (list.Count > 0)
        {
            sb.Append(" [");
            int take = Math.Min(list.Count, 6);
            for (int i = 0; i < take; i++)
            {
                if (i > 0) sb.Append(" | ");
                GameObject? go = list[i];
                sb.Append(go != null ? $"{go.name}:{go.activeInHierarchy}" : "null");
            }

            if (list.Count > take)
                sb.Append(" | ...");
            sb.Append(']');
        }

        return sb.ToString();
    }

    static GameObject? GetFirstGameObject(Il2CppSystem.Collections.Generic.List<GameObject>? list)
    {
        if (list == null || list.Count <= 0) return null;
        return list[0];
    }

    static string DescribeGameObject(GameObject? go)
    {
        if (go == null) return "null";

        int childCount = 0;
        try { childCount = go.transform.childCount; } catch { }

        return $"{GetTransformPath(go.transform)} activeSelf={go.activeSelf} active={go.activeInHierarchy} children={childCount} comps={GetComponentSummary(go)}";
    }

    static string DescribeTransform(Transform? transform)
    {
        if (transform == null) return "null";
        return $"{GetTransformPath(transform)} children={transform.childCount}";
    }

    static string SafeToString(Func<string> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return "?";
        }
    }

    static string GetTransformPath(Transform? transform)
    {
        if (transform == null) return "null";

        var parts = new System.Collections.Generic.List<string>();
        Transform? current = transform;
        int guard = 0;
        while (current != null && guard++ < 32)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    static string GetComponentSummary(GameObject go)
    {
        try
        {
            Component[] components = go.GetComponents<Component>();
            if (components == null || components.Length == 0) return "-";

            var sb = new StringBuilder();
            int take = Math.Min(components.Length, 6);
            for (int i = 0; i < take; i++)
            {
                if (i > 0) sb.Append(',');
                Component? component = components[i];
                sb.Append(component != null ? component.GetType().Name : "null");
            }

            if (components.Length > take)
                sb.Append(",...");
            return sb.ToString();
        }
        catch
        {
            return "?";
        }
    }

    static void DumpHierarchy(GameObject root, int maxDepth, int maxChildren, string tag)
    {
        try
        {
            DumpHierarchyRecursive(root.transform, depth: 0, maxDepth, maxChildren, tag);
        }
        catch (Exception ex)
        {
            Log.LogInfo($"[RisingFame] {tag} hierarchy dump failed: {ex.Message}");
        }
    }

    static void DumpHierarchyRecursive(Transform transform, int depth, int maxDepth, int maxChildren, string tag)
    {
        if (depth > maxDepth) return;

        string indent = new string(' ', depth * 2);
        GameObject go = transform.gameObject;
        Log.LogInfo($"[RisingFame] {tag} {indent}- {go.name} activeSelf={go.activeSelf} active={go.activeInHierarchy} children={transform.childCount} comps={GetComponentSummary(go)}");

        int take = Math.Min(transform.childCount, maxChildren);
        for (int i = 0; i < take; i++)
            DumpHierarchyRecursive(transform.GetChild(i), depth + 1, maxDepth, maxChildren, tag);

        if (transform.childCount > take)
            Log.LogInfo($"[RisingFame] {tag} {indent}  ... {transform.childCount - take} more children");
    }

    static bool IsAuctionExhibitVisible(PlotController plot)
    {
        try
        {
            if (IsAuctionChoosePanelActive())
                return true;

            if (GetPlotItemGridChildCount(plot) > 0)
                return true;

            if (plot.plotInteractItem != null || plot.plotInteractItemTempRecord != null)
                return true;
        }
        catch { }

        return false;
    }

    static void ClearPlotItemGridChildren(PlotController plot)
    {
        try
        {
            if (plot.plotItemGrid == null) return;

            Transform grid = plot.plotItemGrid.transform;
            for (int i = grid.childCount - 1; i >= 0; i--)
            {
                Transform child = grid.GetChild(i);
                UnityEngine.Object.DestroyImmediate(child.gameObject);
            }
        }
        catch { }
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
            MarkNextShowFromPlugin();
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

    static void ScheduleAuctionRefreshStep(AuctionRefreshStep step, int frame)
    {
        _pendingAuctionStep = step;
        _pendingAuctionSequenceFrame = frame;
    }

    static void ClearAuctionRefreshSequence()
    {
        _pendingAuctionStep = AuctionRefreshStep.None;
        _pendingAuctionSequenceFrame = -1;
    }

    static void AdvanceAuctionRefreshSequence(int frame)
    {
        AuctionRefreshStep step = _pendingAuctionStep;
        ClearAuctionRefreshSequence();

        switch (step)
        {
            case AuctionRefreshStep.SingleShow:
                TryRunPendingAuctionAutoOpen("single");
                break;

            case AuctionRefreshStep.ResetDisplay:
                if (TryRunPendingAuctionResetDisplay())
                    ScheduleAuctionRefreshStep(AuctionRefreshStep.ChooseClose, frame + 3);
                break;

            case AuctionRefreshStep.ChooseClose:
                if (TryRunPendingAuctionChooseClose())
                    ScheduleAuctionRefreshStep(AuctionRefreshStep.EnsureOpen, frame + 4);
                break;

            case AuctionRefreshStep.EnsureOpen:
                TryRunPendingAuctionEnsureOpen();
                break;
        }
    }

    static bool TryRunPendingAuctionAutoOpen(string phase)
    {
        if (!_pendingAuctionReroll) return false;

        PlotController? plot = PlotController.Instance;
        if (plot == null || !IsAuctionPlotActive(plot))
        {
            CancelPendingAuctionRefresh("plot lost before reopen");
            return false;
        }

        AuctionController? auction = AuctionController.Instance;
        string probeId = string.Empty;
        if (_auctionProbePluginArmed)
        {
            probeId = _auctionProbePluginId;
            _auctionProbePluginArmed = false;
            SetActiveAuctionProbe(probeId, AuctionProbeSource.Plugin);
        }

        if (!string.IsNullOrEmpty(probeId))
        {
            string beforeCtx = GetAuctionChoiceContext(plot);
            string beforeTemp = GetItemListSummary(plot.tempPlotShop);
            string beforeStep = auction != null ? auction.auctionStep.ToString() : "null";
            bool beforeChoose = IsAuctionChoosePanelActive();
            int beforeIcons = CountActiveItemIcons();
            Log.LogInfo($"[RisingFame] auction_probe_id={probeId} source=plugin phase={phase} pre ctx={beforeCtx} temp={beforeTemp} step={beforeStep} choose={beforeChoose} icons={beforeIcons}");
        }

        bool invoked = false;
        SinglePlotChoiceData? choice = FindAuctionChoice(plot, "ShowAuctionItem");
        if (choice != null)
        {
            PrepareAuctionChoiceContext(plot, choice);
            invoked = TryInvokePlotChoiceCall(plot, choice);
        }
        else
        {
            try
            {
                MarkNextShowFromPlugin();
                plot.ShowAuctionItem();
                invoked = true;
            }
            catch { }
        }

        if (!string.IsNullOrEmpty(probeId))
        {
            string afterCtx = GetAuctionChoiceContext(plot);
            string afterTemp = GetItemListSummary(plot.tempPlotShop);
            string afterStep = auction != null ? auction.auctionStep.ToString() : "null";
            bool afterChoose = IsAuctionChoosePanelActive();
            int afterIcons = CountActiveItemIcons();
            Log.LogInfo($"[RisingFame] auction_probe_id={probeId} source=plugin phase={phase} post ctx={afterCtx} temp={afterTemp} step={afterStep} choose={afterChoose} icons={afterIcons} invoked={invoked}");
        }

        if (invoked)
            Log.LogInfo($"[RisingFame] Auction reroll {phase} show triggered.");
        return invoked;
    }

    static bool TryRunPendingAuctionResetDisplay()
    {
        if (!_pendingAuctionReroll) return false;

        PlotController? plot = PlotController.Instance;
        if (plot == null || !IsAuctionPlotActive(plot))
        {
            CancelPendingAuctionRefresh("plot lost before reset");
            return false;
        }

        try
        {
            ResetPlotItemDisplay(plot);
            _pendingAuctionAwaitChooseClose = true;
            Log.LogInfo("[RisingFame] Auction reroll reset display triggered.");
            return true;
        }
        catch (Exception ex)
        {
            CancelPendingAuctionRefresh($"reset display failed: {ex.Message}");
            return false;
        }
    }

    static bool TryRunPendingAuctionChooseClose()
    {
        if (!_pendingAuctionReroll || !_pendingAuctionAwaitChooseClose) return false;

        PlotController? plot = PlotController.Instance;
        if (plot == null || !IsAuctionPlotActive(plot))
        {
            CancelPendingAuctionRefresh("plot lost before close");
            return false;
        }

        if (!IsAuctionChoosePanelActive())
        {
            _pendingAuctionAwaitChooseClose = false;
            Log.LogInfo("[RisingFame] Auction choose panel already hidden.");
            return true;
        }

        ChooseController? choose = ChooseController.Instance;
        if (choose == null)
        {
            CancelPendingAuctionRefresh("choose controller missing");
            return false;
        }

        try
        {
            choose.HideChoosePanel();
            _pendingAuctionAwaitChooseClose = false;
            Log.LogInfo("[RisingFame] Auction reroll choose close triggered.");
            return true;
        }
        catch
        {
            return false;
        }
    }

    static bool TryRunPendingAuctionEnsureOpen()
    {
        if (!_pendingAuctionReroll) return false;

        PlotController? plot = PlotController.Instance;
        if (plot == null || !IsAuctionPlotActive(plot))
        {
            CancelPendingAuctionRefresh("plot lost before ensure open");
            return false;
        }

        if (IsAuctionChoosePanelActive())
        {
            if (_pendingAuctionEnsureOpenRetries < 2)
            {
                _pendingAuctionEnsureOpenRetries++;
                ScheduleAuctionRefreshStep(AuctionRefreshStep.EnsureOpen, Time.frameCount + 2);
                Log.LogInfo($"[RisingFame] Auction ensure open deferred: panel still active (retry {_pendingAuctionEnsureOpenRetries}).");
                return true;
            }

            Log.LogInfo("[RisingFame] Auction ensure open skipped: panel already active.");
            return true;
        }

        _pendingAuctionEnsureOpenRetries = 0;
        return TryRunPendingAuctionAutoOpen("ensure");
    }

    static bool IsAuctionChoosePanelActive()
    {
        try
        {
            ChooseController? choose = ChooseController.Instance;
            if (choose == null) return false;

            return IsPanelActive(choose.choosePanel)
                || IsPanelActive(choose.chooseRoot)
                || IsPanelActive(choose.itemList);
        }
        catch
        {
            return false;
        }
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
        bool hasToken = instance.tempPlotShop != null
            || ContainsAuctionToken(instance.nowPlot?.plotCallFuc)
            || ContainsAuctionToken(instance.nowPlot?.plotName)
            || ContainsAuctionToken(instance.nowSinglePlot?.clickCallFuc)
            || ContainsAuctionToken(instance.nowPlotText)
            || HasAuctionChoice(instance.nowSinglePlot);

        if (!hasToken) return false;

        if (IsPanelActive(instance.plotPanel)) return true;

        return IsAuctionChoosePanelActive();
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

        SinglePlotChoiceData? fuzzy = null;

        for (int i = 0; i < plot.nowSinglePlot.choices.Count; i++)
        {
            SinglePlotChoiceData choice = plot.nowSinglePlot.choices[i];
            string? current = choice.callFuc;
            if (string.Equals(current, callFuc, StringComparison.OrdinalIgnoreCase))
                return choice;

            if (fuzzy == null
                && !string.IsNullOrWhiteSpace(current)
                && current.Contains(callFuc, StringComparison.OrdinalIgnoreCase))
            {
                fuzzy = choice;
            }
        }

        return fuzzy;
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

        if (callFuc.IndexOf("ShowAuctionItem", StringComparison.OrdinalIgnoreCase) >= 0)
            MarkNextShowFromPlugin();

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
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            PropertyInfo? property = type.GetProperty(name, flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(target, value);
                return true;
            }

            FieldInfo? field = type.GetField(name, flags);
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
            _pendingAuctionAwaitChooseClose = false;
            _pendingAuctionEnsureOpenRetries = 0;
            ClearAuctionRefreshSequence();
            Log.LogInfo("[RisingFame] Auction reroll applied by native exhibit view.");
        }
    }

    internal static void PreparePendingAuctionReroll(PlotController plot)
    {
        if (!_pendingAuctionReroll) return;

        string beforeCached = GetItemListSummary(_lastAuctionTargetList);
        string beforeTemp = GetItemListSummary(plot.tempPlotShop);
        AuctionController? auction = AuctionController.Instance;
        string beforeShown = auction != null ? GetItemListSummary(auction.auctionItemList) : "null";

        if (_lastAuctionTargetList != null)
            ClearItemList(_lastAuctionTargetList);

        if (plot.tempPlotShop != null && !ReferenceEquals(plot.tempPlotShop, _lastAuctionTargetList))
            ClearItemList(plot.tempPlotShop);

        if (auction != null)
            ClearItemList(auction.auctionItemList);

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

        ClearPlotItemGridChildren(plot);

        Log.LogInfo($"[RisingFame] Auction reroll prepare cached {beforeCached} -> {GetItemListSummary(_lastAuctionTargetList)} | temp {beforeTemp} -> {GetItemListSummary(plot.tempPlotShop)} | shown {beforeShown} -> {(auction != null ? GetItemListSummary(auction.auctionItemList) : "null")} | grid={GetPlotItemGridChildCount(plot)}");
    }

    internal static void OnChoosePanelClosed(string source)
    {
        if (!_pendingAuctionReroll) return;
        Log.LogInfo($"[RisingFame] Auction choose panel closed via {source}.");
    }

    internal static string GetAuctionChoiceContext(PlotController plot)
    {
        string nowChoice = TryGetChoiceCall(plot, "nowChoice");
        string newChoice = TryGetChoiceCall(plot, "newChoice");
        return $"nowChoice={nowChoice} newChoice={newChoice}";
    }

    static object? TryGetMember(object target, string memberName)
    {
        try
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = target.GetType();

            PropertyInfo? property = type.GetProperty(memberName, flags);
            if (property != null && property.CanRead)
                return property.GetValue(target);

            FieldInfo? field = type.GetField(memberName, flags);
            if (field != null)
                return field.GetValue(target);
        }
        catch { }

        return null;
    }

    static bool IsLikelyAuctionContext()
    {
        try
        {
            PlotController? plot = PlotController.Instance;
            if (plot == null) return false;

            if (plot.tempPlotShop != null)
                return true;

            if (ContainsAuctionToken(plot.nowPlot?.plotCallFuc)
                || ContainsAuctionToken(plot.nowPlot?.plotName)
                || ContainsAuctionToken(plot.nowSinglePlot?.clickCallFuc)
                || ContainsAuctionToken(plot.nowPlotText)
                || HasAuctionChoice(plot.nowSinglePlot))
            {
                return true;
            }
        }
        catch { }

        return false;
    }

    static bool IsLikelyAuctionItemList(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return path.Contains("ChoosePanel", StringComparison.OrdinalIgnoreCase)
            && path.Contains("ChooseItemList", StringComparison.OrdinalIgnoreCase);
    }

    static string DescribeItemListArgs(object[]? args)
    {
        if (args == null || args.Length == 0) return "none";

        for (int i = 0; i < args.Length; i++)
        {
            object? arg = args[i];
            if (arg is ItemListData itemList)
                return $"ItemListData {GetItemListSummary(itemList)}";
            if (arg is Il2CppSystem.Collections.Generic.List<ItemData> items)
                return $"List<ItemData> {GetItemListSummary(items)}";
        }

        return $"args={args.Length}";
    }

    static string DescribeItemListMember(ItemListController list)
    {
        object? value = TryGetMember(list, "itemListData")
            ?? TryGetMember(list, "itemList")
            ?? TryGetMember(list, "itemDataList")
            ?? TryGetMember(list, "itemListDatas");

        if (value is ItemListData itemList)
            return GetItemListSummary(itemList);
        if (value is Il2CppSystem.Collections.Generic.List<ItemData> items)
            return GetItemListSummary(items);

        return "unknown";
    }

    internal static void LogAuctionEntry(MethodBase method, string phase)
    {
        if (!ShouldLogProbeAny())
            return;

        if (!TryActivateProbeFromAny($"AuctionController.{method.Name}"))
            return;

        if (!IsActiveProbe(_auctionProbeActiveSource))
        {
            ClearActiveAuctionProbe();
            return;
        }

        string id = _auctionProbeActiveId;
        string source = _auctionProbeActiveSource == AuctionProbeSource.Plugin ? "plugin" : "natural";
        AuctionController? auction = AuctionController.Instance;
        string step = auction != null ? auction.auctionStep.ToString() : "null";
        Log.LogInfo($"[RisingFame] auction_probe_id={id} source={source} auction {phase} method={method.Name} step={step}");
    }

    internal static void LogItemListEntry(ItemListController list, MethodBase method, object[]? args, string phase)
    {
        if (!ShouldLogProbeAny())
            return;

        string path = GetTransformPath(list.transform);
        if (!IsLikelyAuctionItemList(path) && !IsLikelyAuctionContext())
            return;

        if (!TryActivateProbeFromAny($"ItemListController.{method.Name}"))
            return;

        if (!IsActiveProbe(_auctionProbeActiveSource))
        {
            ClearActiveAuctionProbe();
            return;
        }

        string id = _auctionProbeActiveId;
        string source = _auctionProbeActiveSource == AuctionProbeSource.Plugin ? "plugin" : "natural";
        string argSummary = DescribeItemListArgs(args);
        string memberSummary = DescribeItemListMember(list);
        Log.LogInfo($"[RisingFame] auction_probe_id={id} source={source} itemlist {phase} method={method.Name} path={path} args={argSummary} member={memberSummary}");
    }

    internal static void LogAuctionProbeShow(PlotController plot, AuctionProbeSource source)
    {
        bool activated = TryActivateProbe(source, $"ShowAuctionItem({source})");
        if (!activated && !IsActiveProbe(source))
            return;

        string id = _auctionProbeActiveId;
        string sourceLabel = source == AuctionProbeSource.Plugin ? "plugin" : "natural";
        AuctionController? auction = AuctionController.Instance;
        string step = auction != null ? auction.auctionStep.ToString() : "null";
        bool choose = IsAuctionChoosePanelActive();
        int icons = CountActiveItemIcons();
        Log.LogInfo($"[RisingFame] auction_probe_id={id} source={sourceLabel} show pre ctx={GetAuctionChoiceContext(plot)} temp={GetItemListSummary(plot.tempPlotShop)} step={step} choose={choose} icons={icons}");
    }

    internal static void LogAuctionProbeShowPost(PlotController plot)
    {
        if (string.IsNullOrWhiteSpace(_auctionProbeActiveId))
            return;

        if (!IsActiveProbe(_auctionProbeActiveSource))
        {
            ClearActiveAuctionProbe();
            return;
        }

        string id = _auctionProbeActiveId;
        string source = _auctionProbeActiveSource == AuctionProbeSource.Plugin ? "plugin" : "natural";
        AuctionController? auction = AuctionController.Instance;
        string step = auction != null ? auction.auctionStep.ToString() : "null";
        bool choose = IsAuctionChoosePanelActive();
        int icons = CountActiveItemIcons();
        Log.LogInfo($"[RisingFame] auction_probe_id={id} source={source} show post ctx={GetAuctionChoiceContext(plot)} temp={GetItemListSummary(plot.tempPlotShop)} step={step} choose={choose} icons={icons} generateSeen={_auctionProbeGenerateSeen}");
    }

    internal static void LogAuctionProbeGenerate(PlotController plot, ItemListData list, float shopLv, int itemNum, string phase)
    {
        if (string.IsNullOrWhiteSpace(_auctionProbeActiveId) && !TryActivateProbeFromAny("GenerateAuctionItem"))
            return;

        if (!IsActiveProbe(_auctionProbeActiveSource))
        {
            ClearActiveAuctionProbe();
            return;
        }

        string id = _auctionProbeActiveId;
        string source = _auctionProbeActiveSource == AuctionProbeSource.Plugin ? "plugin" : "natural";

        if (phase == "pre")
        {
            _auctionProbeGenerateSeen = true;
            MaybeLogProbeStack(id);
        }

        Log.LogInfo($"[RisingFame] auction_probe_id={id} source={source} generate {phase} pool={GetItemListSummary(list)} shopLv={shopLv:0.##} itemNum={itemNum} ctx={GetAuctionChoiceContext(plot)}");

        if (phase == "post")
            ClearActiveAuctionProbe();
    }

    static string TryGetChoiceCall(PlotController plot, string memberName)
    {
        try
        {
            Type type = plot.GetType();
            object? value = null;
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            PropertyInfo? property = type.GetProperty(memberName, flags);
            if (property != null && property.CanRead)
                value = property.GetValue(plot);

            if (value == null)
            {
                FieldInfo? field = type.GetField(memberName, flags);
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

    internal static bool ShouldTraceAuctionRefresh()
    {
        return _pendingAuctionReroll
            || _pendingAuctionAwaitChooseClose
            || _pendingAuctionStep != AuctionRefreshStep.None;
    }

    static void CancelPendingAuctionRefresh(string reason)
    {
        bool hadPending = _pendingAuctionReroll
            || _pendingAuctionAwaitChooseClose
            || _pendingAuctionStep != AuctionRefreshStep.None;

        _pendingAuctionReroll = false;
        _pendingAuctionAwaitChooseClose = false;
        _pendingAuctionEnsureOpenRetries = 0;
        ClearAuctionRefreshSequence();

        if (hadPending)
            Log.LogInfo($"[RisingFame] Auction reroll canceled: {reason}");
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
            int lv = __instance.heroForceLv + 1; // 1-6 (姝﹁€厏瀹楀笀)
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
        Plugin.LogAuctionProbeGenerate(__instance, __0, __1, __3, "pre");
        if (Plugin.ShouldTraceAuctionRefresh())
            Plugin.Log.LogInfo($"[RisingFame] Auction generate pre pool={Plugin.GetItemListSummary(__0)} shopLv={__1:0.##} itemNum={__3} ctx={Plugin.GetAuctionChoiceContext(__instance)}");
    }

    public static void GenerateAuctionItem_Post(PlotController __instance, ItemListData __0, float __1, Il2CppSystem.Collections.Generic.List<int> __2, int __3)
    {
        Plugin.MarkAuctionGenerated();
        Plugin.LogAuctionProbeGenerate(__instance, __0, __1, __3, "post");
        if (Plugin.ShouldTraceAuctionRefresh())
            Plugin.Log.LogInfo($"[RisingFame] Auction generate post pool={Plugin.GetItemListSummary(__0)} shopLv={__1:0.##} itemNum={__3} ctx={Plugin.GetAuctionChoiceContext(__instance)}");
    }

    public static void ShowAuctionItem_Pre(PlotController __instance)
    {
        Plugin.PreparePendingAuctionReroll(__instance);
        Plugin.AuctionProbeSource source = Plugin.ConsumeNextShowSource();
        if (source == Plugin.AuctionProbeSource.None)
            source = Plugin.AuctionProbeSource.Natural;
        Plugin.LogAuctionProbeShow(__instance, source);
        if (Plugin.ShouldTraceAuctionRefresh())
            Plugin.Log.LogInfo($"[RisingFame] ShowAuctionItem pre pool={Plugin.GetItemListSummary(__instance.tempPlotShop)} ctx={Plugin.GetAuctionChoiceContext(__instance)}");
    }

    public static void ShowAuctionItem_Post(PlotController __instance)
    {
        Plugin.LogAuctionProbeShowPost(__instance);
        if (Plugin.ShouldTraceAuctionRefresh())
            Plugin.Log.LogInfo($"[RisingFame] ShowAuctionItem post pool={Plugin.GetItemListSummary(__instance.tempPlotShop)} ctx={Plugin.GetAuctionChoiceContext(__instance)}");
    }
}

[HarmonyPatch]
static class BreakThroughTracePatches
{
    public static void ShowItemParticle3_Pre(ref float __2)
    {
        Plugin.AccelerateBreakThroughShowItemParticle(ref __2);
    }

    public static void ShowItemParticle5_Pre(ref float __2, ref float __3)
    {
        Plugin.AccelerateBreakThroughShowItemParticle(ref __2, ref __3);
    }
}

[HarmonyPatch]
static class AuctionEntryTracePatches
{
    public static void AuctionEntry_Pre(AuctionController __instance, MethodBase __originalMethod)
    {
        Plugin.LogAuctionEntry(__originalMethod, "pre");
    }

    public static void AuctionEntry_Post(AuctionController __instance, MethodBase __originalMethod)
    {
        Plugin.LogAuctionEntry(__originalMethod, "post");
    }
}

[HarmonyPatch]
static class ItemListTracePatches
{
    public static void ItemList_Pre(ItemListController __instance, MethodBase __originalMethod, object[] __args)
    {
        Plugin.LogItemListEntry(__instance, __originalMethod, __args, "pre");
    }

    public static void ItemList_Post(ItemListController __instance, MethodBase __originalMethod, object[] __args)
    {
        Plugin.LogItemListEntry(__instance, __originalMethod, __args, "post");
    }
}

[HarmonyPatch]
static class ChoosePanelTracePatches
{
    public static void HideChoosePanel_Post(ChooseController __instance)
    {
        Plugin.OnChoosePanelClosed("HideChoosePanel");
    }

    public static void UnshowChoosePanel_Post(ChooseController __instance)
    {
        Plugin.OnChoosePanelClosed("UnshowChoosePanel");
    }
}
