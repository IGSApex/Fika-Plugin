using System;
using System.Linq;
using System.Reflection;
using EFT;
using HarmonyLib;
using SPT.Reflection.Patching;
using SPT.Reflection.Utils;
using SPT.SinglePlayer.Patches.ScavMode;

namespace Fika.Core.UI.Patches;

/// <summary>Skips the SPT Scav raid settings screen before it is queued.</summary>
public sealed class SkipScavRaidSettingsScreen_Patch : ModulePatch
{
    private static readonly FieldInfo MenuField = typeof(TarkovApplication)
        .GetFields(PatchConstants.PrivateFlags)
        .FirstOrDefault(field => field.FieldType == typeof(MainMenuShowOperation));

    protected override MethodBase GetTargetMethod()
    {
        return AccessTools.Method(typeof(LoadOfflineRaidScreenPatch), "LoadOfflineRaidScreenForScav");
    }

    [PatchPrefix]
    private static bool Prefix()
    {
        if (Fika.Core.Main.Utils.FikaBackendUtils.IsHeadless)
            return true;

        var config = FikaPlugin.Instance?.Settings;
        if (config == null || (!config.SkipRaidSettingsScreen.Value && config.CanEditRaidSettings))
            return true;

        var menu = MenuField?.GetValue(ClientAppUtils.GetMainApp()) as MainMenuShowOperation;
        if (menu == null)
            return true;

        var raid = menu.raidSettings_0;
        var offline = menu.raidSettings_1;
        if (raid.SelectedLocation?.Id == "laboratory")
            raid.WavesSettings.IsBosses = true;

        menu._isInSession = raid.Local;
        raid.WavesSettings = offline.WavesSettings;
        raid.BotSettings = offline.BotSettings;
        offline.Side = raid.Side;

        raid.TimeAndWeatherSettings = offline.TimeAndWeatherSettings;
        raid.MetabolismDisabled = offline.MetabolismDisabled;
        raid.PlayersSpawnPlace = offline.PlayersSpawnPlace;

        // The same continuation used by SPT's LoadOfflineRaidNextScreen.
        AccessTools.Method(typeof(MainMenuShowOperation), nameof(MainMenuShowOperation.method_52))?.Invoke(menu, null);
        return false;
    }
}
