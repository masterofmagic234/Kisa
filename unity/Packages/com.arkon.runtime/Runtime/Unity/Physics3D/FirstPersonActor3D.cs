using System;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    /// <summary>
    /// Movement adapter for the public first_person_actor.gd, not the private Level1 mission controller.
    /// The owner supplies weapon/hitscan behavior through FireRequested and transition ownership through TransitionBlocked.
    /// </summary>
    [RequireComponent(typeof(CharacterController), typeof(CapsuleBodySweep3D), typeof(DamageReceiver3D))]
    public sealed class FirstPersonActor3D : MonoBehaviour
    {
        public Camera ViewCamera;
        public float WalkSpeed = 4, LookSensitivity = .0028f;
        public float MaximumFloorAngleDegrees = 52, FloorSnapLength = .4f;
        public bool InputEnabled = true, TransitionBlocked, CaptureOnPrimaryClick = true;
        [Tooltip("Default Unity legacy Mouse axes have sensitivity 0.1. Adjust this conversion if InputManager differs; core look is radians per actual source pixel.")]
        public float LegacyMousePixelsPerUnit = 10;
        public bool ReadLegacyMouseLook = true;
        public FirstPersonMotor Motor { get; } = new FirstPersonMotor();
        public event Action FireRequested;
        private CapsuleBodySweep3D sweeps;
        private CharacterController body;
        private DamageReceiver3D damage;

        private void Awake()
        {
            sweeps = GetComponent<CapsuleBodySweep3D>(); body = sweeps.Controller; damage = GetComponent<DamageReceiver3D>();
            if (!ViewCamera) ViewCamera = GetComponentInChildren<Camera>();
            body.stepOffset = 0; // The source performs its own up/forward/down sweeps.
            body.minMoveDistance = 0;
            body.skinWidth = Mathf.Max(.0001f, sweeps.SourceSafeMargin);
            body.slopeLimit = MaximumFloorAngleDegrees;
            var pitch = ViewCamera ? -Mathf.DeltaAngle(0, ViewCamera.transform.localEulerAngles.x) * Mathf.Deg2Rad : 0;
            Motor.SetOrientation(-transform.eulerAngles.y * Mathf.Deg2Rad, pitch);
            SynchronizePolicy();
        }
        private void SynchronizePolicy()
        {
            Motor.WalkSpeed = WalkSpeed; Motor.LookSensitivity = LookSensitivity;
            Motor.InputEnabled = InputEnabled; Motor.TransitionBlocked = TransitionBlocked;
            Motor.Dead = damage && damage.Health.Dead;
        }
        public void SetPointerCaptured(bool value)
        {
            Cursor.lockState = value ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !value;
        }
        /// <summary>Top-left pixel axes: +X right, +Y down. Touch/new-input adapters can supply actual deltas here.</summary>
        public bool ApplySourceLookPixels(Vec2 relativePixels, bool pointerCaptured)
        {
            SynchronizePolicy();
            if (!Motor.ApplyLookPixels(relativePixels, pointerCaptured)) return false;
            transform.rotation = Quaternion.Euler(0, -Motor.Yaw * Mathf.Rad2Deg, 0);
            if (ViewCamera) ViewCamera.transform.localRotation = Quaternion.Euler(-Motor.Pitch * Mathf.Rad2Deg, 0, 0);
            return true;
        }
        private void Update()
        {
            if (Time.timeScale <= 0) return;
            SynchronizePolicy();
            if (!Motor.InputEnabled || Motor.Dead) return;
            if (CaptureOnPrimaryClick && Input.GetMouseButtonDown(0)) SetPointerCaptured(true);
            if (Cursor.lockState != CursorLockMode.Locked) return;
            if (ReadLegacyMouseLook)
                ApplySourceLookPixels(new Vec2(Input.GetAxisRaw("Mouse X") * LegacyMousePixelsPerUnit,
                    -Input.GetAxisRaw("Mouse Y") * LegacyMousePixelsPerUnit), true);
            if (Input.GetMouseButtonDown(0)) FireRequested?.Invoke();
        }
        private void FixedUpdate()
        {
            SynchronizePolicy();
            var axis = new Vec2((Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0),
                (Input.GetKey(KeyCode.S) ? 1 : 0) - (Input.GetKey(KeyCode.W) ? 1 : 0)).LimitLength();
            var grounded = body.isGrounded; var dt = Time.fixedDeltaTime;
            if (!Motor.PrepareMovement(axis, grounded, dt)) return;
            var wanted = Motor.Velocity;
            var horizontal = new MotionVector3(wanted.X, 0, wanted.Z) * dt;
            var up = CapsuleBodySweep3D.ToSource(transform.up).Normalized;
            var origin = CapsuleBodySweep3D.ToSource(transform.position);
            CollisionFlags flags;
            if (GroundedStepMotion3D.TryPlan(sweeps, origin, horizontal, up, grounded, wanted.Y,
                MaximumFloorAngleDegrees * Mathf.Deg2Rad, sweeps.SourceSafeMargin, out var step))
            {
                // Every executed leg re-enters native collision recovery if another actor has moved since prediction.
                body.Move(CapsuleBodySweep3D.ToUnity(step.Up));
                body.Move(CapsuleBodySweep3D.ToUnity(step.Forward));
                body.Move(CapsuleBodySweep3D.ToUnity(step.Down));
                SnapToFloor(up);
                // Horizontal displacement was consumed; never apply it a second time.
                flags = body.Move(CapsuleBodySweep3D.ToUnity(up * (FirstPersonMotor.GroundedPull * dt)));
            }
            else
            {
                flags = body.Move(CapsuleBodySweep3D.ToUnity(wanted * dt));
                if (grounded && wanted.Y <= 0 && !body.isGrounded) SnapToFloor(up);
            }
            if ((flags & CollisionFlags.Below) != 0 || body.isGrounded) Motor.SetVerticalVelocity(0);
            else if ((flags & CollisionFlags.Above) != 0 && Motor.Velocity.Y > 0) Motor.SetVerticalVelocity(0);
        }
        private void SnapToFloor(MotionVector3 up)
        {
            if (body.isGrounded || FloorSnapLength <= 0) return;
            var hit = sweeps.TestMotion(CapsuleBodySweep3D.ToSource(transform.position), -up * FloorSnapLength, sweeps.SourceSafeMargin, 4);
            if (!hit.Collided) return;
            var threshold = Mathf.Cos(MaximumFloorAngleDegrees * Mathf.Deg2Rad);
            foreach (var normal in hit.Normals)
                if (MotionVector3.Dot(normal, up) >= threshold) { body.Move(CapsuleBodySweep3D.ToUnity(hit.Travel)); return; }
        }
    }
}
