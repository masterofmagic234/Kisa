using Arkon.Core;
using UnityEngine;

namespace Arkon.Unity
{
    public sealed class DamageReceiver2D : MonoBehaviour
    {
        public int Maximum = 100;
        public float Invulnerability = .24f;
        private HealthModel health;
        public HealthModel Health => health ?? (health = new HealthModel(Maximum, Invulnerability));
        public int ActorId
        {
            get
            {
                var owner = GetComponentInParent<ActorIdentity2D>();
                return owner ? owner.Id : gameObject.GetInstanceID();
            }
        }
        private void Awake() { var model = Health; }
        private void Update() { Health.Tick(Time.deltaTime); }
        public bool ApplyDamage(int amount, int sourceId) => Health.ApplyDamage(amount, sourceId);
    }
}
