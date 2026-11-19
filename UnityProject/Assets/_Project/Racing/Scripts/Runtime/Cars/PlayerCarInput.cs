using UnityEngine;
using UnityLab.Racing.AI;
using UnityLab.Shared;

namespace UnityLab.Racing.Cars
{
    /// <summary>
    /// Reads the keyboard/gamepad and drives the local player's car.
    /// Controls: W/S or arrows to accelerate and reverse, A/D to steer, Space to brake, R to reset.
    /// With <see cref="GameSession.Autopilot"/> on (automated tests) an AI driver takes the wheel.
    /// </summary>
    [RequireComponent(typeof(CarController))]
    public class PlayerCarInput : MonoBehaviour
    {
        CarController car;

        void Awake() => car = GetComponent<CarController>();

        void Start()
        {
            if (GameSession.Autopilot && !TryGetComponent(out AICarDriver _))
            {
                gameObject.AddComponent<AICarDriver>();
                enabled = false;
            }
        }

        // Input is read every frame in Update; the car applies it in its own FixedUpdate.
        void Update()
        {
            car.SetInputs(Input.GetAxis("Vertical"), Input.GetAxis("Jump"), Input.GetAxis("Horizontal"));

            if (Input.GetKeyDown(KeyCode.R))
                car.ResetCar();
        }

        void OnDisable()
        {
            if (car != null)
                car.SetInputs(0f, 0f, 0f);
        }
    }
}
