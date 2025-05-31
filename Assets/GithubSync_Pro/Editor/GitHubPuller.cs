using UnityEditor;
using UnityEngine;
using System.IO;
using System.Net;
using Unity.Plastic.Newtonsoft.Json;
using System.Collections.Generic;

public class GitHubPuller : EditorWindow
{
    public static string RepositoryOwner = "";
    public static string RepositoryName = "";
    public static string Token = "";

    private string latestVersion = "Unknown";
    private string currentVersion = "Unknown";

    private bool showUpdateWindow = false;
    private float progress = 0f;
    private bool isDownloading = false;
    private int totalFiles = 1;
    private int downloadedFiles = 0;

    // History display toggle
    private bool showPullHistory = false;
    private Vector2 scrollPos;

    // Path for history JSON - now in persistentDataPath
    private string historyFilePath;

    // History data structures
    [System.Serializable]
    public class PullHistoryEntry
    {
        public string version;
        public string notes;
        public string pulledAt;
    }

    [System.Serializable]
    public class PullHistoryData
    {
        public List<PullHistoryEntry> pulls = new List<PullHistoryEntry>();
    }

    private PullHistoryData pullHistoryData = new PullHistoryData();

    [MenuItem("Tools/Check For Updates")]
    public static void CheckForUpdates()
    {
        GitHubPuller window = GetWindow<GitHubPuller>("GitHub Updater");
        window.minSize = new Vector2(400, 450);
    }

    private void OnEnable()
    {
        historyFilePath = Path.Combine(Application.persistentDataPath, "GitHubPuller_History.json");
        LoadPullHistory();
    }

    private void OnGUI()
    {
        GUILayout.Space(10);
        GUILayout.Label("🔄 GitHub Pull", EditorStyles.boldLabel);

        EditorGUILayout.LabelField("GitHub Settings", EditorStyles.boldLabel);
        RepositoryOwner = EditorGUILayout.TextField("Repository Owner", RepositoryOwner);
        RepositoryName = EditorGUILayout.TextField("Repository Name", RepositoryName);
        Token = EditorGUILayout.TextField("Token", Token);

        GUILayout.Space(10);

        if (isDownloading)
        {
            GUILayout.Space(20);
            EditorGUI.ProgressBar(new Rect(10, 120, position.width - 20, 25), progress, $"Downloading... {(int)(progress * 100)}% ({downloadedFiles}/{totalFiles})");
            GUILayout.Space(40);
        }
        else
        {
            GUILayout.Label("Latest Version: " + latestVersion, EditorStyles.boldLabel);
            if (GUILayout.Button("🔍 Check for Updates", GUILayout.Height(30)))
            {
                LoadCurrentVersion();
                CheckVersion();
            }
            if (GUILayout.Button("⬇️  Pull from Main", GUILayout.Height(30)))
            {
                StartPulling();
            }
        }

        GUILayout.Space(15);

        GUILayout.BeginHorizontal();

        if (GUILayout.Button("Show History"))
        {
            showPullHistory = true;
        }
        if (GUILayout.Button("Hide History"))
        {
            showPullHistory = false;
        }
        if (GUILayout.Button("Show History JSON File Location"))
        {
            ShowHistoryJsonFileLocation();
        }

        GUILayout.EndHorizontal();

        if (showPullHistory)
        {
            GUILayout.Label("Pull History", EditorStyles.boldLabel);

            if (pullHistoryData.pulls.Count == 0)
            {
                GUILayout.Label("No pull history found.");
            }
            else
            {
                scrollPos = GUILayout.BeginScrollView(scrollPos, GUILayout.Height(200));
                foreach (var entry in pullHistoryData.pulls)
                {
                    GUILayout.BeginVertical("box");
                    GUILayout.Label($"Version: {entry.version}");
                    GUILayout.Label($"Notes: {entry.notes}");
                    GUILayout.Label($"Pulled At: {entry.pulledAt}");
                    GUILayout.EndVertical();
                    GUILayout.Space(5);
                }
                GUILayout.EndScrollView();
            }
        }

        GUILayout.FlexibleSpace();
    }

    private void ShowHistoryJsonFileLocation()
    {
        if (File.Exists(historyFilePath))
        {
            EditorUtility.RevealInFinder(historyFilePath);
        }
        else
        {
            Debug.LogWarning("History file not found at: " + historyFilePath);
            EditorUtility.DisplayDialog("File Not Found", $"History file not found at:\n{historyFilePath}", "OK");
        }
    }

    private void StartPulling()
    {
        isDownloading = true;
        progress = 0f;
        downloadedFiles = 0;
        totalFiles = 1; // Will be updated during pull
        PullFilesForMainBranch();
        EditorApplication.update += UpdateProgressBar;
    }

    private void LoadCurrentVersion()
    {
        string localVersionPath = Path.Combine(Application.dataPath, "version.txt");
        currentVersion = File.Exists(localVersionPath) ? File.ReadAllText(localVersionPath).Trim() : "Unknown";
    }

    private void CheckVersion()
    {
        latestVersion = GetFileContentFromGitHub("version.txt");
        if (latestVersion == "Unknown")
        {
            EditorUtility.DisplayDialog("Updater", "Could not check for updates.", "Close");
            return;
        }

        if (latestVersion == currentVersion)
        {
            EditorUtility.DisplayDialog("Updater", "You are up to date!", "Close");
        }
        else
        {
            if (EditorUtility.DisplayDialog("Update Available",
                $"A new version ({latestVersion}) is available.\nCurrent version: {currentVersion}",
                "Pull Now", "Cancel"))
            {
                StartPulling();
            }
        }
    }

    private void UpdateProgressBar()
    {
        if (totalFiles == 0) return;
        progress = Mathf.Clamp01((float)downloadedFiles / totalFiles);
        Repaint();

        if (downloadedFiles >= totalFiles)
        {
            isDownloading = false;
            EditorApplication.update -= UpdateProgressBar;
            EditorUtility.DisplayDialog("Update Complete", "Files pulled from main branch.", "OK");

            // Add to pull history after pull completes
            UpdatePullHistory(latestVersion, "Pulled from main branch");
        }
    }

    private void PullFilesForMainBranch(string folderPath = "")
    {
        string url = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/contents/{folderPath}?ref=main";

        using (WebClient client = new WebClient())
        {
            client.Headers.Add("User-Agent", "UnityGitHubPuller");

            try
            {
                string response = client.DownloadString(url);
                var files = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(response);

                totalFiles = files.Count;

                foreach (var file in files)
                {
                    if (file["type"].ToString() == "dir")
                    {
                        PullFilesForMainBranch(file["path"].ToString());
                    }
                    else if (file.ContainsKey("path"))
                    {
                        string filePath = file["path"].ToString();
                        string downloadUrl = $"https://raw.githubusercontent.com/{RepositoryOwner}/{RepositoryName}/main/{filePath}";
                        DownloadFileIfChanged(downloadUrl, filePath);
                        downloadedFiles++;
                    }
                }
            }
            catch (WebException ex)
            {
                Debug.LogError($"Error fetching files: {ex.Message}");
            }
        }
    }

    private void DownloadFileIfChanged(string url, string filePath)
    {
        string localPath = Path.Combine(Application.dataPath, filePath);
        string directoryPath = Path.GetDirectoryName(localPath);

        if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

        try
        {
            using (WebClient client = new WebClient())
            {
                client.Headers.Add("User-Agent", "UnityGitHubPuller");
                byte[] fileData = client.DownloadData(url);
                File.WriteAllBytes(localPath, fileData);
                Debug.Log($"Updated: {filePath}");
            }

            string assetPath = "Assets/" + filePath;
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
            {
                AssetDatabase.ImportAsset(assetPath);
            }
        }
        catch (WebException ex)
        {
            Debug.LogError($"Error downloading {filePath}: {ex.Message}");
        }

        AssetDatabase.Refresh();
    }

