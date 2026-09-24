using System.Collections.Generic;
using System.Linq;
using Hazel;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using UnityEngine;
using static TheOtherRoles.Patches.PlayerControlFixedUpdatePatch;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles;

public class Sheriff : RoleBase<Sheriff>
{
    public static Color color = new Color32(248, 205, 70, byte.MaxValue);

    public static float cooldown = 30f;
    public static bool canKillNeutrals = false;
    public static bool spyCanDieToSheriff = false;
    public bool isFormerDeputy = false;
    public float remainingHandcuffs = 0;

    public Sheriff()
    {
        RoleId = roleId = RoleId.Sheriff;
        currentTarget = null;
        isFormerDeputy = false;
        remainingHandcuffs = 0;
        acTokenChallenge = null;
    }

    public PlayerControl currentTarget;

    public AchievementToken<(bool isTriggeredFalse, bool cleared)> acTokenChallenge = null;

    public override void FixedUpdate()
    {
        if (player != PlayerControl.LocalPlayer) return;
        currentTarget = setTarget();
        setPlayerOutline(currentTarget, color);

        foreach (var auxiliary in Auxiliary.players)
        {
            var marked = auxiliary.markedPlayer;
            if (marked == null || marked == currentTarget || marked.Data == null || marked.Data.IsDead) continue;
            if (auxiliary.player == null || auxiliary.player.Data == null || auxiliary.player.Data.IsDead) continue;
            setPlayerOutline(marked, Auxiliary.markColor);
        }
    }

    public static MurderAttemptResult trySheriffKill(PlayerControl killer, PlayerControl target)
    {
        if (killer == null || target == null || target.Data == null) return MurderAttemptResult.SuppressKill;

        MurderAttemptResult murderAttemptResult = Helpers.checkMuderAttempt(killer, target);
        if (murderAttemptResult == MurderAttemptResult.SuppressKill) return murderAttemptResult;

        if (murderAttemptResult is MurderAttemptResult.PerformKill or MurderAttemptResult.ReverseKill)
        {
            bool isSheriff = killer.isRole(RoleId.Sheriff);
            byte targetId;

            if (((target.Data.Role.IsImpostor && (target != Mini.mini || Mini.isGrownUp())) ||
                (spyCanDieToSheriff && target.isRole(RoleId.Spy)) ||
                (canKillNeutrals && Helpers.isNeutral(target)) ||
                target.isRole(RoleId.Jackal) || target.isRole(RoleId.Sidekick) ||
                (CreatedMadmate.createdMadmate.Any(x => x.PlayerId == target.PlayerId) && CreatedMadmate.canDieToSheriff) ||
                (Madmate.canDieToSheriff && Madmate.madmate.Any(x => x.PlayerId == target.PlayerId))) &&
                !Madmate.madmate.Any(y => y.PlayerId == killer.PlayerId))
            {
                if (isSheriff) _ = new StaticAchievementToken("sheriff.common1");
                targetId = target.PlayerId;
            }
            else
            {
                var protector = !target.Data.Role.IsImpostor && !Helpers.isNeutral(target) ? Auxiliary.getProtectorOf(target) : null;
                if (protector != null && protector.player != null && protector.player.PlayerId != killer.PlayerId)
                {
                    if (PlayerControl.LocalPlayer == protector.player)
                        new CustomMessage(string.Format(ModTranslation.getString("auxiliarySacrifice"), target.Data.PlayerName), 4f);
                    if (PlayerControl.LocalPlayer == killer)
                        new CustomMessage(ModTranslation.getString("auxiliarySacrificeSheriff"), 4f);

                    targetId = protector.player.PlayerId;
                }
                else
                {
                    if (isSheriff) _ = new StaticAchievementToken("sheriff.another1");
                    targetId = killer.PlayerId;
                }
            }

            MessageWriter killWriter = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.UncheckedMurderPlayer, Hazel.SendOption.Reliable, -1);
            killWriter.Write(killer.Data.PlayerId);
            killWriter.Write(targetId);
            killWriter.Write(byte.MaxValue);
            AmongUsClient.Instance.FinishRpcImmediately(killWriter);
            RPCProcedure.uncheckedMurderPlayer(killer.Data.PlayerId, targetId, byte.MaxValue);
        }

        return murderAttemptResult;
    }

    public static void replaceCurrentSheriff(PlayerControl deputy)
    {
        setRole(deputy);
        getRole(deputy).currentTarget = null;
        cooldown = CustomOptionHolder.sheriffCooldown.getFloat();
    }

    public override void OnKill(PlayerControl target)
    {
        if (PlayerControl.LocalPlayer == player)
        {
            acTokenChallenge.Value.isTriggeredFalse = false;

            if (acTokenChallenge.Value.cleared)
            {
                foreach (var dp in GameHistory.deadPlayers)
                {
                    if (dp.player == null || !Helpers.isEvil(dp.player)) continue;
                    if (!isRole(dp.killerIfExisting))
                    {
                        acTokenChallenge.Value.cleared = false;
                        break;
                    }
                }

                foreach (PlayerControl p in PlayerControl.AllPlayerControls)
                {
                    if (p == null || !Helpers.isEvil(p)) continue;
                    if (!p.Data.IsDead)
                    {
                        acTokenChallenge.Value.isTriggeredFalse = true;
                        break;
                    }
                }
            }
        }
    }

    public override GUIContext ProgressContext
    {
        get
        {
            List<GUIContext> contexts = [];

            var deputy = getDeputy(player);
            if (deputy != null && deputy.player != null)
                contexts.Add(ProgressGUI.OneLineText(ModTranslation.getString("deputy") + ": " + deputy.player.Data.PlayerName));

            var auxiliary = Auxiliary.getAuxiliary(player);
            if (auxiliary != null && auxiliary.player != null)
                contexts.Add(ProgressGUI.OneLineText(ModTranslation.getString("auxiliary") + ": " + auxiliary.player.Data.PlayerName));

            return contexts.Count == 0 ? null : ProgressGUI.Holder(contexts);
        }
    }

    public static Deputy getDeputy(PlayerControl sheriff)
    {
        return Deputy.players.FirstOrDefault(x => x.sheriff != null && x.sheriff?.player == sheriff);
    }

    public override void PostInit()
    {
        if (PlayerControl.LocalPlayer != player) return;
        acTokenChallenge ??= new("sheriff.challenge", (true, true), (val, _) => val.cleared && !val.isTriggeredFalse);
    }

    public static void clearAndReload()
    {
        cooldown = CustomOptionHolder.sheriffCooldown.getFloat();
        canKillNeutrals = CustomOptionHolder.sheriffCanKillNeutrals.getBool();
        spyCanDieToSheriff = CustomOptionHolder.spyCanDieToSheriff.getBool();
        players = [];
    }
}
