using UnityEngine;
using UnityEngine.Serialization;
using UnityLab.Racing.Race;

namespace UnityLab.Racing.Cars
{
    public enum Drivetrain
    {
        FrontWheelDrive,
        RearWheelDrive,
        AllWheelDrive,
    }

    /// <summary>
    /// Physics car built on Unity's WheelColliders: engine torque, brakes, steering, an anti-roll bar,
    /// tyre skid feedback (smoke and sound) and engine pitch. It does not read input itself; a driver
    /// (<see cref="PlayerCarInput"/> or <see cref="AI.AICarDriver"/>) calls <see cref="SetInputs"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CarController : MonoBehaviour
    {
        [Header("Engine and brakes")]
        [FormerlySerializedAs("goTorque")]
        [SerializeField] float motorTorque = 1000f;
        [SerializeField] float brakeTorque = 2000f;
        [SerializeField] float maxSteerAngle = 30f;
        [Tooltip("Top speed in m/s. Above it the engine stops pushing.")]
        [SerializeField] float maxSpeed = 80f;
        [FormerlySerializedAs("carType")]
        [SerializeField] Drivetrain drivetrain = Drivetrain.FrontWheelDrive;

        [Header("Stability")]
        [Tooltip("Force that keeps both wheels of an axle loaded in corners, reducing body roll.")]
        [FormerlySerializedAs("antiRoll")]
        [SerializeField] float antiRollStiffness = 2000f;
        [Tooltip("Seconds the car may stay on its side or roof before it is put back on the track.")]
        [SerializeField] float flippedResetDelay = 2f;

        [Header("Skid feedback")]
        [SerializeField] float skidThreshold = 0.1f;
        [FormerlySerializedAs("stereoFactor")]
        [SerializeField] float skidStereoSpread = 5f;
        [SerializeField] AudioSource skidSound;
        [SerializeField] ParticleSystem smokePrefab;

        [Header("Engine sound")]
        [SerializeField] AudioSource engineSound;
        [SerializeField] AnimationCurve engineSoundCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] float engineSoundMinPitch = 0.2f;
        [SerializeField] float engineSoundMaxPitch = 2.5f;

        [Header("Brake lights")]
        [FormerlySerializedAs("leftLight")]
        [SerializeField] GameObject leftBrakeLight;
        [FormerlySerializedAs("rightLight")]
        [SerializeField] GameObject rightBrakeLight;

        Rigidbody body;
        Competitor competitor;
        readonly WheelController[] frontWheels = new WheelController[2];
        readonly WheelController[] rearWheels = new WheelController[2];
        readonly WheelController[] allWheels = new WheelController[4];
        readonly ParticleSystem[] skidSmoke = new ParticleSystem[4];
        readonly float[] skidAmounts = new float[4];
        float throttleInput, brakeInput, steeringInput;
        float flippedTime;

        /// <summary>Current speed in m/s.</summary>
        public float Speed => body.linearVelocity.magnitude;
        public float MaxSpeed => maxSpeed;
        public Rigidbody Body => body;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            competitor = GetComponent<Competitor>();

