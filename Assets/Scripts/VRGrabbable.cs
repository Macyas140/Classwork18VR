using UnityEngine;

/// <summary>
/// Put this on any 3D object that can be picked up and thrown.
/// The object also needs a Collider and a Rigidbody (added automatically).
///
/// While held, the object becomes kinematic and is parented to the hand.
/// When released it becomes a normal physics body again and receives the
/// hand's velocity, so it flies off like a thrown object.
/// </summary>
[RequireComponent(typeof(Rigidbody))]
public class VRGrabbable : MonoBehaviour
{
    [Tooltip("Extra multiplier applied to the throw velocity of this object only.")]
    [SerializeField] private float throwMultiplier = 1f;

    /// <summary>The hand currently holding this object (null if free).</summary>
    public VRGrabber Holder { get; private set; }
    public bool IsHeld => Holder != null;

    private Rigidbody rb;
    private Transform originalParent;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
    }

    /// <summary>Called by VRGrabber when this object is picked up.</summary>
    public void BeginGrab(VRGrabber grabber)
    {
        Debug.Log("Grabbed Twin");
        // Taking the object from the other hand.
        if (Holder != null && Holder != grabber)
            Holder.NotifyLost(this);
        else if (Holder == null)
            originalParent = transform.parent;

        Holder = grabber;

        rb.isKinematic = true;
        transform.SetParent(grabber.transform, true); // keeps the current offset
    }

    /// <summary>Called by VRGrabber when the trigger is released.</summary>
    public void EndGrab(Vector3 linearVelocity, Vector3 angularVelocity)
    {
        transform.SetParent(originalParent, true);

        rb.isKinematic = false;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // fast throws don't tunnel through floors

        SetLinearVelocity(linearVelocity * throwMultiplier);
        rb.angularVelocity = angularVelocity * throwMultiplier;

        Holder = null;
    }

    // Rigidbody.velocity was renamed to linearVelocity in Unity 6.
    private void SetLinearVelocity(Vector3 v)
    {
#if UNITY_6000_0_OR_NEWER
        rb.linearVelocity = v;
#else
        rb.velocity = v;
#endif
    }
}
