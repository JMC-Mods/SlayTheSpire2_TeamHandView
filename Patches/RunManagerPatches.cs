using HarmonyLib;
using MegaCrit.Sts2.Core.Runs;
using TeamHandView.Core;

namespace TeamHandView.Patches;

[HarmonyPatch(typeof(RunManager))]
internal static class RunManagerPatches
{
    [HarmonyPatch(nameof(RunManager.OnEnded))]
    [HarmonyPostfix]
    private static void ClearHandPreviewLockAfterRunEnded()
    {
        // 固定显示只在本局内持续，胜利或失败结算后统一清空。
        RemoteHandPreviewController.ClearRunPreviewState("本局游戏结束");
    }

    [HarmonyPatch(nameof(RunManager.CleanUp))]
    [HarmonyPrefix]
    private static void ClearHandPreviewLockBeforeRunCleanUp()
    {
        // 退出、断线返回主菜单等清理路径也要释放锁定，避免影响下一局。
        RemoteHandPreviewController.ClearRunPreviewState("RunManager 清理");
    }
}