    private string GetFileContentFromGitHub(string filePath)
    {
        try
        {
            string url = $"https://raw.githubusercontent.com/{RepositoryOwner}/{RepositoryName}/main/{filePath}";
            using (WebClient client = new WebClient())
            {
                client.Headers.Add("User-Agent", "UnityGitHubPuller");
                return client.DownloadString(url).Trim();
            }
        }
        catch (WebException ex)
        {
            Debug.LogWarning($"Could not fetch {filePath}: {ex.Message}");
            return "Unknown";
        }
    }

    private void UpdatePullHistory(string version, string notes = "")
    {
        var newEntry = new PullHistoryEntry
        {
            version = version,
            notes = notes,
            pulledAt = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
        };

        pullHistoryData.pulls.Add(newEntry);
        SavePullHistory();
    }

    private void LoadPullHistory()
    {
        if (File.Exists(historyFilePath))
        {
            var json = File.ReadAllText(historyFilePath);
            pullHistoryData = JsonUtility.FromJson<PullHistoryData>(json);
            if (pullHistoryData == null)
                pullHistoryData = new PullHistoryData();
        }
        else
        {
            pullHistoryData = new PullHistoryData();
        }
    }

    private void SavePullHistory()
    {
        var json = JsonUtility.ToJson(pullHistoryData, true);
        File.WriteAllText(historyFilePath, json);
    }
}







//using UnityEditor;
//using UnityEngine;
//using System.IO;
//using System.Net;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Collections.Generic;

//public class GitHubPuller : EditorWindow
//{
//    public static string RepositoryOwner = "";
//    public static string RepositoryName = "";
//    public static string Token = "";

//    private string latestVersion = "Unknown";
//    private string currentVersion = "Unknown";
//    private bool showUpdateWindow = false;

//    private float progress = 0f;
//    private bool isDownloading = false;
//    private int totalFiles = 1;
//    private int downloadedFiles = 0;

//    [MenuItem("Tools/Check For Updates")]
//    public static void CheckForUpdates()
//    {
//        GitHubPuller window = GetWindow<GitHubPuller>("GitHub Updater");
//        window.minSize = new Vector2(400, 200);
//    }

//    private void OnGUI()
//    {
//        GUILayout.Space(10);
//        GUILayout.Label("🔄 GitHub Pull", EditorStyles.boldLabel);

//        EditorGUILayout.LabelField("GitHub Settings", EditorStyles.boldLabel);
//        RepositoryOwner = EditorGUILayout.TextField("Repository Owner", RepositoryOwner);
//        RepositoryName = EditorGUILayout.TextField("Repository Name", RepositoryName);
//        Token = EditorGUILayout.TextField("Token", Token);

//        if (isDownloading)
//        {
//            GUILayout.Space(20);
//            EditorGUI.ProgressBar(new Rect(10, 50, position.width - 20, 25), progress, $"Downloading... {(int)(progress * 100)}% ({downloadedFiles}/{totalFiles})");
//            GUILayout.Space(40);
//        }
//        else
//        {
//            GUILayout.Label("Latest Version: " + latestVersion, EditorStyles.boldLabel);
//            if (GUILayout.Button("🔍 Check for Updates", GUILayout.Height(30)))
//            {
//                LoadCurrentVersion();
//                CheckVersion();
//            }
//            if (GUILayout.Button("⬇️  Pull from Main", GUILayout.Height(30)))
//            {
//                isDownloading = true;
//                progress = 0f;
//                downloadedFiles = 0;
//                PullFilesForMainBranch();
//                EditorApplication.update += UpdateProgressBar;
//            }
//        }

//        GUILayout.FlexibleSpace();
//    }

//    private void LoadCurrentVersion()
//    {
//        string localVersionPath = Path.Combine(Application.dataPath, "version.txt");
//        currentVersion = File.Exists(localVersionPath) ? File.ReadAllText(localVersionPath).Trim() : "Unknown";
//    }

//    private void CheckVersion()
//    {
//        latestVersion = GetFileContentFromGitHub("version.txt");
//        if (latestVersion == "Unknown")
//        {
//            EditorUtility.DisplayDialog("Updater", "Could not check for updates.", "Close");
//            return;
//        }

//        if (latestVersion == currentVersion)
//        {
//            EditorUtility.DisplayDialog("Updater", "You are up to date!", "Close");
//        }
//        else
//        {
//            if (EditorUtility.DisplayDialog("Update Available",
//                $"A new version ({latestVersion}) is available.\nCurrent version: {currentVersion}",
//                "Pull Now", "Cancel"))
//            {
//                isDownloading = true;
//                progress = 0f;
//                downloadedFiles = 0;
//                totalFiles = 1; // Will be recalculated
//                PullFilesForMainBranch(); // Changed to main-only logic
//                EditorApplication.update += UpdateProgressBar;
//            }
//        }
//    }


//    private void UpdateProgressBar()
//    {
//        if (totalFiles == 0) return;
//        progress = Mathf.Clamp01((float)downloadedFiles / totalFiles);
//        Repaint();

//        if (downloadedFiles >= totalFiles)
//        {
//            isDownloading = false;
//            EditorApplication.update -= UpdateProgressBar;
//            EditorUtility.DisplayDialog("Update Complete", "Files pulled from main branch.", "OK");
//        }
//    }

//    private void PullFilesForMainBranch(string folderPath = "")
//    {
//        string url = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/contents/{folderPath}?ref=main";

//        using (WebClient client = new WebClient())
//        {
//            client.Headers.Add("User-Agent", "UnityGitHubPuller");

//            try
//            {
//                string response = client.DownloadString(url);
//                var files = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(response);

//                totalFiles = files.Count;

//                foreach (var file in files)
//                {
//                    if (file["type"].ToString() == "dir")
//                    {
//                        PullFilesForMainBranch(file["path"].ToString());
//                    }
//                    else if (file.ContainsKey("path"))
//                    {
//                        string filePath = file["path"].ToString();
//                        string downloadUrl = $"https://raw.githubusercontent.com/{RepositoryOwner}/{RepositoryName}/main/{filePath}";
//                        DownloadFileIfChanged(downloadUrl, filePath);
//                        downloadedFiles++;
//                    }
//                }
//            }
//            catch (WebException ex)
//            {
//                Debug.LogError($"Error fetching files: {ex.Message}");
//            }
//        }
//    }

//    private void DownloadFileIfChanged(string url, string filePath)
//    {
//        string localPath = Path.Combine(Application.dataPath, filePath);
//        string directoryPath = Path.GetDirectoryName(localPath);

//        if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");
//                byte[] fileData = client.DownloadData(url);
//                File.WriteAllBytes(localPath, fileData);
//                Debug.Log($"Updated: {filePath}");
//            }

//            string assetPath = "Assets/" + filePath;
//            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
//            {
//                AssetDatabase.ImportAsset(assetPath);
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogError($"Error downloading {filePath}: {ex.Message}");
//        }

//        AssetDatabase.Refresh();
//    }

//    private string GetFileContentFromGitHub(string filePath)
//    {
//        try
//        {
//            string url = $"https://raw.githubusercontent.com/{RepositoryOwner}/{RepositoryName}/main/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");
//                return client.DownloadString(url).Trim();
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"Could not fetch {filePath}: {ex.Message}");
//            return "Unknown";
//        }
//    }
//}





//using UnityEditor;
//using UnityEngine;
//using System.IO;
//using System.Net;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Collections.Generic;

