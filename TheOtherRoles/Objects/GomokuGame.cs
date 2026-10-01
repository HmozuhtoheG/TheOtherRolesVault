using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TMPro;
using UnityEngine;

namespace TheOtherRoles.Objects
{
    [TORRPCHolder]
    public static class GomokuGame
    {
        public const int Size = 15;
        public const byte NoPlayer = 255;

        private const float CellSize = 0.27f;
        private const float BoardOffsetX = 1.05f;
        private const float ButtonRowY = 2.4f;
        private const float UiZ = -0.2f;
        private const float InviteCardDuration = 6f;
        private const float InviteCardRestX = -1.8f;
        private const float InviteCardHiddenX = -11f;
        private const float InviteCooldown = 3f;
        private const int MissedChecksBeforeReset = 3;

        private const float SidebarX = -2.75f;
        private const float SidebarTopY = 2.05f;
        private const float SidebarSpacing = 0.7f;
        private const float SidebarBlockWidth = 1.9f;
        private const float SidebarBlockHeight = 0.62f;
        private const int SidebarMaxRows = 7;

        private static readonly (int dx, int dy)[] Directions = { (1, 0), (0, 1), (1, 1), (1, -1) };

        private class Match
        {
            public byte[,] board = new byte[Size, Size];
            public byte blackPlayerId = NoPlayer;
            public byte whitePlayerId = NoPlayer;
            public byte turn = 1;
            public byte winner = 0;
            public byte restartRequestedBy = NoPlayer;
            public int missedChecksBlack = 0;
            public int missedChecksWhite = 0;
        }

        private static readonly Dictionary<byte, Match> matches = new();
        private static byte localViewMatchId = NoPlayer;

        private static GameObject icon;
        private static GameObject panel;
        private static GameObject boardRoot;
        private static GameObject sidebarRoot;
        private static GameObject joinButtonObject;
        private static GameObject restartButtonObject;
        private static SpriteRenderer restartButtonRenderer;
        private static GameObject restartAgreeButton;
        private static GameObject restartDisagreeButton;
        private static GameObject exitButtonObject;
        private static GameObject exitSpectateButtonObject;
        private static GameObject inviteButtonObject;
        private static TextMeshPro playersText;
        private static GameObject inviteListPanel;
        private static GameObject inviteCard;
        private static BoxCollider2D boardClickCollider;
        private static TextMeshPro statusText;
        private static Material gridLineMaterial;
        private static string sidebarSignature;

        private static readonly Dictionary<(int x, int y), (GameObject obj, byte owner)> stoneObjects = new();
        private static readonly List<GameObject> gridLineObjects = new();
        private static readonly Dictionary<Color, Sprite> circleSpriteCache = new();
        private static readonly Dictionary<Color, Sprite> solidSpriteCache = new();
        private static readonly Dictionary<Color, Sprite> thumbSpriteCache = new();
        private static readonly Dictionary<(string, Color), Sprite> textureSpriteCache = new();
        private static readonly Dictionary<byte, float> lastInviteRealtime = new();

        private static int UiLayer => LayerMask.NameToLayer("UI");
        private static byte LocalPlayerId => PlayerControl.LocalPlayer != null ? PlayerControl.LocalPlayer.PlayerId : NoPlayer;

        public static RemoteProcess<byte> CreateMatch = RemotePrimitiveProcess.OfByte("GomokuCreateMatch", (playerId, _) =>
        {
            if (FindMatchOf(playerId) != NoPlayer) return;
            matches[playerId] = new Match { blackPlayerId = playerId };
            RefreshVisuals();
        });

        public static RemoteProcess<(byte matchId, byte playerId)> JoinMatch = new("GomokuJoinMatch", (msg, _) =>
        {
            if (!matches.TryGetValue(msg.matchId, out var match)) return;
            if (match.winner != 0) return;
            if (FindMatchOf(msg.playerId) != NoPlayer) return;
            if (match.blackPlayerId == NoPlayer) match.blackPlayerId = msg.playerId;
            else if (match.whitePlayerId == NoPlayer) match.whitePlayerId = msg.playerId;
            else return;
            RefreshVisuals();
        });

        public static RemoteProcess<(byte matchId, byte playerId, byte x, byte y)> PlaceStone = new("GomokuPlace", (msg, _) =>
        {
            if (!matches.TryGetValue(msg.matchId, out var match)) return;
            if (match.winner != 0) return;
            if (msg.x >= Size || msg.y >= Size) return;
            if (match.board[msg.x, msg.y] != 0) return;
            byte stone = msg.playerId == match.blackPlayerId ? (byte)1 : msg.playerId == match.whitePlayerId ? (byte)2 : (byte)0;
            if (stone == 0 || stone != match.turn) return;

            match.board[msg.x, msg.y] = stone;

            if (CheckWin(match.board, msg.x, msg.y, stone)) match.winner = stone;
            else if (IsBoardFull(match.board)) match.winner = 3;
            else match.turn = stone == 1 ? (byte)2 : (byte)1;

            RefreshVisuals();
        });

        public static RemoteProcess<(byte matchId, byte playerId)> LeaveMatch = new("GomokuLeaveMatch", (msg, _) =>
        {
            if (!matches.TryGetValue(msg.matchId, out var match)) return;
            if (msg.playerId != match.blackPlayerId && msg.playerId != match.whitePlayerId) return;
            matches.Remove(msg.matchId);
            RefreshVisuals();
        });

