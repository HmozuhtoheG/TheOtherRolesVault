using System;
using System.Collections.Generic;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Patches;
using TheOtherRoles.Objects;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.Patches.PlayerControlFixedUpdatePatch;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Itadori : RoleBase<Itadori>
    {
        public static Color color = new Color32(196, 90, 120, byte.MaxValue);

        public static int fingerCount = 20;
        public static float eatCooldown = 5f;
        public static float killCooldown = 45f;
        public static float eatRange = 1.6f;

        public int fingersEaten;
        public bool usedOneTimeKill;
        public bool killedImpostor;
        public bool killedCrewmate;
        public bool becameSukuna;

        public static CustomButton eatButton;
        public static CustomButton judgeButton;
        private static Sprite eatSprite;
        private static Sprite judgeSprite;
        private static AudioClip laughClip;
        private static AudioSource laughSource;

        public bool crewWinEarned => killedImpostor && !killedCrewmate;

        public Itadori()
        {
            RoleId = roleId = RoleId.Itadori;
            fingersEaten = 0;
            usedOneTimeKill = false;
            killedImpostor = false;
            killedCrewmate = false;
            becameSukuna = false;
        }

        public static Sprite getEatSprite()
        {
            if (eatSprite) return eatSprite;
            eatSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.SukunaFinger.png", 115f);
            return eatSprite;
        }

        public static Sprite getJudgeSprite()
        {
            if (judgeSprite) return judgeSprite;
            judgeSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Sukuna.png", 115f);
            return judgeSprite;
        }

        private static AudioClip getLaughClip()
        {
            if (laughClip != null) return laughClip;
            laughClip = Helpers.loadWavFromResources("TheOtherRoles.Resources.SukunaLough.wav", "TORV_ItadoriLaugh");
            if (laughClip != null) laughClip.MarkDontUnload();
            return laughClip;
        }

        private static void playLaugh()
        {
            if (!Constants.ShouldPlaySfx()) return;

            var clip = getLaughClip();
            if (clip == null) return;

            if (laughSource == null)
            {
                var hud = FastDestroyableSingleton<HudManager>.Instance;
                if (hud == null) return;
                laughSource = Helpers.CreateObject<AudioSource>("ItadoriLaughSource", hud.transform, Vector3.zero);
                laughSource.playOnAwake = false;
                laughSource.spatialBlend = 0f;
                if (SoundManager.Instance != null && SoundManager.Instance.SfxChannel != null)
                    laughSource.outputAudioMixerGroup = SoundManager.Instance.SfxChannel;
            }

            laughSource.PlayOneShot(clip, 0.8f);
        }

        public class Finger
        {
            public GameObject Holder;
            public Vector2 Position;
            public bool Eaten;
        }

        public static readonly List<Finger> fingers = new();

        public static RemoteProcess<(float x, float y)> PlaceFinger = new("ItadoriPlaceFinger", (message, _) =>
        {
            var sprite = getEatSprite();
            if (sprite == null) return;

            var holder = new GameObject("SukunaFinger");
            holder.transform.position = new Vector3(message.x, message.y, message.y / 1000f + 0.002f);
            holder.AddSubmergedComponent(SubmergedCompatibility.Classes.ElevatorMover);

            var renderer = holder.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite;
            renderer.sortingOrder = 6;

            fingers.Add(new Finger { Holder = holder, Position = new Vector2(message.x, message.y) });
        });

        public static RemoteProcess<(byte index, byte eaterId)> EatFinger = new("ItadoriEatFinger", (message, _) =>
        {
            if (message.index >= fingers.Count) return;

            var finger = fingers[message.index];
            if (finger.Eaten) return;

            finger.Eaten = true;
            if (finger.Holder != null) UnityEngine.Object.Destroy(finger.Holder);

            var eater = Helpers.playerById(message.eaterId);
            var role = getRole(eater);
            if (role == null || role.becameSukuna) return;

            role.fingersEaten++;
            if (role.fingersEaten >= fingerCount) BecomeSukuna.Invoke(eater.PlayerId);
        });

        public static RemoteProcess<byte> BecomeSukuna = RemotePrimitiveProcess.OfByte("ItadoriBecomeSukuna", (message, _) =>
        {
            var player = Helpers.playerById(message);
            var role = getRole(player);
            if (role == null || role.becameSukuna) return;

            role.becameSukuna = true;
            playLaugh();

            eraseRole(player);
            Sukuna.setRole(player);

            if (player == PlayerControl.LocalPlayer)
                new CustomMessage(ModTranslation.getString("itadoriSukunaAnnounce"), 4f);
        });

        public static void SpawnFingers()
        {
            if (!AmongUsClient.Instance.AmHost) return;
            if (fingers.Count > 0) return;

            var used = new List<Vector2>();
            for (int i = 0; i < fingerCount; i++)
            {
                var point = pickSpawnPoint(used);
                used.Add(point);
                PlaceFinger.Invoke((point.x, point.y));
            }

            TheOtherRolesPlugin.Logger.LogInfo($"[Itadori] spawned {fingers.Count} finger(s)");
        }

        public static bool blockedByWorld(Vector2 point)
        {
            try
            {
                var hits = Physics2D.OverlapPointAll(point, Constants.ShipAndObjectsMask);
                if (hits == null) return false;

                foreach (var hit in hits)
                {
                    if (hit == null || hit.isTrigger) continue;
                    return true;
                }
            }
            catch (Exception ex)
            {
                TheOtherRolesPlugin.Logger.LogWarning($"[Itadori] overlap check failed: {ex.Message}");
            }

            return false;
        }

        private static Vector2 pickSpawnPoint(List<Vector2> used)
        {
            var ship = MapUtilities.CachedShipStatus;
            if (ship == null || ship.FastRooms == null)
            {
                var player = PlayerControl.LocalPlayer;
                return player != null ? (Vector2)player.transform.position : Vector2.zero;
            }

            var rooms = new List<PlainShipRoom>();
            foreach (var room in ship.FastRooms)
            {
                if (room.Value != null && room.Value.roomArea != null) rooms.Add(room.Value);
            }
            if (rooms.Count == 0) return Vector2.zero;

            for (int attempt = 0; attempt < 30; attempt++)
            {
                var room = rooms[rnd.Next(rooms.Count)];
                var bounds = room.roomArea.bounds;

                float x = bounds.min.x + (float)rnd.NextDouble() * (bounds.max.x - bounds.min.x);
                float y = bounds.min.y + (float)rnd.NextDouble() * (bounds.max.y - bounds.min.y);
                var point = new Vector2(x, y);

                if (!room.roomArea.OverlapPoint(point)) continue;
                if (blockedByWorld(point)) continue;

                bool taken = false;
                foreach (var other in used)
                {
                    if (Vector2.Distance(other, point) < 1f) { taken = true; break; }
                }
                if (!taken) return point;
            }

            return rooms[rnd.Next(rooms.Count)].roomArea.bounds.center;
        }

        public static void ClearFingers()
        {
            foreach (var finger in fingers)
            {
                if (finger.Holder != null) UnityEngine.Object.Destroy(finger.Holder);
            }
            fingers.Clear();
        }

        private static int nearestFingerIndex(PlayerControl player)
        {
            if (player == null) return -1;

            Vector2 origin = player.transform.position;
            int best = -1;
            float bestDistance = eatRange;

            for (int i = 0; i < fingers.Count; i++)
            {
                if (fingers[i].Eaten) continue;
                float distance = Vector2.Distance(origin, fingers[i].Position);
                if (distance > bestDistance) continue;
                bestDistance = distance;
                best = i;
            }

            return best;
        }

        private void eat()
        {
            int index = nearestFingerIndex(player);
            if (index < 0) return;

            EatFinger.Invoke(((byte)index, player.PlayerId));
        }

        private void judge()
        {
            if (usedOneTimeKill) return;

            var target = setTarget();
            if (target == null) return;

            usedOneTimeKill = true;

            if (target.Data != null && target.Data.Role.IsImpostor) killedImpostor = true;
            else killedCrewmate = true;

            Helpers.forceMurderPlayer(player, target, true);

            if (judgeButton != null)
            {
                judgeButton.MaxTimer = killCooldown;
                judgeButton.Timer = judgeButton.MaxTimer;
            }
        }

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;

            eatButton = new CustomButton(
                eat,
                () => PlayerControl.LocalPlayer.isRole(RoleId.Itadori) && !becameSukuna,
                () => player.CanMove && !MeetingHud.Instance && !Minigame.Instance && nearestFingerIndex(player) >= 0,
                () => { eatButton.MaxTimer = eatCooldown; eatButton.Timer = eatButton.MaxTimer; },
                getEatSprite(),
                CustomButton.ButtonPositions.upperRowLeft,
                HudManager.Instance,
                KeyCode.F,
                buttonText: ModTranslation.getString("itadoriEatText"),
                abilityTexture: CustomButton.ButtonLabelType.UseButton
            );

            eatButton.MaxTimer = eatCooldown;
            eatButton.Timer = eatCooldown;

            judgeButton = new CustomButton(
                judge,
                () => PlayerControl.LocalPlayer.isRole(RoleId.Itadori) && !becameSukuna && !usedOneTimeKill,
                () => player.CanMove && !MeetingHud.Instance && !Minigame.Instance && setTarget() != null,
                () => { judgeButton.MaxTimer = killCooldown; judgeButton.Timer = judgeButton.MaxTimer; },
                getJudgeSprite(),
                CustomButton.ButtonPositions.lowerRowRight,
                HudManager.Instance,
                KeyCode.G,
                buttonText: ModTranslation.getString("itadoriJudgeText"),
                abilityTexture: CustomButton.ButtonLabelType.KillButton
            );

            judgeButton.MaxTimer = killCooldown;
            judgeButton.Timer = killCooldown;
        }

        public override void ResetRole(bool isShifted)
        {
            if (player != PlayerControl.LocalPlayer) return;
            destroyUi();
        }

        private static void destroyUi()
        {
            if (eatButton != null)
            {
                UnityEngine.Object.Destroy(eatButton.actionButtonGameObject);
                eatButton = null;
            }
            if (judgeButton != null)
            {
                UnityEngine.Object.Destroy(judgeButton.actionButtonGameObject);
                judgeButton = null;
            }
        }

        public static void clearAndReload()
        {
            fingerCount = Mathf.RoundToInt(CustomOptionHolder.itadoriFingerCount.getFloat());
            eatCooldown = CustomOptionHolder.itadoriEatCooldown.getFloat();
            killCooldown = CustomOptionHolder.itadoriKillCooldown.getFloat();
            eatRange = CustomOptionHolder.itadoriEatRange.getFloat();

            ClearFingers();
            destroyUi();
            players = [];
        }

        static public IEnumerable<HelpSprite> GetHelpSprites()
        {
            yield return new(getEatSprite(), "itadoriEatText");
            yield return new(getJudgeSprite(), "itadoriJudgeText");
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%FINGERS%", fingerCount.ToString());
            yield return new("%EATCD%", eatCooldown.ToString());
            yield return new("%KILLCD%", killCooldown.ToString());
        }
    }
}
