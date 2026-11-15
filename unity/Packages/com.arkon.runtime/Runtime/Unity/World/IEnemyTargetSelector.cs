using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    /// <summary>
    /// Immutable identity/receiver handle with live position and life checks.
    /// The actor root owns ActorIdentity2D; its actual layer11 hitbox must resolve
    /// to the supplied receiver. No health proxy or artificial immunity is used.
    /// </summary>
    public readonly struct EnemyTarget2D
    {
        public Transform ActorRoot { get; }
        public DamageReceiver2D Receiver { get; }
        public int ActorId { get; }
        private readonly Rigidbody2D body;
        // AI can run later in the same FixedUpdate as an actor's kinematic move.
        // Its physics position is authoritative even before Transform sync or
        // interpolation catches up, matching TopdownActor2D.Position.
        public Vec2 Position => body ? ArkonCoordinates.ToSource(body.position) :
            ActorRoot ? ArkonCoordinates.ToSource(ActorRoot.position) : Vec2.Zero;
        public HealthModel Health => Receiver ? Receiver.Health : null;
        public bool Alive => ActorRoot && ActorRoot.gameObject.activeInHierarchy && Receiver &&
            Receiver.isActiveAndEnabled && !Receiver.Health.Dead;

        public EnemyTarget2D(Transform actorRoot, DamageReceiver2D receiver)
        {
            ActorRoot = actorRoot; Receiver = receiver; ActorId = receiver ? receiver.ActorId : 0;
            body = actorRoot ? actorRoot.GetComponent<Rigidbody2D>() : null;
        }
    }

    /// <summary>
    /// Optional scene-owned selector for player/escort encounters. Selection is
    /// sampled once before each enemy AI tick. A living windup target is held
    /// until that attack resolves; its position and health remain live.
    /// </summary>
    public interface IEnemyTargetSelector
    {
        bool TrySelectTarget(EnemyPort2D enemy, out EnemyTarget2D target);
    }
}
