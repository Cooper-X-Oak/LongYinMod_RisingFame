using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using UnityEngine;

namespace RisingFame;

[BepInPlugin("com.luoxu.longyin.risingfame", "RisingFame - MingYangTianXia", "1.5.3")]
public class Plugin : BasePlugin
{
    internal static new ManualLogSource Log = null!;

    // ---- Mod ON/OFF ----
    internal static bool Enabled = true;

    // Input debounce to avoid rapid toggle spam.
    static float _nextToggleTime;
    static int _lastPollFrame = -1;

    // Dynamic multiplier: rank1=x1.5, each rank +0.5 (rank6=x4.0)
    internal static float GetMultiplier(HeroData hero)
    {
        if (!Enabled) return 1f;
        return 1.5f + 0.5f * hero.heroForceLv;
    }

    // ---- Beep via Windows API ----
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool Beep(uint freq, uint duration);

    public override void Load()
    {
        Log = base.Log;

        var harmony = new Harmony("com.luoxu.longyin.risingfame");

        // ReadBook exp rate
        TryPatch(harmony, AccessTools.Method(typeof(HeroData), "GetBookExpRate", new[] { typeof(KungfuSkillLvData) }),
            postfix: new HarmonyMethod(typeof(ExpPatches), nameof(ExpPatches.MultiplyExp)),
            name: "HeroData.GetBookExpRate");

        // Combat/fight exp
        TryPatch(harmony, AccessTools.Method(typeof(HeroData), "AddSkillFightExp",
                new[] { typeof(float), typeof(KungfuSkillLvData), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(FightExpPatches), nameof(FightExpPatches.AddSkillFightExp_Pre)),
            name: "HeroData.AddSkillFightExp");

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

        Log.LogInfo("RisingFame v1.5.3 loaded. Mod ON. Press '=' to toggle.");
        Log.LogInfo("Multiplier: rank1 x1.5, +0.5 per rank. Contribution enabled. BookWrite: speed x10, cost/time /10.");
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

            bool pressed = Input.GetKeyDown(KeyCode.Equals) || Input.GetKeyDown(KeyCode.KeypadEquals);
            if (!pressed) return;

            _nextToggleTime = Time.unscaledTime + 0.2f;
            Enabled = !Enabled;

            Log.LogInfo($"[RisingFame] {(Enabled ? "ON" : "OFF")}");

            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    if (Enabled)
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
        catch { }
    }
}

[HarmonyPatch]
static class ExpPatches
{
    public static void MultiplyExp(HeroData __instance, ref float __result)
    {
        float m = Plugin.GetMultiplier(__instance);
        if (m > 1f)
            __result *= m;
    }
}

[HarmonyPatch]
static class FightExpPatches
{
    public static void AddSkillFightExp_Pre(HeroData __instance, ref float __0)
    {
        float m = Plugin.GetMultiplier(__instance);
        if (m > 1f)
            __0 *= m;
    }
}

[HarmonyPatch]
static class FavorPatches
{
    public static void ChangeFavor_Pre(HeroData __instance, ref float __0)
    {
        float m = Plugin.GetMultiplier(__instance);
        if (__0 > 0f && m > 1f)
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
            if (rate > 1f)
            {
                __0 *= rate;
            }
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
            if (rate > 1f)
            {
                __0 *= rate;
            }
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
