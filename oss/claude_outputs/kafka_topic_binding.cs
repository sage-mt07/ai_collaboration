// src/StateStore/Integration/TopicStateStoreBinding.cs
using KsqlDsl.Core.Abstractions;
using KsqlDsl.StateStore.Core;
using KsqlDsl.StateStore.Management;
using KsqlDsl.StateStore.Monitoring;
using KsqlDsl.Messaging.Consumers;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl.StateStore.Integration;

/// <summary>
/// KafkaトピックとRocksDBStateStoreの双方向バインディング
/// KTable準拠でトピックの状態変化をStateStoreに反映
/// Ready状態監視機能付き
/// </summary>
internal class TopicStateStoreBinding<T> : IDisposable where T : class
{
    private readonly IStateStore<string, T> _stateStore;
    private readonly KafkaConsumerManager _consumerManager;
    private readonly EntityModel _entityModel;
    private readonly string _topicName;
    private readonly ILogger<TopicStateStoreBinding<T>> _logger;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private volatile Task? _bindingTask;
    private volatile bool _isRunning = false;
    private volatile bool _disposed = false;
    private readonly object _lock = new();
    private readonly SemaphoreSlim _startSemaphore = new(1, 1);
    
    // Ready状態監視
    private ReadyStateMonitor? _readyMonitor;
    private volatile bool _isReady = false;

    // Ready状態イベント
    public event EventHandler<ReadyStateChangedEventArgs>? ReadyStateChanged;

    public bool IsReady => _isReady;
    public long CurrentLag => _readyMonitor?.TotalLag ?? 0;
    public TimeSpan? TimeToReady => _readyMonitor?.TimeToReady;

    internal TopicStateStoreBinding(
        IStateStore<string, T> stateStore,
        KafkaConsumerManager consumerManager,
        EntityModel entityModel,
        ILoggerFactory? loggerFactory = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _consumerManager = consumerManager ?? throw new ArgumentNullException(nameof(consumerManager));
        _entityModel = entityModel ?? throw new ArgumentNullException(nameof(entityModel));
        _topicName = entityModel.TopicAttribute?.TopicName ?? entityModel.EntityType.Name;
        _logger = loggerFactory?.CreateLogger<TopicStateStoreBinding<T>>()
                 ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<TopicStateStoreBinding<T>>.Instance;
    }