        public static RemoteProcess<(byte matchId, byte playerId)> RequestRestart = new("GomokuRequestRestart", (msg, _) =>
        {
            if (!matches.TryGetValue(msg.matchId, out var match)) return;
            if (msg.playerId != match.blackPlayerId && msg.playerId != match.whitePlayerId) return;
            if (match.restartRequestedBy != NoPlayer) return;

            byte other = msg.playerId == match.blackPlayerId ? match.whitePlayerId : match.blackPlayerId;
            if (other == NoPlayer)
            {
                ResetBoardOnly(match);
                RefreshVisuals();
                return;
            }

            match.restartRequestedBy = msg.playerId;
            RefreshVisuals();
        });

        public static RemoteProcess<(byte matchId, byte playerId)> CancelRestart = new("GomokuCancelRestart", (msg, _) =>
        {
            if (!matches.TryGetValue(msg.matchId, out var match)) return;
            if (match.restartRequestedBy != msg.playerId) return;
            match.restartRequestedBy = NoPlayer;
            RefreshVisuals();
        });

        public static RemoteProcess<(byte matchId, byte playerId, bool agree)> RespondRestart = new("GomokuRespondRestart", (msg, _) =>
        {
            if (!matches.TryGetValue(msg.matchId, out var match)) return;
            if (match.restartRequestedBy == NoPlayer) return;
            byte other = match.restartRequestedBy == match.blackPlayerId ? match.whitePlayerId : match.blackPlayerId;
            if (msg.playerId != other) return;

            if (msg.agree) ResetBoardOnly(match);
            match.restartRequestedBy = NoPlayer;
            RefreshVisuals();
        });

        public static RemoteProcess<(byte matchId, byte fromId, byte toId)> Invite = new("GomokuInvite", (msg, isCalledByMe) =>
        {
            if (isCalledByMe) return;
            if (PlayerControl.LocalPlayer == null || msg.toId != PlayerControl.LocalPlayer.PlayerId) return;
            ShowInviteCard(msg.matchId, msg.fromId);
        });

        public static void OnEnterLobby()
        {
            matches.Clear();
            localViewMatchId = NoPlayer;
            ClosePanel();
            DismissInviteCard();
            lastInviteRealtime.Clear();
            circleSpriteCache.Clear();
            solidSpriteCache.Clear();
            thumbSpriteCache.Clear();
            textureSpriteCache.Clear();
            gridLineMaterial = null;
            if (icon != null) UnityEngine.Object.Destroy(icon);
            icon = CreateIcon();
        }

        public static void OnLeaveLobby()
        {
            ClosePanel();
            DismissInviteCard();
            if (icon != null) UnityEngine.Object.Destroy(icon);
            icon = null;
            UnlockMovement();
        }

        public static void CloseForExternalUI()
        {
            if (panel != null) ClosePanel();
        }

        public static void ValidatePlayersConnected()
        {
            if (matches.Count == 0) return;

            List<byte> toRemove = null;
            foreach (var kv in matches)
            {
                var match = kv.Value;

                bool blackGone = match.blackPlayerId != NoPlayer && Helpers.playerById(match.blackPlayerId) == null;
                match.missedChecksBlack = blackGone ? match.missedChecksBlack + 1 : 0;

                bool whiteGone = match.whitePlayerId != NoPlayer && Helpers.playerById(match.whitePlayerId) == null;
                match.missedChecksWhite = whiteGone ? match.missedChecksWhite + 1 : 0;

                if (match.missedChecksBlack >= MissedChecksBeforeReset || match.missedChecksWhite >= MissedChecksBeforeReset)
                    (toRemove ??= new List<byte>()).Add(kv.Key);
            }

            if (toRemove == null) return;
            foreach (var id in toRemove) matches.Remove(id);
            RefreshVisuals();
        }

        private static byte FindMatchOf(byte playerId)
        {
            if (playerId == NoPlayer) return NoPlayer;
            foreach (var kv in matches)
                if (kv.Value.blackPlayerId == playerId || kv.Value.whitePlayerId == playerId) return kv.Key;
            return NoPlayer;
        }

        private static bool TryGetViewedMatch(out Match match)
        {
            match = null;
            return localViewMatchId != NoPlayer && matches.TryGetValue(localViewMatchId, out match);
        }

        private static bool IsBlack(Match match, byte playerId) => match != null && playerId == match.blackPlayerId;
        private static bool IsWhite(Match match, byte playerId) => match != null && playerId == match.whitePlayerId;

        private static string NameOf(byte id)
        {
            if (id == NoPlayer) return "?";
            var p = Helpers.playerById(id);
            return p != null && p.Data != null ? p.Data.PlayerName : "?";
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max) return s;
            return s.Substring(0, max) + "…";
        }

        private static void ResetBoardOnly(Match match)
        {
            match.board = new byte[Size, Size];
            match.turn = 1;
            match.winner = 0;
        }

        private static void LockMovement()
        {
            if (PlayerControl.LocalPlayer != null) PlayerControl.LocalPlayer.moveable = false;
        }

        private static void UnlockMovement()
        {
            if (PlayerControl.LocalPlayer != null) PlayerControl.LocalPlayer.moveable = true;
        }

        private static bool CheckWin(byte[,] board, int x, int y, byte player)
        {
            foreach (var (dx, dy) in Directions)
            {
                int count = 1 + CountDirection(board, x, y, dx, dy, player) + CountDirection(board, x, y, -dx, -dy, player);
                if (count >= 5) return true;
            }
            return false;
        }

