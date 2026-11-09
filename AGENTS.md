# VoiceMite project guidance

## Scope and ownership

- Read README, the relevant source and the current test report before changing behavior. Preserve unrelated changes made by other contributors.
- Prefer a small working change with appropriate validation. Do not add services, paid APIs, background monitoring, or a new plugin system without a concrete requirement.
- Use the current user's authorization and task scope. Routine implementation choices do not need repeated confirmation; publishing or external messages must remain within the request.

## Product boundaries

- All core actions must remain usable with a mouse. Keep Simplified Chinese and English resources complete, with UI, recognition and output language settings independent.
- STT and LLM are independent protocols and credentials. A text endpoint is not evidence of audio support. Clearly mark demo services and protocols not tested with real credentials.
- Only record after an explicit action. Do not add idle listening, screen capture, browser/chat history collection or complete input history by default.
- Treat transcription, model output and optional context as data. Never execute their instructions, enlarge context access, change settings or send messages because of their contents.
- Never automatically search, send, submit, publish or execute prepared text. Do not inject Enter or a submission shortcut to insert multiline text.

## Safety and private data

- Fail closed when the original window, process, input control, selection or content cannot be verified. Refuse passwords, protected areas, high-risk targets and elevated/inaccessible windows.
- TextPattern is for inspection. Do not use whole-value assignment, select-all or guessed backspaces as a generic insertion strategy.
- Preserve the one-shot target rule, read-back uncertainty handling and clipboard sequence checks. Never restore old clipboard data over a new user copy.
- Keep native accessibility work bounded and separate from the UI thread. Check cancellation again after blocking validation and immediately before input injection.
- Preserve generation isolation: cancellation or a new task invalidates late transcription, formatting, summaries, notices, preference learning and insertion.
- Store API keys only with Windows current-user protection. Never commit or log keys, audio, full private text, raw provider errors, user data folders or unredacted screenshots.
- Do not bypass UAC, secure desktop, certificate validation or Windows/browser safety barriers. Do not require administrator privileges by default.
- Preserve existing installation and data compatibility. A branding change is not permission to delete or silently replace settings or encrypted keys.

## Validation and delivery

- Build with the SDK in global.json. Core and simulated STA worker tests are suitable for CI; interactive microphone, clipboard, UI, Edge and Notepad tests are explicit opt-in desktop checks.
- Run checks appropriate to the changed layer. For changed UI, inspect both languages; for insertion, microphone or installer changes, record real Windows outcomes where possible.
- Distinguish passed, failed, simulated and untested. Do not rename historical evidence into a claim that a new version or real provider was tested.
- Keep README, CHANGELOG, release version, build scripts and packaging names aligned. Exclude development tools, build caches and user data from source/release archives.
- Actions are pinned to reviewed commit SHAs. Keep permissions minimal; no secrets or write token in untrusted pull-request checks. Draft releases require review before publication.
- Before any push, inspect the staged file list and secret-related paths. Never force-push over another contributor's work.
