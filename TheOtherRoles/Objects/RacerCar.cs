using System;
using System.Collections.Generic;
using UnityEngine;
using TheOtherRoles.Utilities;

namespace TheOtherRoles.Objects
{
    public static class RacerCar
    {
        private const float BodyPixelsPerUnit = 528f;
        private const float WheelPixelsPerUnit = 950f;
        private const float WheelCircumferenceUnits = Mathf.PI * 400f / WheelPixelsPerUnit;
        private static readonly Vector3 LeftWheelOffset = new(-0.50f, -0.22f, -0.01f);
        private static readonly Vector3 RightWheelOffset = new(0.69f, -0.22f, -0.01f);
        private static readonly Vector2 SeatOffset = new(0f, 0.08f);
        private const float FlipSpeed = 6f;
        public const float MovementDeadzone = 0.0006f; // ignore jitter smaller than this so the car doesn't flip/spin while standing still

        private const int GearPipCount = 3;
        private const float GearPipSize = 0.09f;
        private const float GearPipSpacing = 0.13f;
        private static readonly Vector3 GearPipRowOffset = new(0f, 0.55f, -0.02f);
        private static readonly Color GearPipOffColor = new(0.25f, 0.25f, 0.25f, 0.6f);
        private static readonly Color[] GearPipOnColors =
        {
            new(0.35f, 0.85f, 0.35f),
            new(0.95f, 0.85f, 0.2f),
            new(0.95f, 0.3f, 0.25f)
        };

        private static Sprite gearPipSprite;

        // driver position only updates on FixedUpdate, so lerp toward it on every frame instead
        // of snapping - otherwise the car looks jerky above 50fps
        public class PositionSmoother : MonoBehaviour
        {
            static PositionSmoother() => ClassInjector.RegisterTypeInIl2Cpp<PositionSmoother>();
            public PositionSmoother(IntPtr ptr) : base(ptr) { }

            private Vector3 fromPos;
            private Vector3 toPos;
            private float fromTime;
            private float toTime;
            private bool hasTarget;

            public void SetTarget(Vector3 target)
            {
                float now = Time.time;
                if (!hasTarget)
                {
                    transform.position = target;
                    hasTarget = true;
                }
                fromPos = transform.position;
                toPos = target;
                fromTime = now;
                toTime = now + Mathf.Max(Time.fixedDeltaTime, 0.001f);
            }

            public void Update()
            {
                if (!hasTarget) return;
                float t = toTime > fromTime ? Mathf.Clamp01((Time.time - fromTime) / (toTime - fromTime)) : 1f;
                transform.position = Vector3.LerpUnclamped(fromPos, toPos, t);
            }
        }

        private class CarVisual
        {
            public GameObject body;
            public PositionSmoother bodySmoother;
            public GameObject wheelLeft;
            public GameObject wheelRight;
            public SpriteRenderer[] gearPips;
            public int lastGear = -1;
            public float targetScaleX = 1f;
            public float currentScaleX = 1f;
        }

        private static Dictionary<byte, CarVisual> cars = new();

        private static Sprite bodySprite;
        public static Sprite getCarBodySprite()
        {
            if (bodySprite) return bodySprite;
            bodySprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.RacerCarBody.png", BodyPixelsPerUnit);
            return bodySprite;
        }

        private static Sprite wheelSprite;
        public static Sprite getCarWheelSprite()
        {
            if (wheelSprite) return wheelSprite;
            wheelSprite = Helpers.loadSpriteFromResources("TheOtherRoles.Resources.RacerCarWheel.png", WheelPixelsPerUnit);
            return wheelSprite;
        }

        private static SpriteRenderer createChild(string name, Transform parent, Vector3 localOffset)
        {
            var child = new GameObject(name) { layer = 11 };
            child.transform.SetParent(parent);
            child.transform.localPosition = localOffset;
            return child.AddComponent<SpriteRenderer>();
        }

        private static GameObject createWheel(Transform parent, Vector3 localOffset)
        {
            var wheelRenderer = createChild("RacerCarWheel", parent, localOffset);
            wheelRenderer.sprite = getCarWheelSprite();
            return wheelRenderer.gameObject;
        }

