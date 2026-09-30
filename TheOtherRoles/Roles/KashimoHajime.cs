using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheOtherRoles.Patches;
using TheOtherRoles.Modules;
using UnityEngine;
using static TheOtherRoles.Patches.PlayerControlFixedUpdatePatch;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class KashimoHajime : RoleBase<KashimoHajime>
    {
        public static Color color = new Color32(158, 224, 255, byte.MaxValue);

        public static float chargeCooldown = 25f;
        public static float chargeDuration = 4f;
        public static float chargeRadius = 2.5f;
        public static float shockDuration = 2f;
        public static float amberDuration = 8f;
        public static float amberSpeedMultiplier = 2f;
        public static float killCooldown = 30f;

        public PlayerControl currentTarget;
        public int kills;
        public int amberKills;
        public bool isCharged;
        public float chargeTimer;
        public bool isAmber;
        public float amberTimer;
        public bool usedAmber;

        private float nextLightning;

        private static readonly Dictionary<byte, float> frozenUntil = [];
        private readonly HashSet<byte> shockedThisCharge = [];

        private static Sprite chargeSprite;
        private static Sprite amberSprite;

        public KashimoHajime()
        {
            RoleId = roleId = RoleId.KashimoHajime;
            currentTarget = null;
            kills = 0;
            amberKills = 0;
            isCharged = false;
            chargeTimer = 0f;
            isAmber = false;
            amberTimer = 0f;
            usedAmber = false;
        }

        public static Sprite getChargeSprite()
        {
            if (chargeSprite) return chargeSprite;
            chargeSprite = BuildBoltSprite(false);
            return chargeSprite;
        }

        public static Sprite getAmberSprite()
        {
            if (amberSprite) return amberSprite;
            amberSprite = BuildBoltSprite(true);
            return amberSprite;
        }

        private static Sprite BuildBoltSprite(bool withAura)
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            var aura = new Color(0.42f, 0.86f, 1f, 0.30f);
            var bolt = new Color(1f, 1f, 1f, 1f);
            var auraCenter = new Vector2(size * 0.5f, size * 0.5f);
            float auraRadius = size * 0.46f;

            Vector2[] outline =
            {
                new(0.60f, 1.00f), new(0.28f, 0.52f), new(0.46f, 0.52f),
                new(0.32f, 0.00f), new(0.72f, 0.50f), new(0.52f, 0.50f),
            };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float u = x / (float)(size - 1);
                    float v = y / (float)(size - 1);

                    if (InsidePolygon(outline, u, v))
                    {
                        texture.SetPixel(x, y, bolt);
                        continue;
                    }

                    if (!withAura) continue;

                    float distance = Vector2.Distance(new Vector2(x, y), auraCenter);
                    if (distance > auraRadius) continue;

                    float fade = 1f - distance / auraRadius;
                    texture.SetPixel(x, y, new Color(aura.r, aura.g, aura.b, aura.a * fade * fade));
                }
            }

            texture.Apply();
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }

        private static bool InsidePolygon(Vector2[] polygon, float x, float y)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                if ((polygon[i].y > y) == (polygon[j].y > y)) continue;
                float cross = (polygon[j].x - polygon[i].x) * (y - polygon[i].y) / (polygon[j].y - polygon[i].y) + polygon[i].x;
                if (x < cross) inside = !inside;
            }
            return inside;
        }

        public static RemoteProcess<byte> Shock = RemotePrimitiveProcess.OfByte("KashimoShock", (targetId, __) =>
        {
            var target = Helpers.playerById(targetId);
            if (target == null || target.Data == null || target.Data.IsDead) return;

            frozenUntil[targetId] = Time.time + shockDuration;
            SpawnLightning(target.transform.position);
        });

        public static RemoteProcess<(byte kashimoId, float duration)> AmberStart = new("KashimoAmberStart", (message, __) =>
        {
            var kashimo = getRole(Helpers.playerById(message.kashimoId));
            if (kashimo == null || kashimo.player == null) return;

            kashimo.isAmber = true;
            kashimo.amberTimer = message.duration;
            kashimo.amberKills = 0;
            kashimo.nextLightning = 0.35f;
            SpawnLightning(kashimo.player.transform.position);
        });

        public static RemoteProcess<byte> AmberEnd = RemotePrimitiveProcess.OfByte("KashimoAmberEnd", (kashimoId, __) =>
        {
            var kashimo = getRole(Helpers.playerById(kashimoId));
            if (kashimo == null || kashimo.player == null) return;
            if (kashimo.player.Data == null || kashimo.player.Data.IsDead) return;

            kashimo.isAmber = false;
            kashimo.amberTimer = 0f;
            SpawnLightning(kashimo.player.transform.position);
            kashimo.player.MurderPlayer(kashimo.player, MurderResultFlags.Succeeded);
        });

        public static bool isFrozen(PlayerControl target)
        {
            return target != null && frozenUntil.TryGetValue(target.PlayerId, out float until) && Time.time < until;
        }

        public bool canCharge => player != null && player.Data != null && !player.Data.IsDead
            && !isCharged && !isAmber && !MeetingHud.Instance && !ExileController.Instance;

        public bool canAmber => player != null && player.Data != null && !player.Data.IsDead
            && !usedAmber && !isAmber && !isCharged && !MeetingHud.Instance && !ExileController.Instance;

        public void useCharge()
        {
            if (player != PlayerControl.LocalPlayer || !canCharge) return;

            isCharged = true;
            chargeTimer = chargeDuration;
            shockedThisCharge.Clear();
        }

        public void useAmber()
        {
            if (player != PlayerControl.LocalPlayer || !canAmber) return;

            usedAmber = true;
            AmberStart.Invoke((player.PlayerId, amberDuration));
        }

        private void updateCharge()
        {
            if (!isCharged) return;

            chargeTimer -= Time.fixedDeltaTime;
            if (chargeTimer <= 0f)
            {
                isCharged = false;
                chargeTimer = 0f;
                shockedThisCharge.Clear();
                return;
            }

            Vector2 origin = player.GetTruePosition();
            foreach (PlayerControl p in PlayerControl.AllPlayerControls)
            {
                if (p == null || p.Data == null || p.Data.IsDead || p == player) continue;
                if (shockedThisCharge.Contains(p.PlayerId)) continue;
                if (Vector2.Distance(p.GetTruePosition(), origin) > chargeRadius) continue;

                shockedThisCharge.Add(p.PlayerId);
                Shock.Invoke(p.PlayerId);
            }

            if (shockedThisCharge.Count > 0)
                _ = new StaticAchievementToken("kashimoHajime.common1");
        }

        public bool canKill => currentTarget != null && currentTarget.Data != null && !currentTarget.Data.IsDead
            && !isCharged && PlayerControl.LocalPlayer.CanMove && !MeetingHud.Instance;

        public void kill()
        {
            var target = currentTarget;
            if (target == null) return;

            if (Helpers.checkMurderAttemptAndKill(player, target) == MurderAttemptResult.SuppressKill) return;

            currentTarget = null;
        }

        private void updateAmber()
        {
            if (!isAmber) return;

            amberTimer -= Time.fixedDeltaTime;

            var killButton = HudManagerStartPatch.kashimoKillButton;
            if (killButton != null && killButton.Timer > 0f) killButton.Timer = 0f;

            if (amberTimer <= 0f)
            {
                amberTimer = 0f;
                AmberEnd.Invoke(player.PlayerId);
                return;
            }

            nextLightning -= Time.fixedDeltaTime;
            if (nextLightning > 0f) return;

            nextLightning = 0.35f;
            SpawnLightning(player.transform.position);
        }

        public override void OnKill(PlayerControl target)
        {
            if (target == null || target == player) return;

            kills++;

            if (isAmber)
            {
                amberKills++;
                if (player == PlayerControl.LocalPlayer)
                {
                    _ = new StaticAchievementToken("kashimoHajime.another1");
                    if (amberKills >= 2) _ = new StaticAchievementToken("kashimoHajime.challenge");
                }
            }

        }

        public static int countLovers()
        {
            int counter = 0;
            foreach (var player in allPlayers)
                if (player.isLovers()) counter += 1;
            return counter;
        }

        public override void FixedUpdate()
        {
            if (player == null || player.Data == null) return;
            if (player != PlayerControl.LocalPlayer) return;

            if (player.Data.IsDead)
            {
                ClearStates();
                return;
            }

            updateCharge();
            updateAmber();

            currentTarget = setTarget();
            setPlayerOutline(currentTarget, color);
        }

        public override void OnMeetingStart() => ClearStates();

        public override void OnDeath(PlayerControl killer = null) => ClearStates();

        private void ClearStates()
        {
            isCharged = false;
            chargeTimer = 0f;
            isAmber = false;
            amberTimer = 0f;
        }

        private static void SpawnLightning(Vector3 position)
        {
            var sprite = getChargeSprite();
            if (sprite == null) return;
            var hud = HudManager.Instance;
            if (hud == null) return;

            var obj = new GameObject("KashimoLightning") { layer = 5 };
            position.z = 0f;
            obj.transform.position = position;
            obj.transform.localScale = Vector3.one * 1.4f;
            obj.transform.rotation = Quaternion.Euler(0f, 0f, UnityEngine.Random.Range(0f, 360f));

            var renderer = obj.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.color = new Color(color.r, color.g, color.b, 0.95f);

            hud.StartCoroutine(Effects.Lerp(0.35f, new System.Action<float>((p) =>
            {
                if (renderer != null) renderer.color = new Color(color.r, color.g, color.b, 0.95f * (1f - p));
                if (p >= 1f && obj != null) UnityEngine.Object.Destroy(obj);
            })));
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", Mathf.RoundToInt(chargeCooldown).ToString());
            yield return new("%RAD%", chargeRadius.ToString("0.#"));
            yield return new("%SHOCK%", shockDuration.ToString("0.#"));
            yield return new("%AMBER%", amberDuration.ToString("0.#"));
        }

        public static void clearAndReload()
        {
            chargeCooldown = CustomOptionHolder.kashimoChargeCooldown.getFloat();
            chargeDuration = CustomOptionHolder.kashimoChargeDuration.getFloat();
            chargeRadius = CustomOptionHolder.kashimoChargeRadius.getFloat();
            shockDuration = CustomOptionHolder.kashimoShockDuration.getFloat();
            amberDuration = CustomOptionHolder.kashimoAmberDuration.getFloat();
            amberSpeedMultiplier = CustomOptionHolder.kashimoAmberSpeed.getFloat();
            killCooldown = CustomOptionHolder.kashimoKillCooldown.getFloat();

            frozenUntil.Clear();
            players = [];
        }

        [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
        public static class KashimoPatches
        {
            public static void Postfix(PlayerPhysics __instance)
            {
                var target = __instance.myPlayer;
                if (target == null || target.Data == null || target.Data.IsDead) return;
                if (!__instance.AmOwner || !target.CanMove) return;

                if (frozenUntil.TryGetValue(target.PlayerId, out float until))
                {
                    if (Time.time >= until) frozenUntil.Remove(target.PlayerId);
                    else
                    {
                        __instance.body.velocity = Vector2.zero;
                        return;
                    }
                }

                var kashimo = getRole(target);
                if (kashimo != null && kashimo.isAmber) __instance.body.velocity *= amberSpeedMultiplier;
            }
        }
    }
}
