using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Validation;

/// <summary>
/// POCO属性主導型KafkaContextのバリデーション結果
/// </summary>

/// <summary>
/// バリデーションモード
/// </summary>
public enum ValidationMode
{
    /// <summary>
    /// 厳格モード（デフォルト）
    /// [Topic]や[Key]などの必須属性未定義時は例外で停止
    /// </summary>
    Strict,

    /// <summary>
    /// ゆるめ運用モード
    /// 属性未定義でも自動補完で動作させる（警告表示）
    /// 学習・PoC用途限定
    /// </summary>
    Relaxed
}
