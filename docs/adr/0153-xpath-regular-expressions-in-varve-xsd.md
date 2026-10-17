# 0153 — XPath regular expressions are translated once, in `Varve.Xsd`

## Status

**Accepted — filed unaccepted by SHACL, part 1 (#120, under milestone 8,
#13), 2026-10-17** (ADR 0066). Decided by the maintainer on the part 1 plan,
with three conditions: the W3C SPARQL query and update suites stay as they
are, and any case that changes is reported; the release notes list the
evaluator's new strictness as a breaking change; and `sh:pattern` runs under
a timeout (ADR 0151). Amends ADR 0051 (the scope of `Varve.Xsd`).

## Context

`sh:pattern` (SHACL 1.2 Core §7.4.3; 2017 §4.4.3) and SPARQL's `REGEX` and
`REPLACE` (SPARQL 1.2 §17.4.3.14, §17.4.3.15) all use the regular expressions
and flags of XPath and XQuery Functions and Operators 3.1 §5.6
(`fn:matches`, `fn:replace`), whose syntax is XML Schema's regular
expressions extended by §5.6.1. .NET's `Regex` is a different dialect.

The evaluator's `XPathRegex` (`Varve.Sparql.Evaluation`, internal) translated
the flags `s`, `m`, `i`, `x` and `q` and the replacement string, and passed the
pattern itself to .NET unchanged. So today:

- `.` matches `\r`, and one UTF-16 code unit, not one character: `^.$` does
  not match a character outside the Basic Multilingual Plane.
- `$` without `m` also matches before a final `\n`.
- `\w` is .NET's `[\p{L}\p{Mn}\p{Nd}\p{Pc}]`, not XPath's "every character but
  `\p{P}`, `\p{Z}` and `\p{C}`", so symbols (`\p{S}`) differ; `\s` is every
  Unicode space, not XPath's four characters.
- `\i`, `\c`, `\I` and `\C` throw, so a valid XPath pattern is an error.
- Constructs XPath forbids are accepted silently: lookaround, named groups,
  `\b`, `\A`, `\z`, `\x41`, inline options such as `(?i)`, and a `{` that is
  not a quantifier.

`Varve.Shacl` is at layer 3 and cannot use an evaluator internal, and a second
translation would drift from the first.

## Decision

1. **A translator from F&O 3.1 §5.6 regular expressions to .NET's dialect, in
   `Varve.Xsd`** (layer 0), public, used by both `Varve.Sparql.Evaluation`
   and `Varve.Shacl`:
   - `XPathRegex.TryTranslate(pattern, flags, out XPathRegexTranslation, out
     XPathRegexError)` parses the pattern by §5.6.1's grammar and writes the
     .NET equivalent, every flag applied in the pattern itself, so translation
     never touches `System.Text.RegularExpressions` and the result needs no
     `RegexOptions` but `CultureInvariant`.
   - `XPathRegex.TryCompile(translation, timeout, …)` builds the `Regex`. It is the
     only member that references `System.Text.RegularExpressions`, so the
     assembly is loaded, and kept by the trimmer, only where a pattern is
     compiled. No `RegexOptions.Compiled` (ignored by Native AOT and the
     browser) and no `NonBacktracking` (it refuses back-references, which
     §5.6.1 allows); every match runs under the caller's timeout.
   - `XPathRegex.TryTranslateReplacement(replacement, groups, …)` translates
     `fn:replace`'s replacement string, moved from the evaluator unchanged.
   - The surface takes F&O's own arguments as the BCL types that hold them —
     the pattern, flags and replacement as strings, a timeout as `TimeSpan` —
     and answers F&O's error code, offset and message as `string` and `int`
     (`RegexArgumentsAreBclPrimitives`).
2. **What the translation does.** Every construct whose meaning differs is
   written out, so the result is compiled with `CultureInvariant` and no other
   option:

   | XPath | .NET written |
   |---|---|
   | a character class, `\s`, `\S`, `\d`, `\D`, `\w`, `\W`, `\i`, `\I`, `\c`, `\C`, `\p{Lu}`, `\P{L}`, and class subtraction `[a-z-[aeiou]]` | the exact set of characters, computed: categories from the runtime's Unicode data (`CharUnicodeInfo`) over every plane, `\s` as its four characters, `\w` as every character but `\p{P}`, `\p{Z}` and `\p{C}`, `\i` and `\c` as XML 1.0 (fifth edition) `NameStartChar` and `NameChar`; written as a BMP class beside the surrogate pairs of its supplementary members |
   | `.` | every character but `\n` and `\r`, or every character under `s`; a surrogate pair as one character |
   | `^`, `$` | `\A`, `\z`; under `m`, §5.6.2's lines: `^` after a newline that is not the last character, `$` before a newline or at the end when the input does not end with one |
   | a character or a range, under `i` | the set with every case-variant added (§5.6.2: same `lower-case` or same `upper-case`); escapes gain none, so `\p{Lu}` stays upper-case |
   | a back-reference `\N` | after checking that group N is closed before it (§5.6.1.4), "group N's text if group N matched, else nothing", case-blind under `i` |
   | `\p{IsBlock}` | .NET's own block escape, in a class written in .NET's syntax; .NET knows only the blocks of the BMP |
   | flag `x` | whitespace removed outside character classes, before parsing |
   | flag `q` | the whole pattern as literal characters; `i` still applies |

3. **What it refuses**, as F&O's `FORX0002` (invalid pattern) or `FORX0001`
   (invalid flags), with the position: an unknown flag; an escape §5.6.1 does
   not define (`\b`, `\B`, `\A`, `\z`, `\Z`, `\G`, `\x…`, `\u…`, `\0`, `\a`,
   `\e`, `\k`, octal); any `(?` other than `(?:`; an unescaped `{`, `}`, `[` or
   `]` outside its construct; a quantifier on nothing, on an anchor or on a
   quantifier; a back-reference to a group not yet closed, or inside a class; a
   range whose end is below its start; an empty character class; an unknown
   general category; and, when compiled, a block name .NET does not know
   (§5.6.1.5).
4. **What it does not reach**, stated: a class that names a Unicode block is
   evaluated over the BMP, because .NET's block escapes are, and no
   supplementary block can be named; its other supplementary members stay
   exact. Under `i`, case-variants come from the runtime's invariant
   single-character mappings. Embedding Unicode's block table would reach the
   rest, and would bring Unicode's data licence into a package that has none.
5. **The evaluator keeps** what is the evaluator's: its per-execution cache by
   pattern and flags, `EvaluationOptions.RegexTimeout`, the mapping of an
   invalid pattern or a timeout to an expression error, and `REPLACE`'s
   refusal of a pattern that matches the empty string (`FORX0003`).
   `Varve.Shacl` has `ShaclOptions.RegexTimeout`, where a timeout or an
   invalid pattern is a failure (ADR 0151).
6. **The W3C SPARQL query and update suites are unchanged.** Every case in
   `baseline/passing.txt` stays there across the move; a case whose outcome
   changes is reported in the pull request, not exempted silently.

## Alternatives considered

- **Move the existing code as it is.** The move the plan first named. Rejected
  by the maintainer once read: it would give SHACL the same divergences, and
  `sh:pattern` with `\i` would throw.
- **Pass categories and `IgnoreCase` to .NET.** Shorter patterns. Rejected:
  .NET classifies UTF-16 units, so no supplementary character is in any
  category, and `IgnoreCase` folds `\p{Lu}`, which §5.6.2 forbids.
- **A regular expression engine of our own.** Exact by construction, and a
  large thing to own. Rejected: the divergences are in syntax and in classes,
  which translation reaches; the remaining one (blocks) is stated.
- **`NonBacktracking`.** No catastrophic backtracking. Rejected: §5.6.1 has
  back-references, which it refuses. The timeout bounds the cost instead.

## Consequences

- **Breaking for `REGEX` and `REPLACE`**, recorded in the release notes: a
  pattern that used a .NET-only construct (lookaround, named groups, `\b`,
  `\A`, `\z`, `\x`, `\u`, inline options, an unescaped `{` or `}` as a
  character) is now an expression error; `.` no longer matches `\r`; `$` no
  longer matches before a final newline; `\w` and `\s` mean XPath's classes;
  `i` no longer folds category escapes; a character outside the BMP is one
  character to `.` and to classes.
- `Varve.Xsd`'s public surface grows by the translator.
- The AOT smoke runs a `REGEX` query and an `sh:pattern`, so a trimming
  regression in either is caught.

## Checks

- **Checked against the accepted ADRs** (0001–0133, 0143–0147). Touches
  **0051** (`Varve.Xsd`'s scope, amended), **0048** (the evaluator keeps its
  structure; an internal moves down), and **0056** (`RegexTimeout` stays an
  evaluator option). No conflict.
- **Layer ownership.** `Varve.Xsd`, layer 0.
- **Analyzer rule.** None.
- **Open questions owned.** None.
