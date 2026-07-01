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

using UiharuMind.Core.AI;
using UiharuMind.Core.Configs;
using UiharuMind.Core.Core.SimpleLog;
using UiharuMind.Core.Core.Singletons;

namespace UiharuMind.Core.AI.Embedding;

public class EmbeddingModelService : Singleton<EmbeddingModelService>
{
    private readonly SemaphoreSlim _sessionLock = new(1, 1);
    private IEmbeddingSession? _session;
    private string _sessionKey = "";
    private DateTime? _lastStartedAt;
    private string _lastError = "";

    public IEmbeddingSession? CurrentSession => _session;
    public event Action? StateChanged;

    public bool IsRunning => _session is { IsRunning: true };
    public string BackendName => _session?.BackendName ?? ConfigManager.Instance.EmbeddingModelSetting.Backend;
    public string ModelPath => _session?.ModelPath ?? GetConfiguredModelPath();
    public int Dimensions => _session?.Dimensions ?? 0;
    public DateTime? LastStartedAt => _lastStartedAt;
    public string LastError => _session?.LastError is { Length: > 0 } sessionError ? sessionError : _lastError;

    public async Task<IEmbeddingSession> GetSessionAsync(CancellationToken cancellationToken = default)
    {
        await _sessionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            EmbeddingModelSettingConfig config = ConfigManager.Instance.EmbeddingModelSetting;
            string key = BuildSessionKey(config);
            if (_session is { IsRunning: true } && _sessionKey == key) return _session;

            StopSessionCore();
            string modelPath = EmbeddingModelResolver.IsRemote(config) ? "" : ResolveModelPath(config);
            _session = await LlmManager.Instance.RuntimeCoordinator
                .CreateEmbeddingSessionAsync(config, modelPath, cancellationToken)
                .ConfigureAwait(false);
            _sessionKey = key;
            _lastStartedAt = DateTime.Now;
            _lastError = "";
            NotifyStateChanged();
            return _session;
        }
        catch (EmbeddingRuntimeException e)
        {
            _lastError = e.Message;
            NotifyStateChanged();
            throw;
        }
        catch (Exception e)
        {
            Log.Error($"Embedding model startup failed: {e.Message}");
            _lastError = e.Message;
            NotifyStateChanged();
            throw new EmbeddingRuntimeException("Embedding model startup failed.", e);
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    public async Task RestartAsync(CancellationToken cancellationToken = default)
    {
        StopSession();
        await GetSessionAsync(cancellationToken).ConfigureAwait(false);
    }

    public void StopSession()
    {
        _sessionLock.Wait();
        try
        {
            StopSessionCore();
        }
        finally
        {
            _sessionLock.Release();
        }
    }

    private void StopSessionCore()
    {
        _session?.Dispose();
        _session = null;
        _sessionKey = "";
        NotifyStateChanged();
    }

    private static string BuildSessionKey(EmbeddingModelSettingConfig embedding)
    {
        return string.Join('|',
            embedding.Backend,
            embedding.SourceMode,
            embedding.ModelPath,
            embedding.ContextSize,
            embedding.BatchSize,
            embedding.UBatchSize,
            embedding.GpuLayers,
            embedding.RemoteEndpoint,
            embedding.RemoteModelId);
    }

    public static IReadOnlyList<EmbeddingModelCandidate> GetManagedCandidates()
    {
        return EmbeddingModelResolver.GetManagedCandidates(EmbeddingModelSettingConfig.Current);
    }

    public static string ResolveModelPath(EmbeddingModelSettingConfig config)
    {
        return EmbeddingModelResolver.ResolveModelPath(config, EmbeddingModelSettingConfig.Current);
    }

    private static string GetConfiguredModelPath()
    {
        EmbeddingModelSettingConfig config = ConfigManager.Instance.EmbeddingModelSetting;
        if (EmbeddingModelResolver.IsRemote(config)) return config.RemoteEndpoint;
        return config.ModelPath;
    }

    private void NotifyStateChanged()
    {
        try
        {
            StateChanged?.Invoke();
        }
        catch (Exception e)
        {
            Log.Error($"Embedding model state notification failed: {e.Message}");
        }
    }
}
