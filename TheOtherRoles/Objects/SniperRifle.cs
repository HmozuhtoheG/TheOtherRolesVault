using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using TheOtherRoles.Utilities;
using UnityEngine;
using static TheOtherRoles.TheOtherRoles;

namespace TheOtherRoles.Objects
{
    public class SniperRifle
    {
        private const float RifleWorldLength = 1.4f;
        private const float MuzzleOffset = 0.55f;
        private const float RifleFlip = -1f;
        private const float MarkerRadius = 2f;
        private const float MarkerFadeTime = 0.8f;

        private static Sprite rifleSprite;
        private static Sprite guideSprite;

        private class Marker
        {
            public GameObject obj;
            public SpriteRenderer renderer;
            public Vector2 direction;
            public bool initialized;
        }

        public readonly PlayerControl owner;
        public GameObject rifleObject;
        public SpriteRenderer renderer;

        private readonly Dictionary<byte, Marker> markers = new();
        private Coroutine aimRoutine;

        public static Sprite getRifleSprite()
        {
            if (rifleSprite) return rifleSprite;
            rifleSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.SniperRifle.png", 100f);
            return rifleSprite;
        }

        public static Sprite getGuideSprite()
        {
            if (guideSprite) return guideSprite;
            guideSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.SniperGuide.png", 100f);
            return guideSprite;
        }

        public static Sprite getArrowSprite()
        {
            return Helpers.loadSpriteFromResources("TheOtherRoles.Resources.SniperRifleArrow.png", 200f);
        }

        public SniperRifle(PlayerControl owner)
        {
            this.owner = owner;

            rifleObject = new GameObject("SniperRifle");
            rifleObject.layer = owner != null ? owner.gameObject.layer : 5;

            if (owner != null)
                rifleObject.transform.SetParent(owner.transform, false);

            renderer = rifleObject.AddComponent<SpriteRenderer>();
            renderer.sprite = getRifleSprite();
            renderer.sortingOrder = resolveSortingOrder();

            NormalizeScale();
        }

        private int resolveSortingOrder()
        {
            var body = owner?.cosmetics?.currentBodySprite?.BodySprite;
            return body != null ? body.sortingOrder + 1 : 12;
        }

        private void NormalizeScale()
        {
            var sprite = renderer != null ? renderer.sprite : null;
            if (sprite == null || rifleObject == null) return;

            float source = Mathf.Max(sprite.rect.width, sprite.rect.height) / sprite.pixelsPerUnit;
            if (source <= 0.001f) return;

            float scale = RifleWorldLength / source;
            rifleObject.transform.localScale = new Vector3(scale * RifleFlip, scale, 1f);
        }

