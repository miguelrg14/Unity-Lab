using UnityEngine;
using UnityEngine.Serialization;

namespace UnityLab.Racing.Race
{
    /// <summary>
    /// Invisible checkpoint gate (a trigger) on the track. The <see cref="CircuitManager"/> numbers
    /// them in hierarchy order; cars must cross them in that order.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class Waypoint : MonoBehaviour
    {
        [FormerlySerializedAs("ID")]
        [SerializeField] int index;

        public int Index
        {
            get => index;
            set => index = value;
        }
    }
}
