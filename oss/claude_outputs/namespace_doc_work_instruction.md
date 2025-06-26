# Namespace別詳細ドキュメント作成 - 作業指示書

## 🎯 作業概要

Kafka.Ksql.Linq OSSライブラリの**効率的な機能追加・変更作業**を実現するため、namespace別の詳細設計ドキュメントを**段階的に作成**する。

## 📋 作業目的

- **変更対象namespace**を素早く特定し、**該当部分のソースのみ**で効率的な変更作業を実現
- 全体概要資料（`docs/architecture/overview.md`）と連携し、**2段階の情報提供体制**を構築
- namespace単位での**局所的な設計詳細**を文書化

---

## 🏗️ 成果物構成

### 📂 ディレクトリ構造
```
docs/architecture/namespaces/
├── [Namespace名].md     ← 各namespace別詳細ドキュメント
├── [Namespace名].md
└── ...
```

### 📝 対象Namespace一覧
1. **Application** (`src/Application/*`)
2. **Core** (`src/Core/*`)
3. **Messaging** (`src/Messaging/*`)
4. **Serialization** (`src/Serialization/*`)
5. **Query** (`src/Query/*`)
6. **StateStore** (`src/StateStore/*`)
7. **Window** (`src/Window/*`)

---

## 📋 各ドキュメント構成テンプレート

### 必須セクション
```markdown
# [Namespace名] 詳細設計

## 🎯 責務・設計方針
- 主要責務
- 設計原則・制約
- 他namespaceとの境界

## 🏗️ 主要クラス構成
- ファイル別のクラス一覧
- 責務・役割
- 変更頻度（🔴高・🟡中・🟢低）

## 🔄 データフロー・依存関係
- namespace内の処理フロー
- 外部namespaceとの連携
- インターフェース・抽象化ポイント

## 🚀 変更頻度・作業パターン
- 典型的な変更パターン
- 影響範囲・連鎖変更
- 変更時の注意点

## 📝 設計制約・注意事項
- アーキテクチャ制約
- パフォーマンス考慮事項
- セキュリティ・品質制約

## 🔗 他Namespaceとの連携
- 依存関係
- インターフェース定義
- 協調動作パターン
```

---

## 🚀 作業手順

### Phase 1: 高変更頻度namespace（優先度：高）
1. **Query** - LINQ→KSQL変換、最も変更頻度が高い
2. **Messaging** - Producer/Consumer拡張が頻繁
3. **StateStore** - Window処理・RocksDB関連の変更

### Phase 2: 中変更頻度namespace（優先度：中）
4. **Serialization** - 新形式対応時の変更
5. **Application** - 設定・Builder拡張

### Phase 3: 安定namespace（優先度：低）
6. **Core** - 基盤部分、変更頻度は低い
7. **Window** - 特殊機能、変更頻度は低い

### 各Phase内での作業単位
1. **該当namespaceのソース提示**（全ソース不要、対象namespaceのみ）
2. **詳細ドキュメント作成**
3. **次namespace移行**

---

## 📏 品質基準

### ✅ 完成基準
- [ ] **変更作業時に必要な情報**が網羅されている
- [ ] **クラス単位の責務・変更頻度**が明記されている
- [ ] **典型的な変更パターン**が具体例付きで記載されている
- [ ] **他namespaceとの依存関係**が明確化されている
- [ ] **設計制約・注意事項**が実用的なレベルで記載されている

### 📊 情報精度
- **ファイルレベル**での責務整理（クラス個別は過度に詳細化しない）
- **変更パターン**は具体例を含む
- **依存関係**は図解または明確な記述

### 🎯 読者対象
- **機能追加・変更作業を行う開発者**
- **該当namespaceの設計意図を理解したい開発者**
- **AI協働開発における文脈共有**

---

## 🔧 作業方式

### 📥 ソース提示方式
```
【作業開始時】
"[Namespace名]のドキュメント作成を開始します。
src/[Namespace名]/ 配下のソースを提示してください。"

【提示内容】
- 対象namespace配下の .cs ファイルのみ
- 全体概要は docs/architecture/overview.md で既に共有済み
- 他namespaceのソースは不要
```

### 📝 成果物作成
```
【ドキュメント作成】
docs/architecture/namespaces/[Namespace名].md

【作業完了確認】
- テンプレート構成に沿った内容
- 変更作業時の実用性確認
- 他namespaceとの整合性確認
```

---

## 📅 推奨スケジュール

### Week 1: Query namespace
- 最も変更頻度が高く、Builder系の詳細設計が重要
- LINQ→KSQL変換ロジックの設計パターン整理

### Week 2: Messaging namespace  
- Producer/Consumer管理の拡張パターン
- エラーハンドリング・リトライロジックの詳細

### Week 3: StateStore namespace
- Window処理・RocksDB連携の複雑な設計
- Ready状態監視・バインディング管理

### Week 4以降: 残りnamespace
- Serialization → Application → Core → Window の順

---

## 🎯 最初の作業開始

**Query namespace** からの開始を推奨します。

### 開始方法
```
【次回作業指示】
"Query namespaceのドキュメント作成を開始します。
src/Query/ 配下のソースファイルを提示してください。
docs/architecture/namespaces/Query.md を作成します。"
```

---

## 📚 参考情報

- **全体概要**: `docs/architecture/overview.md` （既作成）
- **既存ドキュメント**: `docs/dev_guide.md`, `docs/arch_overview.md`
- **設計方針**: POCO属性主導、型安全性優先、Fail-Fast原則

---

*この作業指示に従い、**段階的・効率的**なnamespace別ドキュメント作成を実施してください。*