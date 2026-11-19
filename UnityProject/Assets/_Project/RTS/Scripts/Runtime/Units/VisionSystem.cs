using UnityEngine;
using UnityLab.RTS.Core;

namespace UnityLab.RTS.Units
{
    /// <summary>
    /// Scene-wide owner of the <see cref="VisionMatrix{T}"/> that tells units when enemies enter or
    /// leave their vision radius. An instance (not static lists) so reloading the scene starts clean.
    /// F2 shows the matrix on screen.
    /// </summary>
    public class VisionSystem : MonoBehaviour
    {
        readonly VisionMatrix<Unit> matrix = new VisionMatrix<Unit>();
        bool showDebug;

        public static VisionSystem Instance { get; private set; }
        public VisionMatrix<Unit> Matrix => matrix;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Only one VisionSystem per scene is supported.", this);
                Destroy(this);
                return;
            }
            Instance = this;
            matrix.EnemyEntered += (observer, enemy) => observer.OnEnemyEnteredVision(enemy);
            matrix.EnemyLeft += (observer, enemy) => observer.OnEnemyLeftVision(enemy);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
            matrix.Clear();
        }

        public void Register(Unit unit) => matrix.Add(unit, unit.Team.TeamNumber);

        public void Unregister(Unit unit) => matrix.Remove(unit);

        // After every unit has moved this frame.
        void LateUpdate() => matrix.Update();

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F2))
                showDebug = !showDebug;
        }

        void OnGUI()
        {
            if (!showDebug)
                return;

            GUI.Box(new Rect(90, 90, 70 + 60 * matrix.ArmyB.Count, 30 + 20 * matrix.ArmyA.Count), "Squared distances (army A rows, army B columns)");
            for (int row = 0; row < matrix.ArmyA.Count; row++)
            {
                for (int column = 0; column < matrix.ArmyB.Count; column++)
                {
                    float value = matrix.GetSquaredDistance(row, column);
                    string text = value == VisionMatrix<Unit>.Far ? "---" : value.ToString("0");
                    GUI.Label(new Rect(100 + 60 * column, 115 + 20 * row, 60, 20), text);
                }
            }
        }
    }
}
