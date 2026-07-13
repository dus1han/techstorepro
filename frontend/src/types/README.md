# Types

## The generated schema

The API publishes its OpenAPI document at `http://localhost:5199/openapi/v1.json`. With the API
running:

```sh
npm run codegen
```

writes `src/types/api-schema.d.ts` — every DTO, enum and route the API actually exposes, derived from
the API itself rather than from someone's memory of it. The file is generated; do not hand-edit it.

## Why the hand-written types are still here

`identity.ts`, `catalog.ts` and `inventory.ts` are hand-written mirrors of the API's contracts. They
are what every screen imports today, and nothing yet imports the generated file.

That is a debt, and it is worth naming precisely: **a hand-written type cannot fail.** If the API
renames a field or renumbers an enum, TypeScript keeps compiling happily against the client's stale
copy, and the mistake surfaces as an `undefined` in production rather than as a red build. The
enum numbers are the sharp edge — they are the wire format, and a client that is one value out will
cheerfully label a write-off as a write-on.

The script existing is what makes the drift *checkable*: regenerate, and the diff against these files
is the list of lies the client is currently telling. Migration is module by module — a screen moves to
`api-schema.d.ts`, its hand-written interfaces are deleted, and when the last one goes so does the
file it lived in.
