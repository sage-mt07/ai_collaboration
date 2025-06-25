|タスクID	|概要	|優先度	|対応AI|	備考|
|---|---|---|---|---|
T001|	KafkaDbContext → KafkaContext のドキュメント修正|	中|	天城	|クラス名統一（抽象化の説明追加）
T002|	EnsureKafkaReadyAsync() の定義検討・実装|	高|	鳴瀬|	起動時整合チェックの外部公開
T003|	EnsureTableCreatedAsync<T>() / DropTableAsync() の実装	|中|	鳴瀬|	schema定義とStateStore連携
T004|	.WithDeadLetterQueue() Fluent APIの追加	|低|	鳴瀬|	appsettings.json設定との整合性
T005|	.OnError() / .WithRetry() DSLの定義	|中	|鳴瀬|	初期バージョンではスキップでも可
T006|	.Window(TumblingWindow.Of(...)) 拡張DSLの検討|	中	|鳴瀬	|Window種別ごとの構文追加
T007|	StateStoreの自動接続ロジック明示化	|高	|鳴瀬	|Entity属性との紐付け強化
T008|	.WithManualCommit() DSLの実装	|中	|鳴瀬|	コンシューマ側の挙動制御
T009|	スキーマレジストリ設定との統合度強化	|中||	鳴瀬|	設定読み込みとAvro登録制御の統合
T010|	LINQによる3テーブルJOINの完全対応	|高	|鳴瀬＋じんと|	Expression解析パターン拡張