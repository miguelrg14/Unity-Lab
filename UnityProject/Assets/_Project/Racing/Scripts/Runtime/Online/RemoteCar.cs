using UnityEngine;
using UnityLab.Racing.Protocol;
using UnityLab.Racing.Race;

namespace UnityLab.Racing.Online
{
    /// <summary>
    /// Another player's car. It is not simulated here: it glides towards the last state its owner
    /// sent, extrapolating with the measured velocity so it keeps moving smoothly between updates.
    /// </summary>
    [RequireComponent(typeof(Competitor))]
    public class RemoteCar : MonoBehaviour
    {
        [Tooltip("How quickly the car catches up with the received position (per second).")]
        [SerializeField] float positionSmoothing = 12f;
        [SerializeField] float rotationSmoothing = 12f;
        [Tooltip("Never extrapolate further than this many seconds after the last update.")]
        [SerializeField] float maxExtrapolation = 0.25f;

        Competitor competitor;
        CircuitManager circuit;
        bool hasState;
        Vector3 targetPosition;
        Quaternion targetRotation;
        Vector3 velocity;
        float lastStateTime;

        void Awake()
        {
            competitor = GetComponent<Competitor>();
            competitor.IsRemote = true;

            // A remote car is moved by the network, never by local physics.
            foreach (Rigidbody body in GetComponentsInChildren<Rigidbody>())
            {
                body.isKinematic = true;
                body.interpolation = RigidbodyInterpolation.None;
            }
            targetPosition = transform.position;
            targetRotation = transform.rotation;
        }

        public void Initialize(CircuitManager circuitManager, string nickname)
        {
            circuit = circuitManager;
            competitor.Nickname = nickname;
            circuit.Register(competitor);
        }

        public void ApplyState(CarState state)
        {
            var position = new Vector3(state.Position.X, state.Position.Y, state.Position.Z);
            var rotation = new Quaternion(state.Rotation.X, state.Rotation.Y, state.Rotation.Z, state.Rotation.W);

            float now = Time.time;
            if (hasState && now > lastStateTime)
                velocity = (position - targetPosition) / (now - lastStateTime);
            else
                transform.SetPositionAndRotation(position, rotation);

            hasState = true;
            lastStateTime = now;
            targetPosition = position;
            targetRotation = Quaternion.Normalize(rotation);

            if (circuit != null)
                circuit.ApplyRemoteProgress(competitor, state.WaypointsPassed, state.Finished);
        }

        void Update()
        {
            if (!hasState)
                return;

            float sinceUpdate = Mathf.Min(Time.time - lastStateTime, maxExtrapolation);
            Vector3 predicted = targetPosition + velocity * sinceUpdate;
            float t = 1f - Mathf.Exp(-positionSmoothing * Time.deltaTime);
            float r = 1f - Mathf.Exp(-rotationSmoothing * Time.deltaTime);
            transform.SetPositionAndRotation(
                Vector3.Lerp(transform.position, predicted, t),
                Quaternion.Slerp(transform.rotation, targetRotation, r));
        }
    }
}
