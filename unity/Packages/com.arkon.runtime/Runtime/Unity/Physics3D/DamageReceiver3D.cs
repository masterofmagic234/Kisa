using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    public sealed class DamageReceiver3D : MonoBehaviour
    {
        public int Maximum = 100;
        public float Invulnerability = .24f;
        public int ExplicitActorId;
        private HealthModel health;
        public HealthModel Health => health ?? (health = new HealthModel(Maximum, Invulnerability));
        public int ActorId => ExplicitActorId != 0 ? ExplicitActorId : gameObject.GetInstanceID();
        private void Awake() { var model = Health; }
        private void Update() { Health.Tick(Time.deltaTime); }
        public bool ApplyDamage(int amount, int sourceId = 0) => Health.ApplyDamage(amount, sourceId);
    }
}
