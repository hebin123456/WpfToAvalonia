namespace WpfToAvalonia.Core.Model;

public enum NoteSeverity
{
    /// <summary>信息性：已自动处理，说明做了什么。</summary>
    Info,

    /// <summary>警告：已自动处理但语义可能有差异，建议人工确认。</summary>
    Warning,

    /// <summary>需人工处理：工具无法安全自动转换。</summary>
    Manual,
}

/// <summary>一条转换备注（自动改写说明或人工待办）。</summary>
public sealed record ConversionNote(
    string File,
    int Line,
    NoteSeverity Severity,
    string Rule,
    string Message)
{
    public string Location => Line > 0 ? $"{File}:{Line}" : File;

    public string SeverityMark => Severity switch
    {
        NoteSeverity.Info => "INFO",
        NoteSeverity.Warning => "WARN",
        NoteSeverity.Manual => "TODO",
        _ => "????",
    };
}