        private static int CountDirection(byte[,] board, int x, int y, int dx, int dy, byte player)
        {
            int count = 0;
            int cx = x + dx, cy = y + dy;
            while (cx >= 0 && cx < Size && cy >= 0 && cy < Size && board[cx, cy] == player)
            {
                count++;
                cx += dx;
                cy += dy;
            }
            return count;
        }

        private static bool IsBoardFull(byte[,] board)
        {
            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                    if (board[x, y] == 0) return false;
            return true;
        }

        public static void TogglePanel()
        {
            if (panel != null) ClosePanel();
            else OpenPanel();
        }

        private static void OpenPanel()
        {
            if (Camera.main == null) return;
            localViewMatchId = FindMatchOf(LocalPlayerId);
            BuildPanel();
            RefreshVisuals();
            if (icon != null) icon.SetActive(false);
            LockMovement();
        }

        private static void ClosePanel()
        {
            if (panel != null) UnityEngine.Object.Destroy(panel);
            panel = null;
            boardRoot = null;
            sidebarRoot = null;
            joinButtonObject = null;
            restartButtonObject = null;
            restartButtonRenderer = null;
            restartAgreeButton = null;
            restartDisagreeButton = null;
            exitButtonObject = null;
            exitSpectateButtonObject = null;
            inviteButtonObject = null;
            playersText = null;
            statusText = null;
            inviteListPanel = null;
            boardClickCollider = null;
            sidebarSignature = null;
            stoneObjects.Clear();
            gridLineObjects.Clear();
            if (icon != null) icon.SetActive(true);
            UnlockMovement();
        }

        private static GameObject NewChild(string name, Transform parent, Vector3 localPos)
        {
            var obj = new GameObject(name) { layer = UiLayer };
            obj.transform.SetParent(parent, false);
            obj.transform.localPosition = localPos;
            return obj;
        }

        private static GameObject CreateClickable(Transform parent, string name, Vector3 localPos, Vector2 scale, Sprite sprite, int sortingOrder, Action onClick)
        {
            var obj = NewChild(name, parent, localPos);
            obj.transform.localScale = new Vector3(scale.x, scale.y, 1f);

            var sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = sortingOrder;

            var collider = obj.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one;

            var button = obj.SetUpButton();
            if (onClick != null) button.OnClick.AddListener((UnityEngine.Events.UnityAction)(() => onClick()));

            return obj;
        }

        private static TextMeshPro CreateLabel(Transform parent, string name, Vector3 localPos, float fontSize, Color color, int sortingOrder, string text = null, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            var label = Helpers.CreateObject<TextMeshPro>(name, parent, localPos);
            label.font = VanillaAsset.StandardTextPrefab.font;
            label.alignment = alignment;
            label.fontSize = fontSize;
            label.color = color;
            if (text != null) label.text = text;
            label.sortingOrder = sortingOrder;
            return label;
        }

        private static void BuildPanel()
        {
            panel = NewChild("GomokuPanel", Camera.main.transform, new Vector3(0f, 0f, -30f));

            CreateClickable(panel.transform, "GomokuBackground", Vector3.zero, new Vector2(7.6f, 5.6f),
                GetTextureSprite("GomokuPanelBackground", new Color(0.75f, 0.6f, 0.35f, 0.97f)), 20, null);

            boardRoot = NewChild("GomokuBoardRoot", panel.transform, new Vector3(BoardOffsetX, -0.35f, -0.1f));
            CreateGridLines(boardRoot.transform);

            float boardExtent = (Size - 1) * CellSize + CellSize;
            var clickArea = NewChild("GomokuClickArea", boardRoot.transform, Vector3.zero);
            boardClickCollider = clickArea.AddComponent<BoxCollider2D>();
            boardClickCollider.size = new Vector2(boardExtent, boardExtent);
            clickArea.SetUpButton().OnClick.AddListener((UnityEngine.Events.UnityAction)OnBoardClicked);

            inviteButtonObject = CreateTextButton(panel.transform, ButtonPos(BoardOffsetX - 2.1f), ModTranslation.getString("gomokuInvite"), OnInviteClicked, 0.9f, 0.4f, 1.1f);

            restartButtonObject = CreateTextButton(panel.transform, ButtonPos(BoardOffsetX - 0.7f), ModTranslation.getString("gomokuRestart"), OnRestartClicked, 0.9f, 0.4f, 1.1f);
            restartButtonRenderer = restartButtonObject.GetComponent<SpriteRenderer>();

            restartAgreeButton = CreateIconButton(panel.transform, ButtonPos(BoardOffsetX - 0.95f), 0.4f, 0f, OnRestartAgreeClicked, new Color(0.15f, 0.45f, 0.15f, 0.9f));
            restartDisagreeButton = CreateIconButton(panel.transform, ButtonPos(BoardOffsetX - 0.45f), 0.4f, 180f, OnRestartDisagreeClicked, new Color(0.45f, 0.15f, 0.15f, 0.9f));
            restartAgreeButton.SetActive(false);
            restartDisagreeButton.SetActive(false);

            exitButtonObject = CreateTextButton(panel.transform, ButtonPos(BoardOffsetX + 0.7f), ModTranslation.getString("gomokuExit"), OnExitClicked, 0.9f, 0.4f, 1.1f);
            exitSpectateButtonObject = CreateTextButton(panel.transform, ButtonPos(BoardOffsetX + 0.7f), ModTranslation.getString("gomokuExitSpectate"), OnExitSpectateClicked, 1.3f, 0.4f, 0.8f);

            CreateCloseButton(panel.transform, ButtonPos(BoardOffsetX + 1.9f), 0.42f, ClosePanel, 25);

            statusText = CreateLabel(panel.transform, "GomokuStatus", new Vector3(BoardOffsetX, 1.85f, UiZ), 1.4f, Color.black, 28);
            playersText = CreateLabel(panel.transform, "GomokuPlayers", new Vector3(BoardOffsetX, 1.6f, UiZ), 1.0f, new Color(0.15f, 0.1f, 0.05f), 28);

            joinButtonObject = CreateTextButton(panel.transform, new Vector3(BoardOffsetX, -2.55f, UiZ), "", OnJoinClicked, 1.4f, 0.38f, 1.5f);

            sidebarRoot = NewChild("GomokuSidebar", panel.transform, Vector3.zero);
        }

