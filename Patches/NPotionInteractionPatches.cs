using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Potions;
using TeamHandView.Core;

namespace TeamHandView.Patches;

[HarmonyPatch(typeof(NPotionPopup))]
internal static class NPotionPopupPatches
{
    [HarmonyPatch(nameof(NPotionPopup._Ready))]
    [HarmonyPostfix]
    private static void HideHandPreviewWhenPotionPopupOpens()
    {
        RemoteHandPreviewController.HideForBlockingUi("药水操作弹窗");
    }

    [HarmonyPatch(nameof(NPotionPopup._ExitTree))]
    [HarmonyPostfix]
    private static void RestoreHandPreviewWhenPotionPopupCloses()
    {
        RemoteHandPreviewController.RestoreAfterBlockingUi("药水操作弹窗关闭");
    }
}

[HarmonyPatch(typeof(NTargetManager))]
internal static class NTargetManagerPatches
{
    [HarmonyPatch(
        nameof(NTargetManager.StartTargeting),
        typeof(TargetType),
        typeof(Vector2),
        typeof(TargetMode),
        typeof(Func<bool>),
        typeof(Func<Node, bool>))]
    [HarmonyPostfix]
    private static void HideHandPreviewWhenVectorTargetingStarts()
    {
        RemoteHandPreviewController.HideForBlockingUi("目标选择开始");
    }

    [HarmonyPatch(
        nameof(NTargetManager.StartTargeting),
        typeof(TargetType),
        typeof(Control),
        typeof(TargetMode),
        typeof(Func<bool>),
        typeof(Func<Node, bool>))]
    [HarmonyPostfix]
    private static void HideHandPreviewWhenControlTargetingStarts()
    {
        RemoteHandPreviewController.HideForBlockingUi("目标选择开始");
    }

    [HarmonyPatch("FinishTargeting")]
    [HarmonyPostfix]
    private static void RestoreHandPreviewWhenTargetingEnds()
    {
        RemoteHandPreviewController.RestoreAfterBlockingUi("目标选择结束");
    }
}
