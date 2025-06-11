# topic / stream / table 設計・命名規約／運用議論まとめ
- 日時：2025-06-11
- 参加者：司令、天城

---

## 1. 基本方針

- パッケージ利用者は**C#（LINQ/POCO/DSL）を通してのみKafka/KSQLを操作**  
  → Kafka物理名（topic/stream/table名）は通常意識不要
- **物理名を気にする場面だけ属性で上書き可能**（既存連携等の例外用途）

---

## 2. 命名規約

### ● Kafkaトピック名（topic）

- **基本規則**：`modelサブフォルダ名.クラス名`（すべて小文字・ピリオド区切り）
  - 例：`Model.Billing.InvoiceHeader` → `billing.invoiceheader`
  - namespace深度は**直前1階層まで**が標準
- **属性で個別指定も許可**（例：`[KafkaTopic(Name = "custom.topic")]`）

---

### ● KSQL Stream/Table名

- **トピック名から自動変換**  
  - ピリオドをアンダースコアに変換し、`_stream`または`_table` Suffixを自動付与
  - 例：`billing.invoiceheader` →  
    - Stream名：`billing_invoiceheader_stream`
    - Table名：`billing_invoiceheader_table`
- **KSQL識別子制約対応**  
  - 英数字・アンダースコア以外が含まれる場合は自動でダブルクォート付与
- **属性で明示的な上書きも可**

---

### ● バージョン・用途付与ルール

- バージョン管理は**原則クラス新設で対応**  
  - 例：`OrderDetail` → `OrderDetailV2`
  - トピック名にv1/v2等のSuffixは付けない（属性上書きで例外指定は可）
- 用途分離や拡張時は属性で個別指定

---

## 3. 運用方針・利用者インターフェース

- **stream/table/topic名規約のベースはPOCOクラス名**
- **属性は“例外運用”のみ**（標準運用は自動生成）
- 利用者は「クラス名とModelサブフォルダ（namespace直前1階層）」だけ意識すればよい

---

## 4. 自動生成ロジック例

- クラス名：`InvoiceHeader`
- Namespace：`Model.Billing`
- トピック名：`billing.invoiceheader`
- Stream名：`billing_invoiceheader_stream`
- Table名：`billing_invoiceheader_table`

---

## 5. 例外・拡張運用

- 既存資産・外部連携等の例外時は属性指定
- 命名長が過度に長い、重複リスクがある場合も属性指定で調整可

---

## 6. 設計ガイドへの記載例

- **「C#設計（POCO/クラス名）ベースの命名が原則」**
- **「属性での命名上書きは運用例外のみ」**
- **「stream/tableは自動で物理名変換、バージョン管理は新クラス作成で」**
- **「自動生成規則・属性上書き・命名衝突チェックはフレームワークで担保」**

---

**このまとめはboard_meeting/や利用者ガイド、テンプレ等にそのまま掲載できます。  
追加・修正ご要望があればご指示ください、司令！**