        private static Sprite getGearPipSprite()
        {
            if (gearPipSprite) return gearPipSprite;

            var tex = new Texture2D(4, 4, TextureFormat.ARGB32, false);
            var pixels = new Color[16];
            for (int i = 0; i < 16; i++) pixels[i] = Color.white;
            tex.SetPixels(pixels);
            tex.Apply();

            gearPipSprite = Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f), 4f / GearPipSize);
            return gearPipSprite;
        }

        private static SpriteRenderer createGearPip(Transform parent, int index)
        {
            float totalWidth = (GearPipCount - 1) * GearPipSpacing;
            float x = -totalWidth / 2f + index * GearPipSpacing;
            var pipRenderer = createChild("RacerCarGearPip", parent, GearPipRowOffset + new Vector3(x, 0f, 0f));
            pipRenderer.sprite = getGearPipSprite();
            pipRenderer.color = GearPipOffColor;
            return pipRenderer;
        }

        private static Vector3 pivotPosition(Vector3 driverPosition, float scaleX)
        {
            float x = driverPosition.x - SeatOffset.x * scaleX;
            float y = driverPosition.y - SeatOffset.y;
            float z = driverPosition.y / 1000f - 0.01f;
            return new Vector3(x, y, z);
        }

        public static void SpawnVisual(byte ownerId, Vector3 initialPosition)
        {
            if (cars.ContainsKey(ownerId)) return;

            var body = new GameObject("RacerCarBody") { layer = 11 };
            body.transform.position = pivotPosition(initialPosition, 1f);
            var bodyRenderer = body.AddComponent<SpriteRenderer>();
            bodyRenderer.sprite = getCarBodySprite();

            var bodySmoother = body.AddComponent<PositionSmoother>();

            var gearPips = new SpriteRenderer[GearPipCount];
            for (int i = 0; i < GearPipCount; i++) gearPips[i] = createGearPip(body.transform, i);

            cars[ownerId] = new CarVisual
            {
                body = body,
                bodySmoother = bodySmoother,
                wheelLeft = createWheel(body.transform, LeftWheelOffset),
                wheelRight = createWheel(body.transform, RightWheelOffset),
                gearPips = gearPips
            };
        }

        private static void updateGearPips(CarVisual car, int gear)
        {
            if (car.gearPips == null || car.lastGear == gear) return;
            car.lastGear = gear;

            for (int i = 0; i < car.gearPips.Length; i++)
            {
                var pipRenderer = car.gearPips[i];
                if (pipRenderer == null) continue;
                pipRenderer.color = i < gear ? GearPipOnColors[i] : GearPipOffColor;
            }
        }

        private static void updateWheels(CarVisual car, Vector3 delta)
        {
            if (delta.sqrMagnitude <= MovementDeadzone * MovementDeadzone) return;

            float rotationDegrees;
            if (Mathf.Abs(delta.x) >= Mathf.Abs(delta.y))
            {
                if (delta.x > 0f) car.targetScaleX = -1f;
                else if (delta.x < 0f) car.targetScaleX = 1f;
                rotationDegrees = -(Mathf.Abs(delta.x) / WheelCircumferenceUnits) * 360f;
            }
            else
            {
                rotationDegrees = (delta.magnitude / WheelCircumferenceUnits) * 360f;
            }

            if (car.wheelLeft != null) car.wheelLeft.transform.Rotate(0f, 0f, rotationDegrees);
            if (car.wheelRight != null) car.wheelRight.transform.Rotate(0f, 0f, rotationDegrees);
        }

        private static void updateFlip(CarVisual car)
        {
            car.currentScaleX = Mathf.MoveTowards(car.currentScaleX, car.targetScaleX, FlipSpeed * Time.fixedDeltaTime);
            car.body.transform.localScale = new Vector3(car.currentScaleX, 1f, 1f);
        }

        public static void UpdateVisual(byte ownerId, Vector3 driverPosition, Vector3 delta, int gear)
        {
            if (!cars.TryGetValue(ownerId, out var car) || car.body == null) return;

            updateWheels(car, delta);
            updateFlip(car);
            car.bodySmoother.SetTarget(pivotPosition(driverPosition, car.currentScaleX));
            updateGearPips(car, gear);
        }

        private static void destroyVisual(CarVisual car)
        {
            if (car != null && car.body != null) UnityEngine.Object.Destroy(car.body);
        }

        public static void DespawnVisual(byte ownerId)
        {
            if (cars.TryGetValue(ownerId, out var car)) destroyVisual(car);
            cars.Remove(ownerId);
        }

        public static void DespawnAll()
        {
            foreach (var car in cars.Values) destroyVisual(car);
            cars.Clear();
        }
    }
}
