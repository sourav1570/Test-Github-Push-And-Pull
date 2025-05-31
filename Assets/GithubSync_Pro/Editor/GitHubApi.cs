using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using Unity.Plastic.Newtonsoft.Json;
using Unity.Plastic.Newtonsoft.Json.Linq;

public static class GitHubApi
{
    public class CommitInfo
    {
        public string sha;
        public string treeSha;
    }

    public class TreeItem
    {
        public string path;
        public string mode;
        public string type;
        public string sha;
    }

    public static async Task<CommitInfo> GetLatestCommitAsync(string owner, string repo, string token)
    {
        string refUrl = $"https://api.github.com/repos/{owner}/{repo}/git/ref/heads/main";
        var refData = await GetJsonAsync(refUrl, token);
        string commitSha = refData["object"]["sha"].ToString();

        string commitUrl = $"https://api.github.com/repos/{owner}/{repo}/git/commits/{commitSha}";
        var commitData = await GetJsonAsync(commitUrl, token);
        string treeSha = commitData["tree"]["sha"].ToString();

        return new CommitInfo { sha = commitSha, treeSha = treeSha };
    }

    public static async Task<string> CreateBlobAsync(string owner, string repo, string token, string content)
    {
        string url = $"https://api.github.com/repos/{owner}/{repo}/git/blobs";
        var payload = new
        {
            content = content,
            encoding = "utf-8"
        };
        var response = await PostJsonAsync(url, token, payload);
        return response["sha"].ToString();
    }

    public static async Task<string> CreateTreeAsync(string owner, string repo, string token, string baseTree, List<TreeItem> items)
    {
        string url = $"https://api.github.com/repos/{owner}/{repo}/git/trees";
        var payload = new
        {
            base_tree = baseTree,
            tree = items
        };
        var response = await PostJsonAsync(url, token, payload);
        return response["sha"].ToString();
    }

    public static async Task<string> CreateCommitAsync(string owner, string repo, string token, string message, string treeSha, string parentSha)
    {
        string url = $"https://api.github.com/repos/{owner}/{repo}/git/commits";
        var payload = new
        {
            message = message,
            tree = treeSha,
            parents = new[] { parentSha }
        };
        var response = await PostJsonAsync(url, token, payload);
        return response["sha"].ToString();
    }

    public static async Task<bool> UpdateBranchAsync(string owner, string repo, string token, string branch, string newSha)
    {
        string url = $"https://api.github.com/repos/{owner}/{repo}/git/refs/heads/{branch}";
        var payload = new
        {
            sha = newSha,
            force = true
        };
        var response = await PatchJsonAsync(url, token, payload);
        return response.ContainsKey("object");
    }

    // HTTP helpers
    private static async Task<JObject> GetJsonAsync(string url, string token)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UnityUploader");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);
            var response = await client.GetStringAsync(url);
            return JObject.Parse(response);
        }
    }

    private static async Task<JObject> PostJsonAsync(string url, string token, object payload)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UnityUploader");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(url, content);
            var json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }
    }

    private static async Task<JObject> PatchJsonAsync(string url, string token, object payload)
    {
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.UserAgent.ParseAdd("UnityUploader");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);
            var content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");
            var request = new HttpRequestMessage(new HttpMethod("PATCH"), url) { Content = content };
            var response = await client.SendAsync(request);
            var json = await response.Content.ReadAsStringAsync();
            return JObject.Parse(json);
        }
    }
}
