using UnityEngine;
using UnityEngine.Serialization;

namespace UnityLab.Racing.Cameras
{
    /// <summary>
    /// Chase camera that smoothly follows a target behind the car. G toggles the in-car view, where
    /// the camera sticks to the driver's seat.
    /// </summary>
    public class CarCamera : MonoBehaviour
    {
        [Tooltip("Point the camera looks at, usually at the front of the car.")]
        [FormerlySerializedAs("frontTarget")]
        [SerializeField] Transform target;
        [FormerlySerializedAs("driversCamera")]
        [SerializeField] Transform driverSeat;
        [Tooltip("Position relative to the target, in the target's local space.")]
        [SerializeField] Vector3 offset = new Vector3(0f, 2f, -5f);
        [SerializeField] float followSpeed = 5f;
        [SerializeField] float rotationSpeed = 10f;

        bool driverView;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.G) && driverSeat != null)
                driverView = !driverView;
        }

        void FixedUpdate()
        {
            if (target == null)
                return;

            if (driverView && driverSeat != null)
            {
                transform.SetPositionAndRotation(driverSeat.position, driverSeat.rotation);
                return;
            }

            Vector3 desiredPosition = target.TransformPoint(offset);
            transform.position = Vector3.Lerp(transform.position, desiredPosition, followSpeed * Time.fixedDeltaTime);

            Quaternion desiredRotation = Quaternion.LookRotation(target.position - transform.position, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationSpeed * Time.fixedDeltaTime);
        }
    }
}
