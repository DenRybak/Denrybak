using UnityEngine;

namespace BallisticSniper
{
    public static class RuntimeBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateGame()
        {
            if (Object.FindObjectOfType<BallisticGame>() != null)
            {
                return;
            }

            GameObject root = new GameObject("Ballistic Sniper — Runtime");
            Object.DontDestroyOnLoad(root);
            root.AddComponent<BallisticGame>();
        }
    }
}
