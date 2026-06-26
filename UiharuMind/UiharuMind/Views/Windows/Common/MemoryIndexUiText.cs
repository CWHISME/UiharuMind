using System;
using UiharuMind.Resources.Lang;
using UiharuMind.Services;

namespace UiharuMind.Views.Windows.Common;

internal static class MemoryIndexUiText
{
    public static string GetSourceErrorText(string errorCode, string detail)
    {
        string text = L(errorCode);
        return string.IsNullOrWhiteSpace(detail) ? text : $"{text} ({detail})";
    }

    public static string GetIndexErrorText(string error)
    {
        if (error.StartsWith("Embedding request failed", StringComparison.OrdinalIgnoreCase) ||
            error.StartsWith("Response status code does not indicate success",
                StringComparison.OrdinalIgnoreCase))
        {
            return L("MemoryIndexEmbeddingRequestFailed");
        }

        if (error.Contains("vector store failed", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("readonly database", StringComparison.OrdinalIgnoreCase))
        {
            return L("MemoryIndexStorageFailed");
        }

        return error switch
        {
            "Embedding server is unavailable." => L("MemoryIndexEmbeddingServerUnavailable"),
            "Embedding server startup timed out." => L("MemoryIndexEmbeddingServerTimeout"),
            "Memory name not set" => L("MemoryIndexMemoryNameMissing"),
            "Memory source validation failed" => L("MemorySourceValidationFailed"),
            "Memory vector dimension mismatch" => L("MemoryIndexDimensionMismatch"),
            "Embedding input is too large" => L("MemoryIndexEmbeddingInputTooLarge"),
            _ => error
        };
    }

    private static string L(string key) =>
        Lang.ResourceManager.GetString(key, LocalizationManager.Instance.CurrentCulture) ?? key;
}
