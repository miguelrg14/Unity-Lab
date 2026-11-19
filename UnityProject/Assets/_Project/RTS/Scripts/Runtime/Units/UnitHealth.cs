using System;
using UnityEngine;
using UnityEngine.Serialization;
using UnityLab.RTS.Core;

namespace UnityLab.RTS.Units
{
    /// <summary>Unity wrapper around the engine-free <see cref="Health"/> rules.</summary>
    public class UnitHealth : MonoBehaviour
    {
        [FormerlySerializedAs("maxLife")]
        [SerializeField, Min(1f)] float maxHealth = 100f;
        [FormerlySerializedAs("invencible")]
        [SerializeField] bool invincible;

        Health health;

        public float Normalized => Model.Normalized;
        public bool IsAlive => Model.IsAlive;
        public float Max => maxHealth;

        /// <summary>Raised once, when health reaches zero.</summary>
        public event Action Died;

        Health Model
        {
            get
            {
                if (health == null)
                {
                    health = new Health(maxHealth) { Invincible = invincible };
                    health.Died += () => Died?.Invoke();
                }
                return health;
            }
        }

        /// <returns>True only for the hit that kills.</returns>
        public bool ApplyDamage(float amount) => Model.ApplyDamage(amount);

        public void Heal(float amount) => Model.Heal(amount);

        /// <summary>Kills the unit regardless of invincibility (used by the Delete key).</summary>
        public void Kill()
        {
            Model.Invincible = false;
            Model.ApplyDamage(Model.Current);
        }
    }
}
