🎯 任務：Query層の構造分割と再配置

🧠 背景：
現状、LINQ→KSQL変換やEventSetクラスに多くの責務が集中しており、
構造的負荷が高く、AIの処理範囲・テスト性・保守性に支障をきたしている。

📦 対象構造：
現在の以下のクラス群を再配置・再構成する：

- LinqToKsqlTranslator.cs
- EventSet.cs
- Ksql関連のビルダークラス（SelectBuilderなど）

---

🧩 作業内容：

✅ 1. フォルダ再構成

src/Query/
├── Abstractions/
│ ├── IQueryTranslator.cs
│ ├── IEventSet<T>.cs
│ └── IKsqlBuilder.cs
├── Translation/
│ ├── LinqExpressionAnalyzer.cs
│ ├── KsqlQueryBuilder.cs
│ └── QueryDiagnostics.cs
├── EventSets/
│ ├── EventSetCore<T>.cs
│ ├── EventSetStreaming<T>.cs
│ └── EventSetValidation<T>.cs
└── Builders/
├── SelectBuilder.cs
├── JoinBuilder.cs
├── WindowBuilder.cs
└── （その他3種）


✅ 2. 分割処理

- `EventSet.cs` を3つに分割
  - CRUD基本：`EventSetCore<T>`
  - Push/PullやEMIT制御：`EventSetStreaming<T>`
  - 型検証／Null確認：`EventSetValidation<T>`

- `LinqToKsqlTranslator` を以下に分離
  - `LinqExpressionAnalyzer`：式木の構文解析
  - `KsqlQueryBuilder`：構文構築ロジック
  - `QueryDiagnostics`：デバッグ出力・生成ログ

- ビルダー類を `Builders/` に統合し、共通IF `IKsqlBuilder` を定義

---

📘 命名・構造ポリシー：

- 名前空間は `KsqlDsl.Query.Xxx` とすること
- ファイルは責務単位で1ファイルにし、過剰な結合を避けること
- 可能な限り `IQueryTranslator.ToKsql()` によるエントリポイントに統一

---

🧪 テストへの影響（詩音と連携）：

- ファイル移動後、既存の `ToKsqlTests` などが正しく動作することを確認
- `EventSetTests` も各役割単位に分割予定であるため、命名・責務に配慮すること

---

📁 出力成果物：

- ソース再配置後のファイル群
- `docs/Query/responsibilities.md` に分割根拠とクラス対応一覧（自動生成可）
