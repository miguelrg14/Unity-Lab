using UnityEngine;
using UnityEngine.Serialization;

namespace UnityLab.RTS.Cameras
{
    /// <summary>
    /// Top-down strategy camera: pans with the arrow keys or by pushing the mouse against the
    /// screen edge (Shift to go faster) and zooms with the wheel, tilting down as it gets closer.
    /// </summary>
    public class RtsCamera : MonoBehaviour
    {
        [Header("Pan")]
        [Tooltip("Fraction of the screen, from each edge, that pans the camera.")]
        [SerializeField, Range(0f, 0.2f)] float edgeSize = 0.02f;
        [SerializeField] float panSpeed = 10f;
        [FormerlySerializedAs("panSpeedMult")]
        [SerializeField] float fastPanMultiplier = 2f;
        [Tooltip("Pushing the mouse against the edge pans the camera (off by default in the Editor).")]
        [SerializeField] bool edgePanInEditor;
        [Tooltip("How far the camera may move from where it started, in metres.")]
        [SerializeField] Vector2 panLimits = new Vector2(40f, 40f);

        [Header("Zoom")]
        [SerializeField] Vector2 zoomHeightRange = new Vector2(-10f, 10f);
        [Tooltip("Pitch at the lowest and the highest zoom.")]
        [SerializeField] Vector2 zoomRotationRange = new Vector2(33f, 70f);
        [FormerlySerializedAs("zoomMouseWheelMult")]
        [SerializeField] float zoomSpeed = 1000f;
        [FormerlySerializedAs("updateZoomMouseWheelMult")]
        [SerializeField] float zoomSmoothing = 5f;

        Vector3 initialPosition;
        float currentZoom;
        bool hasFocus = true;

        void Start() => initialPosition = transform.position;

        void OnApplicationFocus(bool focus) => hasFocus = focus;

        void LateUpdate()
        {
            float dt = Time.unscaledDeltaTime;
            Pan(dt);
            Zoom(dt);
        }

        void Pan(float dt)
        {
            float speed = panSpeed * (Input.GetKey(KeyCode.LeftShift) ? fastPanMultiplier : 1f);
            Vector3 mouse = Input.mousePosition;
            bool mouseInside = mouse.x >= 0 && mouse.y >= 0 && mouse.x <= Screen.width && mouse.y <= Screen.height;
            // No edge panning during automated runs, where nobody is moving the mouse.
            bool edgePan = hasFocus && mouseInside && !Shared.GameSession.Autopilot && (!Application.isEditor || edgePanInEditor);

            Vector3 move = Vector3.zero;
            if (Input.GetKey(KeyCode.UpArrow) || (edgePan && mouse.y >= Screen.height * (1f - edgeSize)))
                move += Vector3.forward;
            if (Input.GetKey(KeyCode.DownArrow) || (edgePan && mouse.y <= Screen.height * edgeSize))
                move += Vector3.back;
            if (Input.GetKey(KeyCode.LeftArrow) || (edgePan && mouse.x <= Screen.width * edgeSize))
                move += Vector3.left;
            if (Input.GetKey(KeyCode.RightArrow) || (edgePan && mouse.x >= Screen.width * (1f - edgeSize)))
                move += Vector3.right;

            Vector3 position = transform.position + move.normalized * speed * dt;
            position.x = Mathf.Clamp(position.x, initialPosition.x - panLimits.x, initialPosition.x + panLimits.x);
            position.z = Mathf.Clamp(position.z, initialPosition.z - panLimits.y, initialPosition.z + panLimits.y);
            transform.position = position;
        }

        void Zoom(float dt)
        {
            currentZoom -= Input.GetAxis("Mouse ScrollWheel") * zoomSpeed * dt;
            currentZoom = Mathf.Clamp(currentZoom, zoomHeightRange.x, zoomHeightRange.y);

            float t = 1f - Mathf.Exp(-zoomSmoothing * dt);
            Vector3 position = transform.position;
            position.y = Mathf.Lerp(position.y, initialPosition.y + currentZoom, t);
            transform.position = position;

            float zoomRatio = Mathf.InverseLerp(zoomHeightRange.x, zoomHeightRange.y, currentZoom);
            float pitch = Mathf.Lerp(zoomRotationRange.x, zoomRotationRange.y, zoomRatio);
            Vector3 euler = transform.eulerAngles;
            euler.x = Mathf.LerpAngle(euler.x, pitch, t);
            transform.eulerAngles = euler;
        }
    }
}
