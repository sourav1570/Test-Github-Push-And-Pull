using UnityEditor;
using UnityEngine;
using System.Net;
using System.IO;

public class UpdateChecker : EditorWindow
{
    private string currentVersion = "1.0";

    [MenuItem("Tools/Check for Updates")]
    public static void ShowWindow()
    {
        GetWindow<UpdateChecker>("Check for Updates");
    }

    private void OnGUI()
    {
        GUILayout.Label("Current Version: " + currentVersion, EditorStyles.boldLabel);

        if (GUILayout.Button("Check for Updates"))
        {
            CheckForUpdates();
        }
    }

    private void CheckForUpdates()
    {
        string url = $"https://raw.githubusercontent.com/{GitHubPuller.RepositoryOwner}/{GitHubPuller.RepositoryName}/main/version.txt";

        WebClient client = new WebClient();
        string latestVersion = client.DownloadString(url).Trim();

        if (latestVersion != currentVersion)
        {
            Debug.Log("New update available: " + latestVersion);
            DownloadUpdatedFiles(latestVersion);
        }
        else
        {
            Debug.Log("You are using the latest version.");
        }
    }

    private void DownloadUpdatedFiles(string newVersion)
    {
        string fileUrl = $"https://raw.githubusercontent.com/{GitHubPuller.RepositoryOwner}/{GitHubPuller.RepositoryName}/main/MobileActionKit_{newVersion}.unitypackage";
        string savePath = Application.dataPath + "/DownloadedUpdates/MobileActionKit_" + newVersion + ".unitypackage";

        WebClient client = new WebClient();
        client.DownloadFile(fileUrl, savePath);

        Debug.Log("Downloaded update: " + savePath);
        currentVersion = newVersion;
    }
}
