# SPARQL grammar

Functional specification for the SPARQL parser in `Varve.Sparql` (layer 2):
the text side. The tree it produces, and the translation from grammar to tree,
are in [`sparql-algebra.md`](sparql-algebra.md).

Status: Accepted. Changes only together with the ADR that motivates the change.

## 1. Normative references, and their status

- **SPARQL 1.1 Query Language** — W3C Recommendation, 21 March 2013. §19 the
  grammar (173 productions), §19.1–19.6 the lexical rules, §18 the algebra.
- **SPARQL 1.1 Update** — W3C Recommendation, 21 March 2013. Its grammar is the
  Query grammar's `UpdateUnit` entry point; §3 the operations.
- **SPARQL 1.2 Query Language** — **W3C Working Draft, 21 September 2026**
  (`https://www.w3.org/TR/2026/WD-sparql12-query-20260921/`). §19.7 the grammar
  (195 productions), §4.3 nested triple patterns and their expansion, §4.4 the
  `VERSION` declaration, Appendix A the change list.
- **SPARQL 1.2 Update** — **W3C Working Draft, 12 June 2026**
  (`https://www.w3.org/TR/2026/WD-sparql12-update-20260612/`). Its Appendix A
  lists no grammar change beyond what the Query draft carries.
- **RFC 3987 §2.2** for the IRI check, through `Varve.Iri`; **RFC 3986 §5** for
  resolution against `BASE`.
- **RDF 1.2 Concepts** for triple terms and directional language-tagged
  strings, through `Varve.Rdf` (`rdf-model.md` §1 has their status).

**Both 1.2 documents are Working Drafts.** Unlike the Turtle decision
(`turtle.md` §9), the 1.2 grammar is implemented and its suites are wired,
because the algebra was asked for with 1.2 from the start and a ratchet is what
absorbs manifest churn; the manifests' last content changes date from
February to June 2026. Every 1.2 production is marked in §2 so that a change
to the draft is a diff against a list, and a 1.2 construct is refused when the
caller asks for 1.1 (§5).

Conformance is measured by the syntax suites of the pinned `w3c/rdf-tests`
submodule, each parsed **at its own version** (§5, §8):

| Suite | Version | Cases |
|---|---|---:|
| `sparql/sparql10/syntax-sparql1` … `syntax-sparql5` | 1.1 | 199 |
| `sparql/sparql11/syntax-query` | 1.1 | 94 |
| `sparql/sparql11/syntax-update-1`, `syntax-update-2`, `syntax-fed` | 1.1 | 58 |
| `sparql/sparql12/syntax-triple-terms-positive`, `-negative` | 1.2 | 178 |
| `sparql/sparql12/syntax`, `version`, `codepoint-escapes`, `lang-basedir` | 1.2 | 25 |

The 1.2 manifests mix syntax and evaluation entries; only the syntax entries
are enumerated, and the pinned counts in the conformance tests say so. The
SPARQL 1.0 suites are parsed under 1.1 because 1.0 has no grammar of its own
in this repository: a 1.0 negative case that 1.1 relaxed is an exemption
citing the 1.1 production that relaxed it (§8).

## 2. Grammar

SPARQL 1.2 §19.7, reproduced so that the code can be checked against
something in the repository. Numbering is the 1.2 draft's. A production marked
`# 1.2` does not exist in 1.1; one marked `# 1.2 changed` exists in 1.1 with a
different right-hand side (§2.1 lists the differences). Three 1.1 productions
have no 1.2 counterpart: `[97] Integer`, `[109] GraphTerm` (folded into
`VarOrTerm`) and `[145] LANGTAG` (replaced by `LANG_DIR`).

The EBNF is XML 1.1 §6's. Uppercase names are terminals; the grammar is LL(1)
over them, which is what makes a hand-written recursive descent parser with one
token of lookahead the natural implementation, and no generator is used
(ADR 0048; the dependency register admits none).

