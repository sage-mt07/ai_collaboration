// src/StateStore/Monitoring/ReadyStateMonitor.cs
using Confluent.Kafka;
using KsqlDsl.Core.Extensions;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace KsqlDsl.StateStore.Monitoring;

/// <summary>
/// StateStore Ready状態監視
/// Kafkaトピックとの同期完了（lag=0）を監視・通知
/// </summary>
internal class ReadyStateMonitor : IDisposable
{
    private readonly IConsumer<object, object> _consumer;
    private readonly string _topicName;
    private readonly ILogger<ReadyStateMonitor> _logger;
    private readonly Timer _lagCheckTimer;
    private readonly object _stateLock = new();
    
    private volatile bool _isReady = false;
    private volatile bool _disposed = false;
    private List<TopicPartitionOffset> _endOffsets = new();
    private List<TopicPartitionOffset> _currentOffsets = new();
    private long _totalLag = 0;
    private DateTime _bindingStartedAt = DateTime.UtcNow;
    private DateTime? _readyAchievedAt;

    public bool IsReady => _isReady;
    public long TotalLag => _totalLag;
    public TimeSpan TimeSinceBinding => DateTime.UtcNow - _bindingStartedAt;
    public TimeSpan? TimeToReady => _readyAchievedAt.HasValue 
        ? _readyAchievedAt.Value - _bindingStartedAt 
        : null;

    public event EventHandler<ReadyStateChangedEventArgs>? ReadyStateChanged;
    public event EventHandler<LagUpdatedEventArgs>? LagUpdated;

    internal ReadyStateMonitor(
        IConsumer<object, object> consumer,
        string topicName,
        ILoggerFactory? loggerFactory = null)
    {
        _consumer = consumer ?? throw new ArgumentNullException(nameof(consumer));
        _topicName = topicName ?? throw new ArgumentNullException(nameof(topicName));
        _logger = loggerFactory.CreateLoggerOrNull<ReadyStateMonitor>();

        // 5秒間隔でlag監視
        _lagCheckTimer = new Timer(CheckLag, null, 
            TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));