//public class GitHubPuller : EditorWindow
//{

//    public static string RepositoryOwner = "";
//    public static string RepositoryName = "";
//    public static string Token = "";

//    private string latestVersion = "Unknown";
//    private string currentVersion = "Unknown";
//    private bool showUpdateWindow = false;

//    private List<string> availableVersions = new List<string>();
//    private int selectedVersionIndex = 0;
//    private string selectedVersion => availableVersions.Count > 0 ? availableVersions[selectedVersionIndex] : "main";

//    private float progress = 0f;
//    private bool isDownloading = false;
//    private int totalFiles = 1;
//    private int downloadedFiles = 0;

//    [MenuItem("Tools/Check For Updates")]
//    public static void CheckForUpdates()
//    {
//        GitHubPuller window = GetWindow<GitHubPuller>("GitHub Updater");
//        window.minSize = new Vector2(400, 200);
//        //window.LoadCurrentVersion();
//        //window.CheckVersion();
//    }

//    private void OnGUI()
//    {
//        GUILayout.Space(10);
//        GUILayout.Label("🔄 GitHub Pull", EditorStyles.boldLabel);

//        EditorGUILayout.LabelField("GitHub Settings", EditorStyles.boldLabel);
//        RepositoryOwner = EditorGUILayout.TextField("Repository Owner", RepositoryOwner);
//        RepositoryName = EditorGUILayout.TextField("Repository Name", RepositoryName);
//        Token = EditorGUILayout.TextField("Token", Token);

//        if (showUpdateWindow)
//        {
//            EditorGUILayout.LabelField("Current Version:", currentVersion, EditorStyles.helpBox);
//            EditorGUILayout.LabelField("Available Versions:", EditorStyles.boldLabel);

//            if (availableVersions.Count > 0)
//            {
//                selectedVersionIndex = EditorGUILayout.Popup(selectedVersionIndex, availableVersions.ToArray());
//            }
//            else
//            {
//                GUILayout.Label("Fetching versions...", EditorStyles.label);
//            }

//            GUILayout.Space(10);
//            if (GUILayout.Button("⬇️  Pull Selected Version", GUILayout.Height(30)))
//            {
//                isDownloading = true;
//                progress = 0f;
//                downloadedFiles = 0;
//                PullFilesForVersion(selectedVersion);
//                EditorApplication.update += UpdateProgressBar;
//            }
//        }
//        else if (isDownloading)
//        {
//            GUILayout.Space(20);
//            EditorGUI.ProgressBar(new Rect(10, 50, position.width - 20, 25), progress, $"Downloading... {(int)(progress * 100)}%");
//            GUILayout.Space(40);
//        }
//        else
//        {
//            GUILayout.Label("Latest Version: " + latestVersion, EditorStyles.boldLabel);
//            if (GUILayout.Button("🔍 Check for Updates", GUILayout.Height(30)))
//            {
//                LoadCurrentVersion();
//                CheckVersion();
//            }
//        }

//        GUILayout.FlexibleSpace();
//    }

//    private void LoadCurrentVersion()
//    {
//        string localVersionPath = Path.Combine(Application.dataPath, "version.txt");
//        currentVersion = File.Exists(localVersionPath) ? File.ReadAllText(localVersionPath).Trim() : "Unknown";
//    }

//    private void CheckVersion()
//    {
//        latestVersion = GetFileContentFromGitHub("version.txt");
//        if (latestVersion == "Unknown")
//        {
//            EditorUtility.DisplayDialog("Updater", "Could not check for updates.", "Close");
//            return;
//        }

//        if (latestVersion == currentVersion)
//        {
//            EditorUtility.DisplayDialog("Updater", "You are up to date!", "Close");
//        }
//        else
//        {
//            FetchAvailableVersions();
//            showUpdateWindow = true;
//        }
//    }

//    private void UpdateProgressBar()
//    {
//        if (totalFiles == 0) return;
//        progress = Mathf.Clamp01((float)downloadedFiles / totalFiles);
//        Repaint();

//        if (downloadedFiles >= totalFiles)
//        {
//            isDownloading = false;
//            EditorApplication.update -= UpdateProgressBar;
//            EditorUtility.DisplayDialog("Update Complete", $"Pulled version {selectedVersion}.", "OK");
//        }
//    }

//    private void FetchAvailableVersions()
//    {
//        availableVersions.Clear();
//        try
//        {
//            string url = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/tags";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");
//                string response = client.DownloadString(url);
//                var tags = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(response);

//                foreach (var tag in tags)
//                {
//                    if (tag.ContainsKey("name"))
//                        availableVersions.Add(tag["name"].ToString());
//                }

//                if (availableVersions.Count > 0)
//                {
//                    selectedVersionIndex = 0;
//                }
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"Failed to get tags: {ex.Message}");
//        }
//    }

//    private void PullFilesForVersion(string version, string folderPath = "")
//    {
//        string url = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/contents/{folderPath}?ref={version}";

//        using (WebClient client = new WebClient())
//        {
//            client.Headers.Add("User-Agent", "UnityGitHubPuller");

//            try
//            {
//                string response = client.DownloadString(url);
//                var files = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(response);

//                totalFiles = files.Count;

//                foreach (var file in files)
//                {
//                    if (file["type"].ToString() == "dir")
//                    {
//                        PullFilesForVersion(version, file["path"].ToString());
//                    }
//                    else if (file.ContainsKey("path"))
//                    {
//                        string filePath = file["path"].ToString();
//                        string downloadUrl = $"https://raw.githubusercontent.com/{RepositoryOwner}/{RepositoryName}/{version}/{filePath}";
//                        DownloadFileIfChanged(downloadUrl, filePath);
//                        downloadedFiles++;
//                    }
//                }
//            }
//            catch (WebException ex)
//            {
//                Debug.LogError($"Error fetching files: {ex.Message}");
//            }
//        }
//    }

//    private void DownloadFileIfChanged(string url, string filePath)
//    {
//        string localPath = Path.Combine(Application.dataPath, filePath);
//        string directoryPath = Path.GetDirectoryName(localPath);

//        if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);

//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");
//                byte[] fileData = client.DownloadData(url);
//                File.WriteAllBytes(localPath, fileData);
//                Debug.Log($"Updated: {filePath}");
//            }

//            string assetPath = "Assets/" + filePath;
//            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
//            {
//                AssetDatabase.ImportAsset(assetPath);
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogError($"Error downloading {filePath}: {ex.Message}");
//        }

//        AssetDatabase.Refresh();
//    }

//    private string GetFileContentFromGitHub(string filePath)
//    {
//        try
//        {
//            string url = $"https://raw.githubusercontent.com/{RepositoryOwner}/{RepositoryName}/main/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");
//                return client.DownloadString(url).Trim();
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"Could not fetch {filePath}: {ex.Message}");
//            return "Unknown";
//        }
//    }
//}








//using UnityEditor;
//using UnityEngine;
//using System.IO;
//using System.Net;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Collections.Generic;

//public class GitHubPuller : EditorWindow
//{
//    private string repoOwner = GitHubConfig.RepositoryOwner;
//    private string repoName = GitHubConfig.RepositoryName;

//    private string latestVersion = "Unknown";
//    private string currentVersion = "Unknown";
//    private string whatsNew = "";
//    private bool showUpdateWindow = false;
//    private bool showProgressBar = false;
//    private float progress = 0f;



//    [MenuItem("Tools/Check For Updates")]
//    public static void CheckForUpdates()
//    {
//        GitHubPuller window = GetWindow<GitHubPuller>("Mobile Action Kit");
//        window.LoadCurrentVersion();
//        window.CheckVersion();

