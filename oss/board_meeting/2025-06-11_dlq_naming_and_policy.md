# DLQ（Dead Letter Queue）設計・命名規約／運用議論まとめ
- 日時：2025-06-11
- 参加者：司令、天城

---

## 1. 基本方針

- KafkaトピックごとにDLQ（デッドレターキュー）は**原則自動生成**する
- DLQの名称は**元トピック名＋「-dlq」**とする  
　例：`billing.invoiceheader` → `billing.invoiceheader-dlq`
- DLQの運用要否は各プロジェクト・システムで決定して良いが、**パッケージとしては標準装備**

---

## 2. DLQに格納するデータの対象

- **業務ロジックで“致命的エラー”となった、構造的に正しい（Avroでパース可能な）データ**
  - 例：金額不足・上限超過・参照先なしなど、業務的にNGだがデータ自体は破損していない
- **パース不能・スキーマ不一致等「Avro非対応データ」はDLQではなく障害ログで管理**
  - 再利用不可な壊れたデータはDLQの対象外

---

## 3. DLQのデータ構造・errorMessage等の拡張

- DLQ用POCOは通常POCO＋**errorMessage, errorCode等の追加プロパティ**で設計  
  - 例：  
    ```csharp
    public class InvoiceHeaderDlq : InvoiceHeader
    {
        public string ErrorCode { get; set; }
        public string ErrorMessage { get; set; }
    }
    ```
- **どの項目を含めるか（例：errorCode, errorMessage, ErrorTimestamp等）は業務要件次第で決定**
  - 初期は最低限のerrorMessageだけでも運用可能、必要に応じて拡張

---

## 4. DLQの運用意義・再利用可能性

- DLQは**再送・人手修正・監査証跡**など、運用現場の設計次第で「活用」「保管のみ」いずれもOK
- 「再利用できない場合」でも、データ損失ゼロ・品質保証・障害解析のための保険的役割

---

## 5. 属性上書き・例外運用

- 属性でDLQ名や項目を**明示的に上書き可能**  
  - 例：`[KafkaDLQ(Name = "custom.invoiceheader.deadletter")]`
- DLQ自動生成ON/OFFや保存先変更もプロジェクト設定で切り替え可

---

## 6. 実際の現場でのDLQ運用

- 金融・IoT等“ロストNG”な領域ではDLQは標準
- 小規模・試行用途ではDLQ未活用も現実的。  
- **DLQは“設計・運用・文化次第”で活かし方が変わる**

---

## 7. 設計ガイドへの記載例

- DLQはトピック単位で「-dlq」命名で標準生成
- Avroで読める業務エラーデータのみDLQに送る。壊れたデータは障害ログ
- errorMessage等の項目設計は難しく考えすぎず、最初は最小限→拡張も段階的に
- DLQ運用方針はプロジェクトごとにドキュメント化・明文化する

---

**このまとめはboard_meeting/や利用者ガイド、テンプレ等にそのまま掲載できます。  
修正・追加ご要望があればご指示ください、司令！**
