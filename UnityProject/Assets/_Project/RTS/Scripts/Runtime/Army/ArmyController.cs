using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityLab.RTS.Core;
using UnityLab.RTS.Units;

namespace UnityLab.RTS.Army
{
    /// <summary>
    /// One army: its units and bases. When <see cref="PlayerControlled"/> it also turns the mouse
    /// and keyboard into orders:
    /// left click selects, left drag box-selects, right click moves/attacks/harvests,
    /// A/S/D train melee/artillery/harvester while the base is selected, Delete kills the selection.
    /// </summary>
    [RequireComponent(typeof(TeamMember))]
    public class ArmyController : MonoBehaviour
    {
        [SerializeField] List<ArmyBase> armyBases = new List<ArmyBase>();
        [Tooltip("Off for the army run by the AI commander.")]
        [SerializeField] bool playerControlled = true;
        [Tooltip("Seconds a dead unit stays on the field.")]
        [SerializeField] float corpseDuration = 1f;

        readonly List<Unit> units = new List<Unit>();
        readonly List<Unit> selectedUnits = new List<Unit>();
        TeamMember team;
        int selectableMask, floorMask;
        Vector3 dragStart;
        bool pressStartedOverUi;
        Camera mainCamera;

        public bool PlayerControlled
        {
            get => playerControlled;
            set => playerControlled = value;
        }

        public TeamMember Team => team;
        public IReadOnlyList<Unit> Units => units;
        public IReadOnlyList<Unit> SelectedUnits => selectedUnits;
        public IReadOnlyList<ArmyBase> Bases => armyBases;
        public ArmyBase MainBase => armyBases.Count > 0 ? armyBases[0] : null;
        public ArmyBase SelectedBase { get; private set; }

        /// <summary>True while the player drags a selection box.</summary>
        public bool IsBoxSelecting { get; private set; }
        public SelectionBox CurrentBox { get; private set; }

        void Awake() => team = GetComponent<TeamMember>();

        void Start()
        {
            mainCamera = Camera.main;
            selectableMask = LayerMask.GetMask("Selectable");
            floorMask = LayerMask.GetMask("Floor");

            foreach (Unit unit in FindObjectsByType<Unit>())
            {
                if (unit.Team.TeamNumber == team.TeamNumber)
                    AddUnit(unit);
            }
            foreach (ArmyBase armyBase in armyBases)
            {
                armyBase.Team.TeamNumber = team.TeamNumber;
                armyBase.SetTeamColor(team.Color);
            }

            // The opponent (and the player's army in automated runs) is played by the AI.
            bool autopilot = Shared.GameSession.Autopilot;
            if ((!playerControlled || autopilot) && !TryGetComponent(out AiCommander _))
                gameObject.AddComponent<AiCommander>();
        }

        void Update()
        {
            if (!playerControlled || mainCamera == null)
                return;

            HandleSelection();
            HandleOrders();
            HandleHotkeys();
        }

        void HandleSelection()
        {
            if (Input.GetMouseButtonDown(0))
            {
                pressStartedOverUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
                dragStart = Input.mousePosition;
            }
            if (pressStartedOverUi)
            {
                if (Input.GetMouseButtonUp(0))
                    pressStartedOverUi = false;
                return;
            }

            if (Input.GetMouseButton(0))
            {
                CurrentBox = SelectionBox.FromCorners(dragStart.x, dragStart.y, Input.mousePosition.x, Input.mousePosition.y);
                if (!CurrentBox.IsClick())
                {
                    IsBoxSelecting = true;
                    SelectInBox(CurrentBox);
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                if (!IsBoxSelecting)
                    SelectUnderCursor();
                IsBoxSelecting = false;
            }
        }

        void SelectInBox(SelectionBox box)
        {
            DeselectAll();
            foreach (Unit unit in units)
            {
                Vector3 p = unit.ScreenPosition;
                if (unit.IsAlive && box.Contains(p.x, p.y, p.z))
                    Select(unit);
            }
        }

        void SelectUnderCursor()
        {
            bool additive = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, selectableMask | floorMask))
                return;

