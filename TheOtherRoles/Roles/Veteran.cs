using TheOtherRoles.Modules;
using TheOtherRoles.Patches;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Veteran : RoleBase<Veteran>
    {
        public static Color color = new Color32(255, 77, 0, byte.MaxValue);

        public Veteran()
        {
            RoleId = roleId = RoleId.Veteran;
            remainingAlerts = Mathf.RoundToInt(CustomOptionHolder.veteranAlertNumber.getFloat());
            alertActive = false;
        }

        public static RemoteProcess<byte> ActivateAlert = RemotePrimitiveProcess.OfByte("VeteranAlert", (message, _) =>
        {
            PlayerControl player = Helpers.playerById(message);
            var veteran = getRole(player);
            if (player == null || veteran == null) return;
            veteran.alertActive = true;
            FastDestroyableSingleton<HudManager>.Instance.StartCoroutine(Effects.Lerp(alertDuration, new System.Action<float>((p) => {
                if (p == 1f) veteran.alertActive = false;
            })));
        });

        public static RemoteProcess<(byte veteranId, byte clipIndex)> PlayCounterShout = new("VeteranCounterShout", (message, _) =>
        {
            playCounterShout(message.veteranId, message.clipIndex);
        });

        public static float alertDuration = 3f;
        public static float cooldown = 30f;

        public int remainingAlerts = 5;

        public bool alertActive = false;

        private static Sprite buttonSprite;
        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.AlertButton.png", 115f);
            return buttonSprite;
        }

        private static AudioClip[] counterShoutClips;
        private static bool counterShoutClipsLoaded;

        private static AudioClip[] getCounterShoutClips()
        {
            if (counterShoutClipsLoaded) return counterShoutClips;
            counterShoutClipsLoaded = true;
            counterShoutClips = new AudioClip[]
            {
                Helpers.loadWavFromResources("TheOtherRoles.Resources.yiliwala.yiliwala1.wav", "TORV_Yiliwala1"),
                Helpers.loadWavFromResources("TheOtherRoles.Resources.yiliwala.yiliwala2.wav", "TORV_Yiliwala2"),
                Helpers.loadWavFromResources("TheOtherRoles.Resources.yiliwala.yiliwala3.wav", "TORV_Yiliwala3"),
                Helpers.loadWavFromResources("TheOtherRoles.Resources.yiliwala.yiliwala4.wav", "TORV_Yiliwala4"),
                Helpers.loadWavFromResources("TheOtherRoles.Resources.yiliwala.yiliwala5.wav", "TORV_Yiliwala5"),
            };
            return counterShoutClips;
        }

        private static void playCounterShout(byte veteranId, byte clipIndex)
        {
            if (PlayerControl.LocalPlayer == null) return;
            if (ClientOption.GetValue(ClientOption.ClientOptionType.VeteranCounterSound) == 0) return;
            if (ClientOption.GetValue(ClientOption.ClientOptionType.EnableSoundEffects) == 0) return;
            if (!Constants.ShouldPlaySfx()) return;

            PlayerControl veteran = Helpers.playerById(veteranId);
            if (veteran == null || veteran.Data == null) return;
            if (PlayerControl.LocalPlayer != veteran && !Helpers.isVisible(PlayerControl.LocalPlayer, veteran)) return;

            AudioClip[] clips = getCounterShoutClips();
            if (clips == null || clipIndex >= clips.Length || clips[clipIndex] == null) return;

            SoundManager.Instance.PlaySound(clips[clipIndex], false, 0.8f);
        }

        public static void onCounterKill(PlayerControl veteran)
        {
            if (veteran == null) return;
            AudioClip[] clips = getCounterShoutClips();
            if (clips == null || clips.Length == 0) return;
            PlayCounterShout.Invoke((veteran.PlayerId, (byte)rnd.Next(clips.Length)));
        }

        public static void clearAndReload()
        {
            alertDuration = CustomOptionHolder.veteranAlertDuration.getFloat();
            cooldown = CustomOptionHolder.veteranCooldown.getFloat();
            players = [];
        }
    }
}
