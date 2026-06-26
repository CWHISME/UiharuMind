using System.Text;
using UtfUnknown;

namespace UiharuMind.Core.AI.Memery;

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
    private const float MinimumConfidence = 0.50f;
    private const double MaximumControlCharacterRatio = 0.01d;

    private const string CommonChineseCharacters =
        "的一是不了在人有我他这中大来上个国到说们为子和你地出道也时年得就那要下以生会自着去之过家学对可里后小么心多天而能好都然没日于起还发成事只作当想看文无开手十用主行方又如前所本见经头面公同三已老从动两长知民样现分将外但身些与高意进把法此实回二理美点月明其种声全工己话儿者向情部正名定女问力机给等几很业最间新什打便位因重被走电四第门相次东政海口使教西再平真听世气信少关并内加化由却代军产入先山五太水万市眼体别处总才场师书比住员九笑性通目华报立马命张活难神数件安表原车白应路期叫死常提感金何更反合放做系计或司利受光王果亲界及今京务强六像完德队据论则任形确吃场常";

    static PlainTextFileSourceReader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public bool CanRead(MemorySourceReference source) => source.Kind == MemorySourceKind.PlainTextFile;

    public async Task<MemorySourceReadResult> ReadAsync(
        MemorySourceReference source, CancellationToken cancellationToken)
    {
        string? filePath = source.FilePath;
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new MemorySourceReadResult(false, ErrorCode: "MemorySourceFileMissing", ErrorDetail: filePath ?? "");

        try
        {
            // 先让 UTF.Unknown 基于完整文件判断编码，再读取字节做 BOM 与二进制内容校验。
            DetectionResult detection =
                await CharsetDetector.DetectFromFileAsync(filePath, cancellationToken).ConfigureAwait(false);
            byte[] bytes = await File.ReadAllBytesAsync(filePath, cancellationToken).ConfigureAwait(false);
            Encoding? encoding = DetectBomEncoding(bytes);
            if (encoding == null)
            {
                DetectionDetail? detected = detection.Detected;
                if (detected?.Encoding != null && detected.Confidence >= MinimumConfidence)
                    encoding = detected.Encoding;
            }

            encoding ??= DetectEastAsianFallback(bytes);
            if (encoding == null)
                return new MemorySourceReadResult(false, ErrorCode: "MemorySourceEncodingUnknown");

            string text = Decode(bytes, encoding);

            if (!LooksLikePlainText(text))
                return new MemorySourceReadResult(false, ErrorCode: "MemorySourceNotPlainText");

            return new MemorySourceReadResult(true,
                new MemorySourceDocument(source.Id, source.DisplayName, nameof(MemorySourceKind.PlainTextFile), text,
                    encoding.WebName));
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

    private static Encoding? DetectBomEncoding(ReadOnlySpan<byte> bytes)
    {
        if (HasPrefix(bytes, 0xEF, 0xBB, 0xBF)) return new UTF8Encoding(true);
        if (HasPrefix(bytes, 0xFF, 0xFE, 0x00, 0x00)) return new UTF32Encoding(false, true);
        if (HasPrefix(bytes, 0x00, 0x00, 0xFE, 0xFF)) return new UTF32Encoding(true, true);
        if (HasPrefix(bytes, 0xFF, 0xFE)) return new UnicodeEncoding(false, true);
        if (HasPrefix(bytes, 0xFE, 0xFF)) return new UnicodeEncoding(true, true);
        return null;
    }

    private static bool HasPrefix(ReadOnlySpan<byte> bytes, params byte[] prefix)
    {
        return bytes.Length >= prefix.Length && bytes[..prefix.Length].SequenceEqual(prefix);
    }

    private static Encoding? DetectEastAsianFallback(byte[] bytes)
    {
        (Encoding Encoding, double Score)? best = null;
        foreach (int codePage in new[] { 54936, 950, 932 })
        {
            try
            {
                Encoding encoding = Encoding.GetEncoding(codePage,
                    EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                string text = Decode(bytes, encoding);
                double score = ScoreDecodedText(text, codePage);
                if (best == null || score > best.Value.Score) best = (encoding, score);
            }
            catch (DecoderFallbackException)
            {
            }
        }

        return best is { Score: > 0.5 } ? best.Value.Encoding : null;
    }

    private static string Decode(byte[] bytes, Encoding encoding)
    {
        int preambleLength = bytes.AsSpan().StartsWith(encoding.Preamble)
            ? encoding.Preamble.Length
            : 0;
        return encoding.GetString(bytes, preambleLength, bytes.Length - preambleLength);
    }

    private static double ScoreDecodedText(string text, int codePage)
    {
        if (!LooksLikePlainText(text) || text.Length == 0) return double.MinValue;

        double score = 0;
        foreach (char character in text)
        {
            if (character <= 0x7F)
            {
                score += char.IsControl(character) ? -2 : 0.1;
                continue;
            }

            if (character is >= '\u4E00' and <= '\u9FFF')
            {
                score += CommonChineseCharacters.Contains(character) ? 3 : 0.25;
                continue;
            }

            bool kana = character is >= '\u3040' and <= '\u30FF';
            if (kana)
            {
                score += codePage == 932 ? 2.5 : -3;
                continue;
            }

            if (character is >= '\uFF61' and <= '\uFF9F')
            {
                score += codePage == 932 ? 0.5 : -2;
                continue;
            }

            score -= char.IsLetterOrDigit(character) ? 0 : 0.5;
        }

        return score / text.Length;
    }

    private static bool LooksLikePlainText(string text)
    {
        if (text.IndexOf('\0') >= 0) return false;
        if (text.Length == 0) return true;

        int controlCharacters = 0;
        foreach (char character in text)
        {
            if (char.IsControl(character) && character is not '\r' and not '\n' and not '\t' and not '\f')
                controlCharacters++;
        }

        return (double)controlCharacters / text.Length <= MaximumControlCharacterRatio;
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