//        // If versions match, don't re-open the window.
//        if (window.latestVersion == window.currentVersion)
//        {
//            // Close the window immediately after checking the version, if they're the same
//            window.Close();
//        }
//    }


//    private void LoadCurrentVersion()
//    {
//        string localVersionPath = Path.Combine(Application.dataPath, "version.txt");
//        if (File.Exists(localVersionPath))
//        {
//            currentVersion = File.ReadAllText(localVersionPath).Trim();
//        }
//        else
//        {
//            currentVersion = "Unknown";
//        }
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("GitHub Updater", EditorStyles.boldLabel);


//        if (showUpdateWindow)
//        {
//            // Display only the first line of the current version
//            string firstLineOfVersion = currentVersion.Split(new[] { '\n', '\r' })[0]; // Get the first line
//            GUILayout.Label("Current Version: " + firstLineOfVersion, EditorStyles.boldLabel); // Show current version


//            //  GUILayout.Label("Current Version: " + currentVersion, EditorStyles.boldLabel); // Show current version
//            GUILayout.Label("Update Available: " + latestVersion, EditorStyles.boldLabel); // Show update version
//            //GUILayout.Label("What's New:", EditorStyles.boldLabel);
//            //GUILayout.Label(whatsNew, EditorStyles.wordWrappedLabel);

//            if (GUILayout.Button("Update"))
//            {
//                showProgressBar = true;
//                showUpdateWindow = false;
//                EditorApplication.update += UpdateProgressBar;
//                PullLatestFiles();
//            }
//        }
//        else if (showProgressBar)
//        {
//            EditorGUI.ProgressBar(new Rect(10, 50, position.width - 20, 20), progress, "Updating...");
//        }
//        else
//        {
//            GUILayout.Label("Latest Version: " + latestVersion, EditorStyles.boldLabel);
//            if (GUILayout.Button("Check for Updates"))
//            {
//                LoadCurrentVersion();
//                CheckVersion();
//            }
//        }
//    }


//    private void CheckVersion()
//    {
//        latestVersion = GetLatestVersion();
//        //whatsNew = GetWhatsNew(); // Fetch the What's New content from GitHub

//        if (latestVersion == "Unknown")
//        {
//            EditorUtility.DisplayDialog("Updater", "Could not check for updates. Please try again.", "Close");
//            return;
//        }

//        if (latestVersion == currentVersion)
//        {
//            EditorUtility.DisplayDialog("Updater", "You are up to date!", "Close");
//            return;
//        }
//        else
//        {
//            showUpdateWindow = true;
//        }
//    }


//    private void UpdateProgressBar()
//    {
//        progress += 0.02f;
//        if (progress >= 1f)
//        {
//            showProgressBar = false;
//            progress = 0f;
//            EditorApplication.update -= UpdateProgressBar;
//            EditorUtility.DisplayDialog("Updater", "You are up to date!", "OK");
//        }
//        Repaint();
//    }

//    private string GetLatestVersion()
//    {
//        return GetFileContentFromGitHub("version.txt");
//    }
//    private string GetFileContentFromGitHub(string filePath)
//    {
//        try
//        {
//            string url = $"https://raw.githubusercontent.com/{repoOwner}/{repoName}/main/{filePath}";

//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");
//                string content = client.DownloadString(url);
//                return content.Trim();
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"Could not retrieve {filePath}: {ex.Message}");
//        }
//        return "Unknown";
//    }
//    private void PullLatestFiles(string folderPath = "")
//    {
//        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{folderPath}";
//        using (WebClient client = new WebClient())
//        {
//            client.Headers.Add("User-Agent", "UnityGitHubPuller");

//            try
//            {
//                string response = client.DownloadString(url);
//                var files = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(response);

//                foreach (var file in files)
//                {
//                    if (file["type"].ToString() == "dir")
//                    {
//                        PullLatestFiles(file["path"].ToString());
//                    }
//                    else if (file.ContainsKey("path"))
//                    {
//                        string filePath = file["path"].ToString();
//                        string downloadUrl = $"https://raw.githubusercontent.com/{repoOwner}/{repoName}/main/{filePath}";
//                        DownloadFileIfChanged(downloadUrl, filePath);
//                    }
//                }
//            }
//            catch (WebException ex)
//            {
//                Debug.LogError($"Error fetching files: {ex.Message}");
//            }
//        }
//    }

//    private void DownloadFileIfChanged(string url, string filePath)
//    {
//        string localPath = Path.Combine(Application.dataPath, filePath);
//        string directoryPath = Path.GetDirectoryName(localPath);

//        if (string.IsNullOrEmpty(directoryPath))
//        {
//            Debug.LogError($"Invalid directory path for {filePath}");
//            return;
//        }

//        if (!Directory.Exists(directoryPath))
//        {
//            Directory.CreateDirectory(directoryPath);
//        }

//        string githubSHA = GetFileSHA(filePath);
//        string localSHA = GetLocalFileSHA(localPath);

//        if (!string.IsNullOrEmpty(localSHA) && githubSHA == localSHA)
//        {
//            Debug.Log($"File {filePath} is up to date. Skipping update.");
//            return;
//        }

//        Debug.Log($"Updating file: {filePath}");

//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");
//                byte[] fileData = client.DownloadData(url);

//                // Write new data to the file (no deletion)
//                File.WriteAllBytes(localPath, fileData);
//                Debug.Log($"Updated file: {localPath}");
//            }

//            // Refresh Unity without forcing full reimport unless needed
//            string assetPath = "Assets/" + filePath;
//            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
//            {
//                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.Default);
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogError($"Error downloading file {filePath}: {ex.Message}");
//        }

//        AssetDatabase.Refresh();
//    }

//    private string GetFileSHA(string filePath)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");

//                string response = client.DownloadString(url);
//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);

//                if (jsonResponse.ContainsKey("sha"))
//                {
//                    return jsonResponse["sha"].ToString();
//                }
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"Could not retrieve SHA for {filePath}: {ex.Message}");
//        }
//        return null;
//    }


//    private string GetLocalFileSHA(string filePath)
//    {
//        if (!File.Exists(filePath))
//        {
//            return null;
//        }

//        using (var sha = System.Security.Cryptography.SHA1.Create())
//        {
//            using (var stream = File.OpenRead(filePath))
//            {
//                return System.BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLower();
//            }
//        }
//    }
//}




//using UnityEditor;
//using UnityEngine;
//using System.IO;
//using System.Net;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Collections.Generic;

//public class GitHubPuller : EditorWindow
//{
//    private string repoOwner = GitHubConfig.RepositoryOwner;
//    private string repoName = GitHubConfig.RepositoryName;
//    private string token = GitHubConfig.Token;
//    private string latestVersion = "Unknown";
//    private string currentVersion = "Unknown";
//    private string whatsNew = "";
//    private bool showUpdateWindow = false;
//    private bool showProgressBar = false;
//    private float progress = 0f;



//    [MenuItem("Tools/Check For Updates")]
//    public static void CheckForUpdates()
//    {
//        GitHubPuller window = GetWindow<GitHubPuller>("Mobile Action Kit");
//        window.LoadCurrentVersion();
//        window.CheckVersion();

//        // If versions match, don't re-open the window.
//        if (window.latestVersion == window.currentVersion)
//        {
//            // Close the window immediately after checking the version, if they're the same
//            window.Close();
//        }
//    }


//    private void LoadCurrentVersion()
//    {
//        string localVersionPath = Path.Combine(Application.dataPath, "version.txt");
//        if (File.Exists(localVersionPath))
//        {
//            currentVersion = File.ReadAllText(localVersionPath).Trim();
//        }
//        else
//        {
//            currentVersion = "Unknown";
//        }
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("GitHub Updater", EditorStyles.boldLabel);


