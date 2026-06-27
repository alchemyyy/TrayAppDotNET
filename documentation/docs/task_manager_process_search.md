# Task Manager process search

The Processes page search box accepts plain text or column expressions. Searches are
case-insensitive.

## Lifetime

The **Lifetime** column shows how long a process has existed and updates while it runs:

- Less than one day: `16:12:01`
- One day or more: `1d 8:10:22`

Lifetime expressions accept `ms`, `s`, `m` or `min`, `h`, and `d`, or the displayed
clock format.

## Plain-text search

Plain text searches for a partial match in **Name** or **PID**. For example, `chrome`
matches a process name, while `4242` matches any PID containing those digits. Regex
characters are treated literally in this mode.

## Column expressions

Use `{Column name} operator value`. Typing inside braces opens ranked column
suggestions. Click a suggestion, use Up/Down and Enter, or press Tab to complete it.
Moving the caret back inside existing braces reopens the menu. Column nicknames are
also accepted.

| Syntax | Meaning |
| --- | --- |
| `=`, `!=` | Equal or not equal |
| `<`, `<=`, `>`, `>=` | Numeric comparison |
| `=~`, `!~` | Matches or does not match a case-insensitive .NET regex |
| `&&`, `\|\|` | Boolean AND and OR |
| `( ... )` | Explicit grouping |

`&&` takes precedence over `||`. Spaces around operators are optional. Quote values
that contain expression syntax, especially regex groups or alternation.

```text
{Lifetime}>=1h&&{Lifetime}<2h
{Status}="Running"&&{CPU}>10%
{Command line}=~"--type=(renderer|gpu-process)"
chrome&&({Status}=Running||{Status}=Suspended)
```

A bare section such as `chrome` inside an expression retains the default Name/PID
partial-match behavior.

Memory and byte columns accept binary `k`, `m`, `g`, and `t` suffixes. Other numeric
columns use decimal suffixes, and percentage columns accept `%`.
