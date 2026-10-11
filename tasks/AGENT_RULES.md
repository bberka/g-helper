# Rules for agents working on the G-Helper cross-platform port

Read `tasks/plan.md` first. The legacy WinForms app in `app/` is the behavioural reference: **read it, port from it, never edit it**.
Everything new goes under `src/`, `tests/`, `packaging/`.

## Ground rules
1. Work only in the paths your task owns (listed in `tasks/todo.md`). Other tasks run in parallel in other worktrees;
   touching their paths guarantees merge conflicts. If you need a change in a file you do not own (e.g. a contract in
   `src/GHelper.Core/Platform/*`), make the change **additively** (new members only, never rename/remove/re-sign) and list it
   under "Contract changes" in your final report.
2. Preserve legacy class names, namespaces, public signatures and behaviour when moving code into `GHelper.Core`
   (`GHelper.Mode.PowerNative`, `GHelper.Helpers.AsusService`, `Program.acpi`, `AppConfig.Is(...)`, ...). Windows-only
   bodies are replaced by delegation to `PlatformServices.Current.<Service>`.
3. Core (`GHelper.Core`) must not reference WinForms / System.Drawing.Common bitmaps / WMI / registry / P/Invoke / Avalonia.
   `System.Drawing.Color/Point/Size/Rectangle` primitives are fine. Use `GHelper.Input.Keys`, not WinForms Keys.
   Replace `Program.settingsForm.X()` / `toast` / `MessageBox` / `Invoke` with `Program.Ui` (`IUiHost`), or events.
4. Missing types owned by other parallel tasks: add the minimum stub in `src/GHelper.Core/_Stubs/<yourtask>.cs`
   (namespace + signature the real type will have, `// STUB(<owner-task>)`). The integrator deletes `_Stubs/` after merging.
   Never put real logic in a stub. Never create stubs for types in your own area.
5. Keep git history readable: small commits on your worktree branch, conventional messages
   (`feat(core): ...`). Do not rebase onto/merge main. Do not push.
6. Verify before reporting: `dotnet build` of every project you touched must be green, your tests must pass.
   Say honestly what you could not verify (no ASUS hardware / no Linux box available).
7. No secrets, no network calls beyond what legacy code did (GitHub releases, ASUS driver API).
8. Code style: match surrounding code (4 spaces, C# latest, `Nullable=annotations`, file-scoped or block namespaces as in the
   legacy file you port). Comments only where behaviour is non obvious. No emojis.

## Final report format (keep under 60 lines)
- Done: bullet list of what landed (files/dirs).
- Contract changes: each added interface member / new file outside your paths.
- Stubs created: list of stub types and who owns the real one.
- Not done / known gaps / unverified.
- Build + test commands you ran and their result.