        private static Vector3 ButtonPos(float x) => new Vector3(x, ButtonRowY, UiZ);

        private static GameObject CreateTextButton(Transform parent, Vector3 localPos, string text, Action onClick, float widthScale = 1.6f, float heightScale = 0.5f, float fontSize = 2.2f, int sortingOrder = 25)
        {
            var obj = CreateClickable(parent, "GomokuButton", localPos, new Vector2(widthScale, heightScale),
                GetTextureSprite("GomokuButton", new Color(0.2f, 0.2f, 0.2f, 0.9f)), sortingOrder, onClick);

            var label = CreateLabel(obj.transform, "Label", new Vector3(0f, 0f, -0.05f), fontSize, Color.white, sortingOrder + 1, text);
            label.transform.localScale = new Vector3(1f / widthScale, 1f / heightScale, 1f);

            return obj;
        }

        private static GameObject CreateCloseButton(Transform parent, Vector3 localPos, float size, Action onClick, int sortingOrder = 25)
        {
            var obj = CreateClickable(parent, "GomokuCloseButton", localPos, new Vector2(size, size),
                GetSolidSprite(new Color(0.55f, 0.16f, 0.16f, 0.95f)), sortingOrder, onClick);

            float barLength = size * 0.6f;
            float barThickness = size * 0.14f;
            foreach (float angle in new[] { 45f, -45f })
            {
                var bar = NewChild("Bar", parent, localPos + new Vector3(0f, 0f, -0.05f));
                bar.transform.localScale = new Vector3(barLength, barThickness, 1f);
                bar.transform.localRotation = Quaternion.Euler(0f, 0f, angle);
                var barSr = bar.AddComponent<SpriteRenderer>();
                barSr.sprite = GetSolidSprite(Color.white);
                barSr.sortingOrder = sortingOrder + 1;
            }

            return obj;
        }

        private static GameObject CreateIconButton(Transform parent, Vector3 localPos, float size, float iconRotationZ, Action onClick, Color bgColor, int sortingOrder = 25)
        {
            var obj = CreateClickable(parent, "GomokuIconButton", localPos, new Vector2(size, size), GetSolidSprite(bgColor), sortingOrder, onClick);

            var iconObj = NewChild("Icon", obj.transform, new Vector3(0f, 0f, -0.05f));
            iconObj.transform.localRotation = Quaternion.Euler(0f, 0f, iconRotationZ);
            var iconSr = iconObj.AddComponent<SpriteRenderer>();
            iconSr.sprite = GetThumbSprite(Color.white);
            iconSr.sortingOrder = sortingOrder + 1;

            return obj;
        }

        private static GameObject CreateStoneObject(int x, int y, byte owner)
        {
            var obj = NewChild("GomokuStone", boardRoot.transform, CellLocalPos(x, y) + new Vector3(0f, 0f, -0.02f));
            var sr = obj.AddComponent<SpriteRenderer>();
            sr.sprite = GetCircleSprite(owner == 1 ? new Color(0.05f, 0.05f, 0.05f) : Color.white);
            sr.sortingOrder = 24;
            return obj;
        }

        private static void CreateGridLines(Transform parent)
        {
            if (gridLineMaterial == null) gridLineMaterial = new Material(Shader.Find("Sprites/Default"));
            var mat = gridLineMaterial;
            float half = (Size - 1) / 2f * CellSize;
            Color lineColor = new Color(0f, 0f, 0f, 0.6f);

            for (int i = 0; i < Size; i++)
            {
                float pos = -half + i * CellSize;

                var h = NewChild("GomokuGridH" + i, parent, Vector3.zero);
                var hl = h.AddComponent<LineRenderer>();
                hl.material = mat;
                hl.useWorldSpace = false;
                hl.startColor = hl.endColor = lineColor;
                hl.startWidth = hl.endWidth = 0.02f;
                hl.positionCount = 2;
                hl.SetPosition(0, new Vector3(-half, pos, -0.01f));
                hl.SetPosition(1, new Vector3(half, pos, -0.01f));
                hl.sortingOrder = 21;

                var v = NewChild("GomokuGridV" + i, parent, Vector3.zero);
                var vl = v.AddComponent<LineRenderer>();
                vl.material = mat;
                vl.useWorldSpace = false;
                vl.startColor = vl.endColor = lineColor;
                vl.startWidth = vl.endWidth = 0.02f;
                vl.positionCount = 2;
                vl.SetPosition(0, new Vector3(pos, -half, -0.01f));
                vl.SetPosition(1, new Vector3(pos, half, -0.01f));
                vl.sortingOrder = 21;

                gridLineObjects.Add(h);
                gridLineObjects.Add(v);
            }
        }

        private static Vector3 CellLocalPos(int x, int y)
        {
            return new Vector3((x - (Size - 1) / 2f) * CellSize, -(y - (Size - 1) / 2f) * CellSize, 0f);
        }