            if (!additive)
                DeselectAll();

            Transform target = hit.transform;
            if (target.TryGetComponent(out Unit unit) && unit.Team.TeamNumber == team.TeamNumber && unit.IsAlive)
            {
                Select(unit);
            }
            else if (target.TryGetComponent(out ArmyBase armyBase) && armyBases.Contains(armyBase))
            {
                SelectedBase = armyBase;
                armyBase.Selectable.SetSelected(true);
            }
        }

        void HandleOrders()
        {
            if (!Input.GetMouseButtonDown(1) || selectedUnits.Count == 0)
                return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity, floorMask | selectableMask))
                return;

            bool onFloor = (floorMask & (1 << hit.transform.gameObject.layer)) != 0;
            if (onFloor)
                OrderMoveInFormation(hit.point);
            else
                foreach (Unit unit in selectedUnits)
                    unit.OrderInteract(hit.transform);
        }

        /// <summary>Spreads the selected units on a small grid so they do not fight for one point.</summary>
        void OrderMoveInFormation(Vector3 center)
        {
            int columns = Mathf.CeilToInt(Mathf.Sqrt(selectedUnits.Count));
            const float spacing = 1.6f;
            for (int i = 0; i < selectedUnits.Count; i++)
            {
                int row = i / columns;
                int column = i % columns;
                Vector3 offset = new Vector3((column - (columns - 1) * 0.5f) * spacing, 0f, -row * spacing);
                selectedUnits[i].OrderMove(center + offset);
            }
        }

        void HandleHotkeys()
        {
            if (SelectedBase != null)
            {
                if (Input.GetKeyDown(KeyCode.A))
                    Train(SelectedBase, UnitKind.Melee);
                if (Input.GetKeyDown(KeyCode.S))
                    Train(SelectedBase, UnitKind.Artillery);
                if (Input.GetKeyDown(KeyCode.D))
                    Train(SelectedBase, UnitKind.Harvester);
            }

            if (Input.GetKeyDown(KeyCode.Delete))
            {
                for (int i = selectedUnits.Count - 1; i >= 0; i--)
                    selectedUnits[i].Health.Kill();
            }
        }

        public Unit Train(ArmyBase armyBase, UnitKind kind)
        {
            Unit unit = armyBase.Train(kind);
            if (unit != null)
                AddUnit(unit);
            return unit;
        }

        public void AddUnit(Unit unit)
        {
            if (units.Contains(unit))
                return;
            unit.Team.TeamNumber = team.TeamNumber;
            if (unit.Home == null)
                unit.Home = MainBase;
            units.Add(unit);
            unit.SetTeamColor(team.Color);
            unit.Died += OnUnitDied;
            if (VisionSystem.Instance != null)
                VisionSystem.Instance.Register(unit);
        }

        void Select(Unit unit)
        {
            if (selectedUnits.Contains(unit))
                return;
            selectedUnits.Add(unit);
            unit.Selectable.SetSelected(true);
        }

        public void DeselectAll()
        {
            foreach (Unit unit in selectedUnits)
            {
                if (unit != null)
                    unit.Selectable.SetSelected(false);
            }
            selectedUnits.Clear();
            if (SelectedBase != null)
                SelectedBase.Selectable.SetSelected(false);
            SelectedBase = null;
        }

        void OnUnitDied(Unit unit)
        {
            unit.Died -= OnUnitDied;
            selectedUnits.Remove(unit);
            units.Remove(unit);
            StartCoroutine(RemoveCorpse(unit.gameObject));
        }

        IEnumerator RemoveCorpse(GameObject corpse)
        {
            yield return new WaitForSeconds(corpseDuration);
            if (corpse != null)
                Destroy(corpse);
        }
    }
}
