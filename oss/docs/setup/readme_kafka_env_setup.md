# Kafka 開発環境構築ガイド（テックリード向け）

このガイドは、Kafka/ksqlDB/Schema Registry を含む開発用サンプル環境を構築し、Visual Studio やローカル開発ツールと連携して動作確認を行うための手順を示します。

---

## ✅ 前提環境（Windows）

| 項目             | 内容                                         |
| -------------- | ------------------------------------------ |
| OS             | Windows 10/11 Pro 推奨                       |
| WSL2           | 有効化済みであること                                 |
| Ubuntu         | WSL2上にインストール（20.04 or 22.04）               |
| Docker Desktop | 最新版インストール済、WSL2統合有効化                       |
| Java           | Ubuntu内に default-jdk（またはOpenJDK 17）をインストール |
| Confluent CLI  | confluent-7.9.1.tar を公式サイトから取得             |

## 🔧 WSL2 と Ubuntu のセットアップ手順（初回のみ）

### 1. WSL2 の有効化（PowerShell）

```powershell
wsl --install
```

### 2. Ubuntu の起動とセットアップ

```bash
sudo apt update && sudo apt upgrade -y
sudo apt install curl unzip default-jdk -y
```

---

## 🐳 Docker 環境構築

### 1. Docker Desktop の設定

- Docker Desktop をインストール：[https://www.docker.com/products/docker-desktop/](https://www.docker.com/products/docker-desktop/)
- 起動後、`Settings > Resources > WSL Integration` で Ubuntu にチェック

### 2. このリポジトリの `docker-compose.yml` を使って起動

```bash
docker-compose up -d
```

起動後、以下のポートでサービスが立ち上がります：

- Kafka: `localhost:9093`
- Schema Registry: `http://localhost:8081`
- ksqlDB Server: `http://localhost:8088`

## 🚀 ksqlDB CLI の導入と起動

### 1. Confluent CLI の取得（GitHubではなく公式サイト経由）

- [https://www.confluent.io](https://www.confluent.io) にアクセスし、`confluent-7.9.1.tar` をダウンロード

### 2. 展開と起動

```bash
tar -xvf confluent-7.9.1.tar
cd confluent-7.9.1
./bin/ksql http://localhost:8088
```

ksql> プロンプトが表示されれば成功です。

---

## 🧪 VSでの確認ポイント

- `KafkaDbContext` を構成し、`.AddAsync()` `.ForEachAsync()` にブレークポイント
- Kafka/ksqlDB が動作していれば、ステップ実行で挙動を確認可能

---

この構成は、ライブラリ導入前の検証や業務コード統合試験にも活用可能です。 テックリードは自身のローカル環境で十分な確認が行えるよう、この環境を出発点としてください。

> サンプルコードや利用例については `README_sample.md` を別途参照