        private static bool TryGetCellFromWorldPos(Vector3 worldPos, out int x, out int y)
        {
            Vector3 local = boardRoot.transform.InverseTransformPoint(worldPos);
            float fx = local.x / CellSize + (Size - 1) / 2f;
            float fy = -local.y / CellSize + (Size - 1) / 2f;
            x = Mathf.RoundToInt(fx);
            y = Mathf.RoundToInt(fy);
            return x >= 0 && x < Size && y >= 0 && y < Size;
        }

        private static void RefreshVisuals()
        {
            if (panel == null) return;

            byte localId = LocalPlayerId;
            byte ownMatchId = FindMatchOf(localId);

            if (localViewMatchId != NoPlayer && !matches.ContainsKey(localViewMatchId)) localViewMatchId = ownMatchId;
            matches.TryGetValue(localViewMatchId, out var match);

            RebuildStones(match);
            UpdateStatusText(match, localId);
            UpdateMatchButtons(match, localId, ownMatchId);
            RebuildSidebar(localId);
        }

        private static void RebuildStones(Match match)
        {
            byte[,] board = match?.board;

            List<(int x, int y)> stale = null;
            foreach (var kv in stoneObjects)
                if (kv.Value.obj == null || board == null || board[kv.Key.x, kv.Key.y] != kv.Value.owner) (stale ??= new()).Add(kv.Key);

            if (stale != null)
            {
                foreach (var key in stale)
                {
                    var obj = stoneObjects[key].obj;
                    if (obj != null) UnityEngine.Object.Destroy(obj);
                    stoneObjects.Remove(key);
                }
            }

            if (board == null) return;

            for (int x = 0; x < Size; x++)
                for (int y = 0; y < Size; y++)
                    if (board[x, y] != 0 && !stoneObjects.ContainsKey((x, y)))
                        stoneObjects[(x, y)] = (CreateStoneObject(x, y, board[x, y]), board[x, y]);
        }

        private static void UpdateStatusText(Match match, byte localId)
        {
            bool isBlack = IsBlack(match, localId);
            bool isWhite = IsWhite(match, localId);

            string key;
            if (match == null) key = "gomokuNoMatchSelected";
            else if (match.winner == 1) key = "gomokuWinBlack";
            else if (match.winner == 2) key = "gomokuWinWhite";
            else if (match.winner == 3) key = "gomokuDraw";
            else if (match.blackPlayerId == NoPlayer || match.whitePlayerId == NoPlayer) key = "gomokuWaitingOpponent";
            else if ((isBlack && match.turn == 1) || (isWhite && match.turn == 2)) key = "gomokuYourTurn";
            else if (isBlack || isWhite) key = "gomokuOpponentTurn";
            else key = "gomokuSpectating";

            string text = ModTranslation.getString(key);
            if (statusText.text != text) statusText.text = text;
        }

        private static void UpdateMatchButtons(Match match, byte localId, byte ownMatchId)
        {
            bool isBlack = IsBlack(match, localId);
            bool isWhite = IsWhite(match, localId);
            bool isParticipant = isBlack || isWhite;

            bool canCreate = match == null && ownMatchId == NoPlayer;
            bool canJoin = match != null && !isParticipant && ownMatchId == NoPlayer && match.winner == 0 && (match.blackPlayerId == NoPlayer || match.whitePlayerId == NoPlayer);
            joinButtonObject.SetActive(canCreate || canJoin);
            if (canCreate) SetJoinLabel(ModTranslation.getString("gomokuCreateMatch"));
            else if (canJoin) SetJoinLabel(match.blackPlayerId == NoPlayer ? ModTranslation.getString("gomokuJoinBlack") : ModTranslation.getString("gomokuJoinWhite"));

            bool showPlayers = match != null && (match.blackPlayerId != NoPlayer || match.whitePlayerId != NoPlayer);
            playersText.gameObject.SetActive(showPlayers);
            if (showPlayers)
            {
                string label = $"{NameOf(match.blackPlayerId)} vs {NameOf(match.whitePlayerId)}";
                if (playersText.text != label) playersText.text = label;
            }

            bool awaitingMyResponse = isParticipant && match.restartRequestedBy != NoPlayer && match.restartRequestedBy != localId;
            bool iRequestedRestart = isParticipant && match.restartRequestedBy == localId;

            restartButtonObject.SetActive(isParticipant && !awaitingMyResponse);
            if (isParticipant && !awaitingMyResponse && restartButtonRenderer != null)
                restartButtonRenderer.sprite = GetTextureSprite("GomokuButton", iRequestedRestart ? new Color(0.4f, 0.4f, 0.4f, 0.9f) : new Color(0.2f, 0.2f, 0.2f, 0.9f));

            restartAgreeButton.SetActive(awaitingMyResponse);
            restartDisagreeButton.SetActive(awaitingMyResponse);

            exitButtonObject.SetActive(isParticipant);
            exitSpectateButtonObject.SetActive(match != null && !isParticipant);
            inviteButtonObject.SetActive(match != null && match.winner == 0 && (match.blackPlayerId == NoPlayer || match.whitePlayerId == NoPlayer));
        }

        private static void SetJoinLabel(string text)
        {
            joinButtonObject.GetComponentInChildren<TextMeshPro>().text = text;
        }

        private static string BuildSidebarSignature(byte localId, List<byte> ids)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(localId).Append('|').Append(localViewMatchId).Append('|');

