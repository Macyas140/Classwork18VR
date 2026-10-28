using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class VRTrackingController : MonoBehaviour
{
    public enum Hand { Left = 0, Right = 1 }

    [Header("Tracked Transforms (children of this rig root)")]
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;

    [Header("Head Actions")]
    [SerializeField] private InputActionProperty headPosition;
    [SerializeField] private InputActionProperty headRotation;

    [Header("Left Controller Actions")]
    [SerializeField] private InputActionProperty leftPosition;
    [SerializeField] private InputActionProperty leftRotation;
    [SerializeField] private InputActionProperty leftTrigger;
    [SerializeField] private InputActionProperty leftGrip;

    [Header("Right Controller Actions")]
    [SerializeField] private InputActionProperty rightPosition;
    [SerializeField] private InputActionProperty rightRotation;
    [SerializeField] private InputActionProperty rightTrigger;
    [SerializeField] private InputActionProperty rightGrip;
    [SerializeField] private InputActionProperty buttonA;

    [Header("Trigger Thresholds")]
    [SerializeField, Range(0f, 1f)] private float pressThreshold = 0.6f;
    [SerializeField, Range(0f, 1f)] private float releaseThreshold = 0.4f;

    [Header("Shooting")]
    [Tooltip("Optional. If empty, a primitive sphere is created.")]
    [SerializeField] private GameObject projectilePrefab;
    [SerializeField] private float projectileSpeed = 10f;
    [SerializeField] private float projectileScale = 0.1f;
    [Tooltip("Distance in front of the hand where the sphere spawns.")]
    [SerializeField] private float muzzleOffset = 0.1f;
    [Tooltip("Seconds between shots while holding the grip (0 = one shot per press).")]
    [SerializeField] private float fireCooldown = 0.15f;

    public event Action<Hand, float> OnTriggerPressed;
    public event Action<Hand, float> OnTriggerReleased;

    private readonly float[] triggerValues = new float[2];
    private readonly bool[] triggerPressed = new bool[2];
    private readonly bool[] gripPressed = new bool[2];
    private readonly float[] nextFireTime = new float[2];

    // Every sphere we shoot is tracked here so we can delete them later.
    private readonly List<GameObject> projectiles = new List<GameObject>();

    // ------------------------------------------------------------------
    // Setup
    // ------------------------------------------------------------------

    private void Reset()
    {
        headPosition  = Create("Head Position",  "<XRHMD>/centerEyePosition", "Vector3");
        headRotation  = Create("Head Rotation",  "<XRHMD>/centerEyeRotation", "Quaternion");

        leftPosition  = Create("Left Position",  "<XRController>{LeftHand}/devicePosition", "Vector3");
        leftRotation  = Create("Left Rotation",  "<XRController>{LeftHand}/deviceRotation", "Quaternion");
        leftTrigger   = Create("Left Trigger",   "<XRController>{LeftHand}/trigger", "Axis");

        rightPosition = Create("Right Position", "<XRController>{RightHand}/devicePosition", "Vector3");
        rightRotation = Create("Right Rotation", "<XRController>{RightHand}/deviceRotation", "Quaternion");
        rightTrigger  = Create("Right Trigger",  "<XRController>{RightHand}/trigger", "Axis");

        EnsureNewDefaults();
    }

    // Fills the NEW actions (grip + A) if they have no bindings yet, so you
    // don't need to Reset the component (which would wipe your references).
    private void EnsureNewDefaults()
    {
        if (!HasBinding(leftGrip))
            leftGrip = Create("Left Grip", "<XRController>{LeftHand}/grip", "Axis");
        if (!HasBinding(rightGrip))
            rightGrip = Create("Right Grip", "<XRController>{RightHand}/grip", "Axis");
        if (!HasBinding(buttonA))
            buttonA = Create("Button A", "<XRController>{RightHand}/primaryButton", "Button");
    }

    private static bool HasBinding(InputActionProperty p)
    {
        return p.action != null && p.action.bindings.Count > 0;
    }

    private static InputActionProperty Create(string name, string binding, string controlType)
    {
        var action = new InputAction(name, InputActionType.Value, binding, expectedControlType: controlType);
        return new InputActionProperty(action);
    }

    private void Awake()
    {
        EnsureNewDefaults();
    }

    private void OnEnable()
    {
        foreach (var p in AllActions()) p.action?.Enable();
    }

    private void OnDisable()
    {
        foreach (var p in AllActions()) p.action?.Disable();
    }

    private InputActionProperty[] AllActions() => new[]
    {
        headPosition, headRotation,
        leftPosition, leftRotation, leftTrigger, leftGrip,
        rightPosition, rightRotation, rightTrigger, rightGrip,
        buttonA
    };

    // ------------------------------------------------------------------
    // Update loop
    // ------------------------------------------------------------------

    private void Update()
    {
        ApplyPose(head,      headPosition,  headRotation);
        ApplyPose(leftHand,  leftPosition,  leftRotation);
        ApplyPose(rightHand, rightPosition, rightRotation);

        TrackTrigger(Hand.Left,  leftTrigger);
        TrackTrigger(Hand.Right, rightTrigger);

        TrackGripShoot(Hand.Left,  leftGrip,  leftHand);
        TrackGripShoot(Hand.Right, rightGrip, rightHand);

        // A button: wasPressedThisFrame works on a Value action with a Button control.
        if (buttonA.action != null && buttonA.action.WasPressedThisFrame())
            DeleteAllEntities();
    }

    private static void ApplyPose(Transform target, InputActionProperty position, InputActionProperty rotation)
    {
        if (target == null || position.action == null || rotation.action == null) return;

        target.SetLocalPositionAndRotation(
            position.action.ReadValue<Vector3>(),
            rotation.action.ReadValue<Quaternion>());
    }

    // ------------------------------------------------------------------
    // Trigger tracking (grab)
    // ------------------------------------------------------------------

    private void TrackTrigger(Hand hand, InputActionProperty triggerAction)
    {
        if (triggerAction.action == null) return;

        int i = (int)hand;
        float value = triggerAction.action.ReadValue<float>();
        triggerValues[i] = value;

        if (!triggerPressed[i] && value >= pressThreshold)
        {
            triggerPressed[i] = true;
            OnTriggerPressed?.Invoke(hand, value);
            Debug.Log("GRAB!");
        }
        else if (triggerPressed[i] && value <= releaseThreshold)
        {
            triggerPressed[i] = false;
            OnTriggerReleased?.Invoke(hand, value);
            Debug.Log("throw that ho!");
        }
    }

    public float GetTriggerValue(Hand hand) => triggerValues[(int)hand];
    public bool IsTriggerPressed(Hand hand) => triggerPressed[(int)hand];

    // ------------------------------------------------------------------
    // Shooting (grip)
    // ------------------------------------------------------------------

    private void TrackGripShoot(Hand hand, InputActionProperty gripAction, Transform handTransform)
    {
        if (gripAction.action == null || handTransform == null) return;

        int i = (int)hand;
        float value = gripAction.action.ReadValue<float>();

        if (!gripPressed[i] && value >= pressThreshold)
        {
            gripPressed[i] = true;
            Shoot(handTransform);
            nextFireTime[i] = Time.time + fireCooldown;
        }
        else if (gripPressed[i])
        {
            if (value <= releaseThreshold)
            {
                gripPressed[i] = false;
            }
            else if (fireCooldown > 0f && Time.time >= nextFireTime[i])
            {
                Shoot(handTransform);
                nextFireTime[i] = Time.time + fireCooldown;
            }
        }
    }

    private void Shoot(Transform origin)
    {
        Vector3 dir = -1*(origin.up);
        Vector3 pos = origin.position + dir * muzzleOffset;

        GameObject sphere;
        if (projectilePrefab != null)
        {
            sphere = Instantiate(projectilePrefab, pos, Quaternion.LookRotation(dir));
        }
        else
        {
            sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.transform.SetPositionAndRotation(pos, Quaternion.LookRotation(dir));
            sphere.transform.localScale = Vector3.one * projectileScale;
        }
        sphere.name = "Projectile";

        var rb = sphere.GetComponent<Rigidbody>();
        if (rb == null) rb = sphere.AddComponent<Rigidbody>();

        rb.useGravity = false;                                          // no gravity -> travels forever
        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.AddForce(dir * projectileSpeed, ForceMode.VelocityChange);   // constant velocity, works on any Unity version

        projectiles.Add(sphere);
    }

    // ------------------------------------------------------------------
    // Delete everything (A button)
    // ------------------------------------------------------------------

    private void DeleteAllEntities()
    {
        // Spheres we shot
        foreach (var p in projectiles)
            if (p != null) Destroy(p);
        projectiles.Clear();

        // Cube(s) and anything else grabbable
#if UNITY_2023_1_OR_NEWER
        var grabbables = FindObjectsByType<VRGrabbable>(FindObjectsSortMode.None);
#else
        var grabbables = FindObjectsOfType<VRGrabbable>();
#endif
        foreach (var g in grabbables)
            Destroy(g.gameObject);

        Debug.Log("All entities deleted");
    }
}