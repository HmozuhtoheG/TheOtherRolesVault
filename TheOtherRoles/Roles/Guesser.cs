using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    public class NiceGuesser : RoleBase<NiceGuesser>
    {
        public NiceGuesser()
        {
            RoleId = roleId = RoleId.NiceGuesser;
        }

        public static AchievementToken<int> acTokenNiceGuesser;
        public static int remainingShotsNiceGuesser = 2;
        public static int safeGuessesNiceGuesser = 0;

        public static int remainingShot(bool shoot)
        {
            int remainingShots = remainingShotsNiceGuesser;
            if (shoot) remainingShotsNiceGuesser = Mathf.Max(0, remainingShotsNiceGuesser - 1);
            return remainingShots;
        }

        public static bool consumeSafeGuess()
        {
            if (safeGuessesNiceGuesser <= 0) return false;
            safeGuessesNiceGuesser--;
            return true;
        }

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;
            acTokenNiceGuesser ??= new("niceGuesser.challenge1", 0, (val, _) => val >= 3);
        }

        public static void clearAndReload()
        {
            acTokenNiceGuesser = null;
            remainingShotsNiceGuesser = Mathf.RoundToInt(CustomOptionHolder.guesserNumberOfShots.getFloat());
            safeGuessesNiceGuesser = Mathf.RoundToInt(CustomOptionHolder.guesserSafeGuesses.getFloat());
            players = [];
        }
    }

    public class EvilGuesser : RoleBase<EvilGuesser>
    {
        public EvilGuesser()
        {
            RoleId = roleId = RoleId.EvilGuesser;
        }

        public static MetaContext.Image Illustration = SpriteLoader.FromResource("TheOtherRoles.Resources.Characterillustration.EvilGuesser.png", 300f);

        public static int remainingShotsEvilGuesser = 2;
        public static int safeGuessesEvilGuesser = 0;
        public static AchievementToken<int> acTokenEvilGuesser;

        public static bool consumeSafeGuess()
        {
            if (safeGuessesEvilGuesser <= 0) return false;
            safeGuessesEvilGuesser--;
            return true;
        }

        public override void PostInit()
        {
            if (PlayerControl.LocalPlayer != player) return;
            acTokenEvilGuesser ??= new("evilGuesser.challenge1", 0, (val, _) => val >= 3);
        }

        public static int remainingShot(bool shoot)
        {
            int remainingShots = remainingShotsEvilGuesser;
            if (shoot) remainingShotsEvilGuesser = Mathf.Max(0, remainingShotsEvilGuesser - 1);
            return remainingShots;
        }

        public static void clearAndReload()
        {
            acTokenEvilGuesser = null;
            remainingShotsEvilGuesser = Mathf.RoundToInt(CustomOptionHolder.guesserNumberOfShots.getFloat());
            safeGuessesEvilGuesser = Mathf.RoundToInt(CustomOptionHolder.guesserSafeGuesses.getFloat());
            players = [];
        }
    }

    public static class Guesser
    {
        public static Color color = new Color32(255, 255, 0, byte.MaxValue);

        public static bool isGuesser(byte playerId)
        {
            PlayerControl player = Helpers.playerById(playerId);
            if (Gambler.isGambler(player)) return true;
            if (player.isRole(RoleId.NiceGuesser) || player.isRole(RoleId.EvilGuesser)) return true;
            return false;
        }

        public static int remainingShots(byte playerId, bool shoot = false)
        {
            PlayerControl player = Helpers.playerById(playerId);
            if (Gambler.isGambler(player)) return Gambler.remainingShot(shoot);
            if (player.isRole(RoleId.NiceGuesser)) return NiceGuesser.remainingShot(shoot);
            else return EvilGuesser.remainingShot(shoot);
        }

        public static void clearAndReload()
        {
            NiceGuesser.clearAndReload();
            EvilGuesser.clearAndReload();
        }
    }
}
