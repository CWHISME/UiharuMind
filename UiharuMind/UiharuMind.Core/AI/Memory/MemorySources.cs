using System.Security.Cryptography;
using System.Text;
using UiharuMind.Core.Core.Utils;

namespace UiharuMind.Core.AI.Memory;

/// <summary>
/// 来源标识。文件路径这类会变长变怪的值不能直接当 id(要拼进记录主键、要当文件名兜底),
/// 统一散列成定长十六进制。
/// </summary>
internal static class MemorySourceId
{
    /// <summary>
    /// 把任意字符串散列成定长标识
    /// </summary>
    /// <param name="value">原始值,如文件绝对路径</param>
    /// <returns>大写十六进制串</returns>
    public static string FromValue(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToUpperInvariant();
    }
}

public sealed class MemoryTextSource
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
}

public enum MemorySourceKind
{
    ManualText,
    PlainTextFile
}

public sealed record MemorySourceReference(
    string Id,
    string DisplayName,
    MemorySourceKind Kind,
    string? FilePath = null,
    string? Content = null);

public sealed record MemorySourceDocument(
    string SourceId,
    string SourceName,
    string SourceKind,
    string Text,
    string? EncodingName = null);

public sealed record MemorySourceReadResult(
    bool Success,
    MemorySourceDocument? Document = null,
    string ErrorCode = "",
    string ErrorDetail = "");

public interface IMemorySourceReader
{
    bool CanRead(MemorySourceReference source);
    Task<MemorySourceReadResult> ReadAsync(MemorySourceReference source, CancellationToken cancellationToken);
}

/// <summary>
/// 受理中的来源读取器。索引构建与「加文件时先校验一遍」两条路径必须用同一份名单,
/// 各自持一份迟早会出现「导入时收了、索引时读不了」。
/// </summary>
internal static class MemorySourceReaders
{
    private static readonly IMemorySourceReader[] All =
    [
        new ManualTextSourceReader(),
        new PlainTextFileSourceReader()
    ];

    /// <summary>
    /// 找能读这个来源的读取器
    /// </summary>
    /// <param name="source">来源引用</param>
    /// <returns>读取器;没有能受理的返回 null</returns>
    public static IMemorySourceReader? Find(MemorySourceReference source)
    {
        return Array.Find(All, reader => reader.CanRead(source));
    }

    /// <summary>
    /// 读一个来源
    /// </summary>
    /// <param name="source">来源引用</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>读取结果;无人受理时返回 MemorySourceUnsupported</returns>
    public static Task<MemorySourceReadResult> ReadAsync(
        MemorySourceReference source, CancellationToken cancellationToken)
    {
        IMemorySourceReader? reader = Find(source);
        return reader == null
            ? Task.FromResult(new MemorySourceReadResult(false, ErrorCode: "MemorySourceUnsupported"))
            : reader.ReadAsync(source, cancellationToken);
    }
}

public sealed class ManualTextSourceReader : IMemorySourceReader
{
    public bool CanRead(MemorySourceReference source) => source.Kind == MemorySourceKind.ManualText;

    public Task<MemorySourceReadResult> ReadAsync(
        MemorySourceReference source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(source.Content))
            return Task.FromResult(new MemorySourceReadResult(false, ErrorCode: "MemorySourceEmpty"));

        return Task.FromResult(new MemorySourceReadResult(true,
            new MemorySourceDocument(source.Id, source.DisplayName, nameof(MemorySourceKind.ManualText), source.Content)));
    }
}

public sealed class PlainTextFileSourceReader : IMemorySourceReader
{
    public bool CanRead(MemorySourceReference source) => source.Kind == MemorySourceKind.PlainTextFile;

    public async Task<MemorySourceReadResult> ReadAsync(
        MemorySourceReference source, CancellationToken cancellationToken)
    {
        string? filePath = source.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new MemorySourceReadResult(false, ErrorCode: "MemorySourceFileMissing", ErrorDetail: filePath ?? "");

        try
        {
            // 编码探测链（BOM → charset-detector → 东亚兜底 → 纯文本判定）收在 TextFileCodec,
            // 界面编辑窗与这里共用同一份,不会各自在编码行为上分岔
            TextFileReadResult result = await TextFileCodec.ReadTextAsync(filePath, cancellationToken).ConfigureAwait(false);
            if (!result.Success || result.Text == null || result.Encoding == null)
            {
                return result.ErrorCode switch
                {
                    "FileMissing" => new MemorySourceReadResult(false,
                        ErrorCode: "MemorySourceFileMissing", ErrorDetail: filePath),
                    "EncodingUnknown" => new MemorySourceReadResult(false,
                        ErrorCode: "MemorySourceEncodingUnknown"),
                    "NotPlainText" => new MemorySourceReadResult(false,
                        ErrorCode: "MemorySourceNotPlainText"),
                    _ => new MemorySourceReadResult(false,
                        ErrorCode: "MemorySourceReadFailed", ErrorDetail: result.ErrorDetail)
                };
            }

            return new MemorySourceReadResult(true,
                new MemorySourceDocument(source.Id, source.DisplayName, nameof(MemorySourceKind.PlainTextFile),
                    result.Text, result.Encoding.WebName));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            return new MemorySourceReadResult(false, ErrorCode: "MemorySourceReadFailed", ErrorDetail: e.Message);
        }
    }
}

public enum MemoryIndexStage
{
    Preparing,
    ReadingSources,
    SplittingText,
    GeneratingEmbeddings,
    WritingDatabase,
    Completed
}

public sealed record MemoryIndexProgress(
    MemoryIndexStage Stage,
    double Percentage,
    string CurrentSource,
    int ProcessedSources,
    int TotalSources,
    int CurrentChunk,
    int TotalChunks,
    int FailedSources);

public enum MemoryIndexUpdateStatus
{
    Succeeded,
    Cancelled,
    Failed
}

public sealed record MemoryIndexSourceFailure(string SourceName, string ErrorCode, string ErrorDetail);

public sealed record MemoryIndexUpdateResult(
    MemoryIndexUpdateStatus Status,
    IReadOnlyList<MemoryIndexSourceFailure> Failures,
    string Error = "")
{
    public bool Succeeded => Status == MemoryIndexUpdateStatus.Succeeded;
    public bool Cancelled => Status == MemoryIndexUpdateStatus.Cancelled;
}