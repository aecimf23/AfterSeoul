using UnityEngine;
namespace AfterSeoul.Unity.UI
{
    /// <summary>Identifies modal UI even when it belongs to a nested screen.</summary>
    public sealed class ModalState : MonoBehaviour
    {
        public bool VisibleWithin(Transform host)
        {
            var current=transform;
            while(current!=null && current!=host) {
                if(!current.gameObject.activeSelf)return false;
                current=current.parent;
            }
            return current==host;
        }
    }
}
