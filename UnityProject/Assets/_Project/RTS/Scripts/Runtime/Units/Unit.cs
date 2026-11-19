using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;
using UnityLab.RTS.Army;
using UnityLab.RTS.Core;
using UnityLab.RTS.Selection;

namespace UnityLab.RTS.Units
{
    /// <summary>
    /// Base unit: a NavMesh agent driven by a small state machine (Idle, GoingTo, Attacking, Dying)
    /// that walks where it is ordered and fights in melee. Specialised units (artillery, harvester)
    /// override the per-state updates and the reactions to orders and to enemies coming into view.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Selectable), typeof(TeamMember))]
    [RequireComponent(typeof(UnitHealth))]
    public class Unit : MonoBehaviour, IVisionAgent
    {
        public enum UnitState
        {
            Idle,
            GoingTo,
            Attacking,
            Dying,
        }

        [Header("Movement")]
        [Tooltip("Distance at which a destination counts as reached.")]
        [FormerlySerializedAs("destinyThreshold")]
        [SerializeField] float arrivalThreshold = 0.5f;
        [FormerlySerializedAs("visionSphereRadius")]
        [SerializeField] float visionRadius = 10f;
        [Tooltip("Radius used to decide when two units are close enough to fight in melee.")]
        [SerializeField] float boundingRadius = 1f;

        [Header("Melee")]
        [Tooltip("Seconds between melee hits.")]
        [SerializeField] float attackRate = 2f;
        [FormerlySerializedAs("meleeAttack")]
        [SerializeField] float meleeDamage = 5f;

        static Texture2D healthBackground, healthFill;

        /// <summary>Debug labels with each unit's state (toggled with F1 by the HUD).</summary>
        public static bool ShowStateLabels { get; set; }

        protected readonly List<Unit> enemiesInSight = new List<Unit>();
        protected Unit currentEnemy;
        Vector3 currentDestination;
        float attackCooldown;
        Camera mainCamera;

        public NavMeshAgent Agent { get; private set; }
        public Selectable Selectable { get; private set; }
        public TeamMember Team { get; private set; }
        public UnitHealth Health { get; private set; }
        public ArmyBase Home { get; set; }
        public virtual UnitKind Kind => UnitKind.Melee;
        public UnitState State { get; private set; } = UnitState.Idle;
        public bool IsAlive => State != UnitState.Dying && Health.IsAlive;
        public float BoundingRadius => boundingRadius;
        public IReadOnlyList<Unit> EnemiesInSight => enemiesInSight;
        public Unit CurrentEnemy => currentEnemy;

        /// <summary>Position on screen, refreshed every frame for rectangle selection.</summary>
        public Vector3 ScreenPosition { get; private set; }

        public float X => transform.position.x;
        public float Z => transform.position.z;
        public float VisionRadius => visionRadius;

        /// <summary>Raised when the unit dies, before it is removed from the scene.</summary>
        public event Action<Unit> Died;

        protected virtual void Awake()
        {
            Agent = GetComponent<NavMeshAgent>();
            Selectable = GetComponent<Selectable>();
            Team = GetComponent<TeamMember>();
            Health = GetComponent<UnitHealth>();
            Health.Died += OnDied;
        }

        protected virtual void Start()
        {
            mainCamera = Camera.main;
            if (healthBackground == null)
            {
                healthBackground = MakeTexture(new Color(0.2f, 0.05f, 0.05f, 0.9f));
                healthFill = MakeTexture(new Color(0.35f, 0.9f, 0.4f, 1f));
            }
        }

        static Texture2D MakeTexture(Color color)
        {
            var texture = new Texture2D(1, 1) { hideFlags = HideFlags.HideAndDontSave };
            texture.SetPixel(0, 0, color);
            texture.Apply();
            return texture;
        }

        protected virtual void Update()
        {
            if (mainCamera != null)
                ScreenPosition = mainCamera.WorldToScreenPoint(transform.position);

            switch (State)
            {
                case UnitState.Idle: UpdateIdle(); break;
                case UnitState.GoingTo: UpdateGoingTo(); break;
                case UnitState.Attacking: UpdateAttacking(); break;
            }
        }

        protected virtual void UpdateIdle() { }

        protected virtual void UpdateGoingTo()
        {
            float squaredToDestination = (currentDestination - transform.position).sqrMagnitude;
            if (IsValidTarget(currentEnemy))
            {
                float squaredToEnemy = (currentEnemy.transform.position - transform.position).sqrMagnitude;
                if (squaredToEnemy <= visionRadius * visionRadius)
                {
                    // Chase: keep updating the destination to where the enemy is now.
                    currentDestination = currentEnemy.transform.position;
                    Agent.SetDestination(currentDestination);
                    if (InMeleeRange(currentEnemy))
                    {
                        Agent.isStopped = true;
                        State = UnitState.Attacking;
                    }
                    return;
                }
            }
            else
            {
                currentEnemy = null;
            }

            if (squaredToDestination <= arrivalThreshold * arrivalThreshold || HasArrived())
                SetIdle();
        }