//        if (showUpdateWindow)
//        {
//            // Display only the first line of the current version
//            string firstLineOfVersion = currentVersion.Split(new[] { '\n', '\r' })[0]; // Get the first line
//            GUILayout.Label("Current Version: " + firstLineOfVersion, EditorStyles.boldLabel); // Show current version


//            //  GUILayout.Label("Current Version: " + currentVersion, EditorStyles.boldLabel); // Show current version
//            GUILayout.Label("Update Available: " + latestVersion, EditorStyles.boldLabel); // Show update version
//            //GUILayout.Label("What's New:", EditorStyles.boldLabel);
//            //GUILayout.Label(whatsNew, EditorStyles.wordWrappedLabel);

//            if (GUILayout.Button("Update"))
//            {
//                showProgressBar = true;
//                showUpdateWindow = false;
//                EditorApplication.update += UpdateProgressBar;
//                PullLatestFiles();
//            }
//        }
//        else if (showProgressBar)
//        {
//            EditorGUI.ProgressBar(new Rect(10, 50, position.width - 20, 20), progress, "Updating...");
//        }
//        else
//        {
//            GUILayout.Label("Latest Version: " + latestVersion, EditorStyles.boldLabel);
//            if (GUILayout.Button("Check for Updates"))
//            {
//                LoadCurrentVersion();
//                CheckVersion();
//            }
//        }
//    }


//    private void CheckVersion()
//    {
//        latestVersion = GetLatestVersion();
//        //whatsNew = GetWhatsNew(); // Fetch the What's New content from GitHub

//        if (latestVersion == "Unknown")
//        {
//            EditorUtility.DisplayDialog("Updater", "Could not check for updates. Please try again.", "Close");
//            return;
//        }

//        if (latestVersion == currentVersion)
//        {
//            EditorUtility.DisplayDialog("Updater", "You are up to date!", "Close");
//            return;
//        }
//        else
//        {
//            showUpdateWindow = true;
//        }
//    }


//    private void UpdateProgressBar()
//    {
//        progress += 0.02f;
//        if (progress >= 1f)
//        {
//            showProgressBar = false;
//            progress = 0f;
//            EditorApplication.update -= UpdateProgressBar;
//            EditorUtility.DisplayDialog("Updater", "You are up to date!", "OK");
//        }
//        Repaint();
//    }

//    private string GetLatestVersion()
//    {
//        return GetFileContentFromGitHub("version.txt");
//    }

//    private string GetFileContentFromGitHub(string filePath)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");
//                string response = client.DownloadString(url);

//                if (!string.IsNullOrEmpty(response))
//                {
//                    var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                    if (jsonResponse != null && jsonResponse.ContainsKey("content"))
//                    {
//                        string encodedContent = jsonResponse["content"].ToString();
//                        return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(encodedContent)).Trim();
//                    }
//                }
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"Could not retrieve {filePath}: {ex.Message}");
//        }
//        return "Unknown";
//    }
//    private void PullLatestFiles(string folderPath = "")
//    {
//        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{folderPath}";
//        using (WebClient client = new WebClient())
//        {
//            client.Headers.Add("Authorization", "token " + token);
//            client.Headers.Add("User-Agent", "UnityGitHubPuller");

//            try
//            {
//                string response = client.DownloadString(url);
//                if (string.IsNullOrEmpty(response))
//                {
//                    Debug.LogError("GitHub response was empty.");
//                    return;
//                }

//                var files = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(response);

//                if (files == null || files.Count == 0)
//                {
//                    Debug.LogWarning("No files found in the repository.");
//                    return;
//                }

//                foreach (var file in files)
//                {
//                    if (file.ContainsKey("type") && file["type"].ToString() == "dir")
//                    {
//                        string localFolderPath = Path.Combine(Application.dataPath, file["path"].ToString());
//                        if (!Directory.Exists(localFolderPath))
//                        {
//                            Directory.CreateDirectory(localFolderPath);
//                            Debug.Log($"Created directory: {localFolderPath}");
//                        }

//                        PullLatestFiles(file["path"].ToString()); // Recursively fetch contents inside this folder
//                    }
//                    else if (file.ContainsKey("download_url") && file.ContainsKey("path"))
//                    {
//                        string fileUrl = file["download_url"].ToString();
//                        string filePath = file["path"].ToString();
//                        if (!string.IsNullOrEmpty(fileUrl) && !string.IsNullOrEmpty(filePath))
//                        {
//                            DownloadFileIfChanged(fileUrl, filePath);
//                        }
//                        else
//                        {
//                            Debug.LogWarning($"Invalid file entry: {file}");
//                        }
//                    }
//                }
//            }
//            catch (WebException ex)
//            {
//                Debug.LogError($"Error fetching files from GitHub: {ex.Message}");
//            }
//        }
//    }
//    private void DownloadFileIfChanged(string url, string filePath)
//    {
//        string localPath = Path.Combine(Application.dataPath, filePath);
//        string directoryPath = Path.GetDirectoryName(localPath);

//        if (string.IsNullOrEmpty(directoryPath))
//        {
//            Debug.LogError($"Invalid directory path for {filePath}");
//            return;
//        }

//        if (!Directory.Exists(directoryPath))
//        {
//            Directory.CreateDirectory(directoryPath);
//        }

//        string githubSHA = GetFileSHA(filePath);
//        string localSHA = GetLocalFileSHA(localPath);

//        if (!string.IsNullOrEmpty(localSHA) && githubSHA == localSHA)
//        {
//            Debug.Log($"File {filePath} is up to date. Skipping update.");
//            return;
//        }

//        Debug.Log($"Updating file: {filePath}");

//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                byte[] fileData = client.DownloadData(url);

//                // Write new data to the file (no deletion)
//                File.WriteAllBytes(localPath, fileData);
//                Debug.Log($"Updated file: {localPath}");
//            }

//            // Refresh Unity without forcing full reimport unless needed
//            string assetPath = "Assets/" + filePath;
//            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
//            {
//                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.Default);
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogError($"Error downloading file {filePath}: {ex.Message}");
//        }

//        AssetDatabase.Refresh();
//    }
//    private string GetFileSHA(string filePath)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");

//                string response = client.DownloadString(url);
//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);

//                if (jsonResponse.ContainsKey("sha"))
//                {
//                    return jsonResponse["sha"].ToString();
//                }
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"Could not retrieve SHA for {filePath}: {ex.Message}");
//        }
//        return null;
//    }

//    private string GetLocalFileSHA(string filePath)
//    {
//        if (!File.Exists(filePath))
//        {
//            return null;
//        }

//        using (var sha = System.Security.Cryptography.SHA1.Create())
//        {
//            using (var stream = File.OpenRead(filePath))
//            {
//                return System.BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLower();
//            }
//        }
//    }
//}




//using UnityEditor;
//using UnityEngine;
//using System.IO;
//using System.Net;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Collections.Generic;

//public class GitHubPuller : EditorWindow
//{
//    private string repoOwner = GitHubConfig.RepositoryOwner;
//    private string repoName = GitHubConfig.RepositoryName;
//    private string token = GitHubConfig.Token;
//    private string latestVersion = "Unknown";
//    private string currentVersion = "Unknown";
//    private string whatsNew = "";
//    private bool showUpdateWindow = false;
//    private bool showProgressBar = false;
//    private float progress = 0f;

//    [MenuItem("Tools/Check For Updates")]
//    public static void CheckForUpdates()
//    {
//        GitHubPuller window = GetWindow<GitHubPuller>("Mobile Action Kit");
//        window.LoadCurrentVersion();
//        window.CheckVersion();

