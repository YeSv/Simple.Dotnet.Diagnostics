namespace Simple.Dotnet.Diagnostics.Host;

public static class ErrorCodes
{
    public const int ValidationError = 400;
    public const int NotFound = 404;
    public const int InternalError = 500;

    // Http Dump
    public const int HttpWriteDumpFailed = 1001;
    public const int HttpReadDumpFailed = 1002;
    public const int HttpDeleteDumpFailed = 1003;
    public const int HttpGetDumpsFailed = 1004;
    public const int HttpDumpValidationError = 1010;

    // Http Processes
    public const int HttpGetProcessesFailed = 1011;
    public const int HttpGetProcessByIdFailed = 1012;
    public const int HttpGetProcessByNameFailed = 1013;
    public const int HttpGetProcessesValidationError = 1014;

    public static int ToHttpCode(int errorCode) => errorCode switch
    {
        // Generic
        NotFound => NotFound,
        InternalError => InternalError,

        // Processes
        HttpGetProcessesFailed => InternalError,
        HttpGetProcessByIdFailed => InternalError,
        HttpGetProcessByNameFailed => InternalError,
        HttpGetProcessesValidationError => ValidationError,

        // Dump
        HttpWriteDumpFailed => InternalError,
        HttpReadDumpFailed => InternalError,
        HttpDeleteDumpFailed => InternalError,
        HttpDumpValidationError => ValidationError,
        
        // All
        _ => InternalError
    };
}
