﻿using System.Collections.Generic;
using System.Linq;

namespace KsqlDsl.Validation;

public class ValidationResult
{
    public bool IsValid { get; set; }

    public List<string> Errors { get; set; } = new();

    public List<string> Warnings { get; set; } = new();

    public List<string> AutoCompletedSettings { get; set; } = new();

    public bool HasIssues => Errors.Any() || Warnings.Any();
}