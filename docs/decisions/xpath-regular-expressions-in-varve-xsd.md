---
set: xpath-regular-expressions-in-varve-xsd
namespace: varve
adr: 0153
decisions:
  - key: XPathRegexTranslatedInXsd
    statement: "Varve.Xsd translates XPath and XQuery F&O 3.1 regular expressions and flags to .NET, and refuses what F&O does not define; the evaluator and Varve.Shacl both use it"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-18T09:57:00Z
  - key: RegexCompiledSeparately
    statement: "Translation never touches System.Text.RegularExpressions; only XPathRegex.TryCompile does, with every flag in the pattern, no Compiled and no NonBacktracking, under the caller's timeout"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-18T09:57:00Z
  - key: RegexArgumentsAreBclPrimitives
    statement: "XPathRegex takes the pattern, flags and replacement as the strings fn:matches and fn:replace take, a timeout as TimeSpan and a group count as int, and answers F&O's error code, offset and message as string and int: they are the function's own arguments, not quantities that could be swapped"
    accepted-by: mailto:emil@okkels-klein.dk
    accepted-at: 2026-10-18T09:57:00Z
---

The rulings of [ADR 0153](../adr/0153-xpath-regular-expressions-in-varve-xsd.md), one line each. The ADR is the
narrative; this is what code cites. Filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13) (ADR 0066); every citation is `CS0618` until the maintainer accepts them.
