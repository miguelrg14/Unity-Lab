using UnityEngine;

namespace UnityLab.RTS.Units
{
    /// <summary>Which army a unit or building belongs to, and that army's colour.</summary>
    public class TeamMember : MonoBehaviour
    {
        [SerializeField] Color color = Color.white;
        [SerializeField] int teamNumber;

        public Color Color
        {
            get => color;
            set => color = value;
        }

        public int TeamNumber
        {
            get => teamNumber;
            set => teamNumber = value;
        }

        public bool IsEnemyOf(TeamMember other) => other != null && other.teamNumber != teamNumber;
    }
}
