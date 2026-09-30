; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md
;
; VARVE0001 and VARVE0002 were listed here and never shipped in a release, so
; they leave this file rather than moving to a "Removed Rules" section. Their
; ids stay retired (ADR 0062) and their pages stay under docs/rules/.

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
VARVE0003 | Varve.HotPath | Error | A hot path does not allocate, and calls only hot-path code. [Documentation](https://github.com/Hafeok/Varve/blob/main/docs/rules/VARVE0003.md)
VARVE0004 | Varve.HotPath | Error | A hot path's signature does not force allocation or dispatch. [Documentation](https://github.com/Hafeok/Varve/blob/main/docs/rules/VARVE0004.md)
VARVE0005 | Varve.Layering | Error | Layer declaration is missing, malformed, or disagrees with the assembly. [Documentation](https://github.com/Hafeok/Varve/blob/main/docs/rules/VARVE0005.md)
