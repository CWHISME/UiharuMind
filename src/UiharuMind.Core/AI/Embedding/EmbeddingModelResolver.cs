/****************************************************************************
 * Copyright (c) 2024 CWHISME
 *
 * UiharuMind v0.0.1
 *
 * https://wangjiaying.top
 * https://github.com/CWHISME/UiharuMind
 *
 * Latest Update: 2024.10.07
 ****************************************************************************/

using UiharuMind.Core.AI.Models;

namespace UiharuMind.Core.AI.Embedding;

public static class EmbeddingModelResolver
{
    public static IReadOnlyList<EmbeddingModelCandidate> GetManagedCandidates()
    {
        return LocalModelScanner.Scan(ELocalModelKind.Embedding)
            .Select(x => new EmbeddingModelCandidate
            {
                Name = Path.GetFileName(x.Info.ModelPath),
                Path = x.Info.ModelPath,
                Source = x.IsBuiltIn ? EmbeddingModelCandidateSource.BuiltIn : EmbeddingModelCandidateSource.Application,
                SizeBytes = (long)x.Info.FileSizeBytes
            })
            .OrderBy(x => x.Source)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ResolveModelPath(EmbeddingModelSettingConfig config)
    {
        if (IsRemote(config)) return "";

        string managedPath = config.ModelPath;
        if (!string.IsNullOrWhiteSpace(managedPath))
        {
            if (File.Exists(managedPath)) return managedPath;
            throw new FileNotFoundException("Selected embedding model file not found.", managedPath);
        }

        EmbeddingModelCandidate? candidate = GetManagedCandidates().FirstOrDefault();
        if (candidate == null) throw new FileNotFoundException("No managed embedding model was found.");
        return candidate.Path;
    }

    public static bool IsRemote(EmbeddingModelSettingConfig config)
    {
        return string.Equals(config.SourceMode, EmbeddingModelSettingConfig.SourceModeRemoteApi,
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(config.Backend, EmbeddingModelSettingConfig.BackendOpenAICompatible,
                   StringComparison.OrdinalIgnoreCase);
    }
}
