using System.Collections.Generic;
using UnityEngine;
using TheOtherRoles.Utilities;

namespace TheOtherRoles.Objects
{
    public static class RacerCar
    {
        private const float BodyPixelsPerUnit = 528f;

        private const float WheelPixelsPerUnit = 950f;
        private const float WheelDiameterUnits = 400f / WheelPixelsPerUnit;
        private const float WheelCircumferenceUnits = Mathf.PI * WheelDiameterUnits;

        private static readonly Vector3 LeftWheelOffset = new(-0.50f, -0.22f, -0.01f);
        private static readonly Vector3 RightWheelOffset = new(0.69f, -0.22f, -0.01f);

        // Seat position minus body pivot, at default (unflipped) facing.
        private static readonly Vector2 SeatOffset = new(0f, 0.08f);

        private const float FlipSpeed = 6f;
        private const float MovementDeadzone = 0.0006f;

        private class CarVisual
        {
            public GameObject body;
            public GameObject wheelLeft;
            public GameObject wheelRight;
            public Vector3 lastPosition;
            public float targetScaleX = 1f;
            public float currentScaleX = 1f;
            public bool hasLastPosition;
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

        private static GameObject createWheel(Transform parent, Vector3 localOffset)
        {
            var wheel = new GameObject("RacerCarWheel") { layer = 11 };
            wheel.transform.SetParent(parent);
            wheel.transform.localPosition = localOffset;
            var wheelRenderer = wheel.AddComponent<SpriteRenderer>();
            wheelRenderer.sprite = getCarWheelSprite();
            return wheel;
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

            cars[ownerId] = new CarVisual
            {
                body = body,
                wheelLeft = createWheel(body.transform, LeftWheelOffset),
                wheelRight = createWheel(body.transform, RightWheelOffset),
                lastPosition = initialPosition
            };
        }

        public static void UpdateVisual(byte ownerId, Vector3 driverPosition)
        {
            if (!cars.TryGetValue(ownerId, out var car) || car.body == null) return;

            Vector3 delta = car.hasLastPosition ? driverPosition - car.lastPosition : Vector3.zero;
            car.lastPosition = driverPosition;
            car.hasLastPosition = true;

            bool isMoving = delta.sqrMagnitude > MovementDeadzone * MovementDeadzone;
            if (isMoving)
            {
                bool horizontalDominant = Mathf.Abs(delta.x) >= Mathf.Abs(delta.y);

                float rotationDegrees;
                if (horizontalDominant)
                {
                    if (delta.x > 0f) car.targetScaleX = -1f;
                    else if (delta.x < 0f) car.targetScaleX = 1f;
                    // Unity mirrors the wheels' local rotation automatically when the body's
                    // scale.x flips, so a single fixed spin sign looks correct facing either way.
                    rotationDegrees = -(Mathf.Abs(delta.x) / WheelCircumferenceUnits) * 360f;
                }
                else
                {
                    rotationDegrees = (delta.magnitude / WheelCircumferenceUnits) * 360f;
                }

                if (car.wheelLeft != null) car.wheelLeft.transform.Rotate(0f, 0f, rotationDegrees);
                if (car.wheelRight != null) car.wheelRight.transform.Rotate(0f, 0f, rotationDegrees);
            }

            car.currentScaleX = Mathf.MoveTowards(car.currentScaleX, car.targetScaleX, FlipSpeed * Time.fixedDeltaTime);
            car.body.transform.localScale = new Vector3(car.currentScaleX, 1f, 1f);
            car.body.transform.position = pivotPosition(driverPosition, car.currentScaleX);
        }

        public static void DespawnVisual(byte ownerId)
        {
            if (cars.TryGetValue(ownerId, out var car) && car.body != null)
                Object.Destroy(car.body);
            cars.Remove(ownerId);
        }

        public static void DespawnAll()
        {
            foreach (var car in cars.Values)
                if (car.body != null) Object.Destroy(car.body);
            cars.Clear();
        }
    }
}
