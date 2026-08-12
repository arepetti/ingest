# Translator context

`en-US.json` in this folder is **not a locale catalog**. It is a sidecar that documents every string
in `../en-US.json`: what the string means, where it is rendered, and what a translator has to know to
get it right without reading the codebase.

It exists because JSON has no comments, and because notes embedded in the catalogs themselves would
break the "copy `en-US.json` and translate the values" contract that all six shipped locales rely on.
Nothing imports this file at runtime; only the tests and the scripts in `../../../scripts/` read it.

## Shape

A flat map keyed by the dotted translation key, in the same order as the catalog.

```json
{
  "settings.common.saving": {
    "en": "Saving…",
    "ui": "buttonProgress",
    "context": "Replaces the Save caption while a settings write is in flight. 'Saving' means writing to storage, never the financial sense of saving money. Must fit the same button as the resting caption."
  },
  "accounts.audit.by": {
    "en": " · by {{name}}",
    "ui": "fragment",
    "context": "Appended directly after a rendered date to attribute an action to a person. 'by' means performed by.",
    "placeholders": { "name": "Display name of the account that performed the action, e.g. A. Rossi" },
    "joins": {
      "after": "A LocalizedTime element in ServicesPage.tsx",
      "example": "11 August 2026, 14:32 · by A. Rossi",
      "note": "Keeps its leading space. CJK may drop it only when the first character is full-width."
    }
  }
}
```

| Field | Required | Meaning |
| --- | --- | --- |
| `en` | yes | The English the note was written against. Do not hand-edit; the tooling maintains it. |
| `ui` | yes | Rendering surface, from the closed vocabulary in `ui-surfaces.json`. |
| `context` | yes | Prose for the translator. |
| `placeholders` | when the string interpolates | One entry per `{{name}}`, describing the value that lands there. |
| `joins` | when `ui` is `fragment` | How the string is concatenated. `example` is mandatory for fragments. |

## What makes a useful note

Write for someone fluent in the target language who has never seen the product. Cover, in one to
three sentences:

- **Which sense of an ambiguous word applies.** This is the single highest-value thing a note does.
  *Save* (store, not economise), *Enable* (imperative, not infinitive), *Current* (present, not
  electrical), *Key* (credential, not keyboard), *Field* (data field), *Run* (execute, and as a noun),
  *Period* (reporting window), *Close* (finish a period, or dismiss a dialog).
- **What the user is looking at when they read it**, if `ui` does not already say so.
- **Constraints**: fits inside a badge, replaces a button caption, appears in a narrow column.
- **Product meaning of domain terms**: schema, submission, service, period, reviewer, ingest.
- **Anything a literal translation would get wrong** — imperative versus infinitive, second person
  versus impersonal, the fact that a noun here is a UI object rather than an action.

Do not restate the English, name the file it lives in, or explain the obvious.

## Working on the notes

```bash
npm run i18n:scaffold                       # sync the sidecar with the catalog
npm run i18n:kit -- --area settings         # authoring view: en, ui, call sites, placeholders
npm run i18n:kit -- --area settings --locale it-IT   # review view: adds the current translation
npm run i18n:apply -- scripts/context-parts/settings.json
```

Authored notes are folded in from flat part files under `scripts/context-parts/` (gitignored), which
keeps concurrent authoring off a single 2000-key file. `i18n:apply` refuses a note with an empty
`context`, an unknown `ui`, or an undocumented placeholder.

## The staleness guard

Each note stores the English it was written against. `src/i18n/context.test.ts` fails when that no
longer matches the catalog, so changing an English string forces its note to be revisited in the same
commit — the same discipline the placeholder-parity check already applies to translations. Re-apply
the note through `i18n:apply` to clear the flag; editing `en` on its own defeats the point.
