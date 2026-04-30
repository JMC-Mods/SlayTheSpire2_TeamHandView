using HarmonyLib;
using JmcModLib.Reflection;
using JmcModLib.Utils;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using TeamHandView.Core;

namespace TeamHandView.Patches;

[HarmonyPatch(typeof(NMultiplayerPlayerState))]
internal static class NMultiplayerPlayerStatePatches
{
    private static readonly MemberAccessor IsHighlightedAccessor =
        MemberAccessor.Get(typeof(NMultiplayerPlayerState), "_isHighlighted");

    private static readonly MemberAccessor IsMouseOverAccessor =
        MemberAccessor.Get(typeof(NMultiplayerPlayerState), "_isMouseOver");

    [HarmonyPatch("UpdateHighlightedState")]
    [HarmonyPostfix]
    private static void UpdateHandPreviewVisibility(NMultiplayerPlayerState __instance)
    {
        try
        {
            bool isHighlighted = IsHighlightedAccessor.GetValue<NMultiplayerPlayerState, bool>(__instance);
            bool isMouseOver = IsMouseOverAccessor.GetValue<NMultiplayerPlayerState, bool>(__instance);
            RemoteHandPreviewController.UpdateVisibility(__instance, isHighlighted && isMouseOver);
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"{VersionInfo.Tag} Failed to read multiplayer player highlight state: {ex}");
            RemoteHandPreviewController.Hide(__instance);
        }
    }

    [HarmonyPatch("RefreshCombatValues")]
    [HarmonyPostfix]
    private static void RefreshHandPreview(NMultiplayerPlayerState __instance)
    {
        RemoteHandPreviewController.RefreshIfVisible(__instance);
    }

    [HarmonyPatch("OnCombatEnded")]
    [HarmonyPostfix]
    private static void HideHandPreviewAfterCombat(NMultiplayerPlayerState __instance)
    {
        RemoteHandPreviewController.Hide(__instance);
    }

    [HarmonyPatch(nameof(NMultiplayerPlayerState._ExitTree))]
    [HarmonyPrefix]
    private static void HideHandPreviewBeforeExitTree(NMultiplayerPlayerState __instance)
    {
        RemoteHandPreviewController.Hide(__instance);
    }
}
