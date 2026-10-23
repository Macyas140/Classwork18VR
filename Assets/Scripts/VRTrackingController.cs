using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Minimal VR tracking script: ONLY tracks the headset and the two controllers
/// using the new Input System (no locomotion, no XR Interaction Toolkit).
/// It also raises trigger press / release events, which VRGrabber uses to
/// pick up and throw objects.
///
/// Setup:
///   Rig Root (this script, positioned at the floor)
///   ├── Head       (Camera)
///   ├── LeftHand   (VRGrabber + optional controller model)
///   └── RightHand  (VRGrabber + optional controller model)
///
/// Requires the Input System package and an XR plugin (OpenXR) enabled.
/// </summary>
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

    [Header("Right Controller Actions")]
    [SerializeField] private InputActionProperty rightPosition;
    [SerializeField] private InputActionProperty rightRotation;
    [SerializeField] private InputActionProperty rightTrigger;

    [Header("Trigger Thresholds")]
    [SerializeField, Range(0f, 1f)] private float pressThreshold = 0.6f;
    [SerializeField, Range(0f, 1f)] private float releaseThreshold = 0.4f;

    /// <summary>Fired once when a trigger crosses the press threshold. (hand, value)</summary>
    public event Action<Hand, float> OnTriggerPressed;
    /// <summary>Fired once when a trigger drops below the release threshold. (hand, value)</summary>
    public event Action<Hand, float> OnTriggerReleased;

    private readonly float[] triggerValues = new float[2];
    private readonly bool[] triggerPressed = new bool[2];

    // ------------------------------------------------------------------
    // Setup
    // ------------------------------------------------------------------

    // Runs when the component is added / reset in the editor: fills in default
    // XR bindings so it works without a custom Input Action asset.
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
    }

    private static InputActionProperty Create(string name, string binding, string controlType)
    {
        var action = new InputAction(name, InputActionType.Value, binding, expectedControlType: controlType);
        return new InputActionProperty(action);
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
        leftPosition, leftRotation, leftTrigger,
        rightPosition, rightRotation, rightTrigger
    };

    // ------------------------------------------------------------------
    // Update loop
    // ------------------------------------------------------------------

    private void Update()
    {
        // Poses are in tracking space (relative to the rig root), so use local space.
        ApplyPose(head,      headPosition,  headRotation);
        ApplyPose(leftHand,  leftPosition,  leftRotation);
        ApplyPose(rightHand, rightPosition, rightRotation);

        TrackTrigger(Hand.Left,  leftTrigger);
        TrackTrigger(Hand.Right, rightTrigger);
    }

    private static void ApplyPose(Transform target, InputActionProperty position, InputActionProperty rotation)
    {
        if (target == null || position.action == null || rotation.action == null) return;

        target.SetLocalPositionAndRotation(
            position.action.ReadValue<Vector3>(),
            rotation.action.ReadValue<Quaternion>());
    }

    // ------------------------------------------------------------------
    // Trigger tracking
    // ------------------------------------------------------------------

    /// <summary>
    /// Reads the trigger of one controller, stores its value and raises
    /// press / release events (with hysteresis to avoid flicker).
    /// </summary>
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

    /// <summary>Current analog trigger value (0 to 1).</summary>
    public float GetTriggerValue(Hand hand) => triggerValues[(int)hand];

    /// <summary>True while the trigger is held past the press threshold.</summary>
    public bool IsTriggerPressed(Hand hand) => triggerPressed[(int)hand];
}