//        // If versions match, don't re-open the window.
//        if (window.latestVersion == window.currentVersion)
//        {
//            // Close the window immediately after checking the version, if they're the same
//            window.Close();
//        }
//    }


//    private void LoadCurrentVersion()
//    {
//        string localVersionPath = Path.Combine(Application.dataPath, "version.txt");
//        if (File.Exists(localVersionPath))
//        {
//            currentVersion = File.ReadAllText(localVersionPath).Trim();
//        }
//        else
//        {
//            currentVersion = "Unknown";
//        }
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("GitHub Updater", EditorStyles.boldLabel);

//        if (showUpdateWindow)
//        {
//            // Display only the first line of the current version
//            string firstLineOfVersion = currentVersion.Split(new[] { '\n', '\r' })[0]; // Get the first line
//            GUILayout.Label("Current Version: " + firstLineOfVersion, EditorStyles.boldLabel); // Show current version


//            //  GUILayout.Label("Current Version: " + currentVersion, EditorStyles.boldLabel); // Show current version
//            GUILayout.Label("Update Available: " + latestVersion, EditorStyles.boldLabel); // Show update version
//            //GUILayout.Label("What's New:", EditorStyles.boldLabel);
//            //GUILayout.Label(whatsNew, EditorStyles.wordWrappedLabel);

//            if (GUILayout.Button("Update"))
//            {
//                showProgressBar = true;
//                showUpdateWindow = false;
//                EditorApplication.update += UpdateProgressBar;
//                PullLatestFiles();
//            }
//        }
//        else if (showProgressBar)
//        {
//            EditorGUI.ProgressBar(new Rect(10, 50, position.width - 20, 20), progress, "Updating...");
//        }
//        else
//        {
//            GUILayout.Label("Latest Version: " + latestVersion, EditorStyles.boldLabel);
//            if (GUILayout.Button("Check for Updates"))
//            {
//                LoadCurrentVersion();
//                CheckVersion();
//            }
//        }
//    }


//    private void CheckVersion()
//    {
//        latestVersion = GetLatestVersion();
//        //whatsNew = GetWhatsNew(); // Fetch the What's New content from GitHub

//        if (latestVersion == "Unknown")
//        {
//            EditorUtility.DisplayDialog("Updater", "Could not check for updates. Please try again.", "Close");
//            return;
//        }

//        if (latestVersion == currentVersion)
//        {
//            EditorUtility.DisplayDialog("Updater", "You are up to date!", "Close");
//            return;
//        }
//        else
//        {
//            showUpdateWindow = true;
//        }
//    }


//    private void UpdateProgressBar()
//    {
//        progress += 0.02f;
//        if (progress >= 1f)
//        {
//            showProgressBar = false;
//            progress = 0f;
//            EditorApplication.update -= UpdateProgressBar;
//            EditorUtility.DisplayDialog("Updater", "You are up to date!", "OK");
//        }
//        Repaint();
//    }

//    private string GetLatestVersion()
//    {
//        return GetFileContentFromGitHub("version.txt");
//    }

//    //private string GetWhatsNew()
//    //{
//    //    return GetFileContentFromGitHub("whatsnew.txt");
//    //}

//    private string GetFileContentFromGitHub(string filePath)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");
//                string response = client.DownloadString(url);

//                if (!string.IsNullOrEmpty(response))
//                {
//                    var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                    if (jsonResponse != null && jsonResponse.ContainsKey("content"))
//                    {
//                        string encodedContent = jsonResponse["content"].ToString();
//                        return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(encodedContent)).Trim();
//                    }
//                }
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"Could not retrieve {filePath}: {ex.Message}");
//        }
//        return "Unknown";
//    }
//    private void PullLatestFiles(string folderPath = "")
//    {
//        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{folderPath}";
//        using (WebClient client = new WebClient())
//        {
//            client.Headers.Add("Authorization", "token " + token);
//            client.Headers.Add("User-Agent", "UnityGitHubPuller");

//            try
//            {
//                string response = client.DownloadString(url);
//                if (string.IsNullOrEmpty(response))
//                {
//                    Debug.LogError("GitHub response was empty.");
//                    return;
//                }

//                var files = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(response);

//                if (files == null || files.Count == 0)
//                {
//                    Debug.LogWarning("No files found in the repository.");
//                    return;
//                }

//                foreach (var file in files)
//                {
//                    if (file.ContainsKey("type") && file["type"].ToString() == "dir")
//                    {
//                        string localFolderPath = Path.Combine(Application.dataPath, file["path"].ToString());
//                        if (!Directory.Exists(localFolderPath))
//                        {
//                            Directory.CreateDirectory(localFolderPath);
//                            Debug.Log($"Created directory: {localFolderPath}");
//                        }

//                        PullLatestFiles(file["path"].ToString()); // Recursively fetch contents inside this folder
//                    }
//                    else if (file.ContainsKey("download_url") && file.ContainsKey("path"))
//                    {
//                        string fileUrl = file["download_url"].ToString();
//                        string filePath = file["path"].ToString();
//                        if (!string.IsNullOrEmpty(fileUrl) && !string.IsNullOrEmpty(filePath))
//                        {
//                            DownloadFileIfChanged(fileUrl, filePath);
//                        }
//                        else
//                        {
//                            Debug.LogWarning($"Invalid file entry: {file}");
//                        }
//                    }
//                }
//            }
//            catch (WebException ex)
//            {
//                Debug.LogError($"Error fetching files from GitHub: {ex.Message}");
//            }
//        }
//    }
//    private void DownloadFileIfChanged(string url, string filePath)
//    {
//        string localPath = Path.Combine(Application.dataPath, filePath);
//        string directoryPath = Path.GetDirectoryName(localPath);

//        if (string.IsNullOrEmpty(directoryPath))
//        {
//            Debug.LogError($"Invalid directory path for {filePath}");
//            return;
//        }

//        if (!Directory.Exists(directoryPath))
//        {
//            Directory.CreateDirectory(directoryPath);
//        }

//        string githubSHA = GetFileSHA(filePath);
//        string localSHA = GetLocalFileSHA(localPath);

//        if (!string.IsNullOrEmpty(localSHA) && githubSHA == localSHA)
//        {
//            Debug.Log($"File {filePath} is up to date. Skipping update.");
//            return;
//        }

//        Debug.Log($"Updating file: {filePath}");

//        try
//        {
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                byte[] fileData = client.DownloadData(url);

//                // Write new data to the file (no deletion)
//                File.WriteAllBytes(localPath, fileData);
//                Debug.Log($"Updated file: {localPath}");
//            }

//            // Refresh Unity without forcing full reimport unless needed
//            string assetPath = "Assets/" + filePath;
//            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath) != null)
//            {
//                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.Default);
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogError($"Error downloading file {filePath}: {ex.Message}");
//        }

//        AssetDatabase.Refresh();
//    }

//private void DownloadFileIfChanged(string url, string filePath)
//{
//    string localPath = Path.Combine(Application.dataPath, filePath);
//    string directoryPath = Path.GetDirectoryName(localPath);

//    if (string.IsNullOrEmpty(directoryPath))
//    {
//        Debug.LogError($"Invalid directory path for {filePath}");
//        return;
//    }

//    if (!Directory.Exists(directoryPath))
//    {
//        Directory.CreateDirectory(directoryPath);
//    }

//    string githubSHA = GetFileSHA(filePath);
//    string localSHA = GetLocalFileSHA(localPath);

//    if (githubSHA == localSHA)
//    {
//        Debug.Log($"File {filePath} is up to date. Skipping update.");
//        return;
//    }

