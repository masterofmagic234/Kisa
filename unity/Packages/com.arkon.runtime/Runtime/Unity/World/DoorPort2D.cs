using System;
using System.Collections.Generic;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    /// <summary>Real 2D leaf/overlap adapter for topdown/level3_door.gd and its authored hinge geometry.</summary>
    public sealed class DoorPort2D : MonoBehaviour
    {
        public TopdownWorld2D AssignedWorld;
        public Transform LeafPivot;
        public BoxCollider2D SolidCollider;
        public BoxCollider2D SlamArea;
        public bool Locked;
        public float OpenSpeed = 12, SlamSpeed = 30, CloseSpeed = 7.5f, AutoCloseDelay = 3;
        public DoorModel Model { get; private set; }
        public Vec2 Position => ArkonCoordinates.ToSource(transform.position);
        public event Action<DoorPort2D> Opened;
        public event Action<DoorPort2D> Slammed;
        public event Action<DoorPort2D> Closed;
        private TopdownWorld2D world;
        private Rigidbody2D leafBody;
        private float closedLocalAngle;
        private readonly List<Collider2D> overlaps = new List<Collider2D>(16);
        private bool started;

        private void Awake()
        {
            if (!LeafPivot)
            {
                var pivot = new GameObject("DoorLeafPivot"); pivot.transform.SetParent(transform, false);
                pivot.transform.localPosition = new Vector3(-16 / ArkonCoordinates.PixelsPerUnit, 0, 0);
                LeafPivot = pivot.transform;
            }
            if (LeafPivot == transform || !LeafPivot.IsChildOf(transform))
            { Debug.LogError("Door leaf pivot must be a child of the door root.", this); enabled = false; return; }
            LeafPivot.gameObject.layer = ArkonCoordinates.WallsLayer;
            leafBody = LeafPivot.GetComponent<Rigidbody2D>();
            if (!leafBody) leafBody = LeafPivot.gameObject.AddComponent<Rigidbody2D>();
            leafBody.bodyType = RigidbodyType2D.Kinematic; leafBody.gravityScale = 0; leafBody.useFullKinematicContacts = true;
            if (!SolidCollider) SolidCollider = LeafPivot.gameObject.AddComponent<BoxCollider2D>();
            if (SolidCollider.transform != LeafPivot)
            { Debug.LogError("Door solid collider must be on the leaf pivot.", this); enabled = false; return; }
            SolidCollider.isTrigger = false;
            SolidCollider.size = new Vector2(32, 5) / ArkonCoordinates.PixelsPerUnit;
            SolidCollider.offset = new Vector2(16 / ArkonCoordinates.PixelsPerUnit, 0);
            if (!SlamArea)
            {
                var area = new GameObject("DoorSlamArea"); area.transform.SetParent(LeafPivot, false);
                area.layer = 2; // No collision layer in the source; queried as an area, never a wall.
                area.transform.localPosition = new Vector3(16 / ArkonCoordinates.PixelsPerUnit, 0, 0);
                SlamArea = area.AddComponent<BoxCollider2D>();
            }
            if (SlamArea.transform == LeafPivot || !SlamArea.transform.IsChildOf(LeafPivot))
            { Debug.LogError("Door slam area must be on a separate child of the leaf pivot.", this); enabled = false; return; }
            SlamArea.gameObject.layer = 2;
            SlamArea.isTrigger = true; SlamArea.size = new Vector2(36, 14) / ArkonCoordinates.PixelsPerUnit;
            SlamArea.offset = SlamArea.transform.InverseTransformPoint(LeafPivot.TransformPoint(new Vector3(16 / ArkonCoordinates.PixelsPerUnit, 0, 0)));
            SlamArea.enabled = false;
            closedLocalAngle = LeafPivot.localEulerAngles.z;
            Model = new DoorModel(Position, Locked, -transform.eulerAngles.z * Mathf.Deg2Rad, -closedLocalAngle * Mathf.Deg2Rad);
            Model.OpenSpeed = OpenSpeed; Model.SlamSpeed = SlamSpeed; Model.CloseSpeed = CloseSpeed; Model.AutoCloseDelay = AutoCloseDelay;
            Model.Opened += OnOpened; Model.Slammed += OnSlammed; Model.Closed += OnClosed;
        }

        private void Start() { started = true; BindWorld(); }
        private void OnEnable() { if (started) { BindWorld(); ApplyPose(); } }
        private void BindWorld()
        {
            var candidate = AssignedWorld ? AssignedWorld : SceneObjects.Find<TopdownWorld2D>(this);
            if (!candidate || !candidate.isActiveAndEnabled || candidate.gameObject.scene != gameObject.scene)
            { Debug.LogError("DoorPort2D needs an active TopdownWorld2D in its scene.", this); return; }
            world = candidate; world.Register(this);
        }

        private void OnDisable()
        {
            if (world) world.Unregister(this);
            if (SolidCollider) SolidCollider.enabled = false;
            if (SlamArea) SlamArea.enabled = false;
        }
        private void OnDestroy()
        {
            if (Model != null) { Model.Opened -= OnOpened; Model.Slammed -= OnSlammed; Model.Closed -= OnClosed; }
        }

        public bool Interact(Vec2 interactorPosition, bool dynamicSlam = false)
        {
            if (!isActiveAndEnabled || !world || !world.isActiveAndEnabled || Time.timeScale <= 0 || Model == null) return false;
            Model.Position = Position; Model.GlobalRotation = -transform.eulerAngles.z * Mathf.Deg2Rad; Model.Locked = Locked;
            // Source uses pivot position for side choice, but door root for slam knockback.
            var hinge = ArkonCoordinates.ToSource(LeafPivot.position);
            var accepted = Model.Interact(interactorPosition + Position - hinge, dynamicSlam);
            ApplyPose();
            return accepted;
        }

        private void FixedUpdate()
        {
            if (Model == null || !world || !world.isActiveAndEnabled || world.Transitioning || Time.timeScale <= 0) return;
            Model.Position = Position; Model.Locked = Locked;
            Model.Tick(Time.fixedDeltaTime, DoorwayBlocked);
            ApplyPose();
            if (!Model.HitMonitoring || !SlamArea.enabled) return;
            Physics2D.SyncTransforms(); overlaps.Clear(); SlamArea.Overlap(BodyFilter(), overlaps);
            foreach (var collider in overlaps)
            {
                if (!Local(collider)) continue;
                var enemy = collider.GetComponentInParent<EnemyPort2D>();
                if (enemy && enemy.Model != null && Model.TrySlamHit(enemy.ActorId, enemy.Position,
                    enemy.Model.State == EnemyState.Dead, out var hit)) enemy.Stun(hit.StunSeconds, hit.Knockback);
            }
        }

        private void ApplyPose()
        {
            if (Model == null || !LeafPivot) return;
            LeafPivot.localRotation = Quaternion.Euler(0, 0, ArkonCoordinates.ToUnityAngle(Model.Rotation));
            if (SolidCollider) SolidCollider.enabled = isActiveAndEnabled && Model.ColliderEnabled;
            if (SlamArea) SlamArea.enabled = isActiveAndEnabled && Model.HitMonitoring;
        }

        private bool DoorwayBlocked()
        {
            // Model reaches the closed angle before this callback, but Transform still has last tick's pose.
            // Query the AUTHORED CLOSED rectangle explicitly; never use the currently moving collider pose.
            var worldAngle = LeafPivot.parent.eulerAngles.z + closedLocalAngle;
            var offset = Vector2.Scale(SolidCollider.offset, Abs(LeafPivot.lossyScale));
            var center = (Vector2)LeafPivot.position + (Vector2)(Quaternion.Euler(0, 0, worldAngle) * (Vector3)offset);
            var size = Vector2.Scale(SolidCollider.size, Abs(LeafPivot.lossyScale));
            Physics2D.SyncTransforms(); overlaps.Clear(); Physics2D.OverlapBox(center, size, worldAngle, BodyFilter(), overlaps);
            foreach (var collider in overlaps)
                if (Local(collider) && (collider.GetComponentInParent<TopdownActor2D>() || collider.GetComponentInParent<EnemyPort2D>())) return true;
            return false;
        }

        private bool Local(Collider2D collider) => collider && collider.gameObject.scene == gameObject.scene;
        private static Vector2 Abs(Vector3 value) => new Vector2(Mathf.Abs(value.x), Mathf.Abs(value.y));
        private static ContactFilter2D BodyFilter() => new ContactFilter2D { useLayerMask = true, useTriggers = false, layerMask = 1 << ArkonCoordinates.BodiesLayer };
        private void OnOpened() { ApplyPose(); Opened?.Invoke(this); }
        private void OnSlammed() { ApplyPose(); Slammed?.Invoke(this); }
        private void OnClosed() { ApplyPose(); Closed?.Invoke(this); }
    }
}
