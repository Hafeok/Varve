# KeyValue

A single resource's fields: a dataset's status and settings, the writer record, `/manage/system`.

- **Consumer provides:** label and value pairs, in the order the API documents them. Values are rendered by kind: positions as `PositionStamp`, states as `StateBadge`, IRIs and ids in `code`, times as UTC with a relative caption.
- Labels are the API's field names in sentence case ("Writer epoch", not "Leader term").
- Stacks to one column below 480px.
