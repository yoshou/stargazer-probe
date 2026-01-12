using UnityEditor;
using UnityEngine;

namespace StargazerProbe.Editor
{
    public static class ResetPlayerPrefs
    {
        [MenuItem("Tools/Reset PlayerPrefs to Defaults")]
        public static void ResetToDefaults()
        {
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
            Debug.Log("PlayerPrefs cleared. Settings will reset to defaults on next app start.");
        }
    }
}