        protected virtual void UpdateAttacking()
        {
            if (!IsValidTarget(currentEnemy))
            {
                currentEnemy = null;
                SetIdle();
                return;
            }

            attackCooldown -= Time.deltaTime;
            if (InMeleeRange(currentEnemy))
            {
                FaceTowards(currentEnemy.transform.position);
                if (attackCooldown <= 0f)
                {
                    attackCooldown = attackRate;
                    if (currentEnemy.Health.ApplyDamage(meleeDamage))
                    {
                        currentEnemy = null;
                        SetIdle();
                    }
                }
            }
            else if ((currentEnemy.transform.position - transform.position).sqrMagnitude <= visionRadius * visionRadius)
            {
                GoTo(currentEnemy.transform.position);
            }
            else
            {
                currentEnemy = null;
                SetIdle();
            }
        }

        protected bool InMeleeRange(Unit enemy)
        {
            float reach = boundingRadius + enemy.boundingRadius;
            return (enemy.transform.position - transform.position).sqrMagnitude <= reach * reach;
        }

        /// <summary>
        /// The agent drops its path once it reaches the stopping distance, so "no path and not
        /// moving" also counts as arrived; a destination it cannot get closer to ends the order too.
        /// </summary>
        bool HasArrived()
        {
            if (Agent.pathPending)
                return false;
            if (Agent.hasPath)
                return Agent.remainingDistance <= Agent.stoppingDistance + arrivalThreshold;
            return Agent.velocity.sqrMagnitude < 0.01f;
        }

        protected static bool IsValidTarget(Unit unit) => unit != null && unit.IsAlive;

        protected void FaceTowards(Vector3 point)
        {
            Vector3 direction = Vector3.ProjectOnPlane(point - transform.position, Vector3.up);
            if (direction.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(direction);
        }

        protected void SetIdle()
        {
            if (Agent.isOnNavMesh)
                Agent.isStopped = true;
            State = UnitState.Idle;
        }

        public void SetTeamColor(Color color)
        {
            Team.Color = color;
            Selectable.SetTeamColor(color);
        }

        /// <summary>Right click on the ground: walk there, forgetting any target.</summary>
        public virtual void OrderMove(Vector3 position)
        {
            currentEnemy = null;
            GoTo(position);
        }

        /// <summary>Right click on something: attack it if it is an enemy unit, otherwise walk to it.</summary>
        public virtual void OrderInteract(Transform target)
        {
            if (target.TryGetComponent(out Unit unit) && Team.IsEnemyOf(unit.Team))
            {
                currentEnemy = unit;
                GoTo(target.position);
            }
            else
            {
                currentEnemy = null;
                GoTo(target.position);
            }
        }

        /// <summary>Attack a specific enemy (used by the AI commander).</summary>
        public void OrderAttack(Unit enemy)
        {
            if (IsValidTarget(enemy))
                OrderInteract(enemy.transform);
        }

        public void GoTo(Vector3 destination)
        {
            if (State == UnitState.Dying || !Agent.isOnNavMesh)
                return;
            currentDestination = destination;
            Agent.isStopped = false;
            Agent.SetDestination(destination);
            State = UnitState.GoingTo;
        }

        public virtual void OnEnemyEnteredVision(Unit enemy)
        {
            if (!enemiesInSight.Contains(enemy))
                enemiesInSight.Add(enemy);
        }

        public virtual void OnEnemyLeftVision(Unit enemy) => enemiesInSight.Remove(enemy);

        protected virtual void OnDied()
        {
            if (Agent.isOnNavMesh)
                Agent.isStopped = true;
            Agent.enabled = false;
            State = UnitState.Dying;
            currentEnemy = null;
            enemiesInSight.Clear();
            if (VisionSystem.Instance != null)
                VisionSystem.Instance.Unregister(this);
            Died?.Invoke(this);
        }

        protected virtual string DebugLabel => State.ToString();

        protected virtual void OnGUI()
        {
            if (State == UnitState.Dying || ScreenPosition.z <= 0f || Event.current.type != EventType.Repaint)
                return;

            float x = ScreenPosition.x;
            float y = Screen.height - ScreenPosition.y - 34f;
            GUI.DrawTexture(new Rect(x - 16f, y, 32f, 4f), healthBackground);
            GUI.DrawTexture(new Rect(x - 16f, y, 32f * Health.Normalized, 4f), healthFill);
            if (ShowStateLabels)
                GUI.Label(new Rect(x - 40f, y - 20f, 160f, 20f), DebugLabel);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireSphere(transform.position, visionRadius);
            Gizmos.color = Color.blue;
            foreach (Unit enemy in enemiesInSight)
            {
                if (enemy != null)
                    Gizmos.DrawLine(transform.position, enemy.transform.position);
            }
        }
    }
}
