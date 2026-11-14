using System;
using System.Collections.Generic;
using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    /// <summary>Source radius9 pickup: manual weapons, automatic medkit enter and .12s overlap retry.</summary>
    [RequireComponent(typeof(CircleCollider2D), typeof(Rigidbody2D))]
    public sealed class PickupPort2D : MonoBehaviour
    {
        public string Kind = "medkit";
        public TopdownWorld2D AssignedWorld;
        public PickupModel Model { get; private set; }
        public Vec2 Position => ArkonCoordinates.ToSource(transform.position);
        public event Action<PickupPort2D, TopdownActor2D> Collected;
        private CircleCollider2D trigger;
        private TopdownWorld2D world;
        private readonly List<Collider2D> overlaps = new List<Collider2D>(8);
        private readonly List<IPickupActor> actors = new List<IPickupActor>(1);
        private bool started;

        private void Awake()
        {
            trigger = GetComponent<CircleCollider2D>(); trigger.isTrigger = true;
            trigger.radius = 9 / ArkonCoordinates.PixelsPerUnit; gameObject.layer = 2;
            var body = GetComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0; body.useFullKinematicContacts = true;
            Model = new PickupModel(Kind); Kind = Model.Kind; Model.Collected += OnCollected;
        }
        private void Start() { started = true; BindWorld(); }
        private void OnEnable() { if (started) { BindWorld(); trigger.enabled = !Model.Consumed; } }
        private void BindWorld()
        {
            var candidate = AssignedWorld ? AssignedWorld : SceneObjects.Find<TopdownWorld2D>(this);
            if (!candidate || !candidate.isActiveAndEnabled || candidate.gameObject.scene != gameObject.scene)
            { Debug.LogError("PickupPort2D needs an active TopdownWorld2D in its scene.", this); return; }
            world = candidate; world.Register(this);
        }
        private void OnDisable() { if (world) world.Unregister(this); if (trigger) trigger.enabled = false; }
        private void OnDestroy() { if (Model != null) Model.Collected -= OnCollected; }

        public bool TryCollect(TopdownActor2D actor)
        {
            if (!isActiveAndEnabled || !world || !world.isActiveAndEnabled || Time.timeScale <= 0 || !actor || actor != world.Target) return false;
            var data = world.PickupActor(actor);
            return data != null && Model.Collect(data, world.HasLineOfSight(actor.Position, Position));
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (Model == null || !Model.Automatic || !other || other.gameObject.layer != ArkonCoordinates.BodiesLayer || other.gameObject.scene != gameObject.scene) return;
            TryCollect(other.GetComponentInParent<TopdownActor2D>());
        }

        private void FixedUpdate()
        {
            if (Model == null || !Model.Automatic || Model.Consumed || !world || !world.isActiveAndEnabled || Time.timeScale <= 0) return;
            var filter = new ContactFilter2D { useLayerMask = true, useTriggers = false, layerMask = 1 << ArkonCoordinates.BodiesLayer };
            overlaps.Clear(); trigger.Overlap(filter, overlaps); actors.Clear();
            foreach (var collider in overlaps)
            {
                if (!collider || collider.gameObject.scene != gameObject.scene) continue;
                var actor = collider.GetComponentInParent<TopdownActor2D>();
                var data = world.PickupActor(actor);
                if (data != null && !actors.Contains(data)) actors.Add(data);
            }
            Model.Tick(Time.fixedDeltaTime, actors, _ => world.Target && world.HasLineOfSight(world.Target.Position, Position));
        }

        private void OnCollected(string kind, int actorId)
        {
            var actor = world ? world.Target : null;
            if (world && actor) world.ApplyPickup(this, actor);
            if (trigger) trigger.enabled = false;
            Collected?.Invoke(this, actor);
            Destroy(gameObject);
        }
    }
}
