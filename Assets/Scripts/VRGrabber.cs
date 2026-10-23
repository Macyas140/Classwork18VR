using UnityEngine;

/// <summary>
/// Put this on each hand object (LeftHand / RightHand).
/// Pressing the trigger picks up the closest VRGrabbable inside the grab
/// radius; releasing the trigger throws it using the hand's real velocity.
///
/// It listens to VRTrackingController's trigger events, so that script must
/// be present on the rig root (it is found automatically in the parents).
/// </summary>
[DisallowMultipleComponent]
public class VRGrabber : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Optional. If empty, it is searched in the parents (rig root).")]
    [SerializeField] private VRTrackingController tracking;
    [Tooltip("Which controller's trigger drives this hand.")]
    [SerializeField] private VRTrackingController.Hand hand = VRTrackingController.Hand.Right;

    [Header("Grab")]
    [Tooltip("Radius (meters) of the sphere used to detect objects.")]
    [SerializeField] private float grabRadius = 0.1f;
    [Tooltip("Offset of the grab sphere, in the hand's local space.")]
    [SerializeField] private Vector3 grabCenterOffset = Vector3.zero;
    [SerializeField] private LayerMask grabLayers = ~0;

    [Header("Throw")]
    [Tooltip("Multiplies the hand's linear velocity when releasing. 1 = realistic.")]
    [SerializeField] private float throwVelocityMultiplier = 1.2f;
    [Tooltip("Multiplies the hand's angular velocity when releasing.")]
    [SerializeField] private float throwAngularMultiplier = 1f;

    /// <summary>The object currently held by this hand (null if none).</summary>
    public VRGrabbable Held { get; private set; }

    // --- Velocity estimation (ring buffer of the last few frames) ---
    private const int SampleCount = 6;
    private readonly Vector3[] posSamples = new Vector3[SampleCount];
    private readonly Quaternion[] rotSamples = new Quaternion[SampleCount];
    private readonly float[] timeSamples = new float[SampleCount];
    private int nextSample;
    private int sampleFill;

    private readonly Collider[] hits = new Collider[32];

    // ------------------------------------------------------------------
    // Lifecycle
    // ------------------------------------------------------------------

    private void OnEnable()
    {
        if (tracking == null) tracking = GetComponentInParent<VRTrackingController>();
        if (tracking == null)
        {
            Debug.LogWarning("VRGrabber: no VRTrackingController found.", this);
            return;
        }

        tracking.OnTriggerPressed += HandleTriggerPressed;
        tracking.OnTriggerReleased += HandleTriggerReleased;
    }

    private void OnDisable()
    {
        if (tracking != null)
        {
            tracking.OnTriggerPressed -= HandleTriggerPressed;
            tracking.OnTriggerReleased -= HandleTriggerReleased;
        }

        // Drop whatever we were holding so it doesn't stay frozen in the air.
        if (Held != null)
        {
            var g = Held;
            Held = null;
            g.EndGrab(Vector3.zero, Vector3.zero);
        }
    }

    // Runs after VRTrackingController.Update has moved the hand this frame,
    // so the sampled pose is always the up-to-date one.
    private void LateUpdate()
    {
        posSamples[nextSample] = transform.position;
        rotSamples[nextSample] = transform.rotation;
        timeSamples[nextSample] = Time.unscaledTime;

        nextSample = (nextSample + 1) % SampleCount;
        if (sampleFill < SampleCount) sampleFill++;
    }

    // ------------------------------------------------------------------
    // Grab / release
    // ------------------------------------------------------------------

    private void HandleTriggerPressed(VRTrackingController.Hand h, float value)
    {
        if (h != hand || Held != null) return;

        VRGrabbable target = FindClosestGrabbable();
        if (target == null) return;

        Held = target;
        target.BeginGrab(this);
    }

    private void HandleTriggerReleased(VRTrackingController.Hand h, float value)
    {
        if (h != hand || Held == null) return;

        GetHandVelocity(out Vector3 linear, out Vector3 angular);

        VRGrabbable released = Held;
        Held = null;
        released.EndGrab(linear * throwVelocityMultiplier, angular * throwAngularMultiplier);
    }

    /// <summary>Called by VRGrabbable when the other hand steals the object.</summary>
    internal void NotifyLost(VRGrabbable grabbable)
    {
        if (Held == grabbable) Held = null;
    }

    private VRGrabbable FindClosestGrabbable()
    {
        Vector3 center = transform.TransformPoint(grabCenterOffset);
        int count = Physics.OverlapSphereNonAlloc(
            center, grabRadius, hits, grabLayers, QueryTriggerInteraction.Ignore);

        VRGrabbable best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < count; i++)
        {
            VRGrabbable candidate = hits[i].GetComponentInParent<VRGrabbable>();
            if (candidate == null) continue;

            float d = (hits[i].bounds.ClosestPoint(center) - center).sqrMagnitude;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = candidate;
            }
        }

        return best;
    }

    // ------------------------------------------------------------------
    // Velocity estimation
    // ------------------------------------------------------------------

    /// <summary>
    /// Hand velocity in WORLD space, measured from the last few frames
    /// (more reliable on Quest than reading the device velocity from OpenXR).
    /// </summary>
    private void GetHandVelocity(out Vector3 linear, out Vector3 angular)
    {
        linear = Vector3.zero;
        angular = Vector3.zero;
        if (sampleFill < 2) return;

        int newest = (nextSample - 1 + SampleCount) % SampleCount;
        int oldest = sampleFill < SampleCount ? 0 : nextSample;

        float dt = timeSamples[newest] - timeSamples[oldest];
        if (dt <= 0.0001f) return;

        linear = (posSamples[newest] - posSamples[oldest]) / dt;

        Quaternion delta = rotSamples[newest] * Quaternion.Inverse(rotSamples[oldest]);
        delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (angleDeg > 180f) angleDeg -= 360f;

        if (!float.IsInfinity(axis.x) && !float.IsNaN(axis.x))
            angular = axis * (angleDeg * Mathf.Deg2Rad / dt);
    }

    // ------------------------------------------------------------------
    // Editor helper
    // ------------------------------------------------------------------

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 1f, 1f, 0.6f);
        Gizmos.DrawWireSphere(transform.TransformPoint(grabCenterOffset), grabRadius);
    }
}