        public void Update(float angleDegrees)
        {
            if (rifleObject == null || owner == null || owner.Data == null) return;

            float rad = angleDegrees * Mathf.Deg2Rad;
            rifleObject.transform.localPosition = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), -0.05f) * MuzzleOffset;
            rifleObject.transform.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
        }

        public void StartAimAssist()
        {
            if (owner != PlayerControl.LocalPlayer || !Roles.Sniper.aimAssist) return;

            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud == null) return;

            StopAimAssist();
            aimRoutine = hud.StartCoroutine(CoAimAssist().WrapToIl2Cpp());
        }

        public void StopAimAssist()
        {
            var hud = FastDestroyableSingleton<HudManager>.Instance;
            if (hud != null && aimRoutine != null) hud.StopCoroutine(aimRoutine);
            aimRoutine = null;
        }

        private IEnumerator CoAimAssist()
        {
            yield return Effects.Wait(Roles.Sniper.aimAssistDelay);

            float elapsed = 0f;
            var alive = new HashSet<byte>();

            while (ShouldShowAimAssist())
            {
                elapsed += Time.deltaTime;
                alive.Clear();

                var origin = owner.GetTruePosition();

                foreach (var target in PlayerControl.AllPlayerControls)
                {
                    if (target == null || target.Data == null) continue;
                    if (target == owner || target.Data.IsDead || target.Data.Disconnected) continue;
                    if (!Roles.Sniper.canKillImpostor && target.Data.Role != null && target.Data.Role.IsImpostor) continue;

                    var direction = target.GetTruePosition() - origin;
                    if (direction.sqrMagnitude < 0.01f) continue;

                    direction.Normalize();

                    var marker = GetOrCreateMarker(target.PlayerId);
                    if (marker == null) continue;

                    if (!marker.initialized)
                    {
                        marker.direction = direction;
                        marker.initialized = true;
                    }
                    else
                    {
                        marker.direction = (direction + marker.direction).normalized;
                    }

                    float angle = Mathf.Atan2(marker.direction.y, marker.direction.x);
                    marker.obj.transform.position = new Vector3(origin.x, origin.y, owner.transform.position.z - 0.2f)
                        + new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f) * MarkerRadius;
                    marker.obj.transform.rotation = Quaternion.Euler(0f, 0f, angle * Mathf.Rad2Deg);

                    float fade = Mathf.Clamp01(elapsed / MarkerFadeTime);
                    var color = Color.Lerp(Color.white, new Color(55f / 225f, 1f, 0f), fade);
                    marker.renderer.color = new Color(color.r, color.g, color.b, 0.6f);

                    alive.Add(target.PlayerId);
                }

                foreach (var pair in markers)
                {
                    if (pair.Value?.obj == null) continue;
                    if (!alive.Contains(pair.Key)) pair.Value.obj.SetActive(false);
                }

                yield return null;
            }

            aimRoutine = null;

            float fadeOut = 0.6f;
            while (fadeOut > 0f)
            {
                fadeOut -= Time.deltaTime;
                foreach (var pair in markers)
                {
                    if (pair.Value?.renderer == null) continue;
                    var color = pair.Value.renderer.color;
                    pair.Value.renderer.color = new Color(color.r, color.g, color.b, Mathf.Max(0f, fadeOut));
                }
                yield return null;
            }

            ClearMarkers();
        }

        private bool ShouldShowAimAssist()
        {
            if (owner == null || owner.Data == null || owner.Data.IsDead) return false;
            if (owner != PlayerControl.LocalPlayer) return false;
            if (MeetingHud.Instance || ExileController.Instance) return false;

            var role = Roles.Sniper.local;
            return role != null && role.hasRifle;
        }

        private Marker GetOrCreateMarker(byte playerId)
        {
            if (markers.TryGetValue(playerId, out var existing) && existing.obj != null)
            {
                existing.obj.SetActive(true);
                return existing;
            }

            var sprite = getGuideSprite();
            if (sprite == null) return null;

            var obj = new GameObject("SniperAimAssist");
            obj.layer = 5;

            var markerRenderer = obj.AddComponent<SpriteRenderer>();
            markerRenderer.sprite = sprite;
            markerRenderer.color = new Color(1f, 1f, 1f, 0f);
            markerRenderer.sortingOrder = 100;

            var marker = new Marker { obj = obj, renderer = markerRenderer };
            markers[playerId] = marker;
            return marker;
        }

        private void ClearMarkers()
        {
            foreach (var pair in markers)
                if (pair.Value?.obj != null) Object.Destroy(pair.Value.obj);

            markers.Clear();
        }

        public void ShowNotice(Vector3 position)
        {
            var arrow = new Arrow(new Color(1f, 1f, 1f, 0.85f));

            if (arrow.image != null)
            {
                var sprite = getArrowSprite();
                if (sprite != null) arrow.image.sprite = sprite;
            }

            arrow.Update(position, new Color(1f, 0.35f, 0.35f, 0.9f));
        }

        public void Destroy()
        {
            StopAimAssist();
            ClearMarkers();

            if (rifleObject != null) Object.Destroy(rifleObject);

            rifleObject = null;
            renderer = null;
        }
    }
}
