using UnityEngine;
using UnityEngine.Serialization;

namespace UnityLab.RTS.Units
{
    /// <summary>
    /// Ranged unit. On top of the base movement it runs a second state machine:
    /// None → Alert (an enemy is in sight) → Shooting (line of sight confirmed with a raycast) and
    /// Chasing when its target walks out of range. While alert it checks one visible enemy per
    /// frame, cycling through them, so a crowd does not cost a raycast per enemy per frame.
    /// </summary>
    public class ArtilleryUnit : Unit
    {
        public enum ArtilleryState
        {
            None,
            Alert,
            Shooting,
            Chasing,
        }

        [Header("Ranged attack")]
        [Tooltip("Seconds between shots.")]
        [FormerlySerializedAs("shotAttackRate")]
        [SerializeField] float shotInterval = 1f;
        [FormerlySerializedAs("hitDamage")]
        [SerializeField] float shotDamage = 2f;
        [Tooltip("Seconds between line-of-sight checks while walking.")]
        [SerializeField] float alertCheckInterval = 1f;

        static readonly Vector3 EyeOffset = new Vector3(0f, 1.2f, 0f);
        static readonly Vector3 TargetOffset = new Vector3(0f, 0.85f, 0f);

        LineRenderer gun;
        float shotTimer;
        float alertTimer;
        int nextEnemyToCheck;

        public ArtilleryState RangedState { get; private set; } = ArtilleryState.None;
        public override Core.UnitKind Kind => Core.UnitKind.Artillery;

        protected override void Start()
        {
            base.Start();
            Transform gunTransform = transform.Find("Gun");
            if (gunTransform != null && gunTransform.TryGetComponent(out gun))
                gun.enabled = false;
            else
                Debug.LogWarning($"{name} has no 'Gun' child with a LineRenderer; shots will not be drawn.", this);
        }

        protected override void UpdateIdle()
        {
            switch (RangedState)
            {
                case ArtilleryState.None:
                case ArtilleryState.Chasing:
                    if (enemiesInSight.Count > 0)
                        RangedState = ArtilleryState.Alert;
                    break;
                case ArtilleryState.Alert:
                    CheckNextVisibleEnemy();
                    break;
                case ArtilleryState.Shooting:
                    UpdateShooting();
                    break;
            }
        }

        protected override void UpdateGoingTo()
        {
            base.UpdateGoingTo();
            if (RangedState == ArtilleryState.Alert && IsValidTarget(currentEnemy))
            {
                alertTimer -= Time.deltaTime;
                if (alertTimer <= 0f)
                {
                    alertTimer = alertCheckInterval;
                    TryAcquire(currentEnemy);
                }
            }
            else if (RangedState == ArtilleryState.Chasing)
            {
                RangedState = ArtilleryState.None;
            }
        }

        void UpdateShooting()
        {
            if (!IsValidTarget(currentEnemy))
            {
                currentEnemy = null;
                RangedState = enemiesInSight.Count > 0 ? ArtilleryState.Alert : ArtilleryState.None;
                return;
            }

            FaceTowards(currentEnemy.transform.position);
            shotTimer += Time.deltaTime;
            if (shotTimer < shotInterval)
                return;
            shotTimer = 0f;

            DrawShot(currentEnemy.transform.position + TargetOffset);
            if (currentEnemy.Health.ApplyDamage(shotDamage))
            {
                currentEnemy = null;
                RangedState = enemiesInSight.Count > 0 ? ArtilleryState.Alert : ArtilleryState.None;
            }
        }

        void DrawShot(Vector3 target)
        {
            if (gun == null)
                return;
            gun.enabled = true;
            gun.SetPosition(0, gun.transform.position);
            gun.SetPosition(1, target);
            CancelInvoke(nameof(HideShot));
            Invoke(nameof(HideShot), 0.1f);
        }

        void HideShot()
        {
            if (gun != null)
                gun.enabled = false;
        }

        public override void OrderMove(Vector3 position)
        {
            base.OrderMove(position);
            RangedState = ArtilleryState.None;
        }

        public override void OnEnemyEnteredVision(Unit enemy)
        {
            base.OnEnemyEnteredVision(enemy);
            if (RangedState == ArtilleryState.None || RangedState == ArtilleryState.Chasing)
            {
                RangedState = ArtilleryState.Alert;
                alertTimer = 0f;
            }
        }

        public override void OnEnemyLeftVision(Unit enemy)
        {
            base.OnEnemyLeftVision(enemy);
            if (enemy == currentEnemy && IsValidTarget(enemy))
            {
                // Follow the target that walked out of range.
                OrderInteract(enemy.transform);
                if (RangedState == ArtilleryState.Shooting)
                    RangedState = ArtilleryState.Chasing;
            }
            else if (enemiesInSight.Count == 0 && RangedState == ArtilleryState.Alert)
            {
                RangedState = ArtilleryState.None;
            }
        }

        /// <summary>Round-robin over the visible enemies; safe when the list shrinks or empties.</summary>
        void CheckNextVisibleEnemy()
        {
            if (enemiesInSight.Count == 0)
            {
                RangedState = ArtilleryState.None;
                return;
            }
            nextEnemyToCheck %= enemiesInSight.Count;
            Unit candidate = enemiesInSight[nextEnemyToCheck];
            nextEnemyToCheck++;
            if (IsValidTarget(candidate))
                TryAcquire(candidate);
        }

        /// <summary>Starts shooting the enemy if nothing blocks the line of sight.</summary>
        void TryAcquire(Unit enemy)
        {
            Vector3 eyes = transform.position + EyeOffset;
            Vector3 direction = enemy.transform.position + Vector3.up - eyes;
            Debug.DrawRay(eyes, direction, Color.yellow, 0.25f);

            if (!Physics.Raycast(eyes, direction, out RaycastHit hit, VisionRadius, ~0, QueryTriggerInteraction.Ignore))
                return;
            if (hit.transform.GetComponentInParent<Unit>() != enemy)
                return;

            RangedState = ArtilleryState.Shooting;
            currentEnemy = enemy;
            shotTimer = 0f;
            if (State == UnitState.GoingTo)
                SetIdle();
        }

        protected override string DebugLabel => $"{State} / {RangedState}";
    }
}
