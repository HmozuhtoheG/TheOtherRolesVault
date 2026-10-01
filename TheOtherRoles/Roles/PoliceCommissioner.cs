using System.Collections.Generic;
using System.Linq;
using AmongUs.GameOptions;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.Patches.PlayerControlFixedUpdatePatch;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class PoliceCommissioner : RoleBase<PoliceCommissioner>
    {
        public static Color color = Sheriff.color;

        public static float cooldown = 30f;
        public static float killCooldown = 30f;
        public static int maxUses = 1;
        public static bool canRecruitImpostor = true;
        public static bool canRecruitNeutral = true;
        public static bool diesOnImpostor = true;
        public static bool diesOnNeutral = true;
        public static bool canKill = true;

        public PlayerControl currentTarget;
        public PlayerControl killTarget;
        public int usesLeft;

        private static Sprite recruitButtonSprite;

        public PoliceCommissioner()
        {
            RoleId = roleId = RoleId.PoliceCommissioner;
            currentTarget = null;
            usesLeft = maxUses;
        }

        public static Sprite getRecruitButtonSprite()
        {
            if (recruitButtonSprite) return recruitButtonSprite;
            recruitButtonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.DeputyHandcuffButton.png", 115f);
            return recruitButtonSprite;
        }

        public static bool canRecruit(PlayerControl commissioner, PlayerControl target)
        {
            if (commissioner == null || target == null || target.Data == null) return false;
            if (target == commissioner || target.Data.IsDead || target.Data.Disconnected) return false;
            if (target.isRole(RoleId.Sheriff) || target.isRole(RoleId.Deputy)) return false;

            bool isImpostor = target.Data.Role.IsImpostor;
            if (isImpostor) return canRecruitImpostor;
            if (Helpers.isNeutral(target)) return canRecruitNeutral;
            return true;
        }

        public static RemoteProcess<(byte targetId, byte commissionerId)> Recruit = new("PoliceCommissionerRecruit", (message, __) =>
        {
            var target = Helpers.playerById(message.targetId);
            var commissioner = getRole(Helpers.playerById(message.commissionerId));
            if (target == null || target.Data == null || commissioner == null || commissioner.player == null) return;
            if (commissioner.usesLeft <= 0) return;
            if (!canRecruit(commissioner.player, target)) return;

            bool wasImpostor = target.Data.Role.IsImpostor;
            bool wasNeutral = !wasImpostor && Helpers.isNeutral(target);
            byte targetId = target.PlayerId;
            string commissionerName = commissioner.player.Data.PlayerName;

            commissioner.usesLeft--;

            if (commissioner.player == PlayerControl.LocalPlayer)
            {
                _ = new StaticAchievementToken("policeCommissioner.common1");
                if (wasImpostor) _ = new StaticAchievementToken("policeCommissioner.another1");
            }

            FastDestroyableSingleton<RoleManager>.Instance.SetRole(target, RoleTypes.Crewmate);
            RPCProcedure.erasePlayerRoles(targetId, true, true, true);
            target.setRole(RoleId.Sheriff);

            if (target == PlayerControl.LocalPlayer)
            {
                SoundEffectsManager.play("jackalSidekick");
                new CustomMessage(string.Format(ModTranslation.getString("policeCommissionerRecruited"), commissionerName), 4f);
            }

            if (AmongUsClient.Instance != null && AmongUsClient.Instance.AmHost && wasImpostor)
                LastImpostor.promoteToLastImpostor();

            bool dies = (wasImpostor && diesOnImpostor) || (wasNeutral && diesOnNeutral);
            if (!dies) return;

            if (AmongUsClient.Instance == null || !AmongUsClient.Instance.AmHost) return;

            if (wasImpostor && commissioner.player == PlayerControl.LocalPlayer)
                _ = new StaticAchievementToken("policeCommissioner.challenge");

            Helpers.MurderPlayer(target, commissioner.player, true);
        });

        public override void FixedUpdate()
        {
            if (player != PlayerControl.LocalPlayer) return;
            if (player.Data == null || player.Data.IsDead) { currentTarget = null; killTarget = null; return; }
            if (MeetingHud.Instance || ExileController.Instance) { currentTarget = null; killTarget = null; return; }

            if (canKill)
            {
                killTarget = setTarget();
                setPlayerOutline(killTarget, Palette.ImpostorRed);
            }
            else
            {
                killTarget = null;
            }

            if (usesLeft <= 0)
            {
                currentTarget = null;
            }
            else
            {
                var untargetable = new List<PlayerControl>();
                foreach (var pc in PlayerControl.AllPlayerControls)
                {
                    if (!canRecruit(player, pc)) untargetable.Add(pc);
                }
                currentTarget = setTarget(untargetablePlayers: untargetable);
                setPlayerOutline(currentTarget, color);
            }

            if (HudManagerStartPatch.policeCommissionerUsesText != null)
                HudManagerStartPatch.policeCommissionerUsesText.text = usesLeft.ToString();
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            currentTarget = null;
            killTarget = null;
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%USES%", maxUses.ToString());
            yield return new("%CD%", Mathf.RoundToInt(cooldown).ToString());
            yield return new("%KCD%", Mathf.RoundToInt(killCooldown).ToString());
        }

        public static void clearAndReload()
        {
            cooldown = CustomOptionHolder.policeCommissionerCooldown.getFloat();
            maxUses = Mathf.RoundToInt(CustomOptionHolder.policeCommissionerUses.getFloat());
            canRecruitImpostor = CustomOptionHolder.policeCommissionerCanRecruitImpostor.getBool();
            canRecruitNeutral = CustomOptionHolder.policeCommissionerCanRecruitNeutral.getBool();
            diesOnImpostor = CustomOptionHolder.policeCommissionerDiesOnImpostor.getBool();
            diesOnNeutral = CustomOptionHolder.policeCommissionerDiesOnNeutral.getBool();
            canKill = CustomOptionHolder.policeCommissionerCanKill.getBool();
            killCooldown = CustomOptionHolder.policeCommissionerKillCooldown.getFloat();
            players = [];
        }
    }
}
