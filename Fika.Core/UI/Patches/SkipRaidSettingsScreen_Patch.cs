using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace Fika.Core.UI.Patches;

/// <summary>
/// Bypasses the PMC pre-raid settings menu when configured to do so.
/// The raid settings are still carried into the next stage of matchmaking.
/// Inspired by awnova's RaidSettingsSkipper (MIT licensed).
/// </summary>
public sealed class SkipRaidSettingsScreen_Patch : ModulePatch
{
    protected override MethodBase GetTargetMethod()
    {
        if (AccessTools.Method(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.CG_method_80)) == null
            || AccessTools.Field(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.raidSettings_0)) == null
            || AccessTools.Field(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.raidSettings_1)) == null)
        {
            return null;
        }

        return AccessTools.Method(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.method_50));
    }

    [PatchPrefix]
    private static bool Prefix(MainMenuShowOperation __instance)
    {
        if (Fika.Core.Main.Utils.FikaBackendUtils.IsHeadless) return true;

        var config = FikaPlugin.Instance?.Settings;
        if (config?.SkipRaidSettingsScreen?.Value != true && config?.CanEditRaidSettings != false)
        {
            return true;
        }

        // Advance through the game's own Next handler to preserve raid mode,
        // insurance/matchmaking transitions, and settings already chosen.
        var raidMode = __instance.raidSettings_0.RaidMode;
        __instance.CG_method_80();
        __instance.raidSettings_0.RaidMode = raidMode;
        // Reproduce the Fika Close patch's settings transfer.
        var settings = __instance.raidSettings_0;
        var offline = __instance.raidSettings_1;
        settings.TimeAndWeatherSettings = offline.TimeAndWeatherSettings;
        settings.WavesSettings = offline.WavesSettings;
        settings.MetabolismDisabled = offline.MetabolismDisabled;
        settings.PlayersSpawnPlace = offline.PlayersSpawnPlace;
        return false;
    }
}