        _logger.LogInformation("ReadyStateMonitor initialized for topic: {Topic}", _topicName);
    }

    /// <summary>
    /// Ready状態になるまで待機
    /// </summary>
    internal async Task<bool> WaitUntilReadyAsync(TimeSpan? timeout = null)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ReadyStateMonitor));

        if (_isReady)
        {
            _logger.LogDebug("Already ready for topic: {Topic}", _topicName);
            return true;
        }

        var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(10);
        var tcs = new TaskCompletionSource<bool>();
        
        EventHandler<ReadyStateChangedEventArgs>? handler = null;
        handler = (sender, args) =>
        {
            if (args.IsReady)
            {
                ReadyStateChanged -= handler;
                tcs.TrySetResult(true);
            }
        };

        ReadyStateChanged += handler;

        try
        {
            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            var timeoutTask = Task.Delay(effectiveTimeout, timeoutCts.Token);
            var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

            if (completedTask == timeoutTask)
            {
                _logger.LogWarning("Ready wait timeout after {Timeout} for topic: {Topic}. Current lag: {Lag}", 
                    effectiveTimeout, _topicName, _totalLag);
                return false;
            }

            return await tcs.Task;
        }
        finally
        {
            ReadyStateChanged -= handler;
        }
    }

    /// <summary>
    /// 詳細な同期状態取得
    /// </summary>
    internal ReadyStateInfo GetDetailedState()
    {
        lock (_stateLock)
        {
            return new ReadyStateInfo
            {
                TopicName = _topicName,
                IsReady = _isReady,
                TotalLag = _totalLag,
                EndOffsets = _endOffsets.ToList(),
                CurrentOffsets = _currentOffsets.ToList(),
                TimeSinceBinding = TimeSinceBinding,
                TimeToReady = TimeToReady,
                PartitionCount = _endOffsets.Count
            };
        }
    }

    /// <summary>
    /// lag定期チェック処理
    /// </summary>
    private void CheckLag(object? state)
    {
        if (_disposed || _isReady) return;

        try
        {
            UpdateLagInformation();
            
            var wasReady = _isReady;
            var isCurrentlyReady = _totalLag == 0 && _endOffsets.Count > 0;

            if (!wasReady && isCurrentlyReady)
            {
                lock (_stateLock)
                {
                    _isReady = true;
                    _readyAchievedAt = DateTime.UtcNow;
                }

                _logger.LogInformation(
                    "StateStore READY achieved for topic: {Topic}. Time to ready: {TimeToReady}, Total lag: {Lag}",
                    _topicName, TimeToReady, _totalLag);

                OnReadyStateChanged(new ReadyStateChangedEventArgs
                {
                    TopicName = _topicName,
                    IsReady = true,
                    PreviousLag = _totalLag,
                    CurrentLag = 0,
                    TimeToReady = TimeToReady ?? TimeSpan.Zero
                });
            }

            OnLagUpdated(new LagUpdatedEventArgs
            {
                TopicName = _topicName,
                TotalLag = _totalLag,
                IsReady = _isReady,
                PartitionLags = GetPartitionLags()
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during lag check for topic: {Topic}", _topicName);
        }
    }

    /// <summary>
    /// lag情報更新
    /// </summary>
    private void UpdateLagInformation()
    {
        try
        {
            // 現在のコンシューマー位置を取得
            var assignment = _consumer.Assignment;
            if (assignment == null || assignment.Count == 0)
            {
                _logger.LogDebug("No assignment for consumer on topic: {Topic}", _topicName);
                return;
            }

            // エンドオフセット取得（高水位点）
            var watermarks = _consumer.QueryWatermarkOffsets(assignment.First(), TimeSpan.FromSeconds(10));
            var endOffsets = assignment.Select(tp => new TopicPartitionOffset(tp, watermarks.High)).ToList();

            // 現在のオフセット取得
            var currentPositions = assignment.Select(tp => 
            {
                var position = _consumer.Position(tp);
                return new TopicPartitionOffset(tp, position);
            }).ToList();

            lock (_stateLock)
            {
                _endOffsets = endOffsets;
                _currentOffsets = currentPositions;

                // 総lag計算
                _totalLag = 0;
                for (int i = 0; i < _endOffsets.Count && i < _currentOffsets.Count; i++)
                {
                    var endOffset = _endOffsets[i].Offset.Value;
                    var currentOffset = _currentOffsets[i].Offset.Value;
                    _totalLag += Math.Max(0, endOffset - currentOffset);
                }
            }

            _logger.LogTrace("Lag check completed for topic: {Topic}. Total lag: {Lag}, Partitions: {Partitions}",
                _topicName, _totalLag, assignment.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update lag information for topic: {Topic}", _topicName);
        }
    }

    /// <summary>
    /// パーティション別lag取得
    /// </summary>
    private Dictionary<int, long> GetPartitionLags()
    {
        lock (_stateLock)
        {
            var partitionLags = new Dictionary<int, long>();
            
            for (int i = 0; i < _endOffsets.Count && i < _currentOffsets.Count; i++)
            {
                var partition = _endOffsets[i].Partition.Value;
                var endOffset = _endOffsets[i].Offset.Value;
                var currentOffset = _currentOffsets[i].Offset.Value;
                var lag = Math.Max(0, endOffset - currentOffset);
                
                partitionLags[partition] = lag;
            }
            
            return partitionLags;
        }
    }

    protected virtual void OnReadyStateChanged(ReadyStateChangedEventArgs e)
    {
        ReadyStateChanged?.Invoke(this, e);
    }

    protected virtual void OnLagUpdated(LagUpdatedEventArgs e)
    {
        LagUpdated?.Invoke(this, e);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
            _lagCheckTimer?.Dispose();
            
            _logger.LogDebug("ReadyStateMonitor disposed for topic: {Topic}", _topicName);
        }
    }
}

/// <summary>
/// Ready状態変更イベント引数
/// </summary>
internal class ReadyStateChangedEventArgs : EventArgs
{
    public string TopicName { get; set; } = string.Empty;
    public bool IsReady { get; set; }
    public long PreviousLag { get; set; }
    public long CurrentLag { get; set; }
    public TimeSpan TimeToReady { get; set; }
}

/// <summary>
/// Lag更新イベント引数
/// </summary>
internal class LagUpdatedEventArgs : EventArgs
{
    public string TopicName { get; set; } = string.Empty;
    public long TotalLag { get; set; }
    public bool IsReady { get; set; }
    public Dictionary<int, long> PartitionLags { get; set; } = new();
}

/// <summary>
/// Ready状態詳細情報
/// </summary>
internal class ReadyStateInfo
{
    public string TopicName { get; set; } = string.Empty;
    public bool IsReady { get; set; }
    public long TotalLag { get; set; }
    public List<TopicPartitionOffset> EndOffsets { get; set; } = new();
    public List<TopicPartitionOffset> CurrentOffsets { get; set; } = new();
    public TimeSpan TimeSinceBinding { get; set; }
    public TimeSpan? TimeToReady { get; set; }
    public int PartitionCount { get; set; }

    public override string ToString()
    {
        var readyStatus = IsReady ? "READY" : "NOT_READY";
        var timeInfo = TimeToReady.HasValue 
            ? $"Ready in: {TimeToReady.Value.TotalSeconds:F1}s" 
            : $"Binding for: {TimeSinceBinding.TotalSeconds:F1}s";
        
        return $"Topic: {TopicName}, Status: {readyStatus}, Lag: {TotalLag}, Partitions: {PartitionCount}, {timeInfo}";
    }
}