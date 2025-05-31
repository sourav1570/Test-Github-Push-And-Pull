using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

//public static class GitHubFileTracker
//{
//    public static HashSet<string> autoDetectedFiles = new HashSet<string>();
//    public static HashSet<string> deletedFiles = new HashSet<string>();

//    // 👇 Add this line
//    public static HashSet<string> manuallyRemovedFiles = new HashSet<string>();
//}

//[InitializeOnLoad]
//public static class GitHubFileTracker
//{
//    public static List<string> autoTrackedFiles = new List<string>();
//    public static HashSet<string> autoDetectedFiles = new HashSet<string>();
//    public static HashSet<string> deletedFiles = new HashSet<string>();
//    public static HashSet<string> manuallyRemovedFiles = new HashSet<string>();
//    public static HashSet<string> alreadyPushedFiles = new HashSet<string>();

//    static GitHubFileTracker()
//    {
//        LoadPushedFiles();
//        LoadAutoTrackedFilesFromDisk();
//    }

//    public static void SavePushedFiles()
//    {
//        EditorPrefs.SetString("GitHubPushedFiles", string.Join(";", alreadyPushedFiles));
//    }

//    public static void LoadPushedFiles()
//    {
//        string saved = EditorPrefs.GetString("GitHubPushedFiles", "");
//        alreadyPushedFiles = new HashSet<string>(saved.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
//    }

//    public static void SaveAutoTrackedFilesToDisk()
//    {
//        EditorPrefs.SetString("GitHubAutoTrackedFiles", string.Join(";", autoTrackedFiles));
//    }

//    public static void LoadAutoTrackedFilesFromDisk()
//    {
//        string saved = EditorPrefs.GetString("GitHubAutoTrackedFiles", "");
//        autoTrackedFiles = saved.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
//    }
//}

[InitializeOnLoad]
public static class GitHubFileTracker
{
    public static List<string> autoTrackedFiles = new List<string>();
    public static HashSet<string> autoDetectedFiles = new HashSet<string>();
    public static HashSet<string> deletedFiles = new HashSet<string>();
    public static HashSet<string> manuallyRemovedFiles = new HashSet<string>();
    public static HashSet<string> alreadyPushedFiles = new HashSet<string>();

    static GitHubFileTracker()
    {
        LoadPushedFiles();
        LoadAutoTrackedFilesFromDisk();

        // ✅ Hook into scene saved event
        EditorSceneManager.sceneSaved += OnSceneSaved;
    }

    // ✅ Called when a scene is saved in Unity
    private static void OnSceneSaved(Scene scene)
    {
        string scenePath = scene.path;
        if (!string.IsNullOrEmpty(scenePath) &&
            !autoDetectedFiles.Contains(scenePath) &&
            !manuallyRemovedFiles.Contains(scenePath))
        {
            autoDetectedFiles.Add(scenePath);
            SaveAutoTrackedFilesToDisk(); // Optional: Persist it now
        }
    }

    public static void SavePushedFiles()
    {
        EditorPrefs.SetString("GitHubPushedFiles", string.Join(";", alreadyPushedFiles));
    }

    public static void LoadPushedFiles()
    {
        string saved = EditorPrefs.GetString("GitHubPushedFiles", "");
        alreadyPushedFiles = new HashSet<string>(saved.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
    }

    public static void SaveAutoTrackedFilesToDisk()
    {
        EditorPrefs.SetString("GitHubAutoTrackedFiles", string.Join(";", autoTrackedFiles));
    }

    public static void LoadAutoTrackedFilesFromDisk()
    {
        string saved = EditorPrefs.GetString("GitHubAutoTrackedFiles", "");
        autoTrackedFiles = saved.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
