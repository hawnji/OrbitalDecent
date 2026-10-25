using UnityEngine;

namespace OrbitalDescent
{
    /// <summary>Creates the game at start-up in any scene, so no scene setup or prefabs are required.</summary>
    public static class Bootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Start()
        {
#if UNITY_2023_1_OR_NEWER
            if (Object.FindAnyObjectByType<GameController>() != null) return;
#else
            if (Object.FindObjectOfType<GameController>() != null) return;
#endif
            new GameObject("Orbital Descent").AddComponent<GameController>();
        }
    }
}
