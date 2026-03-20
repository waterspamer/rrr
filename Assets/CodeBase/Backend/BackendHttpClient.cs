using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

public sealed class BackendHttpClient
{
    private readonly BackendConfig config;

    public BackendHttpClient(BackendConfig config)
    {
        this.config = config;
    }

    public Task<TResponse> GetAsync<TResponse>(string path, string sessionToken = null)
    {
        return SendAsync<object, TResponse>(UnityWebRequest.kHttpVerbGET, path, null, sessionToken);
    }

    public Task<TResponse> PostAsync<TRequest, TResponse>(string path, TRequest body, string sessionToken = null)
    {
        return SendAsync<TRequest, TResponse>(UnityWebRequest.kHttpVerbPOST, path, body, sessionToken);
    }

    public Task<TResponse> PutAsync<TRequest, TResponse>(string path, TRequest body, string sessionToken = null)
    {
        return SendAsync<TRequest, TResponse>("PUT", path, body, sessionToken);
    }

    private async Task<TResponse> SendAsync<TRequest, TResponse>(string method, string path, TRequest body, string sessionToken)
    {
        using (UnityWebRequest request = BuildRequest(method, path, body, sessionToken))
        {
            if (config.LogRequests)
                Debug.Log(string.Format("Backend HTTP {0} {1}", method, request.url));

            UnityWebRequestAsyncOperation operation = request.SendWebRequest();
            while (!operation.isDone)
                await Task.Yield();

            string responseText = request.downloadHandler != null ? request.downloadHandler.text : string.Empty;
            if (request.result != UnityWebRequest.Result.Success)
                throw CreateException(request, responseText);

            if (typeof(TResponse) == typeof(string))
                return (TResponse)(object)responseText;

            if (string.IsNullOrWhiteSpace(responseText))
                return default;

            return JsonUtility.FromJson<TResponse>(responseText);
        }
    }

    public string BuildPathWithQuery(string path, IReadOnlyList<KeyValuePair<string, string>> query)
    {
        if (query == null || query.Count == 0)
            return path;

        StringBuilder builder = new StringBuilder(path);
        bool hasQuery = path.Contains("?");
        for (int i = 0; i < query.Count; i++)
        {
            KeyValuePair<string, string> pair = query[i];
            if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value))
                continue;

            builder.Append(hasQuery ? '&' : '?');
            builder.Append(UnityWebRequestEscaper.Escape(pair.Key));
            builder.Append('=');
            builder.Append(UnityWebRequestEscaper.Escape(pair.Value));
            hasQuery = true;
        }

        return builder.ToString();
    }

    private UnityWebRequest BuildRequest<TRequest>(string method, string path, TRequest body, string sessionToken)
    {
        string url = string.Format("{0}/{1}", config.ApiBaseUrl.TrimEnd('/'), path.TrimStart('/'));
        UnityWebRequest request = new UnityWebRequest(url, method)
        {
            downloadHandler = new DownloadHandlerBuffer(),
            timeout = config.RequestTimeoutSeconds
        };

        request.SetRequestHeader("Accept", "application/json");
        if (!string.IsNullOrWhiteSpace(sessionToken))
            request.SetRequestHeader("Authorization", "Bearer " + sessionToken);

        bool hasBody = !EqualityComparer<TRequest>.Default.Equals(body, default);
        if (hasBody && method != UnityWebRequest.kHttpVerbGET)
        {
            string json = JsonUtility.ToJson(body);
            byte[] payload = Encoding.UTF8.GetBytes(json);
            request.uploadHandler = new UploadHandlerRaw(payload);
            request.SetRequestHeader("Content-Type", "application/json");
        }

        return request;
    }

    private static BackendRequestException CreateException(UnityWebRequest request, string responseText)
    {
        string errorCode = null;
        string message = request.error;
        if (!string.IsNullOrWhiteSpace(responseText))
        {
            try
            {
                BackendErrorResponse error = JsonUtility.FromJson<BackendErrorResponse>(responseText);
                if (error != null)
                {
                    if (!string.IsNullOrWhiteSpace(error.code))
                        errorCode = error.code;
                    if (!string.IsNullOrWhiteSpace(error.message))
                        message = error.message;
                }
            }
            catch
            {
            }
        }

        if (string.IsNullOrWhiteSpace(message))
            message = string.Format("HTTP request failed with status {0}.", request.responseCode);

        return new BackendRequestException(message, request.responseCode, errorCode, responseText);
    }
}
