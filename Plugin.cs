using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using System.Reflection;
using UnityEngine;

namespace RisingFame;

[BepInPlugin("com.luoxu.longyin.risingfame", "RisingFame - MingYangTianXia", "1.8.1")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log = null!;

    // ---- Mod ON/OFF ----
    internal static bool Enabled = true;

    // Input debounce to avoid rapid toggle spam.
    static float _nextToggleTime;
    static int _lastPollFrame = -1;

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

        Log.LogInfo("RisingFame v1.8.1 loaded. Mod ON. Press '=' to toggle. Press Alt+R to refresh current panel.");
        Log.LogInfo("Martial exp: rank1 x3.0, +0.5/rank. Living exp: rank1 x2.0, +0.5/rank. Favor: rank1 x1.5, +0.5/rank. Contribution enabled. BookWrite: speed x10, cost/time /10. Quick refresh: breakthrough / special enhance / auction.");
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

            if (TryRestartAuction())
                return "Refresh: auction";
        }
        catch (Exception ex)
        {
            return $"Refresh failed: {ex.Message}";
        }

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

    static bool TryRestartAuction()
    {
        AuctionController? instance = AuctionController.Instance;
        if (instance == null) return false;
        if (!IsPanelActive(instance.auctionPanel)) return false;
        PlotController? plot = PlotController.Instance;
        if (plot == null || plot.tempPlotShop == null) return false;

        instance.RestartAuction(
            instance.heroList,
            plot.tempPlotShop,
            instance.playerSellItem,
            instance.endMatchCallPlot,
            instance.auctionDifficulty,
            instance.havePlayer,
            instance.auctionKeeper);
        return true;
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
