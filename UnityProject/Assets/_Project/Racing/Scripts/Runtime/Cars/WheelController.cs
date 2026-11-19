using UnityEngine;
using UnityEngine.Serialization;

namespace UnityLab.Racing.Cars
{
    /// <summary>Keeps a wheel's visible mesh where its WheelCollider simulates it (spin, steering, suspension).</summary>
    [RequireComponent(typeof(WheelCollider))]
    public class WheelController : MonoBehaviour
    {
        [FormerlySerializedAs("mesh")]
        [SerializeField] GameObject visual;

        public WheelCollider Collider { get; private set; }

        void Awake() => Collider = GetComponent<WheelCollider>();

        // LateUpdate rather than FixedUpdate so the mesh follows interpolated physics without jitter.
        void LateUpdate()
        {
            if (visual == null)
                return;
            Collider.GetWorldPose(out Vector3 position, out Quaternion rotation);
            visual.transform.SetPositionAndRotation(position, rotation);
        }
    }
}
