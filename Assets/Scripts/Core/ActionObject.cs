using Oculus.Interaction;
using UnityEngine;


public abstract class ActionObject : MonoBehaviour
{
    private void Start()
    {
        var obj = GetComponent<PointableElement>();
        if (obj != null)
        {
            obj.WhenPointerEventRaised += Obj_WhenPointerEventRaised;
        }
    }

    private void Obj_WhenPointerEventRaised(PointerEvent obj)
    {
        var editModeManager = EditModeManager.Instance;
        var matBinding = GetComponent<MatActionObjectBinding>();

        if (obj.Type == PointerEventType.Select)
        {
            bool allowSelection = editModeManager != null &&
                (editModeManager.IsEditMode || (editModeManager.IsMatEditMode && matBinding != null));
            if (allowSelection)
            {
                editModeManager.SetSelectedObject(gameObject);
            }
        }

        if (obj.Type == PointerEventType.Unselect)
        {
            if (editModeManager != null && editModeManager.IsMatEditMode && matBinding != null && GameManager.Instance != null && GameManager.Instance.Origin != null)
            {
                _ = matBinding.PersistIfChangedAsync(GameManager.Instance.Origin);
            }
        }
    }



    public abstract void Initialize(Arcor2.ClientSdk.ClientServices.Managers.ActionObjectManager actionObject);

    /// <summary>
    /// Turns off the Meta Interaction SDK grab rig (Grabbable / RayInteractable / Transformer /
    /// Pointable) so it does not fight the unified <see cref="ObjectManipulator"/> grip-grab, and
    /// neuters any EditableObject so it never re-enables that rig. Colliders are left intact so the
    /// object can still be selected by the EditModeManager raycast. Call from Initialize.
    /// </summary>
    protected void DisableMetaGrabBehaviours()
    {
        var behaviours = GetComponentsInChildren<Behaviour>(true);
        foreach (var behaviour in behaviours)
        {
            if (behaviour == null || behaviour == this)
            {
                continue;
            }

            string typeName = behaviour.GetType().Name;
            if (typeName.Contains("Grab") || typeName.Contains("Interactable") ||
                typeName.Contains("Transformer") || typeName.Contains("Pointable"))
            {
                behaviour.enabled = false;
            }
        }

        var editable = GetComponent<EditableObject>();
        if (editable != null)
        {
            editable.DisableManipulationManagement();
        }
    }
}
