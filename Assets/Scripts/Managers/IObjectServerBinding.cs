using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Common contract for a runtime object whose transform is mirrored to the ARCOR2 server.
/// Implemented by both the portal/collision box binding and the MAT binding so the unified
/// <see cref="ObjectManipulator"/> can persist changes without knowing the concrete type.
/// </summary>
public interface IObjectServerBinding
{
    /// <summary>True if the object may be scaled (portals); false for pose-only objects (MATs).</summary>
    bool SupportsScale { get; }

    /// <summary>Push the current transform to the server if it changed since the last capture.</summary>
    Task PersistIfChangedAsync(Transform origin);
}
