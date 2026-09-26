using System;
using UnityEngine;
namespace AfterSeoul.Unity.UI
{
    /// <summary>Identifies modal UI even when it belongs to a nested screen.</summary>
    public sealed class ModalState : MonoBehaviour
    {
        public Action Close { private get; set; }
        public bool BackDismissible { get; set; } = true;

        public void HandleBack()
        {
            if (BackDismissible) Close?.Invoke();
        }

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
