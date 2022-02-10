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

    // WebSocket Counters
    public const int WebSocketCountersFailed = 1021;
    public const int WebSocketCountersValidationError = 1022;

    // ServerSentEvents Counters
    public const int SseCountersFailed = 1031;
    public const int SseCountersValidationError = 1032;

    // Kafka Counters
    public const int KafkaCountersFailed = 1041;

    // Mongo Counters
    public const int MongoCountersFailed = 1051;

    // Actions
    public const int GetActionsFailed = 1101;

    // Traces
    public const int HttpWriteTraceFailed = 1201;
    public const int HttpReadTraceFailed = 1202;
    public const int HttpDeleteTraceFailed = 1203;
    public const int HttpGetTracesFailed = 1204;
    public const int HttpTracesValidationError = 1205;


    public static int ToHttpCode(int errorCode) => errorCode switch
    {
        // Generic
        NotFound => NotFound,
        InternalError => InternalError,

        // Processes
        HttpGetProcessesValidationError => ValidationError,

        // Dump
        HttpDumpValidationError => ValidationError,

        // Counters
        WebSocketCountersValidationError => ValidationError,
        SseCountersValidationError => ValidationError,

        // Traces
        HttpTracesValidationError => ValidationError,
        
        // All
        _ => InternalError
    };
}
