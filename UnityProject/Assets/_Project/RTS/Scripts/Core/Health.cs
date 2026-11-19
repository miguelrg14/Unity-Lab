using System;

namespace UnityLab.RTS.Core
{
    /// <summary>Hit points of a unit or building.</summary>
    public sealed class Health
    {
        public Health(float max)
        {
            if (max <= 0f)
                throw new ArgumentOutOfRangeException(nameof(max), "Maximum health must be positive.");
            Max = max;
            Current = max;
        }

        public float Max { get; }
        public float Current { get; private set; }
        public float Normalized => Current / Max;
        public bool IsAlive => Current > 0f;
        public bool Invincible { get; set; }

        /// <summary>Raised once, on the hit that takes health to zero.</summary>
        public event Action Died;

        /// <summary>Applies damage.</summary>
        /// <returns>True only for the hit that kills, so exactly one attacker gets the kill.</returns>
        public bool ApplyDamage(float amount)
        {
            if (amount < 0f)
                throw new ArgumentOutOfRangeException(nameof(amount), "Use Heal to restore health.");
            if (!IsAlive || Invincible)
                return false;

            Current = Math.Max(0f, Current - amount);
            if (IsAlive)
                return false;

            Died?.Invoke();
            return true;
        }

        /// <summary>Restores health without exceeding the maximum. The dead cannot be healed.</summary>
        public void Heal(float amount)
        {
            if (amount < 0f)
                throw new ArgumentOutOfRangeException(nameof(amount));
            if (IsAlive)
                Current = Math.Min(Max, Current + amount);
        }
    }
}
