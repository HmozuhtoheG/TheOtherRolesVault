using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Utilities;
using UnityEngine;

namespace TheOtherRoles.Patches
{
    [HarmonyPatch]
    public static class LobbyDecorationPatch
    {
        public const string CrateName = "Leftbox";
        public const float HeightRatio = 0.85f;
        public const float SinkRatio = 0.2f;
        public const int Order = 100;
        public const float LoadPixelsPerUnit = 100f;

        private static readonly string[] SpritePaths =
        {
            "TheOtherRoles.Resources.imp11.png",
            "TheOtherRoles.Resources.imp.imp22.png",
            "TheOtherRoles.Resources.imp.imp33.png",
            "TheOtherRoles.Resources.imp.imp44.png",
            "TheOtherRoles.Resources.imp.imp55.png",
            "TheOtherRoles.Resources.imp.imp66.png",
            "TheOtherRoles.Resources.imp.imp77.png"
        };

        public static GameObject Decoration;

        public static RemoteProcess<byte> SetIndex = RemotePrimitiveProcess.OfByte("LobbyDecoSetIndex", (message, _) =>
        {
            ApplyIndex(message);
        });

        public static void ApplyIndex(byte value)
        {
            if (sprites == null || sprites.Length == 0) return;
            index = value % sprites.Length;
            ApplySprite();
        }

        public static int CurrentIndex => index;

        private static int lastPlayerCount = -1;

        private static SpriteRenderer renderer;
        private static Sprite[] sprites;
        private static int index;
        private static float targetWidth;
        private static float targetHeight;

        [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Start))]
        [HarmonyPostfix]
        public static void StartPostfix()
        {
            Build();
        }

        [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.OnDestroy))]
        [HarmonyPostfix]
        public static void OnDestroyPostfix()
        {
            Decoration = null;
            renderer = null;
        }

        [HarmonyPatch(typeof(LobbyBehaviour), nameof(LobbyBehaviour.Update))]
        [HarmonyPostfix]
        public static void UpdatePostfix()
        {
            ShareToLateJoiners();

            if (!Decoration || renderer == null || !Input.GetMouseButtonDown(0)) return;
            if (TextField.AnyoneValid) return;
            if (TORGUIManager.Instance != null && TORGUIManager.Instance.HasSomeUI) return;

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud != null && hud.Chat != null && hud.Chat.IsOpenOrOpening) return;

            var cam = Camera.main;
            if (cam == null) return;

            var bounds = renderer.bounds;
            var mouse = cam.ScreenToWorldPoint(Input.mousePosition);
            mouse.z = bounds.center.z;
            if (!bounds.Contains(mouse)) return;

            Cycle();
        }

        [HarmonyPatch(typeof(ShipStatus), nameof(ShipStatus.Start))]
        [HarmonyPostfix]
        public static void ShipStatusStartPostfix()
        {
            Destroy();
        }

        public static void Destroy()
        {
            if (Decoration) Object.Destroy(Decoration);
            Decoration = null;
            renderer = null;
            lastPlayerCount = -1;
        }

        private static void ShareToLateJoiners()
        {
            if (sprites == null || sprites.Length == 0) return;
            if (!Decoration) return;
            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;
            if (PlayerControl.LocalPlayer == null || PlayerControl.LocalPlayer.Data == null) return;

            int count = GameData.Instance != null ? GameData.Instance.PlayerCount : -1;
            if (count <= 0 || count == lastPlayerCount) return;

            lastPlayerCount = count;
            SetIndex.Invoke((byte)index);
        }

        private static void Cycle()
        {
            if (sprites == null || sprites.Length == 0) return;
            SetIndex.Invoke((byte)((index + 1) % sprites.Length));
        }

        private static void ApplySprite()
        {
            var sprite = sprites[index];
            if (!sprite || renderer == null) return;

            renderer.sprite = sprite;

            var size = sprite.bounds.size;
            if (size.x <= 0f || size.y <= 0f) return;

            var parent = Decoration.transform.parent;
            var parentScale = parent ? parent.lossyScale : Vector3.one;

            float scaleX = parentScale.x != 0f ? targetWidth / (size.x * parentScale.x) : 1f;
            float scaleY = parentScale.y != 0f ? targetHeight / (size.y * parentScale.y) : 1f;
            Decoration.transform.localScale = new Vector3(scaleX, scaleY, 1f);
        }

        private static SpriteRenderer FindCrate()
        {
            if (!LobbyBehaviour.Instance) return null;
            var crate = LobbyBehaviour.Instance.transform.Find(CrateName);
            if (!crate) return null;
            return crate.GetComponent<SpriteRenderer>();
        }

        private static void Build()
        {
            Destroy();

            var crate = FindCrate();
            if (!crate)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[LobbyDeco] crate '{CrateName}' not found");
                return;
            }

            sprites = LoadSprites();
            if (sprites.Length == 0)
            {
                TheOtherRolesPlugin.Logger.LogWarning("[LobbyDeco] no sprite loaded");
                return;
            }

            var bounds = crate.bounds;
            float height = bounds.size.y * HeightRatio;
            var reference = sprites[0].bounds.size;
            targetHeight = height;
            targetWidth = reference.y > 0f ? height * reference.x / reference.y : height;

            Decoration = new GameObject("TORLobbyDecoration");
            Decoration.transform.SetParent(LobbyBehaviour.Instance.transform, true);
            renderer = Decoration.AddComponent<SpriteRenderer>();
            renderer.sortingLayerName = crate.sortingLayerName;
            renderer.sortingOrder = Order;

            Decoration.transform.position = new Vector3(
                bounds.center.x,
                bounds.max.y - bounds.size.y * SinkRatio + height * 0.5f,
                crate.transform.position.z);

            index = 0;
            ApplySprite();

            TheOtherRolesPlugin.Logger.LogInfo($"[LobbyDeco] crate={CrateName} sprites={sprites.Length} size={targetWidth:0.###}x{targetHeight:0.###}");
        }

        private static Sprite[] LoadSprites()
        {
            var list = new System.Collections.Generic.List<Sprite>();
            foreach (var path in SpritePaths)
            {
                var sprite = Helpers.loadSpriteFromResources(path, LoadPixelsPerUnit);
                if (sprite == null)
                {
                    TheOtherRolesPlugin.Logger.LogWarning($"[LobbyDeco] missing sprite {path}");
                    continue;
                }
                list.Add(sprite);
            }
            return list.ToArray();
        }
    }
}
