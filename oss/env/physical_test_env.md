# 詩音：物理環境テスト実行環境ドキュメント

## 概要
このドキュメントは、詩音（テストエンジニア）がローカル物理環境でKafka/ksqlDB等の統合テストおよびE2Eテストを実行するための前提構成と実行手順を示す。

## 対象マシン
- **機種名**：MINISFORUM UM790Pro
- **CPU**：AMD Ryzen 9 7940HS（8C16T）
- **メモリ**：64GB DDR5
- **ストレージ**：1TB PCIe4.0 SSD
- **GPU**：Radeon 780M
- **OS**：Windows 11 Pro
- **仮想化基盤**：WSL2 + Docker Desktop

## テストレイヤと環境構成

| フェーズ | 内容 | 実行環境 | 補足 |
|----------|------|----------|------|
| 1. ユニットテスト | LINQ→KSQL DSL変換の検証 | Visual Studio (.NET 8, xUnit) | DSLロジック確認 |
| 2. 疑似統合テスト | Kafka/ksqlDBのDocker環境で生成クエリ実行 | docker-compose.ksqldb.yml | 本番に近い検証可能 |
| 3. DB連携テスト | SQL Server と Kafka Connect連携（任意） | DockerまたはローカルSQL Server | 必要に応じて構成 |
| 4. 物理結合テスト | DSL→Kafka→ksqlDB→出力確認 | Kafka + ksqlDB on Docker | データ検証スクリプト付属予定 |

## 必要ソフトウェア
- Visual Studio 2022 以降（.NET 8 SDK）
- Docker Desktop（WSL2バックエンド）
- Git（リポジトリ同期）
- Kafka CLIツール（`kcat`, `ksql` CLI）
- SQL Server Management Studio（任意）

## 今後の整備予定
- `docker-compose.ksqldb.yml` の標準構成を `infrastructure/compose/` 配下に配置
- `tests/physical/` に詩音が実行するE2Eテスト群を格納
- `tests/environment/checklist.md` に環境動作チェックリストを記載

## 備考
この構成は、ローカルPCでの再現性・自動化・テスト実行のトライアンドエラーを重視しており、本番接続は考慮されていない。必要に応じてKafkaクラスタを外部環境に移行すること。

---

責任者：司令（環境構築）  
実行担当：詩音（E2E・品質保証）  
設計補佐：天城（生成AI）

