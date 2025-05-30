using UnityEditor;

public class AutoGitHubFileTracker : AssetPostprocessor
{
    static void OnPostprocessAllAssets(
        string[] importedAssets,
        string[] deletedAssets,
        string[] movedAssets,
        string[] movedFromAssetPaths)
    {
        // Track added or changed files
        foreach (string assetPath in importedAssets)
        {
            if (IsValidAsset(assetPath))
            {
                GitHubFileTracker.autoDetectedFiles.Add(assetPath);
            }
        }

        // Track deleted files
        foreach (string assetPath in deletedAssets)
        {
            GitHubFileTracker.deletedFiles.Add(assetPath);
        }
    }

    private static bool IsValidAsset(string path)
    {
        string[] validExtensions = { ".cs", ".prefab", ".mat", ".png", ".jpg", ".shader", ".txt", ".asset" };
        foreach (string ext in validExtensions)
        {
            if (path.EndsWith(ext)) return true;
        }
        return false;
    }
}
