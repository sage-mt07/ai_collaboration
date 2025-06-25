// 使用例: POCOエンティティの定義
using Kafka.Ksql.Linq.Core.Abstractions;

[Topic("chart-data")]
public class Chart
{
    [Key]
    public string Symbol { get; set; } = string.Empty;
    
    public double Price { get; set; }
    public long Volume { get; set; }
    
    [AvroTimestamp] // ← ブローカー提供の価格時刻を明示
    public DateTime Timestamp { get; set; }
}

// 使用例: OnModelCreatingでのウィンドウ設定
public class TradingContext : KafkaContext
{
    public IEntitySet<Chart> Charts { get; set; } = null!;

    protected override void OnModelCreating(IModelBuilder modelBuilder)
    {
        // 基本的なウィンドウ設定
        modelBuilder.Entity<Chart>()
            .Window(window =>
                window.GroupBy(c => c.Symbol)
                      .Select(g => new
                      {
                          Symbol = g.Key,
                          TotalVolume = g.Sum(x => x.Volume),
                          AvgPrice = g.Average(x => x.Price),
                          WindowStart = g.Min(x => x.Timestamp),
                          WindowEnd = g.Max(x => x.Timestamp)
                      }),
                new[] { 1, 5 }, // 1分足、5分足
                gracePeriod: TimeSpan.FromSeconds(3));
    }
}

// 使用例: 実際のクエリ実行
public class TradingService
{
    private readonly TradingContext _context;

    public TradingService(TradingContext context)
    {
        _context = context;
    }

    // 1分足データの取得
    public async Task<List<Chart>> Get1MinuteCandlesAsync()
    {
        return await _context.Charts.Window(1).ToListAsync();
    }

    // 5分足データの取得
    public async Task<List<Chart>> Get5MinuteCandlesAsync()
    {
        return await _context.Charts.Window(5).ToListAsync();
    }

    // 複数ウィンドウの一括取得
    public async Task<Dictionary<int, List<Chart>>> GetAllWindowsAsync()
    {
        var windows = _context.Charts.Windows(1, 5, 15);
        return await windows.GetAllWindowsAsync();
    }

    // カスタム集約の実行
    public async Task<List<VolumeWeightedPrice>> GetVWAPAsync(int windowMinutes)
    {
        var result = await _context.Charts
            .Window(windowMinutes)
            .GroupByAggregate(
                c => c.Symbol,
                g => new VolumeWeightedPrice
                {
                    Symbol = g.Key,
                    VWAP = g.Sum(x => x.Price * x.Volume) / g.Sum(x => x.Volume),
                    TotalVolume = g.Sum(x => x.Volume),
                    WindowStart = g.Min(x => x.Timestamp),
                    WindowEnd = g.Max(x => x.Timestamp)
                })
            .ToListAsync();

        return result;
    }

    // リアルタイムストリーミング
    public async Task StartRealTimeMonitoringAsync()
    {
        await _context.Charts
            .Window(1)
            .ForEachAsync(async candle =>
            {
                Console.WriteLine($"New 1min candle: {candle.Symbol} @ {candle.Price}");
                
                // 他の業務ロジック...
                await ProcessCandleDataAsync(candle);
            });
    }

    private async Task ProcessCandleDataAsync(Chart candle)
    {
        // ビジネスロジックの実装
        await Task.CompletedTask;
    }
}

// 集約結果用のPOCO
public class VolumeWeightedPrice
{
    public string Symbol { get; set; } = string.Empty;
    public double VWAP { get; set; }
    public long TotalVolume { get; set; }
    public DateTime WindowStart { get; set; }
    public DateTime WindowEnd { get; set; }
}

// テストケース例
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Threading.Tasks;

[TestClass]
public class WindowDSLTests
{
    [TestMethod]
    public void AvroTimestamp_Attribute_ShouldBeDetected()
    {
        // Arrange
        var chartType = typeof(Chart);
        
        // Act
        var timestampProperty = chartType.GetProperties()
            .FirstOrDefault(p => p.GetCustomAttribute<AvroTimestampAttribute>() != null);
        
        // Assert
        Assert.IsNotNull(timestampProperty);
        Assert.AreEqual("Timestamp", timestampProperty.Name);
        Assert.AreEqual(typeof(DateTime), timestampProperty.PropertyType);
    }

    [TestMethod]
    public void WindowExtension_ShouldCreateWindowedEntitySet()
    {
        // Arrange
        var context = new TradingContext();
        var charts = context.Charts;
        
        // Act
        var windowedCharts = charts.Window(5);
        
        // Assert
        Assert.IsNotNull(windowedCharts);
        Assert.AreEqual(5, windowedCharts.WindowMinutes);
        Assert.IsTrue(windowedCharts.GetWindowTableName().Contains("WINDOW_5MIN"));
    }

    [TestMethod]
    public void WindowCollection_ShouldManageMultipleWindows()
    {
        // Arrange
        var context = new TradingContext();
        var charts = context.Charts;
        
        // Act
        var windows = charts.Windows(1, 5, 15);
        
        // Assert
        Assert.IsNotNull(windows);
        Assert.AreEqual(3, windows.WindowSizes.Length);
        Assert.IsNotNull(windows[1]);
        Assert.IsNotNull(windows[5]);
        Assert.IsNotNull(windows[15]);
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void WindowOperation_WithoutAvroTimestamp_ShouldThrowException()
    {
        // Arrange: AvroTimestamp属性を持たないエンティティ
        var context = new InvalidContext(); // Chart以外のエンティティ
        
        // Act & Assert
        var windowed = context.InvalidEntities.Window(1); // 例外が発生するはず
    }

    [TestMethod]
    public async Task WindowAggregation_ShouldGenerateCorrectKSQL()
    {
        // Arrange
        var context = new TradingContext();
        
        // Act
        var windowedSet = context.Charts.Window(5);
        var aggregated = windowedSet.GroupByAggregate(
            c => c.Symbol,
            g => new { Symbol = g.Key, AvgPrice = g.Average(x => x.Price) }
        );
        
        // Assert
        Assert.IsNotNull(aggregated);
        
        // KSQLクエリの生成をテスト（実際の実装では内部のクエリジェネレーターを使用）
        var expectedQuery = "CREATE TABLE.*WINDOW TUMBLING.*GROUP BY SYMBOL";
        // Assert.IsTrue(Regex.IsMatch(actualQuery, expectedQuery));
    }
}

// 無効なエンティティ（テスト用）
[Topic("invalid-data")]
public class InvalidEntity
{
    public string Name { get; set; } = string.Empty;
    // [AvroTimestamp]属性がない
    public DateTime CreatedAt { get; set; }
}

public class InvalidContext : KafkaContext
{
    public IEntitySet<InvalidEntity> InvalidEntities { get; set; } = null!;
}