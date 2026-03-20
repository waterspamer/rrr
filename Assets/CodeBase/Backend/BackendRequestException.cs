using System;

public sealed class BackendRequestException : Exception
{
    public long StatusCode { get; }
    public string ErrorCode { get; }
    public string ResponseBody { get; }

    public BackendRequestException(string message, long statusCode, string errorCode, string responseBody)
        : base(message)
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }
}
