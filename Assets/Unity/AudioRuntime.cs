using UnityEngine;
using UnityEngine.SceneManagement;

namespace AfterSeoul.Unity
{
    /// <summary>Lifetime owner for generated audio and a scene-aware fallback listener.</summary>
    // Attach is also used by editor previews/tests, whose teardown must release native AudioClips.
    [ExecuteAlways]
    public sealed class AudioRuntime : MonoBehaviour
    {
        private AudioListener _fallback;
        private float _nextListenerCheck;

        private void OnEnable() => SceneManager.sceneLoaded += OnSceneLoaded;
        private void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;
        private void OnDestroy() => Sfx.Release(this);
        private void OnSceneLoaded(Scene scene, LoadSceneMode mode) => EnsureListener();

        private void Update()
        {
            // Edit-time attachment is explicit; do not add listeners while editing a scene.
            if (!Application.isPlaying) return;
            if (Time.unscaledTime < _nextListenerCheck) return;
            _nextListenerCheck = Time.unscaledTime + 1f;
            EnsureListener();
        }

        internal void EnsureListener()
        {
            bool externalListener = false;
            foreach (var listener in Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
            {
                if (listener != _fallback && listener.isActiveAndEnabled)
                {
                    externalListener = true;
                    break;
                }
            }
            if (_fallback == null && !externalListener)
                _fallback = gameObject.AddComponent<AudioListener>();
            if (_fallback != null) _fallback.enabled = !externalListener;
        }
    }
}
