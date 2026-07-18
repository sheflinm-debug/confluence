using System.Reflection;
using UnityEditor;
using UnityEngine;

/// Tools > Auto-Assign AudioClips
///
/// Scans Assets/Audio/ for files whose names (without extension) exactly match
/// public AudioClip fields on AudioManager. Assigns any found clips and saves
/// the scene. Run once after importing audio files.
public static class AudioClipAutoAssign
{
    [MenuItem("Tools/Auto-Assign AudioClips")]
    public static void Run()
    {
        AudioManager mgr = Object.FindObjectOfType<AudioManager>();
        if (mgr == null)
        {
            EditorUtility.DisplayDialog("AudioClip Auto-Assign",
                "No AudioManager found in the scene. Open the simulation scene first.", "OK");
            return;
        }

        string[] guids = AssetDatabase.FindAssets("t:AudioClip", new[] { "Assets/Audio" });
        if (guids.Length == 0)
        {
            EditorUtility.DisplayDialog("AudioClip Auto-Assign",
                "No AudioClips found in Assets/Audio/. Import .ogg / .wav files there first.", "OK");
            return;
        }

        // Build name → clip map (filename without extension → clip)
        var clipMap = new System.Collections.Generic.Dictionary<string, AudioClip>(
            System.StringComparer.OrdinalIgnoreCase);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clip != null)
            {
                string key = System.IO.Path.GetFileNameWithoutExtension(path);
                clipMap[key] = clip;
            }
        }

        SerializedObject so = new SerializedObject(mgr);
        so.Update();

        int assigned = 0;
        int skipped  = 0;

        FieldInfo[] fields = typeof(AudioManager).GetFields(BindingFlags.Public | BindingFlags.Instance);
        foreach (FieldInfo fi in fields)
        {
            if (fi.FieldType != typeof(AudioClip)) continue;

            SerializedProperty prop = so.FindProperty(fi.Name);
            if (prop == null) continue;

            // Only assign if currently null (don't overwrite manual assignments)
            if (prop.objectReferenceValue != null) { skipped++; continue; }

            if (clipMap.TryGetValue(fi.Name, out AudioClip clip))
            {
                prop.objectReferenceValue = clip;
                assigned++;
                Debug.Log($"[AudioAutoAssign] {fi.Name} ← {clip.name}");
            }
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(mgr);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(mgr.gameObject.scene);

        string msg = $"Assigned {assigned} clip(s). {skipped} field(s) already had a clip (not overwritten).\n\n" +
                     $"Searched {clipMap.Count} clip(s) in Assets/Audio/.";
        EditorUtility.DisplayDialog("AudioClip Auto-Assign", msg, "OK");
        Debug.Log($"[AudioAutoAssign] Done — {assigned} assigned, {skipped} skipped.");
    }
}