```
[1]    QueryUnit                   ::= Query
[2]    Query                       ::= Prologue ( SelectQuery | ConstructQuery | DescribeQuery | AskQuery ) ValuesClause
[3]    UpdateUnit                  ::= Update
[4]    Prologue                    ::= ( BaseDecl | PrefixDecl | VersionDecl )*   # 1.2 changed
[5]    BaseDecl                    ::= 'BASE' IRIREF
[6]    PrefixDecl                  ::= 'PREFIX' PNAME_NS IRIREF
[7]    VersionDecl                 ::= 'VERSION' VersionSpecifier   # 1.2
[8]    VersionSpecifier            ::= STRING_LITERAL1 | STRING_LITERAL2   # 1.2
[9]    SelectQuery                 ::= SelectClause DatasetClause* WhereClause SolutionModifier
[10]   SubSelect                   ::= SelectClause WhereClause SolutionModifier ValuesClause
[11]   SelectClause                ::= 'SELECT' ( 'DISTINCT' | 'REDUCED' )? ( ( Var | ( '(' Expression 'AS' Var ')' ) )+ | '*' )
[12]   ConstructQuery              ::= 'CONSTRUCT' ( ConstructTemplate DatasetClause* WhereClause SolutionModifier | DatasetClause* 'WHERE' ConstructTemplate SolutionModifier )   # 1.2 changed
[13]   DescribeQuery               ::= 'DESCRIBE' ( VarOrIri+ | '*' ) DatasetClause* WhereClause? SolutionModifier
[14]   AskQuery                    ::= 'ASK' DatasetClause* WhereClause SolutionModifier
[15]   DatasetClause               ::= 'FROM' ( DefaultGraphClause | NamedGraphClause )
[16]   DefaultGraphClause          ::= SourceSelector
[17]   NamedGraphClause            ::= 'NAMED' SourceSelector
[18]   SourceSelector              ::= iri
[19]   WhereClause                 ::= 'WHERE'? GroupGraphPattern
[20]   SolutionModifier            ::= GroupClause? HavingClause? OrderClause? LimitOffsetClauses?
[21]   GroupClause                 ::= 'GROUP' 'BY' GroupCondition+
[22]   GroupCondition              ::= BuiltInCall | FunctionCall | '(' Expression ( 'AS' Var )? ')' | Var
[23]   HavingClause                ::= 'HAVING' HavingCondition+
[24]   HavingCondition             ::= Constraint
[25]   OrderClause                 ::= 'ORDER' 'BY' OrderCondition+
[26]   OrderCondition              ::= ( ( 'ASC' | 'DESC' ) BrackettedExpression )| ( Constraint | Var )
[27]   LimitOffsetClauses          ::= LimitClause OffsetClause? | OffsetClause LimitClause?
[28]   LimitClause                 ::= 'LIMIT' INTEGER
[29]   OffsetClause                ::= 'OFFSET' INTEGER
[30]   ValuesClause                ::= ( 'VALUES' DataBlock )?
[31]   Update                      ::= Prologue ( Update1 ( ';' Update )? )?
[32]   Update1                     ::= Load | Clear | Drop | Add | Move | Copy | Create | DeleteWhere | Modify | InsertData | DeleteData   # 1.2 changed
[33]   Load                        ::= 'LOAD' 'SILENT'? iri ( 'INTO' GraphRef )?
[34]   Clear                       ::= 'CLEAR' 'SILENT'? GraphRefAll
[35]   Drop                        ::= 'DROP' 'SILENT'? GraphRefAll
[36]   Create                      ::= 'CREATE' 'SILENT'? GraphRef
[37]   Add                         ::= 'ADD' 'SILENT'? GraphOrDefault 'TO' GraphOrDefault
[38]   Move                        ::= 'MOVE' 'SILENT'? GraphOrDefault 'TO' GraphOrDefault
[39]   Copy                        ::= 'COPY' 'SILENT'? GraphOrDefault 'TO' GraphOrDefault
[40]   InsertData                  ::= 'INSERT' 'DATA' QuadData
[41]   DeleteData                  ::= 'DELETE' 'DATA' QuadData
[42]   DeleteWhere                 ::= 'DELETE' 'WHERE' QuadPattern
[43]   Modify                      ::= ( 'WITH' iri )? ( DeleteClause InsertClause? | InsertClause ) UsingClause* 'WHERE' GroupGraphPattern
[44]   DeleteClause                ::= 'DELETE' QuadPattern
[45]   InsertClause                ::= 'INSERT' QuadPattern
[46]   UsingClause                 ::= 'USING' ( iri | 'NAMED' iri )
[47]   GraphOrDefault              ::= 'DEFAULT' | 'GRAPH'? iri
[48]   GraphRef                    ::= 'GRAPH' iri
[49]   GraphRefAll                 ::= GraphRef | 'DEFAULT' | 'NAMED' | 'ALL'
[50]   QuadPattern                 ::= '{' Quads '}'
[51]   QuadData                    ::= '{' Quads '}'
[52]   Quads                       ::= TriplesTemplate? ( QuadsNotTriples '.'? TriplesTemplate? )*
[53]   QuadsNotTriples             ::= 'GRAPH' VarOrIri '{' TriplesTemplate? '}'
[54]   TriplesTemplate             ::= TriplesSameSubject ( '.' TriplesTemplate? )?
[55]   GroupGraphPattern           ::= '{' ( SubSelect | GroupGraphPatternSub ) '}'
[56]   GroupGraphPatternSub        ::= TriplesBlock? ( GraphPatternNotTriples '.'? TriplesBlock? )*
[57]   TriplesBlock                ::= TriplesSameSubjectPath ( '.' TriplesBlock? )?
[58]   ReifiedTripleBlock          ::= ReifiedTriple PropertyList   # 1.2
[59]   ReifiedTripleBlockPath      ::= ReifiedTriple PropertyListPath   # 1.2
[60]   GraphPatternNotTriples      ::= GroupOrUnionGraphPattern | OptionalGraphPattern | MinusGraphPattern | GraphGraphPattern | ServiceGraphPattern | Filter | Bind | InlineData
[61]   OptionalGraphPattern        ::= 'OPTIONAL' GroupGraphPattern
[62]   GraphGraphPattern           ::= 'GRAPH' VarOrIri GroupGraphPattern
[63]   ServiceGraphPattern         ::= 'SERVICE' 'SILENT'? VarOrIri GroupGraphPattern
[64]   Bind                        ::= 'BIND' '(' Expression 'AS' Var ')'
[65]   InlineData                  ::= 'VALUES' DataBlock
[66]   DataBlock                   ::= InlineDataOneVar | InlineDataFull
[67]   InlineDataOneVar            ::= Var '{' DataBlockValue* '}'
[68]   InlineDataFull              ::= ( NIL | '(' Var* ')' ) '{' ( '(' DataBlockValue* ')' | NIL )* '}'
[69]   DataBlockValue              ::= iri | RDFLiteral | NumericLiteral | BooleanLiteral | 'UNDEF' | TripleTermData   # 1.2 changed
[70]   Reifier                     ::= '~' VarOrReifierId?   # 1.2
[71]   VarOrReifierId              ::= Var | iri | BlankNode   # 1.2
[72]   MinusGraphPattern           ::= 'MINUS' GroupGraphPattern
[73]   GroupOrUnionGraphPattern    ::= GroupGraphPattern ( 'UNION' GroupGraphPattern )*
[74]   Filter                      ::= 'FILTER' Constraint
[75]   Constraint                  ::= BrackettedExpression | BuiltInCall | FunctionCall
[76]   FunctionCall                ::= iri ArgList
[77]   ArgList                     ::= NIL | '(' 'DISTINCT'? Expression ( ',' Expression )* ')'
[78]   ExpressionList              ::= NIL | '(' Expression ( ',' Expression )* ')'
[79]   ConstructTemplate           ::= '{' ConstructTriples? '}'
[80]   ConstructTriples            ::= TriplesSameSubject ( '.' ConstructTriples? )?
[81]   TriplesSameSubject          ::= VarOrTerm PropertyListNotEmpty | TriplesNode PropertyList | ReifiedTripleBlock   # 1.2 changed
[82]   PropertyList                ::= PropertyListNotEmpty?
[83]   PropertyListNotEmpty        ::= Verb ObjectList ( ';' ( Verb ObjectList )? )*
[84]   Verb                        ::= VarOrIri | 'a'
[85]   ObjectList                  ::= Object ( ',' Object )*
[86]   Object                      ::= GraphNode Annotation   # 1.2 changed
[87]   TriplesSameSubjectPath      ::= VarOrTerm PropertyListPathNotEmpty | TriplesNodePath PropertyListPath | ReifiedTripleBlockPath   # 1.2 changed
[88]   PropertyListPath            ::= PropertyListPathNotEmpty?
[89]   PropertyListPathNotEmpty    ::= ( VerbPath | VerbSimple ) ObjectListPath ( ';' ( ( VerbPath | VerbSimple ) ObjectListPath )? )*   # 1.2 changed
[90]   VerbPath                    ::= Path
[91]   VerbSimple                  ::= Var
[92]   ObjectListPath              ::= ObjectPath ( ',' ObjectPath )*
[93]   ObjectPath                  ::= GraphNodePath AnnotationPath   # 1.2 changed
[94]   Path                        ::= PathAlternative
[95]   PathAlternative             ::= PathSequence ( '|' PathSequence )*
[96]   PathSequence                ::= PathEltOrInverse ( '/' PathEltOrInverse )*
[97]   PathElt                     ::= PathPrimary PathMod?
[98]   PathEltOrInverse            ::= PathElt | '^' PathElt
[99]   PathMod                     ::= '?' | '*' | '+'
[100]  PathPrimary                 ::= iri | 'a' | '!' PathNegatedPropertySet | '(' Path ')'
[101]  PathNegatedPropertySet      ::= PathOneInPropertySet | '(' ( PathOneInPropertySet ( '|' PathOneInPropertySet )* )? ')'
[102]  PathOneInPropertySet        ::= iri | 'a' | '^' ( iri | 'a' )
[103]  TriplesNode                 ::= Collection | BlankNodePropertyList
[104]  BlankNodePropertyList       ::= '[' PropertyListNotEmpty ']'
[105]  TriplesNodePath             ::= CollectionPath | BlankNodePropertyListPath
[106]  BlankNodePropertyListPath   ::= '[' PropertyListPathNotEmpty ']'
[107]  Collection                  ::= '(' GraphNode+ ')'
[108]  CollectionPath              ::= '(' GraphNodePath+ ')'
[109]  AnnotationPath              ::= ( Reifier | AnnotationBlockPath )*   # 1.2
[110]  AnnotationBlockPath         ::= '{|' PropertyListPathNotEmpty '|}'   # 1.2
[111]  Annotation                  ::= ( Reifier | AnnotationBlock )*   # 1.2
[112]  AnnotationBlock             ::= '{|' PropertyListNotEmpty '|}'   # 1.2
[113]  GraphNode                   ::= VarOrTerm | TriplesNode | ReifiedTriple   # 1.2 changed
[114]  GraphNodePath               ::= VarOrTerm | TriplesNodePath | ReifiedTriple   # 1.2 changed
[115]  VarOrTerm                   ::= Var | iri | RDFLiteral | NumericLiteral | BooleanLiteral | BlankNode | NIL | TripleTerm   # 1.2 changed
[116]  ReifiedTriple               ::= '<<' ReifiedTripleSubject Verb ReifiedTripleObject Reifier? '>>'   # 1.2
[117]  ReifiedTripleSubject        ::= Var | iri | RDFLiteral | NumericLiteral | BooleanLiteral | BlankNode | ReifiedTriple | TripleTerm   # 1.2
[118]  ReifiedTripleObject         ::= Var | iri | RDFLiteral | NumericLiteral | BooleanLiteral | BlankNode | ReifiedTriple | TripleTerm   # 1.2
[119]  TripleTerm                  ::= '<<(' TripleTermSubject Verb TripleTermObject ')>>'   # 1.2
[120]  TripleTermSubject           ::= Var | iri | RDFLiteral | NumericLiteral | BooleanLiteral | BlankNode | TripleTerm   # 1.2
[121]  TripleTermObject            ::= Var | iri | RDFLiteral | NumericLiteral | BooleanLiteral | BlankNode | TripleTerm   # 1.2
[122]  TripleTermData              ::= '<<(' TripleTermDataSubject ( iri | 'a' ) TripleTermDataObject ')>>'   # 1.2
[123]  TripleTermDataSubject       ::= iri   # 1.2
[124]  TripleTermDataObject        ::= iri | RDFLiteral | NumericLiteral | BooleanLiteral | TripleTermData   # 1.2
[125]  VarOrIri                    ::= Var | iri
[126]  Var                         ::= VAR1 | VAR2
[127]  Expression                  ::= ConditionalOrExpression
[128]  ConditionalOrExpression     ::= ConditionalAndExpression ( '||' ConditionalAndExpression )*
[129]  ConditionalAndExpression    ::= ValueLogical ( '&&' ValueLogical )*
[130]  ValueLogical                ::= RelationalExpression
[131]  RelationalExpression        ::= NumericExpression ( '=' NumericExpression | '!=' NumericExpression | '<' NumericExpression | '>' NumericExpression | '<=' NumericExpression | '>=' NumericExpression | 'IN' ExpressionList | 'NOT' 'IN' ExpressionList )?
[132]  NumericExpression           ::= AdditiveExpression
[133]  AdditiveExpression          ::= MultiplicativeExpression ( '+' MultiplicativeExpression | '-' MultiplicativeExpression | ( NumericLiteralPositive | NumericLiteralNegative ) ( ( '*' UnaryExpression ) | ( '/' UnaryExpression ) )* )*
[134]  MultiplicativeExpression    ::= UnaryExpression ( '*' UnaryExpression | '/' UnaryExpression )*
[135]  UnaryExpression             ::= '!' UnaryExpression | '+' PrimaryExpression | '-' PrimaryExpression | PrimaryExpression   # 1.2 changed
[136]  PrimaryExpression           ::= BrackettedExpression | BuiltInCall | iriOrFunction | RDFLiteral | NumericLiteral | BooleanLiteral | Var | ExprTripleTerm   # 1.2 changed
[137]  ExprTripleTerm              ::= '<<(' ExprTripleTermSubject Verb ExprTripleTermObject ')>>'   # 1.2
[138]  ExprTripleTermSubject       ::= iri | Var   # 1.2
[139]  ExprTripleTermObject        ::= iri | RDFLiteral | NumericLiteral | BooleanLiteral | Var | ExprTripleTerm   # 1.2
[140]  BrackettedExpression        ::= '(' Expression ')'
[141]  BuiltInCall                 ::= Aggregate   # 1.2 changed
                                       | 'STR' '(' Expression ')'
                                       | 'LANG' '(' Expression ')'
                                       | 'LANGMATCHES' '(' Expression ',' Expression ')'
                                       | 'LANGDIR' '(' Expression ')'
                                       | 'DATATYPE' '(' Expression ')'
                                       | 'BOUND' '(' Var ')'
                                       | 'IRI' '(' Expression ')'
                                       | 'URI' '(' Expression ')'
                                       | 'BNODE' ( '(' Expression ')'
                                       | NIL )
                                       | 'RAND' NIL
                                       | 'ABS' '(' Expression ')'
                                       | 'CEIL' '(' Expression ')'
                                       | 'FLOOR' '(' Expression ')'
                                       | 'ROUND' '(' Expression ')'
                                       | 'CONCAT' ExpressionList
                                       | SubstringExpression
                                       | 'STRLEN' '(' Expression ')'
                                       | StrReplaceExpression
                                       | 'UCASE' '(' Expression ')'
                                       | 'LCASE' '(' Expression ')'
                                       | 'ENCODE_FOR_URI' '(' Expression ')'
                                       | 'CONTAINS' '(' Expression ',' Expression ')'
                                       | 'STRSTARTS' '(' Expression ',' Expression ')'
                                       | 'STRENDS' '(' Expression ',' Expression ')'
                                       | 'STRBEFORE' '(' Expression ',' Expression ')'
                                       | 'STRAFTER' '(' Expression ',' Expression ')'
                                       | 'YEAR' '(' Expression ')'
                                       | 'MONTH' '(' Expression ')'
                                       | 'DAY' '(' Expression ')'
                                       | 'HOURS' '(' Expression ')'
                                       | 'MINUTES' '(' Expression ')'
                                       | 'SECONDS' '(' Expression ')'
                                       | 'TIMEZONE' '(' Expression ')'
                                       | 'TZ' '(' Expression ')'
                                       | 'NOW' NIL
                                       | 'UUID' NIL
                                       | 'STRUUID' NIL
                                       | 'MD5' '(' Expression ')'
                                       | 'SHA1' '(' Expression ')'
                                       | 'SHA256' '(' Expression ')'
                                       | 'SHA384' '(' Expression ')'
                                       | 'SHA512' '(' Expression ')'
                                       | 'COALESCE' ExpressionList
                                       | 'IF' '(' Expression ',' Expression ',' Expression ')'
                                       | 'STRLANG' '(' Expression ',' Expression ')'
                                       | 'STRLANGDIR' '(' Expression ',' Expression ',' Expression ')'
                                       | 'STRDT' '(' Expression ',' Expression ')'
                                       | 'sameTerm' '(' Expression ',' Expression ')'
                                       | 'isIRI' '(' Expression ')'
                                       | 'isURI' '(' Expression ')'
                                       | 'isBLANK' '(' Expression ')'
                                       | 'isLITERAL' '(' Expression ')'
                                       | 'isNUMERIC' '(' Expression ')'
                                       | 'hasLANG' '(' Expression ')'
                                       | 'hasLANGDIR' '(' Expression ')'
                                       | RegexExpression
                                       | ExistsFunc
                                       | NotExistsFunc
                                       | 'isTRIPLE' '(' Expression ')'
                                       | 'TRIPLE' '(' Expression ',' Expression ',' Expression ')'
                                       | 'SUBJECT' '(' Expression ')'
                                       | 'PREDICATE' '(' Expression ')'
                                       | 'OBJECT' '(' Expression ')'
[142]  RegexExpression             ::= 'REGEX' '(' Expression ',' Expression ( ',' Expression )? ')'
[143]  SubstringExpression         ::= 'SUBSTR' '(' Expression ',' Expression ( ',' Expression )? ')'
[144]  StrReplaceExpression        ::= 'REPLACE' '(' Expression ',' Expression ',' Expression ( ',' Expression )? ')'
[145]  ExistsFunc                  ::= 'EXISTS' GroupGraphPattern
[146]  NotExistsFunc               ::= 'NOT' 'EXISTS' GroupGraphPattern
[147]  Aggregate                   ::= 'COUNT' '(' 'DISTINCT'? ( '*'
                                       | Expression ) ')'
                                       | 'SUM' '(' 'DISTINCT'? Expression ')'
                                       | 'MIN' '(' 'DISTINCT'? Expression ')'
                                       | 'MAX' '(' 'DISTINCT'? Expression ')'
                                       | 'AVG' '(' 'DISTINCT'? Expression ')'
                                       | 'SAMPLE' '(' 'DISTINCT'? Expression ')'
                                       | 'GROUP_CONCAT' '(' 'DISTINCT'? Expression ( ';' 'SEPARATOR' '=' String )? ')'
[148]  iriOrFunction               ::= iri ArgList?
[149]  RDFLiteral                  ::= String ( LANG_DIR | '^^' iri )?   # 1.2 changed
[150]  NumericLiteral              ::= NumericLiteralUnsigned | NumericLiteralPositive | NumericLiteralNegative
[151]  NumericLiteralUnsigned      ::= INTEGER | DECIMAL | DOUBLE
[152]  NumericLiteralPositive      ::= INTEGER_POSITIVE | DECIMAL_POSITIVE | DOUBLE_POSITIVE
[153]  NumericLiteralNegative      ::= INTEGER_NEGATIVE | DECIMAL_NEGATIVE | DOUBLE_NEGATIVE
[154]  BooleanLiteral              ::= 'true' | 'false'
[155]  String                      ::= STRING_LITERAL1 | STRING_LITERAL2 | STRING_LITERAL_LONG1 | STRING_LITERAL_LONG2
[156]  iri                         ::= IRIREF | PrefixedName
[157]  PrefixedName                ::= PNAME_LN | PNAME_NS
[158]  BlankNode                   ::= BLANK_NODE_LABEL | ANON
[159]  IRIREF                      ::= '<' ( [^<>"{}|^`\]-[#x00-#x20] | UCHAR ) * '>'   # 1.2 changed
[160]  PNAME_NS                    ::= PN_PREFIX? ':'
[161]  PNAME_LN                    ::= PNAME_NS PN_LOCAL
[162]  BLANK_NODE_LABEL            ::= '_:' ( PN_CHARS_U | [0-9] ) ((PN_CHARS|'.')* PN_CHARS)?
[163]  VAR1                        ::= '?' VARNAME
[164]  VAR2                        ::= '$' VARNAME
[165]  LANG_DIR                    ::= '@' [a-zA-Z]+ ('-' [a-zA-Z0-9]+)* ('--' [a-zA-Z]+)?   # 1.2
[166]  INTEGER                     ::= [0-9]+
[167]  DECIMAL                     ::= [0-9]* '.' [0-9]+
[168]  DOUBLE                      ::= ( ([0-9]+ ('.'[0-9]*)? ) | ( '.' ([0-9])+ ) ) EXPONENT   # 1.2 changed
[169]  EXPONENT                    ::= [eE] [+-]? [0-9]+
[170]  INTEGER_POSITIVE            ::= '+' INTEGER
[171]  DECIMAL_POSITIVE            ::= '+' DECIMAL
[172]  DOUBLE_POSITIVE             ::= '+' DOUBLE
[173]  INTEGER_NEGATIVE            ::= '-' INTEGER
[174]  DECIMAL_NEGATIVE            ::= '-' DECIMAL
[175]  DOUBLE_NEGATIVE             ::= '-' DOUBLE
[176]  STRING_LITERAL1             ::= "'" ( ([^#x27#x5C#xA#xD]) | ECHAR | UCHAR )* "'"   # 1.2 changed
[177]  STRING_LITERAL2             ::= '"' ( ([^#x22#x5C#xA#xD]) | ECHAR | UCHAR )* '"'   # 1.2 changed
[178]  STRING_LITERAL_LONG1        ::= "'''" ( ( "'" | "''" )? ( [^'\] | ECHAR | UCHAR ) )* "'''"   # 1.2 changed
[179]  STRING_LITERAL_LONG2        ::= '"""' ( ( '"' | '""' )? ( [^"\] | ECHAR | UCHAR ) )* '"""'   # 1.2 changed
[180]  ECHAR                       ::= '\' [tbnrf\"']
[181]  UCHAR                       ::= ('\u' HEX HEX HEX HEX) | ('\U' HEX HEX HEX HEX HEX HEX HEX HEX)   # 1.2
[182]  NIL                         ::= '(' WS* ')'
[183]  WS                          ::= #x20 | #x9 | #xD | #xA
[184]  ANON                        ::= '[' WS* ']'
[185]  PN_CHARS_BASE               ::= [A-Z] | [a-z] | [#x00C0-#x00D6] | [#x00D8-#x00F6] | [#x00F8-#x02FF] | [#x0370-#x037D] | [#x037F-#x1FFF] | [#x200C-#x200D] | [#x2070-#x218F] | [#x2C00-#x2FEF] | [#x3001-#xD7FF] | [#xF900-#xFDCF] | [#xFDF0-#xFFFD] | [#x10000-#xEFFFF]
[186]  PN_CHARS_U                  ::= PN_CHARS_BASE | '_'
[187]  VARNAME                     ::= ( PN_CHARS_U | [0-9] ) ( PN_CHARS_U | [0-9] | #x00B7 | [#x0300-#x036F] | [#x203F-#x2040] )*
[188]  PN_CHARS                    ::= PN_CHARS_U | '-' | [0-9] | #x00B7 | [#x0300-#x036F] | [#x203F-#x2040]
[189]  PN_PREFIX                   ::= PN_CHARS_BASE ((PN_CHARS|'.')* PN_CHARS)?
[190]  PN_LOCAL                    ::= (PN_CHARS_U | ':' | [0-9] | PLX ) ((PN_CHARS | '.' | ':' | PLX)* (PN_CHARS | ':' | PLX) )?
[191]  PLX                         ::= PERCENT | PN_LOCAL_ESC
[192]  PERCENT                     ::= '%' HEX HEX
[193]  HEX                         ::= [0-9] | [A-F] | [a-f]
[194]  PN_LOCAL_ESC                ::= '\' ( '_' | '~' | '.' | '-' | '!' | '$' | '&' | "'" | '(' | ')' | '*' | '+' | ',' | ';' | '=' | '/' | '?' | '#' | '@' | '%' )
```

### 2.1 What 1.2 changed, production by production

| 1.2 | 1.1 | Change |
|---|---|---|
| `[4] Prologue` | `[4]` | `VersionDecl` may appear among the declarations |
| `[7] VersionDecl`, `[8] VersionSpecifier` | — | `VERSION "1.2"`; a short string only, so `'''1.2'''` and a bare `1.2` are errors (`version-bad-01..03`) |
| `[12] ConstructQuery` | `[10]` | The short form takes a `ConstructTemplate`, which is what `'{' TriplesTemplate? '}'` was |
| `[32] Update1` | `[30]` | Alternatives reordered; no change in language |
| `[58] ReifiedTripleBlock`, `[59] ReifiedTripleBlockPath` | — | A reified triple as the subject of a property list |
| `[69] DataBlockValue` | `[65]` | `TripleTermData` in `VALUES` |
| `[70] Reifier`, `[71] VarOrReifierId` | — | `~ ?r`, `~ :iri`, `~ _:b`, or a bare `~` |
| `[81] TriplesSameSubject`, `[87] TriplesSameSubjectPath` | `[75]`, `[81]` | The reified-triple-block alternative |
| `[86] Object`, `[93] ObjectPath` | `[80]`, `[87]` | An object may carry `Annotation` / `AnnotationPath` |
| `[89] PropertyListPathNotEmpty` | `[83]` | Erratum `errata-query-2`: `ObjectListPath` after `;`, not `ObjectList`. Applied under 1.1 too |
| `[109]–[112] Annotation*` | — | `{| … |}` blocks after an object |
| `[113] GraphNode`, `[114] GraphNodePath` | `[104]`, `[105]` | `ReifiedTriple` as a node |
| `[115] VarOrTerm` | `[106]` | `GraphTerm` inlined; `TripleTerm` added |
| `[116]–[124]` | — | `<< … >>` reified triples and `<<( … )>>` triple terms, in patterns and in data |
| `[135] UnaryExpression` | `[118]` | `'!' UnaryExpression`, so `!!?x` parses. Listed by the draft as an erratum; applied under 1.1 too |
| `[136] PrimaryExpression` | `[119]` | `ExprTripleTerm` |
| `[137]–[139] ExprTripleTerm*` | — | `<<( … )>>` in an expression |
| `[141] BuiltInCall` | `[121]` | `LANGDIR`, `hasLANG`, `hasLANGDIR`, `STRLANGDIR`, `isTRIPLE`, `TRIPLE`, `SUBJECT`, `PREDICATE`, `OBJECT` |
| `[149] RDFLiteral` | `[129]` | `LANG_DIR` in place of `LANGTAG` |
| `[159] IRIREF`, `[176]–[179] STRING_LITERAL*` | `[139]`, `[156]–[159]` | `UCHAR` inside the production, because escapes are processed during parsing (§3.2) rather than before it |
| `[165] LANG_DIR` | `[145]` | The optional `--ltr` / `--rtl` suffix |
| `[168] DOUBLE` | `[148]` | Refactored; same language |
| `[181] UCHAR` | — | Named; 1.1 defined the same escapes in prose |

### 2.2 Points the grammar makes that are easy to get wrong

- **Keywords are case-insensitive except `a`.** `select`, `Select` and
  `SELECT` are one token; `A` is not `a`. Function names such as `isIRI` and
  `sameTerm` are keywords and case-insensitive too.
- **Longest match, and no white space inside a signed number.** `?x-1` is the
  variable `?x` followed by the negative number `-1`, which `[133]
  AdditiveExpression` then reads as a subtraction; `?x - 1` is the same
  subtraction spelt out. `?a<?b&&?c>?d` is `?a`, the IRI `<?b&&?c>`, `?d`
  (§19.3 of the draft).
- **`INSERT DATA`, `DELETE DATA` and `DELETE WHERE`** are two keywords with any
  white space, comments included, between them.
- **`QuadData` is `Quads` with no variables**, which the grammar cannot say and
  the parser must (§4).
- **`NIL` and `ANON` are terminals** that may contain white space: `( )` is
  `NIL`, `[ ]` is `ANON`. `( ?x )` is neither.
- **`PN_LOCAL` may end in `:` and may contain `.`** but may not end in one;
  `:a.` is the name `:a` followed by the dot that ends a triples block.
  `PN_LOCAL_ESC` lets `\.` and the other reserved characters through, and a
  `%HH` sequence is kept as the three characters it is written with, never
  decoded (§19.2 note).
- **`a` is a verb only.** In `[84] Verb` and `[100] PathPrimary` it is
  `rdf:type`; as a subject or object it is a syntax error, because
  `[115] VarOrTerm` does not admit it.
- **`BIND` closes the triples block before it.** `?s :p ?o BIND(… AS ?v) ?s :q ?w`
  is two `TriplesBlock`s with the `Bind` between them, and the scope rule for
  `?v` looks at the first (§4).
- **A reifier or annotation follows an object only when the verb is simple**
  — an IRI, `a` or a variable, not a path with `/`, `|`, `^`, `*`, `+`, `?`
  or `!` (draft §19.7 notes). `?s :p/:q ?o {| … |}` is an error.
- **`<<` and `<<(` are different tokens** and so are `>>` and `)>>`.
  `<< ?s ?p ?o >>` is a reified triple, a shorthand that expands (§3.4 of the
  algebra specification); `<<( ?s ?p ?o )>>` is a triple term.

## 3. Lexical rules

### 3.1 Input

A SPARQL string is a sequence of Unicode scalar values (draft §19.1): no
surrogate code points, in the text or produced by an escape. The parser takes
**UTF-8** natively, and takes UTF-16 by transcoding it into a pooled buffer
first, so that one lexer serves both and positions have one meaning (§6).
Ill-formed UTF-8, and a lone surrogate in UTF-16, are errors at the offending
byte, not replaced.

### 3.2 Escapes — draft §19.2

Three forms, allowed in three places, and nowhere else:

| Where | `\uXXXX`, `\UXXXXXXXX` | `\t \n \r \b \f \" \' \\` | `\~ \. \- …` (reserved) |
|---|---|---|---|
| `IRIREF`, in a term or in `BASE` / `PREFIX` | yes | no | no |
| `PN_LOCAL` | no | no | yes |
| Strings | yes | yes | no |

**Escapes are processed while the terminal is being lexed, and each escape is
substituted once.** SPARQL 1.1 §19.2 said the numeric escapes were processed
before the grammar was applied, which would let `ASK {}` be the
query `ASK {}` and would let a `\` produce a backslash that then starts a
second escape. The 1.2 draft moves processing into parsing (Appendix A), which
is what the 1.1 test suite already required (`syn-codepoint-escape-bad-04`,
`-05`: the backslash an escape produces is a backslash, not the start of
another escape) and what every 1.0 case that uses an escape uses it for. So the
1.2 rule applies under every version: a numeric escape outside an `IRIREF` or a
string is an error (`codepoint-esc-01..04-bad`), and an escape that would
produce a surrogate, U+D800–U+DFFF, is an error whether it stands alone or is
half of a pair (`surrogate-esc-01..05-bad`, `syn-invalid-codepoint-escaped-bad-01`).

An escape in an `IRIREF` is processed before the IRI check (§3.5), so
`<http://example/a>` is `<http://example/a>` and `< >` fails the
check for containing a space.

### 3.3 White space and comments — draft §19.3, §19.4

`WS` is `#x20 | #x9 | #xD | #xA`. A comment is `#` outside an `IRIREF` and a
string, to the end of line or of input, and is white space. White space is
significant inside strings, inside `NIL` and `ANON` (where it is allowed), and
nowhere else.

### 3.4 Keywords and tokens

The lexer is driven by the parser: it returns the next terminal from the
current position, choosing the longest match, and a keyword is recognised only
where the grammar expects one. This is what lets `SELECT` be a prefixed name's
local part in `ex:SELECT`, and lets the variable `?select` exist.

`true` and `false` are keywords in `[154] BooleanLiteral` and case-insensitive
there; `TRUE` is `true`. They are not IRIs and cannot be prefixed names' local
parts without a prefix.

### 3.5 IRIs, prefixes and base — draft §19.5

- **Every IRI is checked**, after escape processing and after prefix
  expansion, against RFC 3987 §2.2 through `Varve.Iri`. `<abc#def>` passes
  and `<abc##def>` fails, at the position of the `IRIREF` or of the prefixed
  name. A prefixed name whose expansion is not a valid IRI is an error at the
  name, as in `turtle.md` §3.
- **A relative IRI is resolved against the base** per RFC 3986 §5, through
  `Varve.Iri`. The base is the `BASE` declaration if there is one, and the
  caller's base otherwise; a `BASE` that is itself relative is resolved
  against the caller's base and must come out absolute. With neither, a
  relative IRI is an error saying so, rather than a term that no dataset holds.
- **`PREFIX` declarations are in scope from the declaration on**, and a prefix
  used before its declaration is an error. The draft says a prefix may not be
  redeclared in the same query. The parser accepts a redeclaration and uses
  the most recent binding from that point on — Oxigraph and Turtle do the
  same, and a syntax suite that relied on the refusal would be the first
  evidence for it. Recorded as an open question (§9).
- **`a` expands to `rdf:type`**, `http://www.w3.org/1999/02/22-rdf-syntax-ns#type`,
  and never needs the prefix declared.

### 3.6 Blank node labels — draft §19.6

A label is scoped to the string being parsed; the same label names the same
blank node everywhere in it. Beyond that the draft forbids:

- a blank node anywhere in `DELETE WHERE`, `DELETE DATA` and a `DeleteClause`;
- the same label in two separate basic graph patterns of a query — where
  "separate" is what the translation makes separate: across `{ }` group
  boundaries, and on either side of an `OPTIONAL`, `UNION`, `GRAPH`, `MINUS`,
  `BIND`, `VALUES`, `SERVICE` or property path within one group, each of
  which closes the basic graph pattern before it; a `FILTER` does not
  (`syn-blabel-cross-graph-bad`, `-optional-bad`, `-union-bad`,
  `syn-bad-OPT-breaks-BGP`, `-UNION-breaks-BGP`, `-GRAPH-breaks-BGP`);
- the same label in two `WHERE` clauses of one update request, or in two
  `INSERT DATA` operations of one request (`syntax-update-bad-*`).

The same label may appear in several `QuadPattern`s of one operation, and in
an `INSERT` template and its `WHERE` clause.

The parser generates labels for `ANON` (`[]`) and for the fresh blank nodes
the 1.2 expansions introduce (algebra specification §3.4). A generated label
is `b` followed by a counter. If a label the author wrote collides with a
generated one, the parse is repeated with a longer prefix; this is total,
deterministic, and never happens on the test corpora.

## 4. What the parser checks beyond the EBNF

The draft's §19.7 notes and §18.3.1 add rules the EBNF cannot state. Each is
an error at the position of the offending token, with the production named in
the message, and the syntax suites hold the parser to every one of them.

| Rule | Where the draft says it | Suite case |
|---|---|---|
| `QuadData` (`INSERT DATA`, `DELETE DATA`) contains no variables | §19.7 notes | `syntax-update-bad-*` |
| No blank node in `DELETE DATA`, `DELETE WHERE`, `DELETE { }` | §19.6 | `syntax-update-bad-*` |
| The number of values in each `VALUES` row equals the number of variables | §19.7 notes | `syn-bad-*` |
| No variable twice in a `VALUES` variable list | §19.7 notes, new in 1.2 | `duplicated-values-variable` |
| `(expr AS ?v)` in `SELECT`: `?v` not in scope in the pattern nor bound by an earlier `AS` | §18.3.1, §18.3.4.4 | `syn-bad-*`, `group-by-scope-bad-1..3` |
| `BIND (expr AS ?v)`: `?v` not in scope from the preceding elements of the group | §18.3.1, §19.7 notes | `syn-bind-*` |
| Aggregates only in `SELECT`, `HAVING`, `ORDER BY`; never nested | §19.7 notes | `nested-aggregate-functions`, `agg-*` |
| `DISTINCT` in a function call only when the function is a custom aggregate | §19.7 notes | — |
| A projected variable in a query level that aggregates is a group key, or bound by `AS`; `SELECT *` is refused with `GROUP BY` or with an aggregate in `HAVING` / `ORDER BY` | §11.4, §19.7 notes | `agg08`–`agg12` |
| A reifier or annotation only after a simple verb | §19.7 notes | `syntax-triple-terms-negative` |
| Every IRI valid after expansion; `BASE` absolute | §19.5 | `syn-bad-*` |
| `LIMIT` and `OFFSET` fit in a signed 64-bit integer | — | — |

The scope rule for `SELECT` uses the in-scope table of draft §18.3.1, with one
reading it forces: **after `GROUP BY`, the only variables in scope are the
group keys** — the variables named by `GROUP BY ?v` and `GROUP BY (expr AS ?v)`
— so `SELECT (123 AS ?z) WHERE { ?s :p ?z } GROUP BY ?s` is legal
(`group-by-scope-1`) and `… GROUP BY ?z` is not (`group-by-scope-bad-2`).

A **duplicate `VALUES` variable** is refused under every version. The rule is
new in the draft, but a row with two bindings for one variable has no meaning
under 1.1 either, and no 1.1 case relies on it.

## 5. Versions

The parser takes a `SparqlVersion` option: `1.1`, `1.2-basic` or `1.2`, the
labels of draft §4.4.1. **The default is `1.2`** for an API caller; the
conformance harness passes each suite's own version. The option is the widest
grammar the caller accepts; a `VERSION` declaration in the text may narrow it
and may not widen it:

| Text says | Option `1.1` | Option `1.2-basic` | Option `1.2` |
|---|---|---|---|
| nothing | 1.1 | 1.2-basic | 1.2 |
| `VERSION "1.1"` | error: the declaration is not 1.1 syntax | 1.1 | 1.1 |
| `VERSION "1.2-basic"` | error | 1.2-basic | 1.2-basic |
| `VERSION "1.2"` | error | error: wider than the caller allows | 1.2 |
| any other label | error | error | error |

A declaration applies from the point it appears (`version-02` declares after a
`PREFIX`), the last of several wins (`version-06`), and the tree records the
label so that the serialiser writes it back. The draft lets a processor treat
an unknown label as a warning; this parser treats it as an error, because a
warning is a channel the parser does not have and a silently ignored version
is the failure the declaration exists to prevent.

What each version refuses, by production:

| Construct | Productions | 1.1 | 1.2-basic | 1.2 |
|---|---|---|---|---|
| `VERSION` declaration | `[7]`, `[8]` | refused | accepted | accepted |
| Triple terms in patterns, data, expressions | `[115]`, `[119]`–`[124]`, `[137]`–`[139]` | refused | refused | accepted |
| Reified triples, reifiers, annotations | `[58]`, `[59]`, `[70]`, `[71]`, `[109]`–`[112]`, `[116]`–`[118]` | refused | refused | accepted |
| `TRIPLE`, `isTRIPLE`, `SUBJECT`, `PREDICATE`, `OBJECT` | `[141]` | refused | refused | accepted |
| `LANGDIR`, `hasLANG`, `hasLANGDIR`, `STRLANGDIR` | `[141]` | refused | accepted | accepted |
| `"…"@en--ltr` | `[165]` | refused | accepted | accepted |
| `!!?x` | `[135]` | accepted (erratum) | accepted | accepted |
| Escapes processed during parsing; surrogates refused | §3.2 | applied | applied | applied |

A refusal is a syntax error naming the production and the version, at the
position of the construct. The conformance harness has one test that pins the
mechanism: `version-01.rq` parses under `1.2` and is an error under `1.1`.

## 6. Position reporting

As `n-triples.md` §4: every error carries the **byte offset** from the start
of the input, the **1-based line**, and the **1-based column counted in
bytes**. For UTF-16 input the offset is in the transcoded UTF-8, which is the
only form the lexer sees, and the specification says so rather than have a
caller discover that a column past a non-ASCII character is not what an editor
shows. Every node of the tree carries its span in the same units
(`sparql-algebra.md` §2.3).

An error names what was found and what the grammar expected — the production,
or the set of terminals — because a SPARQL error with no expectation in it is
the kind of message a query author reads three times.

## 7. No recovery, no `[HotPath]`

**The first error ends the parse.** Turtle's recovery unit is the statement
(`turtle.md` §5) because a document is a set of statements and the rest are
still worth having. A query is one unit: half a `WHERE` clause is not a query
anybody can run, and a `SELECT` that lost its `FILTER` is a wrong answer
rather than a partial one. An update request is one commit (ADR 0005), so
accepting some of its operations would be a lie about what was committed.
There is no `OnError` handler and no resynchronisation.

**No method in the parser carries `[HotPath]`.** The attribute (ADR 0026)
marks code whose allocation is measured to be zero per unit of work. A parser
whose output is a tree allocates that tree, so the attribute would be false on
every method that builds one. The allocation claim the parser does make is the
sharper one in §8: it allocates the tree and nothing else.

## 8. Tests, and the gate

- **The syntax suites of §1**, parsed at their version, gated by the ratchet
  (ADR 0007): a case that passed and now fails is a build failure; a case that
  vanished is one too; an exemption without a justification naming a
  production is one too.
- **SPARQL 1.0 exemptions.** A 1.0 negative case that SPARQL 1.1 relaxed is
  exempted with the 1.1 production that relaxed it, one line each. The
  expected set is small and listed in `exemptions.txt`; anything else in the
  1.0 suites passes as written.
- **Every corpus file parsed twice**, as UTF-8 and as UTF-16, must give the
  same tree or the same error kind at the same offset. The parser has no
  chunked entry point, so `turtle.md` §8's chunk-boundary oracle does not
  apply; this is its analogue.
- **Allocation.** Two queries with a fixed vocabulary, one of 50 triple
  patterns and one of 400, are parsed and the difference in allocated bytes
  is asserted **equal** to the difference for building the same two trees by
  hand. Not "small": equal. That is the sharpest statement of "the parser
  allocates the tree and nothing else" that can be tested, and it is
  insensitive to the fixed cost of a lexer buffer.
- **The version test** of §5.
- **The round-trip property**, which belongs to the serialiser and is in
  `sparql-algebra.md` §6.

## 9. Open questions

1. **Prefix redeclaration** (§3.5). The draft forbids it; the parser accepts it
   with last-wins. Resolved by whichever suite first tests it, or by the draft
   dropping the sentence. Owner: `Varve.Sparql`, due when the 1.2 draft
   reaches Candidate Recommendation.
2. **The 1.2 draft's own open issues** in §18 (issues 226, 229, 230, 231) do
   not affect the grammar. Issue 226 — recursive application of the path
   translation — is handled by the algebra specification not applying that
   rewrite at all (`sparql-algebra.md` §4.4).
