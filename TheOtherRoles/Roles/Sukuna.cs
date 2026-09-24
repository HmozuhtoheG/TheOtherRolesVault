using System;
using System.Collections.Generic;
using System.Linq;
using TheOtherRoles.MetaContext;
using TheOtherRoles.Modules;
using TheOtherRoles.Objects;
using TheOtherRoles.Patches;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Roles
{
    [TORRPCHolder]
    public class Sukuna : RoleBase<Sukuna>
    {
        public static Color color = Palette.ImpostorRed;

        public static float cooldown = 30f;
        public static float chantDuration = 8f;

        public bool isChanting;
        public float chantTimer;
        public bool facingLeft;

        private GameObject verseObject;
        private TMPro.TextMeshPro verseText;
        private Vector3 verseScale = Vector3.one;
        private int lastVerse = -1;

        private static Sprite slashSprite;
        private static Sprite buttonSprite;

        public Sukuna()
        {
            RoleId = roleId = RoleId.Sukuna;
            isChanting = false;
            chantTimer = 0f;
            facingLeft = false;
        }

        public static Sprite getButtonSprite()
        {
            if (buttonSprite) return buttonSprite;
            buttonSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.CurseKillButton.png", 115f);
            return buttonSprite;
        }

        public static Sprite getSlashSprite()
        {
            if (slashSprite) return slashSprite;
            slashSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.Sukuna.png", 115f);
            return slashSprite;
        }

        public static RemoteProcess<(byte sukunaId, float duration, bool facingLeft)> StartChant = new("SukunaStartChant", (message, _) =>
        {
            var sukuna = getRole(Helpers.playerById(message.sukunaId));
            if (sukuna == null || sukuna.player == null) return;

            sukuna.isChanting = true;
            sukuna.chantTimer = message.duration;
            sukuna.facingLeft = message.facingLeft;
            sukuna.lastVerse = -1;
            sukuna.ShowVerse();
            SoundEffectsManager.playAtPosition("warlockCurse", sukuna.player.GetTruePosition(), 1f, 10f);
        });

        public static RemoteProcess<(byte sukunaId, Vector2 origin, Vector2 direction)> ReleaseSlash = new("SukunaReleaseSlash", (message, _) =>
        {
            SpawnSlash(Helpers.playerById(message.sukunaId), message.origin, message.direction);
        });

        public static float getPlayerBodySize(PlayerControl target)
        {
            var body = target != null && target.cosmetics != null ? target.cosmetics.currentBodySprite : null;
            if (body == null || body.BodySprite == null) return 1.1f;
            return Mathf.Max(0.6f, body.BodySprite.bounds.size.y);
        }

        private static void SpawnSlash(PlayerControl sukuna, Vector2 origin, Vector2 direction)
        {
            float elementSize = getPlayerBodySize(sukuna);
            bool facingLeft = direction.x < 0f;

            var sprite = getSlashSprite();
            float spriteSize = sprite.rect.width / sprite.pixelsPerUnit;
            float scale = elementSize / Mathf.Max(0.01f, spriteSize);

            int count = Mathf.CeilToInt(50f / Mathf.Max(0.4f, elementSize));
            for (int i = 1; i <= count; i++)
            {
                Vector2 position = origin + direction * (i * elementSize * 0.8f);
                var slashObject = new GameObject("SukunaSlash") { layer = 5 };
                slashObject.transform.position = position;
                slashObject.transform.localScale = Vector3.one * scale;

                var renderer = slashObject.AddComponent<SpriteRenderer>();
                renderer.sprite = sprite;
                renderer.flipX = facingLeft;
                renderer.color = Color.white;

                HudManager.Instance.StartCoroutine(Effects.Lerp(0.6f, new Action<float>((p) =>
                {
                    if (renderer != null) renderer.color = new Color(1f, 1f, 1f, 1f - p);
                    if (p == 1f && slashObject != null) UnityEngine.Object.Destroy(slashObject);
                })));
            }
        }

        private void ShowVerse()
        {
            if (player == null || player.cosmetics == null || player.cosmetics.nameText == null) return;

            if (verseObject == null)
            {
                verseText = UnityEngine.Object.Instantiate(player.cosmetics.nameText, player.cosmetics.nameText.transform.parent);
                verseText.transform.localPosition = new Vector3(0f, 0.6f, -0.01f);
                verseText.fontSize = 1.5f;
                verseText.enableWordWrapping = false;
                verseText.color = color;
                verseScale = verseText.transform.localScale;
                verseObject = verseText.gameObject;
            }

            verseObject.SetActive(true);
            verseText.text = "";
        }

        private void HideVerse()
        {
            if (verseObject != null) UnityEngine.Object.Destroy(verseObject);
            verseObject = null;
            verseText = null;
            lastVerse = -1;
        }

        private void UpdateVerse()
        {
            if (verseText == null) return;

            float interval = Mathf.Max(0.5f, chantDuration / 3f);
            float elapsed = chantDuration - chantTimer;
            int verse = Mathf.Clamp((int)(elapsed / interval), 0, 2);

            if (verse != lastVerse)
            {
                lastVerse = verse;
                verseText.text = string.Join("\n", Enumerable.Range(0, verse + 1).Select(i => ModTranslation.getString("sukunaVerse" + (i + 1))));
            }

            float pulse = 1f + 0.15f * Mathf.Sin(Time.time * 6f);
            verseText.transform.localScale = verseScale * pulse;
        }

        public void TryStartChant()
        {
            if (player != PlayerControl.LocalPlayer || player.Data == null || player.Data.IsDead) return;
            if (isChanting || MeetingHud.Instance || ExileController.Instance) return;

            facingLeft = player.cosmetics != null && player.cosmetics.currentBodySprite != null && player.cosmetics.currentBodySprite.BodySprite != null
                && player.cosmetics.currentBodySprite.BodySprite.flipX;

            StartChant.Invoke((player.PlayerId, chantDuration, facingLeft));
        }

        private void ReleaseSlashAndKill()
        {
            player.moveable = true;

            Vector2 origin = player.GetTruePosition();
            Vector2 direction = facingLeft ? Vector2.left : Vector2.right;

            var victims = new List<PlayerControl>();
            foreach (PlayerControl target in PlayerControl.AllPlayerControls)
            {
                if (target == null || target.Data == null || target == player) continue;
                if (target.Data.IsDead || target.Data.Disconnected) continue;

                Vector2 relative = target.GetTruePosition() - origin;
                float along = Vector2.Dot(relative, direction);
                if (along < 0f) continue;

                float perpendicular = Mathf.Abs(Vector2.Dot(relative, new Vector2(-direction.y, direction.x)));
                if (perpendicular > getPlayerBodySize(player) * 0.5f) continue;

                victims.Add(target);
            }

            ReleaseSlash.Invoke((player.PlayerId, origin, direction));

            foreach (var victim in victims)
                Helpers.forceMurderPlayer(player, victim, false);
        }

        public override void FixedUpdate()
        {
            if (player == null || player.Data == null) return;
            if (!isChanting) return;

            if (player.Data.IsDead || MeetingHud.Instance || ExileController.Instance)
            {
                isChanting = false;
                HideVerse();
                if (player == PlayerControl.LocalPlayer) player.moveable = true;
                return;
            }

            chantTimer -= Time.fixedDeltaTime;
            UpdateVerse();

            if (player.cosmetics != null && player.cosmetics.currentBodySprite != null && player.cosmetics.currentBodySprite.BodySprite != null)
                player.cosmetics.currentBodySprite.BodySprite.flipX = facingLeft;

            if (player == PlayerControl.LocalPlayer)
            {
                player.moveable = false;
                if (player.MyPhysics != null && player.MyPhysics.body != null)
                    player.MyPhysics.body.velocity = Vector2.zero;

                if (chantTimer <= 0f)
                {
                    isChanting = false;
                    HideVerse();
                    ReleaseSlashAndKill();
                }
            }
            else if (chantTimer <= 0f)
            {
                isChanting = false;
                HideVerse();
            }
        }

        public override void OnMeetingStart()
        {
            isChanting = false;
            HideVerse();
            if (player == PlayerControl.LocalPlayer) player.moveable = true;
        }

        public override void OnDeath(PlayerControl killer = null)
        {
            isChanting = false;
            HideVerse();
            if (player == PlayerControl.LocalPlayer) player.moveable = true;
        }

        static public IEnumerable<DocumentReplacement> GetReplacementPart()
        {
            yield return new("%CD%", Mathf.RoundToInt(cooldown).ToString());
            yield return new("%CHANT%", chantDuration.ToString("0.#"));
        }

        public static void clearAndReload()
        {
            cooldown = CustomOptionHolder.sukunaCooldown.getFloat();
            chantDuration = CustomOptionHolder.sukunaChantDuration.getFloat();
            players = [];
        }
    }
}
