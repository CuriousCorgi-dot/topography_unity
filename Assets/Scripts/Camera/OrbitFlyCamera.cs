// OrbitFlyCamera.cs
//
// Reusable exploration camera for a DepthWizard terrain scene: starts in
// orbit mode (right-mouse-drag to rotate, scroll to zoom around the
// terrain's center), and toggles to a WASD first-person flythrough with a
// single key press.
//
// Deliberately terrain-agnostic: on Start it reads Terrain.activeTerrain to
// find where to orbit and how big a step size makes sense, instead of any
// hardcoded position/size. Drop this on a camera in any scene - the
// placeholder pyramid terrain today, Team A's real patches later - and it
// frames itself correctly with no per-scene tuning. If the terrain isn't
// there yet when this script starts (e.g. a loader spawns it later), it
// keeps checking every frame until one shows up; call Recenter() manually
// if a scene swaps terrains after that.
//
// Uses the new Input System (this project's Active Input Handling is set to
// "Input System Package (New)" - see ProjectSettings/ProjectSettings.asset,
// activeInputHandler: 1), not the legacy UnityEngine.Input class.
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Camera))]
public class OrbitFlyCamera : MonoBehaviour
{
    public enum Mode { Orbit, Fly }

    [Header("Target")]
    [Tooltip("Terrain to orbit/fly around. Leave empty to auto-use Terrain.activeTerrain.")]
    [SerializeField] private Terrain targetTerrain;

    [Header("Mode toggle")]
    [Tooltip("Press to switch between Orbit and Fly modes.")]
    [SerializeField] private Key toggleKey = Key.Tab;
    [SerializeField] private Mode startMode = Mode.Orbit;

    [Header("Orbit mode (right-mouse drag to rotate, scroll to zoom)")]
    [SerializeField] private float orbitSensitivity = 0.25f;
    [SerializeField] private float zoomSensitivity = 2f;
    [SerializeField] private float minDistance = 10f;
    [SerializeField] private float maxDistanceMultiplier = 2.5f; // relative to terrain diagonal
    [SerializeField] private float minPitch = 5f;
    [SerializeField] private float maxPitch = 85f;

    [Header("Fly mode (WASD + mouse look, Shift to sprint)")]
    [SerializeField] private float flySpeed = 50f;
    [SerializeField] private float flySprintMultiplier = 3f;
    [SerializeField] private float flyLookSensitivity = 0.15f;

    Mode currentMode;
    bool terrainInitialized;
    Vector3 orbitPivotPosition;
    float yaw;
    float pitch;
    float distance;
    float maxDistance = 100000f;

    void Awake()
    {
        currentMode = startMode;
    }

    void Start()
    {
        Terrain terrain = ResolveTerrain();
        if (terrain != null)
        {
            InitializeFromTerrain(terrain);
            terrainInitialized = true;
        }
        else
        {
            // Nothing to frame yet - hold the camera's current placement and
            // derive orbit state from it so Update() has something sane to
            // fall back on while it waits for a terrain to appear.
            orbitPivotPosition = transform.position + transform.forward * 50f;
            distance = 50f;
            yaw = transform.eulerAngles.y;
            pitch = NormalizePitch(transform.eulerAngles.x);
        }

        ApplyMode();
    }

    void Update()
    {
        if (!terrainInitialized)
        {
            Terrain terrain = ResolveTerrain();
            if (terrain != null)
            {
                InitializeFromTerrain(terrain);
                terrainInitialized = true;
            }
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard[toggleKey].wasPressedThisFrame)
        {
            currentMode = currentMode == Mode.Orbit ? Mode.Fly : Mode.Orbit;
            ApplyMode();
        }

        if (currentMode == Mode.Orbit)
        {
            TickOrbit();
        }
        else
        {
            TickFly();
        }
    }

    /// <summary>Force re-reading the target terrain's bounds on the next frame (e.g. after a scene loader swaps terrains at runtime).</summary>
    public void Recenter()
    {
        terrainInitialized = false;
    }

    Terrain ResolveTerrain()
    {
        return targetTerrain != null ? targetTerrain : Terrain.activeTerrain;
    }

    void InitializeFromTerrain(Terrain terrain)
    {
        Bounds bounds = terrain.terrainData.bounds; // local-space bounds
        Vector3 worldCenter = terrain.transform.position + bounds.center;
        orbitPivotPosition = new Vector3(worldCenter.x, terrain.transform.position.y + bounds.size.y * 0.5f, worldCenter.z);

        float diagonal = Mathf.Max(new Vector2(bounds.size.x, bounds.size.z).magnitude, 1f);
        maxDistance = diagonal * maxDistanceMultiplier;
        distance = Mathf.Clamp(diagonal * 0.6f, minDistance, maxDistance);

        yaw = -35f;
        pitch = 35f;
        UpdateOrbitTransform();
    }

    void TickOrbit()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null) return;

        if (mouse.rightButton.isPressed)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * orbitSensitivity;
            pitch -= delta.y * orbitSensitivity;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
        }

        float scroll = mouse.scroll.ReadValue().y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            distance = Mathf.Clamp(distance - scroll * zoomSensitivity * 0.1f, minDistance, maxDistance);
        }

        UpdateOrbitTransform();
    }

    void TickFly()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null) return;

        if (mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue();
            yaw += delta.x * flyLookSensitivity;
            pitch -= delta.y * flyLookSensitivity;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }

        Vector3 move = Vector3.zero;
        if (keyboard.wKey.isPressed) move += Vector3.forward;
        if (keyboard.sKey.isPressed) move += Vector3.back;
        if (keyboard.aKey.isPressed) move += Vector3.left;
        if (keyboard.dKey.isPressed) move += Vector3.right;
        if (keyboard.eKey.isPressed || keyboard.spaceKey.isPressed) move += Vector3.up;
        if (keyboard.qKey.isPressed || keyboard.leftCtrlKey.isPressed) move += Vector3.down;

        float speed = flySpeed * (keyboard.leftShiftKey.isPressed ? flySprintMultiplier : 1f);
        transform.position += transform.TransformDirection(move.normalized) * speed * Time.deltaTime;

        // Keep the orbit pivot roughly under the camera so switching back to
        // Orbit mode doesn't snap the view somewhere unrelated.
        orbitPivotPosition = transform.position + transform.forward * distance;
    }

    void UpdateOrbitTransform()
    {
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        transform.SetPositionAndRotation(orbitPivotPosition + rotation * new Vector3(0f, 0f, -distance), rotation);
    }

    void ApplyMode()
    {
        if (currentMode == Mode.Fly)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            // Re-derive orbit state from wherever flying left the camera so
            // the transition back to Orbit mode doesn't jump.
            pitch = NormalizePitch(transform.eulerAngles.x);
            yaw = transform.eulerAngles.y;
            distance = Mathf.Clamp(Vector3.Distance(transform.position, orbitPivotPosition), minDistance, maxDistance);
            UpdateOrbitTransform();
        }
    }

    float NormalizePitch(float eulerX)
    {
        if (eulerX > 180f) eulerX -= 360f;
        return Mathf.Clamp(eulerX, minPitch, maxPitch);
    }
}