    /// <summary>
    /// トピック監視を開始（堅牢なエラーハンドリング付き）
    /// Ready状態監視も同時開始
    /// </summary>
    internal async Task StartBindingAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TopicStateStoreBinding<T>));

        await _startSemaphore.WaitAsync();
        try
        {
            if (_isRunning || _bindingTask != null)
            {
                _logger.LogDebug("Binding already running for topic: {Topic}", _topicName);
                return;
            }

            _logger.LogInformation("Starting Topic-StateStore binding with Ready monitoring: {Topic} -> {Store}", 
                _topicName, _stateStore.StoreName);

            var tcs = new TaskCompletionSource<bool>();
            
            _bindingTask = Task.Run(async () =>
            {
                try
                {
                    _isRunning = true;
                    tcs.SetResult(true); // 開始完了シグナル
                    
                    await ConsumeAndUpdateStateStoreWithRetry(_cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Topic binding cancelled: {Topic}", _topicName);
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Topic binding fatal error: {Topic}", _topicName);
                    tcs.TrySetException(ex);
                }
                finally
                {
                    _isRunning = false;
                }
            });

            // 開始完了を待機（最大5秒）
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await tcs.Task.WaitAsync(timeoutCts.Token);
            
            _logger.LogInformation("Topic binding started successfully: {Topic}", _topicName);
        }
        catch (Exception ex)
        {
            _isRunning = false;
            _logger.LogError(ex, "Failed to start topic binding: {Topic}", _topicName);
            throw;
        }
        finally
        {
            _startSemaphore.Release();
        }
    }

    /// <summary>
    /// Ready状態になるまで待機
    /// </summary>
    public async Task<bool> WaitUntilReadyAsync(TimeSpan? timeout = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(TopicStateStoreBinding<T>));

        if (_readyMonitor == null)
        {
            _logger.LogWarning("Ready monitor not initialized for topic: {Topic}", _topicName);
            return false;
        }

        return await _readyMonitor.WaitUntilReadyAsync(timeout);
    }

    /// <summary>
    /// 詳細なReady状態情報取得
    /// </summary>
    public ReadyStateInfo GetReadyStateInfo()
    {
        return _readyMonitor?.GetDetailedState() ?? new ReadyStateInfo
        {
            TopicName = _topicName,
            IsReady = false,
            TotalLag = -1
        };
    }

    /// <summary>
    /// 自動再試行付きの消費処理
    /// </summary>
    private async Task ConsumeAndUpdateStateStoreWithRetry(CancellationToken cancellationToken)
    {
        var retryCount = 0;
        const int maxRetries = 5;
        var baseDelay = TimeSpan.FromSeconds(1);

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeAndUpdateStateStore(cancellationToken);
                retryCount = 0; // 成功時はリセット
            }
            catch (OperationCanceledException)
            {
                throw; // キャンセレーションは再スローしてループを抜ける
            }
            catch (Exception ex)
            {
                retryCount++;
                
                if (retryCount >= maxRetries)
                {
                    _logger.LogCritical(ex, 
                        "Topic binding failed after {MaxRetries} retries: {Topic}. Giving up.", 
                        maxRetries, _topicName);
                    throw;
                }

                var delay = TimeSpan.FromMilliseconds(baseDelay.TotalMilliseconds * Math.Pow(2, retryCount - 1));
                _logger.LogWarning(ex, 
                    "Topic binding error (attempt {Attempt}/{MaxRetries}): {Topic}. Retrying in {Delay}ms", 
                    retryCount, maxRetries, _topicName, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }
    }

    /// <summary>
    /// トピック監視を停止（Graceful Shutdown）
    /// </summary>
    internal async Task StopBindingAsync(TimeSpan? timeout = null)
    {
        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
        
        if (!_isRunning || _bindingTask == null)
        {
            _logger.LogDebug("Binding not running for topic: {Topic}", _topicName);
            return;
        }

        _logger.LogInformation("Stopping Topic-StateStore binding: {Topic}", _topicName);

        try
        {
            _cancellationTokenSource.Cancel();
            
            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            await _bindingTask.WaitAsync(timeoutCts.Token);
            
            _logger.LogInformation("Topic binding stopped gracefully: {Topic}", _topicName);
        }
        catch (TimeoutException)
        {
            _logger.LogWarning("Topic binding stop timed out: {Topic}. Forcing termination.", _topicName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping topic binding: {Topic}", _topicName);
        }
        finally
        {
            _bindingTask = null;
            _isRunning = false;
        }
    }

    /// <summary>
    /// バインディング状態確認（Ready状態含む）
    /// </summary>
    internal bool IsHealthy()
    {
        return _isRunning && 
               _bindingTask != null && 
               !_bindingTask.IsCompleted && 
               !_disposed;
    }

    /// <summary>
    /// 詳細なヘルスステータス取得（Ready情報含む）
    /// </summary>
    internal BindingHealthStatus GetHealthStatus()
    {
        var readyInfo = GetReadyStateInfo();
        
        return new BindingHealthStatus
        {
            IsRunning = _isRunning,
            HasTask = _bindingTask != null,
            TaskStatus = _bindingTask?.Status.ToString() ?? "None",
            IsDisposed = _disposed,
            TopicName = _topicName,
            StoreName = _stateStore.StoreName,
            StoreSize = _stateStore.EstimatedSize,
            
            // Ready状態情報
            IsReady = _isReady,
            CurrentLag = readyInfo.TotalLag,
            TimeToReady = readyInfo.TimeToReady,
            TimeSinceBinding = readyInfo.TimeSinceBinding
        };
    }

    /// <summary>
    /// Kafkaトピックを消費してStateStoreに反映
    /// KTable準拠：最新状態のみ保持、削除メッセージ対応
    /// Ready状態監視付き
    /// </summary>
    private async Task ConsumeAndUpdateStateStore(CancellationToken cancellationToken)
    {
        var consumer = await _consumerManager.GetConsumerAsync<T>();

        // Ready状態監視開始
        if (_readyMonitor == null)
        {
            var rawConsumer = GetRawConsumerFromManager(consumer);
            if (rawConsumer != null)
            {
                _readyMonitor = new ReadyStateMonitor(rawConsumer, _topicName, 
                    _logger.LoggerFactory);
                
                _readyMonitor.ReadyStateChanged += OnReadyStateChanged;
                
                _logger.LogInformation("Ready state monitoring started for topic: {Topic}", _topicName);
            }
        }

        await foreach (var kafkaMessage in consumer.ConsumeAsync(cancellationToken))
        {
            try
            {
                var key = ExtractKey(kafkaMessage.Value);

                if (kafkaMessage.Value == null)
                {
                    // Tombstone（削除メッセージ）
                    var deleted = _stateStore.Delete(key);
                    _logger.LogTrace("StateStore DELETE: {Key} (Success: {Deleted})", key, deleted);
                }
                else
                {
                    // 通常メッセージ（更新/挿入）
                    _stateStore.Put(key, kafkaMessage.Value);
                    _logger.LogTrace("StateStore PUT: {Key}", key);
                }

                // 定期的にフラッシュ（パフォーマンス考慮）
                if (DateTime.UtcNow.Millisecond % 1000 < 10)
                {
                    _stateStore.Flush();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to update StateStore from topic message: {Topic}", _topicName);
            }
        }
    }

    /// <summary>
    /// Ready状態変更時のハンドラ
    /// </summary>
    private void OnReadyStateChanged(object? sender, ReadyStateChangedEventArgs e)
    {
        _isReady = e.IsReady;
        
        if (e.IsReady)
        {
            _logger.LogInformation(
                "StateStore READY: {Topic} -> {Store}. Time to ready: {TimeToReady}s, Final lag: {Lag}",
                _topicName, _stateStore.StoreName, e.TimeToReady.TotalSeconds, e.CurrentLag);
        }
        
        // 外部イベント転送
        ReadyStateChanged?.Invoke(this, e);
    }

    /// <summary>
    /// ConsumerManagerから生Consumer取得（Ready監視用）
    /// </summary>
    private Confluent.Kafka.IConsumer<object, object>? GetRawConsumerFromManager(
        Messaging.Abstractions.IKafkaConsumer<T, object> managedConsumer)
    {
        // リフレクションまたは内部APIでアクセス
        // 実装依存のため、実際のConsumerManagerの実装に合わせて調整
        try
        {
            var field = managedConsumer.GetType().GetField("_consumer", 
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return field?.GetValue(managedConsumer) as Confluent.Kafka.IConsumer<object, object>;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract raw consumer for ready monitoring: {Topic}", _topicName);
            return null;
        }
    }

    /// <summary>
    /// エンティティからキーを抽出
    /// </summary>
    private string ExtractKey(T entity)
    {
        if (entity == null) return Guid.NewGuid().ToString();

        var keyProperties = _entityModel.KeyProperties;
        if (keyProperties.Length == 0)
        {
            return entity.GetHashCode().ToString();
        }

        if (keyProperties.Length == 1)
        {
            var value = keyProperties[0].GetValue(entity);
            return value?.ToString() ?? Guid.NewGuid().ToString();
        }

        // 複合キー
        var keyParts = new string[keyProperties.Length];
        for (int i = 0; i < keyProperties.Length; i++)
        {
            keyParts[i] = keyProperties[i].GetValue(entity)?.ToString() ?? "null";
        }
        
        return string.Join("|", keyParts);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            
            try
            {
                StopBindingAsync(TimeSpan.FromSeconds(5)).Wait(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error during binding disposal: {Topic}", _topicName);
            }
            
            _cancellationTokenSource?.Dispose();
            _startSemaphore?.Dispose();
        }
    }
}

/// <summary>
/// バインディングヘルス状態
/// </summary>
internal class BindingHealthStatus
{
    public bool IsRunning { get; set; }
    public bool HasTask { get; set; }
    public string TaskStatus { get; set; } = string.Empty;
    public bool IsDisposed { get; set; }
    public string TopicName { get; set; } = string.Empty;
    public string StoreName { get; set; } = string.Empty;
    public long StoreSize { get; set; }
    
    public bool IsHealthy => IsRunning && HasTask && !IsDisposed && TaskStatus != "Faulted";
    
    public override string ToString()
    {
        return $"Topic: {TopicName}, Store: {StoreName}, Running: {IsRunning}, Size: {StoreSize}, Status: {TaskStatus}";
    }
}
}

/// <summary>
/// 複数のTopicStateStoreBindingを管理（ヘルスチェック・再起動機能付き）
/// </summary>
internal class StateStoreBindingManager : IDisposable
{
    private readonly List<IDisposable> _bindings = new();
    private readonly Dictionary<string, BindingHealthStatus> _healthStatuses = new();
    private readonly ILogger<StateStoreBindingManager> _logger;
    private readonly Timer? _healthCheckTimer;
    private readonly object _lock = new();

    internal StateStoreBindingManager(ILoggerFactory? loggerFactory = null)
    {
        _logger = loggerFactory?.CreateLogger<StateStoreBindingManager>()
                 ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<StateStoreBindingManager>.Instance;
        
        // 30秒ごとにヘルスチェック
        _healthCheckTimer = new Timer(PerformHealthCheck, null, 
            TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    internal async Task<TopicStateStoreBinding<T>> CreateBindingAsync<T>(
        IStateStore<string, T> stateStore,
        KafkaConsumerManager consumerManager,
        EntityModel entityModel,
        ILoggerFactory? loggerFactory = null) where T : class
    {
        var binding = new TopicStateStoreBinding<T>(stateStore, consumerManager, entityModel, loggerFactory);
        
        try
        {
            await binding.StartBindingAsync();
            
            lock (_lock)
            {
                _bindings.Add(binding);
                var status = binding.GetHealthStatus();
                _healthStatuses[status.TopicName] = status;
            }
            
            _logger.LogInformation("Created StateStore binding for {EntityType}", typeof(T).Name);
            return binding;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create binding for {EntityType}", typeof(T).Name);
            binding.Dispose();
            throw;
        }
    }

    private void PerformHealthCheck(object? state)
    {
        lock (_lock)
        {
            var unhealthyBindings = new List<string>();
            
            foreach (var binding in _bindings.OfType<TopicStateStoreBinding<object>>())
            {
                var status = binding.GetHealthStatus();
                _healthStatuses[status.TopicName] = status;
                
                if (!status.IsHealthy)
                {
                    unhealthyBindings.Add(status.TopicName);
                    _logger.LogWarning("Unhealthy binding detected: {Status}", status);
                }
            }
            
            if (unhealthyBindings.Count > 0)
            {
                _logger.LogWarning("Found {Count} unhealthy bindings: {Topics}", 
                    unhealthyBindings.Count, string.Join(", ", unhealthyBindings));
            }
        }
    }

    internal Dictionary<string, BindingHealthStatus> GetAllHealthStatuses()
    {
        lock (_lock)
        {
            return new Dictionary<string, BindingHealthStatus>(_healthStatuses);
        }
    }

    public void Dispose()
    {
        _healthCheckTimer?.Dispose();
        
        _logger.LogInformation("Disposing {BindingCount} StateStore bindings", _bindings.Count);
        
        lock (_lock)
        {
            foreach (var binding in _bindings)
            {
                binding?.Dispose();
            }
            _bindings.Clear();
            _healthStatuses.Clear();
        }
    }
}