//    Debug.Log($"Updating file: {filePath} (SHA mismatch detected)");

//    using (WebClient client = new WebClient())
//    {
//        client.Headers.Add("Authorization", "token " + token);

//        try
//        {
//            byte[] fileData = client.DownloadData(url);
//            File.WriteAllBytes(localPath, fileData);
//            Debug.Log($"Downloaded and replaced file: {localPath}");
//        }
//        catch (WebException ex)
//        {
//            Debug.LogError($"Error downloading file {filePath}: {ex.Message}");
//        }
//    }

//    AssetDatabase.ImportAsset("Assets/" + filePath, ImportAssetOptions.ForceUpdate);
//    AssetDatabase.Refresh();
//}
//    private string GetFileSHA(string filePath)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");

//                string response = client.DownloadString(url);
//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);

//                if (jsonResponse.ContainsKey("sha"))
//                {
//                    return jsonResponse["sha"].ToString();
//                }
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"Could not retrieve SHA for {filePath}: {ex.Message}");
//        }
//        return null;
//    }

//    private string GetLocalFileSHA(string filePath)
//    {
//        if (!File.Exists(filePath))
//        {
//            return null;
//        }

//        using (var sha = System.Security.Cryptography.SHA1.Create())
//        {
//            using (var stream = File.OpenRead(filePath))
//            {
//                return System.BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLower();
//            }
//        }
//    }
//}








//using UnityEditor;
//using UnityEngine;
//using System.IO;
//using System.Net;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Collections.Generic;

//public class GitHubPuller : EditorWindow
//{
//    private string repoOwner = GitHubConfig.RepositoryOwner;
//    private string repoName = GitHubConfig.RepositoryName;
//    private string token = GitHubConfig.Token;
//    private string latestVersion = "Unknown";

//    [MenuItem("Tools/GitHub Puller")]
//    public static void ShowWindow()
//    {
//        GetWindow<GitHubPuller>("GitHub Puller");
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("GitHub Updater", EditorStyles.boldLabel);

//        if (GUILayout.Button("Check for Updates"))
//        {
//            latestVersion = GetLatestVersion();
//            Debug.Log("Latest Version on GitHub: " + latestVersion);
//        }

//        GUILayout.Label("Latest Version: " + latestVersion, EditorStyles.boldLabel);

//        if (GUILayout.Button("Pull Latest Changes"))
//        {
//            PullLatestFiles();
//        }
//    }

//    private string GetLatestVersion()
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/version.txt";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");
//                string response = client.DownloadString(url);

//                if (!string.IsNullOrEmpty(response))
//                {
//                    var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                    if (jsonResponse != null && jsonResponse.ContainsKey("content"))
//                    {
//                        string encodedContent = jsonResponse["content"].ToString();
//                        return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(encodedContent)).Trim();
//                    }
//                }
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"Could not retrieve the latest version: {ex.Message}");
//        }
//        return "Unknown";
//    }

//    private void PullLatestFiles(string folderPath = "")
//    {
//        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{folderPath}";
//        using (WebClient client = new WebClient())
//        {
//            client.Headers.Add("Authorization", "token " + token);
//            client.Headers.Add("User-Agent", "UnityGitHubPuller");

//            try
//            {
//                string response = client.DownloadString(url);
//                if (string.IsNullOrEmpty(response))
//                {
//                    Debug.LogError("GitHub response was empty.");
//                    return;
//                }

//                var files = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(response);

//                if (files == null || files.Count == 0)
//                {
//                    Debug.LogWarning("No files found in the repository.");
//                    return;
//                }

//                foreach (var file in files)
//                {
//                    if (file.ContainsKey("type") && file["type"].ToString() == "dir")
//                    {
//                        string localFolderPath = Path.Combine(Application.dataPath, file["path"].ToString());
//                        if (!Directory.Exists(localFolderPath))
//                        {
//                            Directory.CreateDirectory(localFolderPath);
//                            Debug.Log($"Created directory: {localFolderPath}");
//                        }

//                        PullLatestFiles(file["path"].ToString()); // Recursively fetch contents inside this folder
//                    }
//                    else if (file.ContainsKey("download_url") && file.ContainsKey("path"))
//                    {
//                        string fileUrl = file["download_url"].ToString();
//                        string filePath = file["path"].ToString();
//                        if (!string.IsNullOrEmpty(fileUrl) && !string.IsNullOrEmpty(filePath))
//                        {
//                            DownloadFileIfChanged(fileUrl, filePath);
//                        }
//                        else
//                        {
//                            Debug.LogWarning($"Invalid file entry: {file}");
//                        }
//                    }
//                }
//            }
//            catch (WebException ex)
//            {
//                Debug.LogError($"Error fetching files from GitHub: {ex.Message}");
//            }
//        }
//    }

//    private void DownloadFileIfChanged(string url, string filePath)
//    {
//        string localPath = Path.Combine(Application.dataPath, filePath);
//        string directoryPath = Path.GetDirectoryName(localPath);

//        if (string.IsNullOrEmpty(directoryPath))
//        {
//            Debug.LogError($"Invalid directory path for {filePath}");
//            return;
//        }

//        if (!Directory.Exists(directoryPath))
//        {
//            Directory.CreateDirectory(directoryPath);
//        }

//        string githubSHA = GetFileSHA(filePath);
//        string localSHA = GetLocalFileSHA(localPath);

//        if (githubSHA == localSHA)
//        {
//            Debug.Log($"File {filePath} is up to date. Skipping update.");
//            return;
//        }

//        Debug.Log($"Updating file: {filePath} (SHA mismatch detected)");

//        using (WebClient client = new WebClient())
//        {
//            client.Headers.Add("Authorization", "token " + token);

//            try
//            {
//                byte[] fileData = client.DownloadData(url);
//                File.WriteAllBytes(localPath, fileData);
//                Debug.Log($"Downloaded and replaced file: {localPath}");
//            }
//            catch (WebException ex)
//            {
//                Debug.LogError($"Error downloading file {filePath}: {ex.Message}");
//            }
//        }

//        AssetDatabase.ImportAsset("Assets/" + filePath, ImportAssetOptions.ForceUpdate);
//        AssetDatabase.Refresh();
//    }

//    private string GetFileSHA(string filePath)
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{filePath}";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");

//                string response = client.DownloadString(url);
//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);

//                if (jsonResponse.ContainsKey("sha"))
//                {
//                    return jsonResponse["sha"].ToString();
//                }
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"Could not retrieve SHA for {filePath}: {ex.Message}");
//        }
//        return null;
//    }

//    private string GetLocalFileSHA(string filePath)
//    {
//        if (!File.Exists(filePath))
//        {
//            return null;
//        }

//        using (var sha = System.Security.Cryptography.SHA1.Create())
//        {
//            using (var stream = File.OpenRead(filePath))
//            {
//                return System.BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLower();
//            }
//        }
//    }
//}




//using UnityEditor;
//using UnityEngine;
//using System.IO;
//using System.Net;
//using Unity.Plastic.Newtonsoft.Json;
//using System.Collections.Generic;

//public class GitHubPuller : EditorWindow
//{
//    private string repoOwner = GitHubConfig.RepositoryOwner;
//    private string repoName = GitHubConfig.RepositoryName;
//    private string token = GitHubConfig.Token;
//    private string latestVersion = "Unknown";

//    [MenuItem("Tools/GitHub Puller")]
//    public static void ShowWindow()
//    {
//        GetWindow<GitHubPuller>("GitHub Puller");
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("GitHub Updater", EditorStyles.boldLabel);

//        if (GUILayout.Button("Check for Updates"))
//        {
//            latestVersion = GetLatestVersion();
//            Debug.Log("Latest Version on GitHub: " + latestVersion);
//        }

