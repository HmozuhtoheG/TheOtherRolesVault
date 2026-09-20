using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Illusionist : RoleBase<Illusionist>
    {
        public static Color color = Palette.ImpostorRed;

        public static float cooldown = 25f;
        public static int maxUses = 3;
        public static float triggerRadius = 1.5f;
        public static float useDistance = 1.5f;
        public static float decoyOffset = 0.55f;

        public int usesLeft = 3;
        public bool decoyMode = false;

        public static CustomButton illusionButton;
        public static TMPro.TMP_Text usesText = null;
        private static Sprite hideSprite;
        private static Sprite decoySprite;

        public static MetaContext.Image RoleIcon = SpriteLoader.FromResource("TheOtherRoles.Resources.CleanButton.png", 115f);
        public static MetaContext.Image Illustration = SpriteLoader.FromResource("TheOtherRoles.Resources.VultureButton.png", 115f);

        public class Illusion
        {
            public byte ownerId;
            public byte victimId;
            public byte decoyId;
            public bool isDecoy;
            public bool spent;
            public Vector2 position;
            public DeadBody body;
            public PlayerDisplay display;
        }

        public static List<Illusion> active = [];

        public Illusionist()
        {
            RoleId = roleId = RoleId.Illusionist;
            usesLeft = maxUses;
            decoyMode = false;
        }

        public static Sprite getHideSprite()
        {
            if (hideSprite) return hideSprite;
            hideSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.CamoButton.png", 115f);
            return hideSprite;
        }

        public static Sprite getDecoySprite()
        {
            if (decoySprite) return decoySprite;
            decoySprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.MorphButton.png", 115f);
            return decoySprite;
        }

        private static DeadBody findBody(byte parentId)
        {
            foreach (var body in UnityEngine.Object.FindObjectsOfType<DeadBody>())
                if (body != null && body.ParentId == parentId) return body;
            return null;
        }

        private static DeadBody findTarget(PlayerControl local)
        {
            if (local == null || local.Data == null || local.Data.IsDead) return null;

            DeadBody result = null;
            float closest = float.MaxValue;
            Vector2 position = local.GetTruePosition();

            foreach (Collider2D collider in Physics2D.OverlapCircleAll(position, useDistance, Constants.PlayersOnlyMask))
            {
                if (collider.tag != "DeadBody") continue;
                DeadBody body = collider.GetComponent<DeadBody>();
                if (body == null || body.Reported) continue;
                if (active.Any(x => x != null && x.victimId == body.ParentId)) continue;

                float distance = Vector2.Distance(position, body.TruePosition);
                if (distance >= closest) continue;
                closest = distance;
                result = body;
            }
            return result;
        }

        private static PlayerDisplay spawnDecoy(PlayerControl target, Vector2 position)
        {
            if (target == null || target.Data == null) return null;

            var display = VanillaAsset.GetPlayerDisplay();
            if (display == null) return null;

            display.transform.position = new Vector3(position.x + decoyOffset, position.y, position.y / 1000f);
            display.transform.localScale = new Vector3(0.7f, 0.7f, 1f);
            display.UpdateFromPlayerOutfit(target, false, false);
            display.Cosmetics.ToggleName(true);
            display.Cosmetics.nameText.text = target.Data.PlayerName;
            display.Cosmetics.nameText.color = Color.white;
            return display;
        }

        private static void removeIllusion(byte victimId)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] == null || active[i].victimId != victimId) continue;
                drop(active[i]);
                active.RemoveAt(i);
            }
        }

        private static void drop(Illusion illusion)
        {
            if (illusion == null) return;
            if (illusion.display != null) UnityEngine.Object.Destroy(illusion.display.gameObject);
            if (illusion.body != null)
            {
                illusion.body.gameObject.SetActive(true);
                if (illusion.body.myCollider != null) illusion.body.myCollider.tag = "DeadBody";
            }
            illusion.display = null;
        }

        public static void clearIllusions()
        {
            foreach (var illusion in active) drop(illusion);
            active.Clear();
        }

        public static RemoteProcess<(byte ownerId, byte victimId, float x, float y, byte decoyId, bool isDecoy)> PlaceIllusion = new("IllusionistPlace", (message, _) =>
        {
            var body = findBody(message.victimId);
            if (body == null) return;

            removeIllusion(message.victimId);

            var illusion = new Illusion
            {
                ownerId = message.ownerId,
                victimId = message.victimId,
                decoyId = message.decoyId,
                isDecoy = message.isDecoy,
                position = new Vector2(message.x, message.y),
                body = body
            };

            if (message.isDecoy) illusion.display = spawnDecoy(Helpers.playerById(message.decoyId), illusion.position);

            if (message.isDecoy)
            {
                if (body.myCollider != null) body.myCollider.tag = "Untagged";
            }
            else
            {
                body.gameObject.SetActive(false);
            }
            active.Add(illusion);

            if (PlayerControl.LocalPlayer != null && PlayerControl.LocalPlayer.PlayerId == message.ownerId)
                SoundEffectsManager.play(message.isDecoy ? "morphlingMorph" : "morphlingSample", 0.7f);
        });

        public static RemoteProcess<(byte victimId, byte reporterId)> TriggerIllusion = new("IllusionistTrigger", (message, _) =>
        {
            removeIllusion(message.victimId);
            RPCProcedure.uncheckedCmdReportDeadBody(message.reporterId, message.victimId);
            SoundEffectsManager.play("fail", 0.7f);
        });

        public static void update(PlayerControl local)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i] != null && active[i].body != null) continue;
                if (active[i]?.display != null) UnityEngine.Object.Destroy(active[i].display.gameObject);
                active.RemoveAt(i);
            }

            if (local == null || local.Data == null) return;

            bool isIllusionist = local.isRole(RoleId.Illusionist);
            if (isIllusionist && !local.Data.IsDead) updateTarget(local);

            if (active.Count == 0) return;
            if (local.Data.IsDead || local.Data.Disconnected) return;
            if (local.Data.Role == null || local.Data.Role.IsImpostor) return;
            if (local.inVent) return;
            if (MeetingHud.Instance || ExileController.Instance) return;

            Vector2 position = local.GetTruePosition();

            for (int i = 0; i < active.Count; i++)
            {
                var illusion = active[i];
                if (illusion == null || illusion.spent) continue;
                if (Vector2.Distance(position, illusion.position) > triggerRadius) continue;

                byte reporterId = local.PlayerId;
                if (illusion.isDecoy && illusion.decoyId != byte.MaxValue)
                {
                    var decoy = Helpers.playerById(illusion.decoyId);
                    if (decoy != null && decoy.Data != null && !decoy.Data.IsDead && !decoy.Data.Disconnected) reporterId = illusion.decoyId;
                }

                Helpers.handleVampireBiteOnBodyReport();
                Helpers.HandleUndertakerDropOnBodyReport();
                Helpers.handleTrapperTrapOnBodyReport();

                illusion.spent = true;
                TriggerIllusion.Invoke((illusion.victimId, reporterId));
                break;
            }
        }

        private static DeadBody highlighted;

        private static void updateTarget(PlayerControl local)
        {
            var role = getRole(local);
            var target = role != null && role.usesLeft > 0 ? findTarget(local) : null;

            if (target != highlighted)
            {
                if (highlighted != null) Helpers.SetDeadBodyOutline(highlighted, null);
                highlighted = target;
            }
            if (highlighted != null) Helpers.SetDeadBodyOutline(highlighted, color);
        }

        private void onUse()
        {
            var body = findTarget(player);
            if (body == null) return;

            byte decoyId = byte.MaxValue;
            if (decoyMode)
            {
                var candidates = PlayerControl.AllPlayerControls.ToArray()
                    .Where(x => x != null && x.Data != null && !x.Data.IsDead && !x.Data.Disconnected && x != player)
                    .ToList();
                if (candidates.Count == 0) return;
                decoyId = candidates[rnd.Next(candidates.Count)].PlayerId;
            }

            PlaceIllusion.Invoke((player.PlayerId, body.ParentId, body.TruePosition.x, body.TruePosition.y, decoyId, decoyMode));

            usesLeft--;
            illusionButton.Timer = illusionButton.MaxTimer;
        }

        public void refreshButton()
        {
            if (illusionButton == null) return;
            illusionButton.Sprite = decoyMode ? getDecoySprite() : getHideSprite();
            illusionButton.buttonText = ModTranslation.getString(decoyMode ? "illusionistDecoy" : "illusionistHide");
        }

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;
            var hudManager = HudManager.Instance;

            illusionButton = new CustomButton(
                onUse,
                () => PlayerControl.LocalPlayer.isRole(RoleId.Illusionist) && usesLeft > 0,
                () => player.CanMove && !MeetingHud.Instance && !Minigame.Instance && findTarget(player) != null,
                () => { illusionButton.Timer = illusionButton.MaxTimer; },
                getHideSprite(),
                CustomButton.ButtonPositions.upperRowLeft,
                hudManager,
                KeyCode.F,
                buttonText: ModTranslation.getString("illusionistHide"),
                abilityTexture: CustomButton.ButtonLabelType.UseButton
            );
            illusionButton.MaxTimer = cooldown;
            illusionButton.Timer = cooldown;
            usesText = illusionButton.ShowUsesIcon(3);
            usesText.text = usesLeft.ToString();
            refreshButton();
        }

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (usesText != null) usesText.text = usesLeft > 0 ? usesLeft.ToString() : "";
        }

        public override void OnMeetingStart()
        {
            clearIllusions();
            highlighted = null;
        }

        public override void ResetRole(bool isShifted) => destroyUi();

        private static void destroyUi()
        {
            clearIllusions();
            highlighted = null;
            usesText = null;
            if (illusionButton != null)
            {
                UnityEngine.Object.Destroy(illusionButton.actionButtonGameObject);
                illusionButton = null;
            }
        }

        static public IEnumerable<HelpSprite> GetHelpSprites()
        {
            yield return new(getHideSprite(), "illusionistHideHint");
            yield return new(getDecoySprite(), "illusionistDecoyHint");
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", cooldown.ToString());
            yield return new("%USES%", maxUses.ToString());
            yield return new("%RADIUS%", triggerRadius.ToString());
        }

        public static void clearAndReload()
        {
            cooldown = CustomOptionHolder.illusionistCooldown.getFloat();
            maxUses = Mathf.RoundToInt(CustomOptionHolder.illusionistUses.getFloat());
            triggerRadius = CustomOptionHolder.illusionistTriggerRadius.getFloat();

            destroyUi();
            hideSprite = null;
            decoySprite = null;
            players = [];
        }
    }

    [HarmonyPatch(typeof(PlayerPhysics), nameof(PlayerPhysics.FixedUpdate))]
    public static class IllusionistProximityPatch
    {
        public static void Postfix(PlayerPhysics __instance)
        {
            if (!__instance.AmOwner || __instance.myPlayer == null) return;
            Illusionist.update(__instance.myPlayer);
        }
    }

    [HarmonyPatch(typeof(HudManager), nameof(HudManager.Update))]
    public static class IllusionistSwitchPatch
    {
        public static void Postfix()
        {
            var role = Illusionist.local;
            if (role == null) return;
            if (!Input.GetKeyDown(KeyCode.LeftShift) && !Input.GetKeyDown(KeyCode.RightShift)) return;

            var local = PlayerControl.LocalPlayer;
            if (local == null || local.Data == null || local.Data.IsDead) return;
            if (MeetingHud.Instance || ExileController.Instance || Minigame.Instance) return;

            var chat = FastDestroyableSingleton<HudManager>.Instance?.Chat;
            if (chat != null && chat.IsOpenOrOpening) return;

            role.decoyMode = !role.decoyMode;
            role.refreshButton();
        }
    }
}