            frontWheels[0] = FindWheel("FR_Wheel");
            frontWheels[1] = FindWheel("FL_Wheel");
            rearWheels[0] = FindWheel("BL_Wheel");
            rearWheels[1] = FindWheel("BR_Wheel");
            frontWheels.CopyTo(allWheels, 0);
            rearWheels.CopyTo(allWheels, 2);
        }

        void Start()
        {
            if (smokePrefab != null)
            {
                for (int i = 0; i < skidSmoke.Length; i++)
                {
                    skidSmoke[i] = Instantiate(smokePrefab, transform);
                    skidSmoke[i].Stop();
                }
            }
            SetBrakeLights(false);
        }

        /// <summary>Driver commands, applied on the next physics step.</summary>
        /// <param name="throttle">-1 (reverse) to 1 (full throttle).</param>
        /// <param name="brake">0 to 1.</param>
        /// <param name="steering">-1 (left) to 1 (right).</param>
        public void SetInputs(float throttle, float brake, float steering)
        {
            throttleInput = Mathf.Clamp(throttle, -1f, 1f);
            brakeInput = Mathf.Clamp01(brake);
            steeringInput = Mathf.Clamp(steering, -1f, 1f);
        }

        void FixedUpdate()
        {
            ApplyTorque(throttleInput);
            ApplyBrake(brakeInput);
            ApplySteering(steeringInput);

            // Wheel order: front right, front left, rear left, rear right.
            ApplyAntiRoll(frontWheels[1].Collider, frontWheels[0].Collider);
            ApplyAntiRoll(rearWheels[0].Collider, rearWheels[1].Collider);

            UpdateSkid();
            UpdateEngineSound();
            CheckFlipped();
        }

        void ApplyTorque(float throttle)
        {
            // Above top speed the engine stops pushing instead of keeping the last torque value.
            float torque = Speed < maxSpeed ? motorTorque * throttle : 0f;
            WheelController[] driven = drivetrain == Drivetrain.FrontWheelDrive ? frontWheels
                : drivetrain == Drivetrain.RearWheelDrive ? rearWheels
                : allWheels;

            foreach (WheelController wheel in allWheels)
                wheel.Collider.motorTorque = System.Array.IndexOf(driven, wheel) >= 0 ? torque : 0f;
        }

        void ApplyBrake(float brake)
        {
            foreach (WheelController wheel in allWheels)
                wheel.Collider.brakeTorque = brakeTorque * brake;
            SetBrakeLights(brake > 0.01f);
        }

        void ApplySteering(float steering)
        {
            foreach (WheelController wheel in frontWheels)
                wheel.Collider.steerAngle = steering * maxSteerAngle;
        }

        /// <summary>
        /// Anti-roll bar: the more one wheel's suspension is compressed compared with the other wheel
        /// of the same axle, the more force pushes the light side down and the heavy side up.
        /// </summary>
        void ApplyAntiRoll(WheelCollider left, WheelCollider right)
        {
            bool leftGrounded = left.GetGroundHit(out WheelHit leftHit);
            bool rightGrounded = right.GetGroundHit(out WheelHit rightHit);
            float leftTravel = leftGrounded ? SuspensionTravel(left, leftHit) : 1f;
            float rightTravel = rightGrounded ? SuspensionTravel(right, rightHit) : 1f;

            float force = (leftTravel - rightTravel) * antiRollStiffness;
            if (leftGrounded)
                body.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (rightGrounded)
                body.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        static float SuspensionTravel(WheelCollider wheel, WheelHit hit) =>
            (-wheel.transform.InverseTransformPoint(hit.point).y - wheel.radius) / wheel.suspensionDistance;

        void UpdateSkid()
        {
            int skidding = 0;
            for (int i = 0; i < allWheels.Length; i++)
            {
                WheelCollider wheel = allWheels[i].Collider;
                skidAmounts[i] = 0f;
                if (!wheel.GetGroundHit(out WheelHit hit))
                    continue;
                if (Mathf.Abs(hit.forwardSlip) < skidThreshold && Mathf.Abs(hit.sidewaysSlip) < skidThreshold)
                    continue;

                skidding++;
                skidAmounts[i] = Mathf.Abs(hit.forwardSlip) + Mathf.Abs(hit.sidewaysSlip);
                if (skidSmoke[i] != null)
                {
                    skidSmoke[i].transform.position = wheel.transform.position - wheel.transform.up * wheel.radius;
                    skidSmoke[i].Emit(1);
                }
            }

            if (skidSound == null)
                return;
            if (skidding == 0)
            {
                if (skidSound.isPlaying)
                    skidSound.Stop();
                return;
            }

            skidSound.volume = (float)skidding / allWheels.Length;
            // Left wheels pull the sound to the left speaker, right wheels to the right one.
            float right = skidAmounts[0] + skidAmounts[3];
            float left = skidAmounts[1] + skidAmounts[2];
            skidSound.panStereo = Mathf.Clamp((right - left) * skidStereoSpread, -1f, 1f);
            if (!skidSound.isPlaying)
                skidSound.Play();
        }

        void UpdateEngineSound()
        {
            if (engineSound == null)
                return;
            float speedRatio = Mathf.Clamp01(Speed / maxSpeed);
            engineSound.pitch = Mathf.Lerp(engineSoundMinPitch, engineSoundMaxPitch, engineSoundCurve.Evaluate(speedRatio));
        }

        void CheckFlipped()
        {
            bool flipped = Vector3.Dot(transform.up, Vector3.up) < 0.3f && Speed < 2f;
            flippedTime = flipped ? flippedTime + Time.fixedDeltaTime : 0f;
            if (flippedTime > flippedResetDelay)
                ResetCar();
        }

        /// <summary>
        /// Puts the car back on the track at the last waypoint it crossed, facing the next one.
        /// Without race information it is simply turned upright where it is.
        /// </summary>
        public void ResetCar()
        {
            flippedTime = 0f;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;

            if (competitor != null && competitor.Circuit != null && competitor.Progress != null && competitor.Progress.WaypointsPassed > 0)
            {
                CircuitManager circuit = competitor.Circuit;
                int next = competitor.Progress.NextWaypoint;
                int previous = (next - 1 + circuit.WaypointCount) % circuit.WaypointCount;
                Vector3 from = circuit.GetWaypointPosition(previous);
                Vector3 to = circuit.GetWaypointPosition(next);
                Vector3 direction = Vector3.ProjectOnPlane(to - from, Vector3.up);
                transform.SetPositionAndRotation(
                    from + Vector3.up * 1.5f,
                    Quaternion.LookRotation(direction.sqrMagnitude > 0.01f ? direction : transform.forward, Vector3.up));
            }
            else
            {
                Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                transform.SetPositionAndRotation(
                    transform.position + Vector3.up * 1.5f,
                    Quaternion.LookRotation(forward.sqrMagnitude > 0.01f ? forward : Vector3.forward, Vector3.up));
            }
            Physics.SyncTransforms();
        }

        void SetBrakeLights(bool on)
        {
            if (leftBrakeLight != null && leftBrakeLight.activeSelf != on)
                leftBrakeLight.SetActive(on);
            if (rightBrakeLight != null && rightBrakeLight.activeSelf != on)
                rightBrakeLight.SetActive(on);
        }

        WheelController FindWheel(string wheelName)
        {
            Transform wheel = transform.Find(wheelName);
            if (wheel == null || !wheel.TryGetComponent(out WheelController controller))
                throw new MissingComponentException($"{name} needs a child '{wheelName}' with a WheelController.");
            return controller;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < allWheels.Length; i++)
            {
                if (allWheels[i] != null)
                    Gizmos.DrawWireSphere(allWheels[i].transform.position, skidAmounts[i]);
            }
        }
    }
}
