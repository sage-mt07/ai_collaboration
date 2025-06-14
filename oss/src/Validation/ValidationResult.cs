using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KsqlDsl.Validation;

public class ValidationResult
{
    /// <summary>
    /// バリデーション成功フラグ
    /// </summary>
    public bool IsValid { get; set; }

    /// <summary>
    /// エラーメッセージ一覧
    /// </summary>
    public List<string> Errors { get; set; } = new();

    /// <summary>
    /// 警告メッセージ一覧
    /// </summary>
    public List<string> Warnings { get; set; } = new();

    /// <summary>
    /// 自動補完された設定一覧
    /// </summary>
    public List<string> AutoCompletedSettings { get; set; } = new();

    /// <summary>
    /// エラーまたは警告が存在するかどうか
    /// </summary>
    public bool HasIssues => Errors.Any() || Warnings.Any();
}
