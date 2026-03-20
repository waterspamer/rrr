using UnityEngine;

[CreateAssetMenu(fileName = "BackendConfig", menuName = "Russian Road Rage/Backend Config")]
public sealed class BackendConfig : ScriptableObject
{
    private const string DefaultApiBaseUrl = "https://rrr-demo.tonforspeed.space/api/v1";
    private const string ResourcesPath = "Backend/BackendConfig";

    [SerializeField] private string apiBaseUrl = DefaultApiBaseUrl;
    [SerializeField] private string webSocketUrl;
    [SerializeField] private bool logRequests = true;
    [SerializeField] private float requestTimeoutSeconds = 15.0f;

    public string ApiBaseUrl => NormalizeBaseUrl(string.IsNullOrWhiteSpace(apiBaseUrl) ? DefaultApiBaseUrl : apiBaseUrl);
    public bool LogRequests => logRequests;
    public int RequestTimeoutSeconds => Mathf.Max(1, Mathf.RoundToInt(requestTimeoutSeconds));

    public string ResolveWebSocketUrl(string sessionToken)
    {
        string baseUrl = string.IsNullOrWhiteSpace(webSocketUrl)
            ? BuildDefaultWebSocketUrl()
            : webSocketUrl.Trim();

        string separator = baseUrl.Contains("?") ? "&" : "?";
        return string.Format("{0}{1}session_token={2}", baseUrl, separator, UnityWebRequestEscaper.Escape(sessionToken));
    }

    public static BackendConfig LoadOrDefault()
    {
        BackendConfig asset = Resources.Load<BackendConfig>(ResourcesPath);
        if (asset != null)
            return asset;

        BackendConfig config = CreateInstance<BackendConfig>();
        config.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return config;
    }

    private string BuildDefaultWebSocketUrl()
    {
        string url = ApiBaseUrl;
        if (!url.EndsWith("/ws"))
            url = string.Format("{0}/ws", url.TrimEnd('/'));

        if (url.StartsWith("https://"))
            return "wss://" + url.Substring("https://".Length);

        if (url.StartsWith("http://"))
            return "ws://" + url.Substring("http://".Length);

        return url;
    }

    private static string NormalizeBaseUrl(string value)
    {
        return value.Trim().TrimEnd('/');
    }
}

internal static class UnityWebRequestEscaper
{
    public static string Escape(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? string.Empty : UnityEngine.Networking.UnityWebRequest.EscapeURL(value);
    }
}
