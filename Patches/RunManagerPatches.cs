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
        // JML 会在本局结束时清理客户端本局 sidecar；这里仅释放现有 UI 节点。
        RemoteHandPreviewController.ClearRunPreviewState("本局游戏结束");
    }

    [HarmonyPatch(nameof(RunManager.CleanUp))]
    [HarmonyPrefix]
    private static void ClearHandPreviewLockBeforeRunCleanUp()
    {
        // 保存退出或 SL 期间只清理内存和节点，不写空持久化值，避免丢失锁定目标。
        RemoteHandPreviewController.ClearRunPreviewState("RunManager 清理");
    }
}
