using System;

namespace Simple.Dotnet.Diagnostics.Core;

public readonly record struct DiagnosticsError(string? Validation, Exception? Exception)
{
    public DiagnosticsError(string validation) : this(validation, default) { }
    public DiagnosticsError(Exception exception) : this(default, exception) { }

    public override string ToString() => Validation ?? Exception!.ToString();
}


