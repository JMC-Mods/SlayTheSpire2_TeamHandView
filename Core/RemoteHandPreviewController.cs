using Godot;
using JmcModLib.Utils;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;

namespace TeamHandView.Core;

internal static class RemoteHandPreviewController
{
    private const float CardSpacing = 18f;
    private const float EdgePadding = 24f;
    private const float TargetGap = 18f;
    // 不主动抬高 ZIndex：原生 CardPreviewContainer 会在暂停菜单等界面打开时被移动到暗幕下方。
    private const int PreviewZIndex = 0;

    private static readonly Dictionary<ulong, PreviewState> Previews = [];
    private static readonly Dictionary<ulong, NMultiplayerPlayerState> PendingRefreshes = [];
    private static ulong? HoveredNetId;
    private static ulong? LockedNetId;

    public static void UpdateVisibility(NMultiplayerPlayerState playerState, bool isHighlighted)
    {
        try
        {
            if (isHighlighted)
            {
                HoveredNetId = playerState.Player?.NetId;
                ShowOrRefresh(playerState);
            }
            else
            {
                ClearHovered(playerState);
                HideIfUnlocked(playerState);
            }
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"{VersionInfo.Tag} 更新远程手牌预览显示状态失败：{ex}");
            Hide(playerState);
        }
    }

    public static void RefreshIfVisible(NMultiplayerPlayerState playerState)
    {
        try
        {
            if (playerState.Player is null)
                return;

            ulong netId = playerState.Player.NetId;
            if (!Previews.ContainsKey(netId) && LockedNetId != netId)
                return;

            ScheduleRefresh(netId, playerState);
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"{VersionInfo.Tag} 刷新远程手牌预览失败：{ex}");
            Hide(playerState);
        }
    }

    public static void TogglePreviewLock()
    {
        try
        {
            if (!TeamHandViewSettings.Enabled)
            {
                UnlockCurrentPreview(hideLockedPreview: true);
                return;
            }

            if (TryGetVisibleHoveredNetId(out ulong hoveredNetId))
            {
                if (LockedNetId == hoveredNetId)
                {
                    LockedNetId = null;
                    ModLogger.Info($"{VersionInfo.Tag} 已解除锁定其他玩家手牌预览。");
                    return;
                }

                if (LockedNetId is { } previouslyLockedNetId && previouslyLockedNetId != hoveredNetId)
                    HidePreview(previouslyLockedNetId);

                LockedNetId = hoveredNetId;
                ModLogger.Info($"{VersionInfo.Tag} 已锁定其他玩家手牌预览。");
                return;
            }

            UnlockCurrentPreview(hideLockedPreview: true);
        }
        catch (Exception ex)
        {
            ModLogger.Warn($"{VersionInfo.Tag} 切换远程手牌预览锁定状态失败：{ex}");
            UnlockCurrentPreview(hideLockedPreview: true);
        }
    }

    public static void Hide(NMultiplayerPlayerState playerState)
    {
        if (playerState.Player is null)
            return;

        HidePreview(playerState.Player.NetId);
    }

    public static void HideForBlockingUi(string reason)
    {
        if (Previews.Count == 0)
            return;

        HidePreviewRoots(reason);
    }

    public static void RestoreAfterBlockingUi(string reason)
    {
        if (Previews.Count == 0 || IsPreviewBlockedByGameUi())
            return;

        bool restoredAny = false;
        foreach (PreviewState preview in Previews.Values)
        {
            if (GodotObject.IsInstanceValid(preview.Root) && !preview.Root.Visible)
            {
                preview.Root.Visible = true;
                restoredAny = true;
            }
        }

        if (restoredAny)
            ModLogger.Debug($"{VersionInfo.Tag} 已恢复远程手牌预览：{reason}");
    }

    public static void ClearRunPreviewState(string reason)
    {
        bool hadState = LockedNetId is not null || Previews.Count > 0;

        HoveredNetId = null;
        LockedNetId = null;
        PendingRefreshes.Clear();

        foreach (ulong netId in Previews.Keys.ToArray())
            HidePreview(netId);

        if (hadState)
            ModLogger.Debug($"{VersionInfo.Tag} 已清理远程手牌预览状态：{reason}");
    }

    private static void HidePreview(ulong netId)
    {
        if (HoveredNetId == netId)
            HoveredNetId = null;

        PendingRefreshes.Remove(netId);

        if (!Previews.Remove(netId, out PreviewState? preview))
            return;

        FreeCardNodes(preview);
        preview.Root.QueueFreeSafely();
    }

    private static void ShowOrRefresh(NMultiplayerPlayerState playerState)
    {
        if (IsPreviewBlockedByGameUi())
        {
            HidePreviewRoots("游戏交互界面正在显示");
            return;
        }

        if (!CanPreview(playerState, out IReadOnlyList<CardModel> cards))
        {
            Hide(playerState);
            return;
        }

        Control? parent = NRun.Instance?.GlobalUi?.CardPreviewContainer;
        if (parent is null || !GodotObject.IsInstanceValid(parent))
            return;

        ulong netId = playerState.Player.NetId;
        PreviewState preview = GetOrCreatePreview(parent, netId);

        float scale = Mathf.Clamp(TeamHandViewSettings.CardScale, 0.25f, 0.65f);
        int columns = Mathf.Clamp(TeamHandViewSettings.MaxColumns, 1, Math.Max(1, cards.Count));
        Vector2 cardSize = NCard.defaultSize * scale;
        Vector2 previewSize = GetPreviewSize(cards.Count, columns, cardSize);

        preview.Root.MouseFilter = Control.MouseFilterEnum.Ignore;
        preview.Root.Visible = true;
        preview.Root.Size = previewSize;
        preview.Root.ZIndex = PreviewZIndex;

        SyncCards(preview, cards, columns, scale, cardSize, forceVisualRefresh: true);
        PositionPreview(playerState, preview.Root, previewSize);
    }

    private static bool CanPreview(NMultiplayerPlayerState playerState, out IReadOnlyList<CardModel> cards)
    {
        cards = [];

        if (!TeamHandViewSettings.Enabled)
            return false;

        if (playerState.Player is null || LocalContext.IsMe(playerState.Player))
            return false;

        if (!playerState.IsInsideTree())
            return false;

        IReadOnlyList<CardModel>? handCards = playerState.Player.PlayerCombatState?.Hand.Cards;
        if (handCards is not { Count: > 0 })
            return false;

        cards = handCards;
        return true;
    }

    private static PreviewState GetOrCreatePreview(Control parent, ulong netId)
    {
        if (Previews.TryGetValue(netId, out PreviewState? preview) && GodotObject.IsInstanceValid(preview.Root))
        {
            if (preview.Root.GetParent() != parent)
            {
                preview.Root.GetParent()?.RemoveChild(preview.Root);
                parent.AddChildSafely(preview.Root);
            }

            return preview;
        }

        Control root = new()
        {
            Name = $"TeamHandViewRemoteHandPreview_{netId}",
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = PreviewZIndex
        };
        parent.AddChildSafely(root);
        preview = new PreviewState(root);
        Previews[netId] = preview;
        return preview;
    }

    private static void SyncCards(
        PreviewState preview,
        IReadOnlyList<CardModel> cards,
        int columns,
        float scale,
        Vector2 cardSize,
        bool forceVisualRefresh)
    {
        HideUnusedCards(preview, cards.Count);

        bool layoutChanged = preview.Columns != columns || !Mathf.IsEqualApprox(preview.Scale, scale);

        for (int i = 0; i < cards.Count; i++)
        {
            if (!TryGetCardNode(preview, cards[i], i, out NCard? cardNode, out bool modelChanged) || cardNode is null)
                continue;

            int row = i / columns;
            int column = i % columns;

            cardNode.MouseFilter = Control.MouseFilterEnum.Ignore;
            cardNode.Scale = Vector2.One * scale;
            cardNode.Position = new Vector2(
                column * (cardSize.X + CardSpacing),
                row * (cardSize.Y + CardSpacing));

            if (modelChanged || layoutChanged || forceVisualRefresh)
                cardNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal);
        }

        preview.Columns = columns;
        preview.Scale = scale;
    }

    private static bool TryGetCardNode(
        PreviewState preview,
        CardModel card,
        int index,
        out NCard? cardNode,
        out bool modelChanged)
    {
        modelChanged = false;

        if (index < preview.CardNodes.Count && GodotObject.IsInstanceValid(preview.CardNodes[index]))
        {
            cardNode = preview.CardNodes[index];
            cardNode.Visible = true;
            if (!ReferenceEquals(cardNode.Model, card))
            {
                cardNode.Model = card;
                modelChanged = true;
            }

            return true;
        }

        cardNode = NCard.Create(card);
        if (cardNode is null)
            return false;

        modelChanged = true;
        cardNode.Visible = true;
        cardNode.MouseFilter = Control.MouseFilterEnum.Ignore;

        if (index < preview.CardNodes.Count)
            preview.CardNodes[index] = cardNode;
        else
            preview.CardNodes.Add(cardNode);

        preview.Root.AddChildSafely(cardNode);
        return true;
    }

    private static void HideUnusedCards(PreviewState preview, int cardCount)
    {
        for (int i = cardCount; i < preview.CardNodes.Count; i++)
        {
            if (GodotObject.IsInstanceValid(preview.CardNodes[i]))
                preview.CardNodes[i].Visible = false;
        }
    }

    private static void FreeCardNodes(PreviewState preview)
    {
        foreach (NCard cardNode in preview.CardNodes)
        {
            if (GodotObject.IsInstanceValid(cardNode))
                cardNode.QueueFreeSafely();
        }

        preview.CardNodes.Clear();
    }

    private static void ScheduleRefresh(ulong netId, NMultiplayerPlayerState playerState)
    {
        bool alreadyScheduled = PendingRefreshes.ContainsKey(netId);
        PendingRefreshes[netId] = playerState;

        if (alreadyScheduled)
            return;

        // 同一帧内能量、星星、手牌增删可能连环触发，延后一帧只刷新一次。
        Callable.From(() => FlushScheduledRefresh(netId)).CallDeferred();
    }

    private static void FlushScheduledRefresh(ulong netId)
    {
        if (!PendingRefreshes.Remove(netId, out NMultiplayerPlayerState? playerState))
            return;

        if (!GodotObject.IsInstanceValid(playerState))
        {
            HidePreview(netId);
            return;
        }

        if (Previews.ContainsKey(netId) || LockedNetId == netId)
            ShowOrRefresh(playerState);
    }

    private static Vector2 GetPreviewSize(int cardCount, int columns, Vector2 cardSize)
    {
        int rows = (cardCount + columns - 1) / columns;
        int actualColumns = Math.Min(columns, cardCount);

        return new Vector2(
            actualColumns * cardSize.X + Math.Max(0, actualColumns - 1) * CardSpacing,
            rows * cardSize.Y + Math.Max(0, rows - 1) * CardSpacing);
    }

    private static void PositionPreview(NMultiplayerPlayerState playerState, Control root, Vector2 previewSize)
    {
        Vector2 viewportSize = playerState.GetViewport().GetVisibleRect().Size;
        Vector2 targetPosition = playerState.GlobalPosition;
        Vector2 targetSize = playerState.Size;

        Vector2 position = new(targetPosition.X + targetSize.X + TargetGap, targetPosition.Y);
        if (position.X + previewSize.X > viewportSize.X - EdgePadding)
            position.X = targetPosition.X - previewSize.X - TargetGap;

        position.Y = targetPosition.Y - Math.Max(0f, (previewSize.Y - targetSize.Y) * 0.5f);
        position += GetConfiguredPositionOffset();
        position.X = Mathf.Clamp(position.X, EdgePadding, Math.Max(EdgePadding, viewportSize.X - previewSize.X - EdgePadding));
        position.Y = Mathf.Clamp(position.Y, EdgePadding, Math.Max(EdgePadding, viewportSize.Y - previewSize.Y - EdgePadding));

        root.GlobalPosition = position;
    }

    private static Vector2 GetConfiguredPositionOffset()
    {
        return new Vector2(
            Mathf.Clamp(
                TeamHandViewSettings.PositionOffsetX,
                TeamHandViewSettings.MinPositionOffset,
                TeamHandViewSettings.MaxPositionOffset),
            Mathf.Clamp(
                TeamHandViewSettings.PositionOffsetY,
                TeamHandViewSettings.MinPositionOffset,
                TeamHandViewSettings.MaxPositionOffset));
    }

    private static bool IsPreviewBlockedByGameUi()
    {
        return NHoverTipSet.shouldBlockHoverTips || NTargetManager.Instance?.IsInSelection == true;
    }

    private static void HidePreviewRoots(string reason)
    {
        PendingRefreshes.Clear();

        bool hidAny = false;
        foreach (PreviewState preview in Previews.Values)
        {
            if (GodotObject.IsInstanceValid(preview.Root))
            {
                hidAny |= preview.Root.Visible;
                preview.Root.Visible = false;
            }
        }

        if (hidAny)
            ModLogger.Debug($"{VersionInfo.Tag} 已临时隐藏远程手牌预览：{reason}");
    }

    private static void HideIfUnlocked(NMultiplayerPlayerState playerState)
    {
        if (playerState.Player is null)
            return;

        // 锁定状态下允许预览在焦点离开后继续停留。
        ulong netId = playerState.Player.NetId;
        if (LockedNetId == netId)
            return;

        HidePreview(netId);
    }

    private static void ClearHovered(NMultiplayerPlayerState playerState)
    {
        if (playerState.Player is null)
            return;

        if (HoveredNetId == playerState.Player.NetId)
            HoveredNetId = null;
    }

    private static bool TryGetVisibleHoveredNetId(out ulong netId)
    {
        if (HoveredNetId is { } hoveredNetId
            && Previews.TryGetValue(hoveredNetId, out PreviewState? preview)
            && GodotObject.IsInstanceValid(preview.Root)
            && preview.Root.Visible)
        {
            netId = hoveredNetId;
            return true;
        }

        netId = default;
        return false;
    }

    private static void UnlockCurrentPreview(bool hideLockedPreview)
    {
        if (LockedNetId is not { } lockedNetId)
            return;

        LockedNetId = null;
        if (hideLockedPreview)
            HidePreview(lockedNetId);

        ModLogger.Info($"{VersionInfo.Tag} 已解除锁定其他玩家手牌预览。");
    }

    private sealed class PreviewState(Control root)
    {
        public Control Root { get; } = root;
        public List<NCard> CardNodes { get; } = [];
        public int Columns { get; set; } = -1;
        public float Scale { get; set; } = -1f;
    }
}
