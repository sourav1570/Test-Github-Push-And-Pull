using UnityEditor;

[InitializeOnLoad]
public static class GitHubFileTrackerLoader
{
    static GitHubFileTrackerLoader()
    {
        GitHubFileTracker.LoadAutoTrackedFilesFromDisk();
    }
}
