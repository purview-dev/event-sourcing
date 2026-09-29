### New Rules

| Rule ID | Category | Severity | Notes |
|---------|----------|----------|-------|
| EVENTSTOREEF001 | EntityFrameworkCore | Error | Dictionary-like EF complex properties must be marked opaque or remodelled as entry collections |
| EVENTSTOREEF002 | EntityFrameworkCore | Error | Opaque properties cannot be used in generated EF snapshot query expressions |
| EVENTSTOREEF003 | EntityFrameworkCore | Error | EF snapshot complex members must expose a constructor EF can bind (parameterless, or scalar parameters only) |
| EVENTSTOREEF004 | EntityFrameworkCore | Error | Value-type EF snapshot members must be settable (no read-only member on a struct) |
