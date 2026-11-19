using UnityEngine;
using UnityEngine.Serialization;
using UnityLab.Racing.Cars;
using UnityLab.Racing.Race;

namespace UnityLab.Racing.AI
{
    /// <summary>
    /// AI driver. It steers towards the next waypoint of its own race progress, brakes before sharp
    /// corners depending on speed, avoids obstacles with a fan of raycast "whiskers" and puts the car
    /// back on the track if it gets stuck.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class AICarDriver : MonoBehaviour
    {
        [Header("Driving")]
        [Tooltip("Steering per degree of angle to the target, clamped to full lock.")]
        [SerializeField] float steeringSensitivity = 0.05f;
        [FormerlySerializedAs("acelerationSensitivity")]
        [SerializeField] float accelerationSensitivity = 1f;
        [SerializeField] float brakeSensitivity = 0.1f;
        [Tooltip("Angle to the target at which a corner counts as the sharpest possible.")]
        [SerializeField] float cornerDegrees = 90f;

        [Header("Obstacle avoidance")]
        [FormerlySerializedAs("frontSensorsLenght")]
        [SerializeField] float frontSensorLength = 6f;
        [FormerlySerializedAs("sideSensorsLenght")]
        [SerializeField] float sideSensorLength = 2f;
        [SerializeField] Vector3 frontSensorPosition = new Vector3(0f, 0.2f, 0.5f);
        [FormerlySerializedAs("frontSideSensorPosition")]
        [SerializeField] float sensorSideOffset = 0.2f;
        [FormerlySerializedAs("front_SensorAngle")]
        [SerializeField] float frontSensorAngle = 20f;
        [FormerlySerializedAs("frontSide_SensorAngle")]
        [SerializeField] float frontSideSensorAngle = 45f;
        [FormerlySerializedAs("side_SensorAngle")]
        [SerializeField] float sideSensorAngle = 90f;

        [Header("Recovery")]
        [SerializeField] float stuckSpeed = 1f;
        [SerializeField] float stuckResetDelay = 4f;

        CarController car;
        Competitor competitor;
        readonly RaycastHit[] hits = new RaycastHit[8];
        float stuckTime;

        void Awake()
        {
            car = GetComponent<CarController>();
            competitor = GetComponent<Competitor>();
        }

        void FixedUpdate()
        {
            if (competitor == null || competitor.Circuit == null || competitor.Progress == null)
                return;

            if (competitor.Progress.Finished)
            {
                // Coast to a stop after the finish line.
                car.SetInputs(0f, 0.6f, 0f);
                return;
            }

            Vector3 target = competitor.Circuit.GetWaypointPosition(competitor.Progress.NextWaypoint);
            Vector3 localTarget = transform.InverseTransformPoint(target);
            float targetAngle = Mathf.Atan2(localTarget.x, localTarget.z) * Mathf.Rad2Deg;

            float speedRatio = car.Speed / car.MaxSpeed;
            float cornerRatio = Mathf.Clamp(Mathf.Abs(targetAngle), 0f, cornerDegrees) / cornerDegrees;

            float throttle = 1f;
            float brake = 0f;
            // Fast into a corner: brake proportionally to how sharp it is.
            if (speedRatio >= 0.08f && cornerRatio >= 0.2f)
                brake = Mathf.Lerp(0f, 0.5f + speedRatio * brakeSensitivity, cornerRatio);
            // Very sharp corner: lift off the throttle as well.
            if (speedRatio >= 0.16f && cornerRatio >= 0.4f)
                throttle = Mathf.Lerp(0f, accelerationSensitivity, 1f - cornerRatio);

            float steering = Mathf.Clamp(targetAngle * steeringSensitivity, -1f, 1f);
            float avoidance = SenseObstacles();
            if (avoidance != 0f)
                steering = Mathf.Clamp(avoidance, -1f, 1f);

            car.SetInputs(throttle, brake, steering);
            CheckStuck();
        }

        /// <summary>
        /// Casts three whiskers on each side of the bumper. A hit on the right steers left and vice
        /// versa, weighted so the long front whiskers matter more than the short side ones.
        /// Computed from scratch every step, so the avoidance never accumulates.
        /// </summary>
        /// <returns>Steering correction, or 0 when the way is clear.</returns>
        float SenseObstacles()
        {
            Vector3 origin = transform.position + transform.forward * frontSensorPosition.z + transform.up * frontSensorPosition.y;
            Vector3 rightOrigin = origin + transform.right * sensorSideOffset;
            Vector3 leftOrigin = origin - transform.right * sensorSideOffset;

            float correction = 0f;
            correction -= Whisker(rightOrigin, frontSensorAngle, frontSensorLength) * 1f;
            correction -= Whisker(rightOrigin, frontSideSensorAngle, frontSensorLength) * 0.6f;
            correction -= Whisker(rightOrigin, sideSensorAngle, sideSensorLength) * 0.3f;
            correction += Whisker(leftOrigin, -frontSensorAngle, frontSensorLength) * 1f;
            correction += Whisker(leftOrigin, -frontSideSensorAngle, frontSensorLength) * 0.6f;
            correction += Whisker(leftOrigin, -sideSensorAngle, sideSensorLength) * 0.3f;
            return correction;
        }

        /// <returns>0 when clear, up to 1 the closer the obstacle is.</returns>
        float Whisker(Vector3 origin, float angle, float length)
        {
            Vector3 direction = Quaternion.AngleAxis(angle, transform.up) * transform.forward;
            int count = Physics.RaycastNonAlloc(origin, direction, hits, length, ~0, QueryTriggerInteraction.Ignore);

            float closest = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider hitCollider = hits[i].collider;
                // Ignore our own car, the ground and the track surface.
                if (hitCollider.attachedRigidbody == car.Body || hitCollider.CompareTag("Terrain") || hitCollider is WheelCollider)
                    continue;
                if (Vector3.Dot(hits[i].normal, Vector3.up) > 0.7f)
                    continue;
                closest = Mathf.Min(closest, hits[i].distance);
            }

            if (closest == float.MaxValue)
            {
                Debug.DrawRay(origin, direction * length, Color.green);
                return 0f;
            }
            Debug.DrawRay(origin, direction * closest, Color.red);
            return 1f - closest / length;
        }

        void CheckStuck()
        {
            stuckTime = car.Speed < stuckSpeed ? stuckTime + Time.fixedDeltaTime : 0f;
            if (stuckTime > stuckResetDelay)
            {
                stuckTime = 0f;
                car.ResetCar();
            }
        }
    }
}
