; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
VARVE0001 | Varve.Layering | Error | Reference is not to a strictly lower layer. [Documentation](https://github.com/Hafeok/Varve/blob/main/docs/rules/VARVE0001.md)
VARVE0002 | Varve.Layering | Error | Layer is not declared, or a referenced Varve assembly carries no layer metadata. [Documentation](https://github.com/Hafeok/Varve/blob/main/docs/rules/VARVE0002.md)
