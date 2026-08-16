using System;
using UiharuMind.Resources.Lang;
using UiharuMind.Shared.Services;

namespace UiharuMind.Features.Memory;

internal static class MemoryIndexUiText
{
    public static string GetSourceErrorText(string errorCode, string detail)
    {
        string text = Loc.Text(errorCode);
        return string.IsNullOrWhiteSpace(detail) ? text : $"{text} ({detail})";
    }

    public static string GetIndexErrorText(string error)
    {
        if (error.StartsWith("Embedding request failed", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("LLamaSharp embedding request failed", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("Response status code does not indicate success",
                StringComparison.OrdinalIgnoreCase))
        {
            return Loc.Text("MemoryIndexEmbeddingRequestFailed");
        }

        if (error.StartsWith("Embedding model startup failed", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("Failed to load LLamaSharp embedding model", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("Remote embedding backend is not implemented", StringComparison.OrdinalIgnoreCase))
        {
            return Loc.Text("MemoryIndexEmbeddingServerUnavailable");
        }

        if (error.Contains("vector store failed", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("readonly database", StringComparison.OrdinalIgnoreCase))
        {
            return Loc.Text("MemoryIndexStorageFailed");
        }

        return error switch
        {
            "Embedding server is unavailable." => Loc.Text("MemoryIndexEmbeddingServerUnavailable"),
            "Embedding model is unavailable." => Loc.Text("MemoryIndexEmbeddingServerUnavailable"),
            "Embedding server startup timed out." => Loc.Text("MemoryIndexEmbeddingServerTimeout"),
            "Memory name not set" => Loc.Text("MemoryIndexMemoryNameMissing"),
            "Memory source validation failed" => Loc.Text("MemorySourceValidationFailed"),
            "Memory vector dimension mismatch" => Loc.Text("MemoryIndexDimensionMismatch"),
            "Embedding input is too large" => Loc.Text("MemoryIndexEmbeddingInputTooLarge"),
            _ => error
        };
    }
}
