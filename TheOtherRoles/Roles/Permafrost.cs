using System;
using System.Collections.Generic;
using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Utilities;
using UnityEngine;
using Image = UnityEngine.UI.Image;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Permafrost : RoleBase<Permafrost>
    {
        public static Color color = Palette.ImpostorRed;

        public Permafrost()
        {
            RoleId = roleId = RoleId.Permafrost;
            isSpraying = false;
            sprayElapsed = 0f;
            sprayCooldownTimer = 0f;
            hasGroundAnchor = false;
            anchorStableTime = 0f;
            anchorPosition = Vector2.zero;
            aimedVent = null;
            hasPlayerAim = false;
            playerAimStableTime = 0f;
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%SPRAYDURATION%", maxSprayDuration.ToString());
            yield return new("%SPRAYCOOLDOWN%", sprayCooldown.ToString());
            yield return new("%SPRAYRANGE%", sprayRange.ToString());
            yield return new("%BLOCKLIFETIME%", blockLifetime.ToString());
            yield return new("%VENTFREEZE%", ventFreezeDuration.ToString());
            yield return new("%FREEZEDURATION%", freezeDuration.ToString());
        }

        public static float sprayCooldown = 22f;
        public static float maxSprayDuration = 10f;
        public static float sprayRange = 2.4f;
        public static float slowFactor = -0.5f;
        public static float blockLifetime = 75f;
        public static float blockSlowFactor = -0.6f;
        public static float blockSlowDuration = 2.5f;
        public static float ventFreezeDuration = 6f;
        public static int maxActiveBlocks = 2;
        public static bool blockBreakable = true;
        public static int blockBreakHits = 3;
        public static float freezeDuration = 20f;

        private const float AnchorDriftTolerance = 0.5f;
        private const float FrostMarkLinger = 1.4f;
        private const float FrostMarkSize = 46f;
        private const float FrozenOverlaySize = 130f;

        public bool isSpraying;
        public float sprayElapsed;
        public float sprayCooldownTimer;
        public bool hasGroundAnchor;
        public float anchorStableTime;
        public Vector2 anchorPosition;
        public Vent aimedVent;
        public bool hasPlayerAim;
        public float playerAimStableTime;

        private static readonly Dictionary<int, DateTime> frozenVents = new();
        private static readonly Dictionary<byte, DateTime> frozenUntil = new();

        private static bool sprayButtonHeld;
        public static bool sprayButtonHovered;

        private static bool IsPointerDown()
        {
            if (Input.GetMouseButton(0)) return true;
            for (int i = 0; i < Input.touchCount; i++)
            {
                TouchPhase phase = Input.GetTouch(i).phase;
                if (phase != TouchPhase.Ended && phase != TouchPhase.Canceled) return true;
            }
            return false;
        }

        private static bool IsSprayInputHeld()
        {
            var button = HudManagerStartPatch.permafrostSprayButton;
            if (button == null) return false;

            if (button.hotkey.HasValue && Input.GetKey(button.hotkey.Value)) return true;

            return sprayButtonHeld || (sprayButtonHovered && IsPointerDown());
        }

        private void StartSprayLocal()
        {
            isSpraying = true;
            sprayElapsed = 0f;
            hasGroundAnchor = false;
            anchorStableTime = 0f;
            aimedVent = null;
            hasPlayerAim = false;
            playerAimStableTime = 0f;
            StartSprayRpc.Invoke(player.PlayerId);
        }

        private void StopSprayLocal()
        {
            if (!isSpraying) return;

            bool completedFull = sprayElapsed >= maxSprayDuration - 0.05f;
            bool anchoredFullDuration = hasGroundAnchor && anchorStableTime >= maxSprayDuration - 0.15f;
            bool sealedVent = completedFull && anchoredFullDuration && aimedVent != null;
            bool formedBlock = completedFull && anchoredFullDuration && aimedVent == null;
            bool frozeTargets = completedFull && hasPlayerAim && playerAimStableTime >= maxSprayDuration - 0.15f;

            isSpraying = false;
            sprayCooldownTimer = sprayCooldown;
            sprayElapsed = 0f;
            StopSprayRpc.Invoke(player.PlayerId);

            var button = HudManagerStartPatch.permafrostSprayButton;
            if (button != null)
            {
                button.MaxTimer = sprayCooldown;
                button.Timer = sprayCooldown;
            }

            if (sealedVent)
                SealVentRpc.Invoke(aimedVent.Id);
            else if (formedBlock)
                SpawnBlockRpc.Invoke((player.PlayerId, anchorPosition.x, anchorPosition.y));
            else if (frozeTargets)
                foreach (PlayerControl target in GetAllSprayTargets(player, sprayRange))
                    FreezeTargetRpc.Invoke(target.PlayerId);

            hasGroundAnchor = false;
            anchorStableTime = 0f;
            aimedVent = null;
            hasPlayerAim = false;
            playerAimStableTime = 0f;
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;

            if (sprayCooldownTimer > 0f)
                sprayCooldownTimer = Mathf.Max(0f, sprayCooldownTimer - Time.fixedDeltaTime);

            bool held = IsSprayInputHeld();
            bool blocked = player.Data.IsDead || !player.CanMove || MeetingHud.Instance || ExileController.Instance;

            if (!isSpraying)
            {
                if (!held || blocked || sprayCooldownTimer > 0f) return;
                StartSprayLocal();
            }
            else if (!held || blocked)
            {
                StopSprayLocal();
                return;
            }

            sprayElapsed += Time.fixedDeltaTime;

            PlayerControl sprayTarget = GetNearestSprayTarget(player, sprayRange);
            if (sprayTarget != null)
            {
                hasGroundAnchor = false;
                anchorStableTime = 0f;
                aimedVent = null;

                hasPlayerAim = true;
                playerAimStableTime += Time.fixedDeltaTime;
            }
            else
            {
                hasPlayerAim = false;
                playerAimStableTime = 0f;

                Vent vent = GetNearestSprayVent(player, sprayRange);
                Vector2 anchor = vent != null ? (Vector2)vent.transform.position : (Vector2)player.transform.position;

                if (!hasGroundAnchor || Vector2.Distance(anchorPosition, anchor) > AnchorDriftTolerance)
                {
                    hasGroundAnchor = true;
                    anchorPosition = anchor;
                    anchorStableTime = 0f;
                }
                else
                {
                    anchorStableTime += Time.fixedDeltaTime;
                }
                aimedVent = vent;
            }

            if (sprayElapsed >= maxSprayDuration - 0.02f)
                StopSprayLocal();
        }

        public override void OnMeetingStart()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (isSpraying) StopSprayLocal();
        }

        public static void clearAndReload()
        {
            sprayCooldown = CustomOptionHolder.permafrostSprayCooldown.getFloat();
            maxSprayDuration = CustomOptionHolder.permafrostMaxSprayDuration.getFloat();
            sprayRange = CustomOptionHolder.permafrostSprayRange.getFloat();
            slowFactor = CustomOptionHolder.permafrostSlowFactor.getFloat();
            blockLifetime = CustomOptionHolder.permafrostBlockLifetime.getFloat();
            blockSlowFactor = CustomOptionHolder.permafrostBlockSlowFactor.getFloat();
            blockSlowDuration = CustomOptionHolder.permafrostBlockSlowDuration.getFloat();
            ventFreezeDuration = CustomOptionHolder.permafrostVentFreezeDuration.getFloat();
            maxActiveBlocks = (int)CustomOptionHolder.permafrostMaxActiveBlocks.getFloat();
            blockBreakable = CustomOptionHolder.permafrostBlockBreakable.getBool();
            blockBreakHits = (int)CustomOptionHolder.permafrostBlockBreakHits.getFloat();
            freezeDuration = CustomOptionHolder.permafrostFreezeDuration.getFloat();

            frozenVents.Clear();
            frozenUntil.Clear();
            IceBlock.ClearAll();
            ClearVisuals();
            sprayButtonHeld = false;
            sprayButtonHovered = false;
            players = [];
        }

        public static PlayerControl GetNearestSprayTarget(PlayerControl from, float maxRange)
        {
            PlayerControl best = null;
            float bestDist = maxRange;
            foreach (PlayerControl p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p == from || p.Data == null || p.Data.IsDead) continue;
                if (from.Data.Role.IsImpostor && p.Data.Role.IsImpostor) continue;
                float dist = Vector2.Distance(from.transform.position, p.transform.position);
                if (dist <= bestDist)
                {
                    bestDist = dist;
                    best = p;
                }
            }
            return best;
        }

        public static List<PlayerControl> GetAllSprayTargets(PlayerControl from, float maxRange)
        {
            var targets = new List<PlayerControl>();
            foreach (PlayerControl p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p == from || p.Data == null || p.Data.IsDead) continue;
                if (from.Data.Role.IsImpostor && p.Data.Role.IsImpostor) continue;
                if (Vector2.Distance(from.transform.position, p.transform.position) <= maxRange)
                    targets.Add(p);
            }
            return targets;
        }

        public static Vent GetNearestSprayVent(PlayerControl from, float maxRange)
        {
            if (MapUtilities.CachedShipStatus == null || MapUtilities.CachedShipStatus.AllVents == null) return null;
            Vent best = null;
            float bestDist = maxRange;
            Vector2 pos = from.transform.position;
            for (int i = 0; i < MapUtilities.CachedShipStatus.AllVents.Length; i++)
            {
                Vent vent = MapUtilities.CachedShipStatus.AllVents[i];
                if (vent == null) continue;
                float dist = Vector2.Distance(vent.transform.position, pos);
                if (dist <= bestDist)
                {
                    bestDist = dist;
                    best = vent;
                }
            }
            return best;
        }

        public static RemoteProcess<byte> StartSprayRpc = RemotePrimitiveProcess.OfByte("PermafrostStartSpray", (casterId, _) =>
        {
            var role = Permafrost.getRole(Helpers.playerById(casterId));
            if (role != null) role.isSpraying = true;
        });

        public static RemoteProcess<byte> StopSprayRpc = RemotePrimitiveProcess.OfByte("PermafrostStopSpray", (casterId, _) =>
        {
            var role = Permafrost.getRole(Helpers.playerById(casterId));
            if (role != null) role.isSpraying = false;
        });

        public static RemoteProcess<(byte casterId, float x, float y)> SpawnBlockRpc = new("PermafrostSpawnBlock", (m, _) =>
        {
            if (IceBlock.blocks.Count >= maxActiveBlocks) return;
            IceBlock.Create(new Vector2(m.x, m.y));
        });

        public static RemoteProcess<int> SealVentRpc = RemotePrimitiveProcess.OfInteger("PermafrostSealVent", (ventId, _) =>
        {
            frozenVents[ventId] = DateTime.UtcNow.AddSeconds(ventFreezeDuration);
        });

        public static RemoteProcess<(byte blockId, byte playerId)> TouchBlockRpc = new("PermafrostTouchBlock", (m, _) =>
        {
            IceBlock.slowUntil[m.playerId] = DateTime.UtcNow.AddSeconds(blockSlowDuration);
            if (blockBreakable && IceBlock.blocks.TryGetValue(m.blockId, out var block))
            {
                if (block.creditedPlayers.Add(m.playerId) && block.creditedPlayers.Count >= blockBreakHits)
                    block.Shatter();
            }
        });

        public static RemoteProcess<byte> FreezeTargetRpc = RemotePrimitiveProcess.OfByte("PermafrostFreezeTarget", (targetId, _) =>
        {
            frozenUntil[targetId] = DateTime.UtcNow.AddSeconds(freezeDuration);
        });

        private static Sprite sprayButtonSprite;
        public static Sprite getSprayButtonSprite()
        {
            if (sprayButtonSprite) return sprayButtonSprite;
            sprayButtonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.PermafrostSprayButton.png", 115f);
            return sprayButtonSprite;
        }

        private static Sprite frostMarkSprite;
        private static Sprite getFrostMarkSprite()
        {
            if (frostMarkSprite) return frostMarkSprite;
            frostMarkSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.PermafrostFrostMark.png", 100f);
            return frostMarkSprite;
        }

        private static Sprite particleSprite;
        private static Sprite getParticleSprite()
        {
            if (particleSprite) return particleSprite;
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float r = size / 2f;
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = x + 0.5f - r;
                    float dy = y + 0.5f - r;
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((r - d) / (r * 0.65f));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, a);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply();
            particleSprite = Sprite.Create(tex, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f), 100f);
            return particleSprite;
        }

        private class FrostParticle
        {
            public GameObject obj;
            public SpriteRenderer renderer;
            public Vector2 velocity;
            public float life;
            public float maxLife;
            public float startScale;
            public float startAlpha;
        }

        private static readonly List<FrostParticle> activeParticles = new();
        private static float particleSpawnAccumulator;
        private const int ParticlesPerTick = 3;
        private const float ParticleSpawnInterval = 0.018f;

        private static void SpawnParticle(Vector2 origin, Vector2? aim)
        {
            Vector2 dir;
            if (aim.HasValue && (aim.Value - origin).sqrMagnitude > 0.01f)
                dir = (aim.Value - origin).normalized;
            else
                dir = UnityEngine.Random.insideUnitCircle.normalized;

            Vector2 perp = new(-dir.y, dir.x);
            float spread = UnityEngine.Random.Range(-0.4f, 0.4f);
            Vector2 launchDir = (dir + perp * spread).normalized;
            Vector2 spawnOffset = dir * UnityEngine.Random.Range(0.1f, 0.35f) + perp * UnityEngine.Random.Range(-0.2f, 0.2f);

            var obj = new GameObject("PermafrostParticle");
            Vector3 pos = new(origin.x + spawnOffset.x, origin.y + 0.35f + spawnOffset.y, (origin.y + 0.35f) / 1000f + 0.03f);
            obj.transform.position = pos;

            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = getParticleSprite();
            bool isPuff = UnityEngine.Random.value < 0.3f;
            float startAlpha = isPuff ? 0.7f : 1f;
            renderer.color = isPuff ? new Color(0.88f, 0.97f, 1f, startAlpha) : new Color(0.55f, 0.87f, 1f, startAlpha);

            activeParticles.Add(new FrostParticle
            {
                obj = obj,
                renderer = renderer,
                velocity = launchDir * UnityEngine.Random.Range(2.4f, 4f),
                life = 0f,
                maxLife = UnityEngine.Random.Range(0.3f, 0.48f),
                startScale = isPuff ? UnityEngine.Random.Range(0.4f, 0.6f) : UnityEngine.Random.Range(0.2f, 0.32f),
                startAlpha = startAlpha
            });
        }

        private static void UpdateParticles()
        {
            particleSpawnAccumulator += Time.deltaTime;
            while (particleSpawnAccumulator >= ParticleSpawnInterval)
            {
                particleSpawnAccumulator -= ParticleSpawnInterval;
                foreach (var caster in Permafrost.players)
                {
                    if (!caster.isSpraying || caster.player == null) continue;
                    if (caster.player.Data == null || caster.player.Data.IsDead || !caster.player.Visible) continue;

                    Vector2 origin = caster.player.transform.position;
                    Vector2? aim = null;
                    var sprayTarget = GetNearestSprayTarget(caster.player, sprayRange);
                    if (sprayTarget != null) aim = sprayTarget.transform.position;
                    else
                    {
                        var vent = GetNearestSprayVent(caster.player, sprayRange);
                        if (vent != null) aim = vent.transform.position;
                    }

                    for (int i = 0; i < ParticlesPerTick; i++)
                        SpawnParticle(origin, aim);
                }
            }

            for (int i = activeParticles.Count - 1; i >= 0; i--)
            {
                var particle = activeParticles[i];
                particle.life += Time.deltaTime;
                if (particle.obj == null || particle.life >= particle.maxLife)
                {
                    if (particle.obj != null) UnityEngine.Object.Destroy(particle.obj);
                    activeParticles.RemoveAt(i);
                    continue;
                }
                float t = particle.life / particle.maxLife;
                particle.obj.transform.position += (Vector3)(particle.velocity * Time.deltaTime);
                particle.obj.transform.localScale = Vector3.one * Mathf.Lerp(particle.startScale, particle.startScale * 0.2f, t);
                Color c = particle.renderer.color;
                c.a = Mathf.Lerp(particle.startAlpha, 0f, t);
                particle.renderer.color = c;
            }
        }

        private class FrostBubble
        {
            public PlayerControl target;
            public RectTransform rect;
            public CanvasGroup group;
            public float lastActiveTime;
        }

        private static GameObject frostBubbleLayerRoot;
        private static readonly Dictionary<byte, FrostBubble> frostBubbles = new();

        private static void EnsureFrostBubbleLayer()
        {
            if (frostBubbleLayerRoot != null) return;
            frostBubbleLayerRoot = new GameObject("PermafrostFrostMarkLayer");
            frostBubbleLayerRoot.layer = LayerMask.NameToLayer("UI");
            var canvas = frostBubbleLayerRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 8990;
        }

        private static FrostBubble CreateFrostBubble(PlayerControl target)
        {
            EnsureFrostBubbleLayer();
            var obj = new GameObject("PermafrostFrostMark");
            obj.transform.SetParent(frostBubbleLayerRoot.transform, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(FrostMarkSize, FrostMarkSize);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var group = obj.AddComponent<CanvasGroup>();

            var iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(obj.transform, false);
            var iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(FrostMarkSize, FrostMarkSize);
            var image = iconObj.AddComponent<Image>();
            image.sprite = getFrostMarkSprite();
            image.raycastTarget = false;

            return new FrostBubble { target = target, rect = rect, group = group, lastActiveTime = Time.unscaledTime };
        }

        private static void UpdateFrostMarks()
        {
            var now = DateTime.UtcNow;
            foreach (PlayerControl p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p.Data == null || p.Data.IsDead) continue;

                bool inLiveSpray = false;
                foreach (var caster in Permafrost.players)
                {
                    if (!caster.isSpraying || caster.player == null || caster.player == p) continue;
                    if (caster.player.Data == null || caster.player.Data.IsDead) continue;
                    if (caster.player.Data.Role.IsImpostor && p.Data.Role.IsImpostor) continue;
                    if (Vector2.Distance(caster.player.transform.position, p.transform.position) <= sprayRange)
                    {
                        inLiveSpray = true;
                        break;
                    }
                }

                bool blockSlowed = IceBlock.slowUntil.TryGetValue(p.PlayerId, out var until) && now < until;

                if (inLiveSpray || blockSlowed)
                {
                    if (!frostBubbles.TryGetValue(p.PlayerId, out var bubble) || bubble.rect == null)
                        frostBubbles[p.PlayerId] = bubble = CreateFrostBubble(p);
                    bubble.lastActiveTime = Time.unscaledTime;
                }

                if (p == PlayerControl.LocalPlayer)
                    fogTargetActive = inLiveSpray || blockSlowed;
            }

            List<byte> toRemove = null;
            foreach (var kv in frostBubbles)
            {
                var bubble = kv.Value;
                if (bubble.rect == null)
                {
                    (toRemove ??= new()).Add(kv.Key);
                    continue;
                }

                float sinceActive = Time.unscaledTime - bubble.lastActiveTime;
                if (sinceActive > FrostMarkLinger)
                {
                    UnityEngine.Object.Destroy(bubble.rect.gameObject);
                    (toRemove ??= new()).Add(kv.Key);
                    continue;
                }

                var target = bubble.target;
                bool visible = target != null && target.Visible && Camera.main != null;
                if (visible)
                {
                    Vector3 worldPos = target.transform.position + new Vector3(0f, 0.75f, 0f);
                    Vector3 screenPos = Camera.main.WorldToScreenPoint(worldPos);
                    visible = screenPos.z > 0f;
                    if (visible) bubble.rect.position = screenPos;
                }

                bubble.group.alpha = visible ? Mathf.Clamp01(1f - sinceActive / FrostMarkLinger) : 0f;
            }
            if (toRemove != null)
                foreach (var key in toRemove) frostBubbles.Remove(key);
        }

        private class FrozenOverlay
        {
            public RectTransform rect;
            public CanvasGroup group;
        }

        private static readonly Dictionary<byte, FrozenOverlay> frozenOverlays = new();

        private static FrozenOverlay CreateFrozenOverlay()
        {
            EnsureFrostBubbleLayer();
            var obj = new GameObject("PermafrostFrozenOverlay");
            obj.transform.SetParent(frostBubbleLayerRoot.transform, false);
            var rect = obj.AddComponent<RectTransform>();
            rect.sizeDelta = new Vector2(FrozenOverlaySize, FrozenOverlaySize);
            rect.pivot = new Vector2(0.5f, 0.5f);
            var group = obj.AddComponent<CanvasGroup>();

            var iconObj = new GameObject("Icon");
            iconObj.transform.SetParent(obj.transform, false);
            var iconRect = iconObj.AddComponent<RectTransform>();
            iconRect.anchorMin = iconRect.anchorMax = new Vector2(0.5f, 0.5f);
            iconRect.pivot = new Vector2(0.5f, 0.5f);
            iconRect.sizeDelta = new Vector2(FrozenOverlaySize, FrozenOverlaySize);
            var image = iconObj.AddComponent<Image>();
            image.sprite = IceBlock.getIceBlockSprite();
            image.raycastTarget = false;

            return new FrozenOverlay { rect = rect, group = group };
        }

        private static void UpdateFrozenOverlays()
        {
            var now = DateTime.UtcNow;
            foreach (PlayerControl p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p.Data == null) continue;
                bool frozen = frozenUntil.TryGetValue(p.PlayerId, out var until) && now < until;

                if (!frozen)
                {
                    if (frozenOverlays.TryGetValue(p.PlayerId, out var stale) && stale.rect != null)
                        UnityEngine.Object.Destroy(stale.rect.gameObject);
                    frozenOverlays.Remove(p.PlayerId);
                    continue;
                }

                if (!frozenOverlays.TryGetValue(p.PlayerId, out var overlay) || overlay.rect == null)
                    frozenOverlays[p.PlayerId] = overlay = CreateFrozenOverlay();

                bool visible = p.Visible && Camera.main != null;
                if (visible)
                {
                    Vector3 screenPos = Camera.main.WorldToScreenPoint(p.transform.position);
                    visible = screenPos.z > 0f;
                    if (visible) overlay.rect.position = screenPos;
                }
                overlay.group.alpha = visible ? 1f : 0f;

                if (p == PlayerControl.LocalPlayer)
                    fogTargetActive = true;
            }
        }

        private static GameObject fogOverlayRoot;
        private static Image fogOverlayImage;
        private static bool fogTargetActive;
        private static float fogAlpha;
        private const float FogMaxAlpha = 0.42f;
        private const float FogFadeSpeed = 3f;

        private static void EnsureFogOverlay()
        {
            if (fogOverlayRoot != null) return;
            fogOverlayRoot = new GameObject("PermafrostFogOverlay");
            fogOverlayRoot.layer = LayerMask.NameToLayer("UI");
            var canvas = fogOverlayRoot.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 9500;

            var imgObj = new GameObject("Fog");
            imgObj.transform.SetParent(fogOverlayRoot.transform, false);
            var rect = imgObj.AddComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            fogOverlayImage = imgObj.AddComponent<Image>();
            fogOverlayImage.raycastTarget = false;
            fogOverlayImage.color = new Color(0.82f, 0.94f, 1f, 0f);
        }

        private static void UpdateLocalFog()
        {
            EnsureFogOverlay();
            float target = fogTargetActive ? FogMaxAlpha : 0f;
            fogAlpha = Mathf.MoveTowards(fogAlpha, target, FogFadeSpeed * Time.deltaTime);
            Color c = fogOverlayImage.color;
            c.a = fogAlpha;
            fogOverlayImage.color = c;
        }

        private static void ClearVisuals()
        {
            foreach (var particle in activeParticles)
                if (particle.obj != null) UnityEngine.Object.Destroy(particle.obj);
            activeParticles.Clear();

            foreach (var bubble in frostBubbles.Values)
                if (bubble.rect != null) UnityEngine.Object.Destroy(bubble.rect.gameObject);
            frostBubbles.Clear();

            foreach (var overlay in frozenOverlays.Values)
                if (overlay.rect != null) UnityEngine.Object.Destroy(overlay.rect.gameObject);
            frozenOverlays.Clear();

            fogTargetActive = false;
            fogAlpha = 0f;
            if (fogOverlayImage != null)
            {
                Color c = fogOverlayImage.color;
                c.a = 0f;
                fogOverlayImage.color = c;
            }
        }

        [HarmonyPatch(typeof(TORGUIManager), nameof(TORGUIManager.Update))]
        private static class PermafrostVisualPatch
        {
            private static void Postfix()
            {
                IceBlock.ExpireOld(blockLifetime);
                IceBlock.Tick();
                UpdateParticles();
                UpdateFrostMarks();
                UpdateFrozenOverlays();
                UpdateLocalFog();
            }
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
        private static class PermafrostPhysicsPatch
        {
            public static void Postfix(PlayerPhysics __instance)
            {
                var target = __instance.myPlayer;
                if (target == null || target.Data == null || target.Data.IsDead) return;
                if (!__instance.AmOwner || !target.CanMove) return;

                bool inLiveSpray = false;
                foreach (var caster in Permafrost.players)
                {
                    if (!caster.isSpraying || caster.player == null || caster.player == target) continue;
                    if (caster.player.Data == null || caster.player.Data.IsDead) continue;
                    if (caster.player.Data.Role.IsImpostor && target.Data.Role.IsImpostor) continue;
                    if (Vector2.Distance(caster.player.transform.position, target.transform.position) <= sprayRange)
                    {
                        inLiveSpray = true;
                        break;
                    }
                }
                if (inLiveSpray) __instance.body.velocity *= (slowFactor + 1f);

                if (IceBlock.slowUntil.TryGetValue(target.PlayerId, out var until) && DateTime.UtcNow < until)
                    __instance.body.velocity *= (blockSlowFactor + 1f);

                if (frozenUntil.TryGetValue(target.PlayerId, out var frozenUntilTime) && DateTime.UtcNow < frozenUntilTime)
                    __instance.body.velocity = Vector2.zero;

                foreach (var block in IceBlock.blocks.Values)
                {
                    if (!block.isMature) continue;
                    if (Vector2.Distance(block.position, target.transform.position) > IceBlock.TouchRadius) continue;
                    bool alreadySlowed = IceBlock.slowUntil.TryGetValue(target.PlayerId, out var currentExpiry) && DateTime.UtcNow < currentExpiry;
                    if (alreadySlowed) continue;
                    TouchBlockRpc.Invoke((block.id, target.PlayerId));
                }
            }
        }

        [HarmonyPatch(typeof(Vent), nameof(Vent.CanUse))]
        private static class PermafrostVentFreezePatch
        {
            public static void Postfix(Vent __instance, ref float __result, [HarmonyArgument(1)] ref bool canUse, [HarmonyArgument(2)] ref bool couldUse)
            {
                if (!frozenVents.TryGetValue(__instance.Id, out var expiry)) return;
                if (DateTime.UtcNow >= expiry)
                {
                    frozenVents.Remove(__instance.Id);
                    return;
                }
                canUse = couldUse = false;
                __result = float.MaxValue;
            }
        }

        private static bool IsSprayPassiveButton(PassiveUiElement instance)
        {
            var button = HudManagerStartPatch.permafrostSprayButton;
            return button?.actionButton != null && instance == button.actionButton.GetComponent<PassiveButton>();
        }

        [HarmonyPatch(typeof(PassiveUiElement), nameof(PassiveUiElement.ReceiveClickDown))]
        private static class PermafrostSprayButtonDownPatch
        {
            public static void Postfix(PassiveUiElement __instance)
            {
                if (IsSprayPassiveButton(__instance)) sprayButtonHeld = true;
            }
        }

        [HarmonyPatch(typeof(PassiveUiElement), nameof(PassiveUiElement.ReceiveClickUp))]
        private static class PermafrostSprayButtonUpPatch
        {
            public static void Postfix(PassiveUiElement __instance)
            {
                if (IsSprayPassiveButton(__instance)) sprayButtonHeld = false;
            }
        }

        [HarmonyPatch(typeof(PassiveUiElement), nameof(PassiveUiElement.ReleaseButton))]
        private static class PermafrostSprayButtonReleasePatch
        {
            public static void Postfix(PassiveUiElement __instance)
            {
                if (IsSprayPassiveButton(__instance)) sprayButtonHeld = false;
            }
        }
    }
}
