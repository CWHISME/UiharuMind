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

using UiharuMind.Core.AI.Runtime.Backends;

namespace UiharuMind.Core.AI.Embedding;

public static class EmbeddingModelResolver
{
    public static IReadOnlyList<EmbeddingModelCandidate> GetManagedCandidates(EmbeddingModelSettingConfig config)
    {
        List<EmbeddingModelCandidate> candidates = new();
        AddCandidates(candidates, config.ExternalEmbeddedModelPath, EmbeddingModelCandidateSource.Application);
        AddCandidates(candidates, config.DefaultEmbeddedModelPath, EmbeddingModelCandidateSource.BuiltIn);
        return candidates
            .GroupBy(x => System.IO.Path.GetFullPath(x.Path), StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .OrderBy(x => x.Source)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static string ResolveModelPath(EmbeddingModelSettingConfig config, EmbeddingModelSettingConfig llamaConfig)
    {
        if (IsRemote(config)) return "";

        string managedPath = config.ModelPath;
        if (!string.IsNullOrWhiteSpace(managedPath))
        {
            if (File.Exists(managedPath)) return managedPath;
            throw new FileNotFoundException("Selected embedding model file not found.", managedPath);
        }

        EmbeddingModelCandidate? candidate = GetManagedCandidates(llamaConfig).FirstOrDefault();
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

    private static void AddCandidates(
        ICollection<EmbeddingModelCandidate> candidates,
        string directory,
        EmbeddingModelCandidateSource source)
    {
        if (!Directory.Exists(directory)) return;

        foreach (string path in Directory.GetFiles(directory, "*.gguf", SearchOption.TopDirectoryOnly))
        {
            FileInfo fileInfo = new(path);
            candidates.Add(new EmbeddingModelCandidate
            {
                Name = fileInfo.Name,
                Path = fileInfo.FullName,
                Source = source,
                SizeBytes = fileInfo.Length
            });
        }
    }
}