//        GUILayout.Label("Latest Version: " + latestVersion, EditorStyles.boldLabel);

//        if (GUILayout.Button("Pull Latest Changes"))
//        {
//            PullLatestFiles();
//        }
//    }

//    private string GetLatestVersion()
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/version.txt";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");
//                string response = client.DownloadString(url);

//                if (!string.IsNullOrEmpty(response))
//                {
//                    var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                    if (jsonResponse != null && jsonResponse.ContainsKey("content"))
//                    {
//                        string encodedContent = jsonResponse["content"].ToString();
//                        return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(encodedContent)).Trim();
//                    }
//                }
//            }
//        }
//        catch (WebException ex)
//        {
//            Debug.LogWarning($"Could not retrieve the latest version: {ex.Message}");
//        }
//        return "Unknown";
//    }

//    private void PullLatestFiles(string folderPath = "")
//    {
//        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/{folderPath}";
//        using (WebClient client = new WebClient())
//        {
//            client.Headers.Add("Authorization", "token " + token);
//            client.Headers.Add("User-Agent", "UnityGitHubPuller");

//            try
//            {
//                string response = client.DownloadString(url);
//                if (string.IsNullOrEmpty(response))
//                {
//                    Debug.LogError("GitHub response was empty.");
//                    return;
//                }

//                var files = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(response);

//                if (files == null || files.Count == 0)
//                {
//                    Debug.LogWarning("No files found in the repository.");
//                    return;
//                }

//                foreach (var file in files)
//                {
//                    if (file.ContainsKey("type") && file["type"].ToString() == "dir")
//                    {
//                        // Create the directory if it's missing
//                        string localFolderPath = Path.Combine(Application.dataPath, file["path"].ToString());
//                        if (!Directory.Exists(localFolderPath))
//                        {
//                            Directory.CreateDirectory(localFolderPath);
//                            Debug.Log($"Created directory: {localFolderPath}");
//                        }

//                        // Recursively fetch contents inside this folder
//                        PullLatestFiles(file["path"].ToString());
//                    }
//                    else if (file.ContainsKey("download_url") && file.ContainsKey("path"))
//                    {
//                        string fileUrl = file["download_url"].ToString();
//                        string filePath = file["path"].ToString();
//                        if (!string.IsNullOrEmpty(fileUrl) && !string.IsNullOrEmpty(filePath))
//                        {
//                            DownloadFile(fileUrl, filePath);
//                        }
//                        else
//                        {
//                            Debug.LogWarning($"Invalid file entry: {file}");
//                        }
//                    }
//                }
//            }
//            catch (WebException ex)
//            {
//                Debug.LogError($"Error fetching files from GitHub: {ex.Message}");
//            }
//        }
//    }


//    private void DownloadFile(string url, string filePath)
//    {
//        string localPath = Path.Combine(Application.dataPath, filePath);
//        string directoryPath = Path.GetDirectoryName(localPath);

//        if (string.IsNullOrEmpty(directoryPath))
//        {
//            Debug.LogError($"Invalid directory path for {filePath}");
//            return;
//        }

//        if (!Directory.Exists(directoryPath))
//        {
//            Directory.CreateDirectory(directoryPath);
//        }

//        // 🔴 Delete the existing file to ensure a fresh update
//        if (File.Exists(localPath))
//        {
//            File.Delete(localPath);
//            Debug.Log($"Deleted old file: {localPath}");
//        }

//        using (WebClient client = new WebClient())
//        {
//            client.Headers.Add("Authorization", "token " + token);

//            try
//            {
//                byte[] fileData = client.DownloadData(url); // Download as byte array
//                File.WriteAllBytes(localPath, fileData); // Overwrite with new content
//                Debug.Log($"Downloaded and replaced file: {localPath}");
//            }
//            catch (WebException ex)
//            {
//                Debug.LogError($"Error downloading file {filePath}: {ex.Message}");
//            }
//        }

//        // 🔄 Force Unity to re-import the asset
//        string relativePath = "Assets/" + filePath;
//        AssetDatabase.ImportAsset(relativePath, ImportAssetOptions.ForceUpdate);
//        AssetDatabase.Refresh();
//    }


//}





//using UnityEditor;
//using UnityEngine;
//using System.IO;
//using System.Net;
//using Unity.Plastic.Newtonsoft.Json; // Install Newtonsoft JSON via Package Manager
//using System.Collections.Generic;

//public class GitHubPuller : EditorWindow
//{
//    private string repoOwner = GitHubConfig.RepositoryOwner;
//    private string repoName = GitHubConfig.RepositoryName;
//    private string token = GitHubConfig.Token;
//    private string latestVersion = "Unknown";

//    [MenuItem("Tools/GitHub Puller")]
//    public static void ShowWindow()
//    {
//        GetWindow<GitHubPuller>("GitHub Puller");
//    }

//    private void OnGUI()
//    {
//        GUILayout.Label("GitHub Updater", EditorStyles.boldLabel);

//        if (GUILayout.Button("Check for Updates"))
//        {
//            latestVersion = GetLatestVersion();
//            Debug.Log("Latest Version on GitHub: " + latestVersion);
//        }

//        GUILayout.Label("Latest Version: " + latestVersion, EditorStyles.boldLabel);

//        if (GUILayout.Button("Pull Latest Changes"))
//        {
//            PullLatestFiles();
//        }
//    }

//    private string GetLatestVersion()
//    {
//        try
//        {
//            string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/version.txt";
//            using (WebClient client = new WebClient())
//            {
//                client.Headers.Add("Authorization", "token " + token);
//                client.Headers.Add("User-Agent", "UnityGitHubPuller");
//                string response = client.DownloadString(url);

//                var jsonResponse = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
//                if (jsonResponse.ContainsKey("content"))
//                {
//                    string encodedContent = jsonResponse["content"].ToString();
//                    return System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(encodedContent)).Trim();
//                }
//            }
//        }
//        catch (WebException)
//        {
//            Debug.LogWarning("Could not retrieve the latest version. Defaulting to unknown.");
//        }
//        return "Unknown";
//    }

//    private void PullLatestFiles()
//    {
//        string url = $"https://api.github.com/repos/{repoOwner}/{repoName}/contents/";
//        using (WebClient client = new WebClient())
//        {
//            client.Headers.Add("Authorization", "token " + token);
//            client.Headers.Add("User-Agent", "UnityGitHubPuller");

//            string response = client.DownloadString(url);
//            var files = JsonConvert.DeserializeObject<object[]>(response);

//            foreach (var file in files)
//            {
//                var jsonFile = JsonConvert.DeserializeObject<Dictionary<string, object>>(file.ToString());
//                if (jsonFile.ContainsKey("download_url") && jsonFile.ContainsKey("path"))
//                {
//                    string fileUrl = jsonFile["download_url"].ToString();
//                    string filePath = jsonFile["path"].ToString();

//                    DownloadFile(fileUrl, filePath);
//                }
//            }
//        }
//    }

//    private void DownloadFile(string url, string filePath)
//    {
//        string localPath = Path.Combine(Application.dataPath, filePath);
//        string directoryPath = Path.GetDirectoryName(localPath);

//        // Ensure the directory exists
//        if (!Directory.Exists(directoryPath))
//        {
//            Directory.CreateDirectory(directoryPath);
//        }

//        using (WebClient client = new WebClient())
//        {
//            client.Headers.Add("Authorization", "token " + token);
//            client.DownloadFile(url, localPath);
//        }

//        Debug.Log($"Updated file: {localPath}");
//        AssetDatabase.Refresh();
//    }

//}