            int shown = Mathf.Min(ids.Count, SidebarMaxRows);
            sb.Append(shown).Append(';');
            for (int i = 0; i < shown; i++)
            {
                var match = matches[ids[i]];
                sb.Append(ids[i]).Append(',').Append(match.blackPlayerId).Append(',').Append(match.whitePlayerId).Append(';');
            }
            return sb.ToString();
        }

        private static void RebuildSidebar(byte localId)
        {
            if (sidebarRoot == null) return;

            var ids = new List<byte>(matches.Keys);
            ids.Sort();

            string signature = BuildSidebarSignature(localId, ids);
            if (signature == sidebarSignature) return;
            sidebarSignature = signature;

            for (int i = sidebarRoot.transform.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(sidebarRoot.transform.GetChild(i).gameObject);

            CreateLabel(sidebarRoot.transform, "SidebarTitle", new Vector3(SidebarX, 2.55f, UiZ), 0.95f, new Color(0.15f, 0.1f, 0.05f), 26, ModTranslation.getString("gomokuOngoingMatches"));

            int row = 0;
            foreach (var matchId in ids)
            {
                if (row >= SidebarMaxRows) break;
                var match = matches[matchId];
                float y = SidebarTopY - row * SidebarSpacing;
                bool selected = matchId == localViewMatchId;
                bool mine = match.blackPlayerId == localId || match.whitePlayerId == localId;
                CreateMatchBlock(sidebarRoot.transform, new Vector3(SidebarX, y, UiZ), matchId, match, selected, mine);
                row++;
            }

            if (ids.Count == 0)
                CreateLabel(sidebarRoot.transform, "SidebarEmpty", new Vector3(SidebarX, 1.6f, UiZ), 0.8f, new Color(0.3f, 0.2f, 0.1f), 0, ModTranslation.getString("gomokuNoOngoingMatches"));
        }

        private static void CreateMatchBlock(Transform parent, Vector3 localPos, byte matchId, Match match, bool selected, bool mine)
        {
            if (selected)
            {
                var highlight = NewChild("Highlight", parent, localPos + new Vector3(0f, 0f, 0.01f));
                highlight.transform.localScale = new Vector3(SidebarBlockWidth + 0.08f, SidebarBlockHeight + 0.08f, 1f);
                var hSr = highlight.AddComponent<SpriteRenderer>();
                hSr.sprite = GetSolidSprite(new Color(0.95f, 0.8f, 0.3f, 0.95f));
                hSr.sortingOrder = 24;
            }

            var block = CreateClickable(parent, "MatchBlock", localPos, new Vector2(SidebarBlockWidth, SidebarBlockHeight),
                GetSolidSprite(mine ? new Color(0.22f, 0.4f, 0.22f, 0.95f) : new Color(0.15f, 0.15f, 0.22f, 0.95f)), 25,
                () => SelectMatch(matchId));

            string blackName = match.blackPlayerId == NoPlayer ? "?" : NameOf(match.blackPlayerId);
            string whiteName = match.whitePlayerId == NoPlayer ? ModTranslation.getString("gomokuWaitingSeat") : NameOf(match.whitePlayerId);

            var blackLabel = CreateLabel(block.transform, "Black", new Vector3(0f, 0.16f, -0.05f), 1.3f, Color.black, 26, "● " + Truncate(blackName, 8));
            blackLabel.transform.localScale = new Vector3(1f / SidebarBlockWidth, 1f / SidebarBlockHeight, 1f);

            var whiteLabel = CreateLabel(block.transform, "White", new Vector3(0f, -0.16f, -0.05f), 1.3f, Color.white, 26, "○ " + Truncate(whiteName, 8));
            whiteLabel.transform.localScale = new Vector3(1f / SidebarBlockWidth, 1f / SidebarBlockHeight, 1f);
        }

        private static void SelectMatch(byte matchId)
        {
            if (!matches.ContainsKey(matchId)) return;
            localViewMatchId = matchId;
            RefreshVisuals();
        }

        private static void OnJoinClicked()
        {
            if (PlayerControl.LocalPlayer == null) return;
            byte id = LocalPlayerId;
            if (FindMatchOf(id) != NoPlayer) return;

            if (localViewMatchId == NoPlayer || !matches.ContainsKey(localViewMatchId))
            {
                CreateMatch.Invoke(id);
                localViewMatchId = id;
                RefreshVisuals();
                return;
            }

            var match = matches[localViewMatchId];
            if (match.winner != 0) return;
            if (match.blackPlayerId != NoPlayer && match.whitePlayerId != NoPlayer) return;
            JoinMatch.Invoke((localViewMatchId, id));
        }

        private static void OnRestartClicked()
        {
            if (PlayerControl.LocalPlayer == null) return;
            if (!TryGetViewedMatch(out var match)) return;
            byte localId = LocalPlayerId;
            if (!IsBlack(match, localId) && !IsWhite(match, localId)) return;

            if (match.restartRequestedBy == localId) CancelRestart.Invoke((localViewMatchId, localId));
            else if (match.restartRequestedBy == NoPlayer) RequestRestart.Invoke((localViewMatchId, localId));
        }

        private static void OnRestartAgreeClicked()
        {
            if (PlayerControl.LocalPlayer == null) return;
            if (!TryGetViewedMatch(out _)) return;
            RespondRestart.Invoke((localViewMatchId, LocalPlayerId, true));
        }

        private static void OnRestartDisagreeClicked()
        {
            if (PlayerControl.LocalPlayer == null) return;
            if (!TryGetViewedMatch(out _)) return;
            RespondRestart.Invoke((localViewMatchId, LocalPlayerId, false));
        }

        private static void OnExitClicked()
        {
            if (PlayerControl.LocalPlayer == null) return;
            if (!TryGetViewedMatch(out var match)) return;
            byte localId = LocalPlayerId;
            if (!IsBlack(match, localId) && !IsWhite(match, localId)) return;

            LeaveMatch.Invoke((localViewMatchId, localId));
            localViewMatchId = NoPlayer;
            RefreshVisuals();
        }

        private static void OnExitSpectateClicked()
        {
            localViewMatchId = FindMatchOf(LocalPlayerId);
            RefreshVisuals();
        }

        private static void OnBoardClicked()
        {
            if (PlayerControl.LocalPlayer == null) return;
            if (!TryGetViewedMatch(out var match) || match.winner != 0) return;
            byte id = LocalPlayerId;
            byte myStone = id == match.blackPlayerId ? (byte)1 : id == match.whitePlayerId ? (byte)2 : (byte)0;
            if (myStone == 0 || myStone != match.turn) return;

            Vector3 mouseWorld = Camera.main.ScreenToWorldPoint(Input.mousePosition);
            mouseWorld.z = 0f;
            if (!TryGetCellFromWorldPos(mouseWorld, out int x, out int y)) return;
            if (match.board[x, y] != 0) return;

            PlaceStone.Invoke((localViewMatchId, id, (byte)x, (byte)y));
        }

        private static void OnInviteClicked()
        {
            if (inviteListPanel != null) { CloseInviteList(); return; }
            if (localViewMatchId == NoPlayer) return;
            BuildInviteListPanel();
        }

        private static void CloseInviteList()
        {
            if (inviteListPanel != null) UnityEngine.Object.Destroy(inviteListPanel);
            inviteListPanel = null;
            if (boardClickCollider != null) boardClickCollider.enabled = true;
        }

        private static void BuildInviteListPanel()
        {
            if (panel == null || localViewMatchId == NoPlayer) return;
            byte inviteMatchId = localViewMatchId;

            if (boardClickCollider != null) boardClickCollider.enabled = false;

            inviteListPanel = NewChild("GomokuInviteList", panel.transform, new Vector3(0f, 0f, -1f));

            CreateClickable(inviteListPanel.transform, "Background", Vector3.zero, new Vector2(5.0f, 5.4f),
                GetTextureSprite("GomokuPanelBackground", new Color(0.12f, 0.14f, 0.22f, 0.97f)), 40, null);

            CreateLabel(inviteListPanel.transform, "Title", new Vector3(-0.3f, 2.35f, -0.1f), 1.3f, Color.white, 42, ModTranslation.getString("gomokuInviteListTitle"));

            CreateCloseButton(inviteListPanel.transform, new Vector3(2.05f, 2.35f, -0.2f), 0.4f, CloseInviteList, 42);

            const float rowHeight = 0.34f;
            const float startY = 1.85f;
            const int maxRows = 12;
            int row = 0;
            byte localId = LocalPlayerId;

            foreach (var player in PlayerControl.AllPlayerControls)
            {
                if (player == null || player.PlayerId == localId) continue;
                if (FindMatchOf(player.PlayerId) != NoPlayer) continue;
                if (row >= maxRows) break;

                float y = startY - row * rowHeight;
                byte targetId = player.PlayerId;
                string targetName = player.Data != null ? player.Data.PlayerName : "";

                CreateLabel(inviteListPanel.transform, "Name" + row, new Vector3(-2.3f, y, -0.1f), 1.2f, Color.white, 42, targetName, TextAlignmentOptions.Left);

                CreateTextButton(inviteListPanel.transform, new Vector3(1.75f, y, -0.1f), ModTranslation.getString("gomokuInviteSend"), () => OnInviteSendClicked(inviteMatchId, targetId), 1.1f, 0.3f, 1.05f, 42);

                row++;
            }

            if (row == 0)
                CreateLabel(inviteListPanel.transform, "Empty", new Vector3(0f, 0.5f, -0.1f), 1.4f, Color.white, 42, ModTranslation.getString("gomokuInviteListEmpty"));
        }

        private static void OnInviteSendClicked(byte matchId, byte targetId)
        {
            if (PlayerControl.LocalPlayer == null) return;
            float now = Time.realtimeSinceStartup;
            if (lastInviteRealtime.TryGetValue(targetId, out float last) && now - last < InviteCooldown) return;
            lastInviteRealtime[targetId] = now;

            Invite.Invoke((matchId, LocalPlayerId, targetId));
            CloseInviteList();
        }

        private static void ShowInviteCard(byte matchId, byte fromId)
        {
            if (Camera.main == null) return;
            DismissInviteCard();

            var sender = Helpers.playerById(fromId);
            string senderName = sender != null && sender.Data != null ? sender.Data.PlayerName : "?";

            inviteCard = NewChild("GomokuInviteCard", Camera.main.transform, new Vector3(InviteCardHiddenX, 0.6f, -40f));

            CreateClickable(inviteCard.transform, "Background", Vector3.zero, new Vector2(3.6f, 1.4f),
                GetTextureSprite("GomokuPanelBackground", new Color(0.1f, 0.12f, 0.2f, 0.95f)), 60,
                () => OnInviteCardClicked(matchId, fromId));

            var accent = NewChild("Accent", inviteCard.transform, new Vector3(-1.65f, 0f, -0.05f));
            accent.transform.localScale = new Vector3(0.1f, 1.4f, 1f);
            var accentSr = accent.AddComponent<SpriteRenderer>();
            accentSr.sprite = GetSolidSprite(new Color(0.85f, 0.65f, 0.2f));
            accentSr.sortingOrder = 61;

            CreateLabel(inviteCard.transform, "Title", new Vector3(0.1f, 0.32f, -0.1f), 1.3f, Color.white, 62, ModTranslation.getString("gomokuInviteTitle"));
            CreateLabel(inviteCard.transform, "Message", new Vector3(0.1f, -0.12f, -0.1f), 1.1f, new Color(0.9f, 0.9f, 0.9f), 62, string.Format(ModTranslation.getString("gomokuInviteMessage"), senderName));
            CreateLabel(inviteCard.transform, "Hint", new Vector3(0.1f, -0.5f, -0.1f), 0.85f, new Color(0.75f, 0.75f, 0.75f), 62, ModTranslation.getString("gomokuInviteHint"));

            TORGUIManager.Instance.StartCoroutine(CoAnimateInviteCard(inviteCard).WrapToIl2Cpp());
        }

        private static IEnumerator CoAnimateInviteCard(GameObject card)
        {
            const float slideDuration = 0.35f;
            float t = 0f;
            while (t < slideDuration && card != null)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / slideDuration);
                p = 1f - Mathf.Pow(1f - p, 3f);
                card.transform.localPosition = new Vector3(Mathf.Lerp(InviteCardHiddenX, InviteCardRestX, p), card.transform.localPosition.y, card.transform.localPosition.z);
                yield return null;
            }
            if (card == null) yield break;
            card.transform.localPosition = new Vector3(InviteCardRestX, card.transform.localPosition.y, card.transform.localPosition.z);

            float waitT = 0f;
            while (waitT < InviteCardDuration && inviteCard == card)
            {
                waitT += Time.deltaTime;
                yield return null;
            }
            if (inviteCard == card) DismissInviteCard();
        }

        private static void OnInviteCardClicked(byte matchId, byte fromId)
        {
            DismissInviteCard();
            if (Camera.main == null) return;
            if (panel == null) OpenPanel();
            if (matches.ContainsKey(matchId)) localViewMatchId = matchId;
            OnJoinClicked();
            RefreshVisuals();
        }

        private static void DismissInviteCard()
        {
            if (inviteCard != null) UnityEngine.Object.Destroy(inviteCard);
            inviteCard = null;
        }

        private static GameObject CreateIcon()
        {
            if (Camera.main == null) return null;

            const float iconScale = 0.9f;
            var obj = CreateClickable(Camera.main.transform, "GomokuIcon", new Vector3(-4.2f, -2.55f, -30f), new Vector2(iconScale, iconScale),
                GetTextureSprite("GomokuIcon", new Color(0.75f, 0.6f, 0.35f)), 10, TogglePanel);

            var label = CreateLabel(obj.transform, "Label", new Vector3(0f, 0.65f, -0.05f), 1.3f, Color.white, 11, ModTranslation.getString("gomokuTitle"));
            label.transform.localScale = new Vector3(1f / iconScale, 1f / iconScale, 1f);
            label.outlineColor = Color.black;
            label.outlineWidth = 0.15f;

            return obj;
        }

        private static Sprite GetCircleSprite(Color color)
        {
            if (circleSpriteCache.TryGetValue(color, out var cached) && cached != null) return cached;

            int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Vector2 center = new Vector2(size / 2f, size / 2f);
            float radius = size / 2f - 2f;
            Color clear = new Color(0f, 0f, 0f, 0f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, Vector2.Distance(new Vector2(x, y), center) <= radius ? color : clear);
            tex.Apply();

            float pixelsPerUnit = size / (CellSize * 0.85f);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit);
            return circleSpriteCache[color] = sprite;
        }

        private static Sprite GetThumbSprite(Color color)
        {
            if (thumbSpriteCache.TryGetValue(color, out var cached) && cached != null) return cached;

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false);
            Color clear = new Color(0f, 0f, 0f, 0f);

            bool InFist(int x, int y) => x >= 3 && x <= 27 && y >= 2 && y <= 12;
            bool InThumb(int x, int y) => x >= 3 && x <= 14 && y >= 12 && y <= 23;
            bool InThumbTip(int x, int y) => x >= 3 && x <= 19 && y >= 22 && y <= 28;

            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    tex.SetPixel(x, y, InFist(x, y) || InThumb(x, y) || InThumbTip(x, y) ? color : clear);
            tex.Apply();

            float pixelsPerUnit = size / (CellSize * 1.6f);
            var sprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit);
            return thumbSpriteCache[color] = sprite;
        }

        private static Sprite GetSolidSprite(Color color)
        {
            if (solidSpriteCache.TryGetValue(color, out var cached) && cached != null) return cached;

            var tex = new Texture2D(4, 4, TextureFormat.ARGB32, false);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = color;
            tex.SetPixels(pixels);
            tex.Apply();

            var sprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f);
            return solidSpriteCache[color] = sprite;
        }

        private static Sprite GetTextureSprite(string resourceName, Color fallbackColor)
        {
            var key = (resourceName, fallbackColor);
            if (textureSpriteCache.TryGetValue(key, out var cached) && cached != null && cached.texture != null) return cached;

            Sprite sprite = null;
            Texture2D texture = Helpers.loadTextureFromResources("TheOtherRoles.Resources." + resourceName + ".png");
            if (texture != null)
                sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), texture.width);

            if (sprite == null) sprite = GetSolidSprite(fallbackColor);
            return textureSpriteCache[key] = sprite;
        }
    }
}
