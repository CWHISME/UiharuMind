# UiharuMind Help

UiharuMind is a cross-platform desktop AI assistant. It runs GGUF models locally on llama.cpp and can also call
any OpenAI-compatible online service. On top of chatting it adds agents that work in a folder, group chats where
several characters talk to each other, and a set of quick tools (screenshots, OCR, translation, clipboard history,
auto click) that stay one shortcut away.

This page is the in-app manual. The main window has four pages — **Conversation**, **Character**, **Model** and
**Log** — and one **Settings** window, which is described page by page further down.

## Getting started

1. Open **Model** and add at least one model.
2. For an online model: **Add remote model**, pick a provider preset (or custom), then fill in the endpoint, the
   model ID and your API key.
3. For a local model: open **Settings → Local Models (Text)** and download or select a llama.cpp runtime first,
   then put the GGUF file into the local models folder (**Model → folder icon**). The list rescans automatically.
4. Go back to **Conversation**, choose the model at the top (or leave it on the default), pick a character on the
   right and start typing.

Nothing else is required. If the list stays empty, see **Troubleshooting** at the end.

### Where your data lives

Everything is stored under `~/.uiharu` (the `UIHARU_HOME` environment variable overrides this). Configuration,
sessions, characters, knowledge bases, world books, skills, clipboard history, logs, and agent workspaces live
there; model files and engine packages live under `External/`. The tray menu has **Open Data Directory**, and the
Log page has **Open log folder**.

## Models

The **Model** page has four tabs: **Chat models**, **Image models**, **Voice models** and **Get models**.

Each entry in the list shows its state. Local models can be running or loading; remote models are marked **API**,
and models that accept images are marked **Vision**. Right-click an entry for **Favorite**, **Runtime parameters…**,
**Show in folder**, **Model info** or delete. The folder button rescans the local model folder.

### Local models (GGUF)

Local models run through llama.cpp's `llama-server`. The runtime is a separate download:

- **Settings → Local Models (Text)** holds the engine version, the update channel (**Stable** / **Preview**) and the
  load stall timeout; each package in the download list carries a red / amber / green startup-risk dot.
- **Model → Runtime parameters…** adjusts one model only; anything you leave alone follows the global values.
  It covers context size, GPU layers, batch and physical batch, CPU threads, KV cache type, and the estimated
  memory use with those values.
- **Settings → Local Models (Text)** also holds the global parameters: fit to memory, prompt threads, parallel
  slots, continuous batching, Flash Attention, KV cache types, MoE layers on CPU, keeping the KV cache in RAM,
  prompt cache limit and reuse chunk, context shift, full SWA cache, load mode, environment variables and extra
  arguments.

Before a heavier model loads, the app estimates its memory use and warns you if the startup risk is high. Native
out-of-memory errors can make the app unresponsive, so read that dialog rather than clicking through it.

### Remote models

**Add remote model** asks for a display name, the full request endpoint (for example
`https://api.example.com/v1/chat/completions`), the model ID sent in the request, and the API key. Advanced fields:

| Field | What it is for |
| --- | --- |
| Vision support | The model accepts images. Required for image questions. |
| Context length | The context window in tokens; used for the occupancy readout. |
| Thinking mode | Thinking effort sent with each request; **Default** follows the model. |
| Max output tokens | Output budget; empty uses the default for the chosen thinking mode. |
| Reasoning content round-trip | Some thinking models need the reasoning text echoed back with tool calls. **Follow preset** resolves it per model. |
| Omit sampling parameters | For models that reject `temperature`/`top_p`/penalties. |

Keys are stored encrypted on this device. **Copy from an existing model** reuses another model's key and settings.

### Voice models

**Voice models** lists local models for read-aloud (speech synthesis) and dictation (speech recognition), installed
and downloaded from **Get models → Voice models**. The engine itself is **Settings → Local Models (Voice)**
(audio.cpp). You can pick a default synthesis model and a default recognition model. Without them, read-aloud and
hold-to-talk do nothing.

### Image models

**Image models** is an ordered fallback list used by agents for image generation and editing. An entry can be a
local stable-diffusion.cpp model or an online provider. Models are tried from top to bottom; the next one is used
only when the current one is unavailable (not configured, unreachable, rate limited, or a server error). A prompt
that gets rejected never moves on to the next model. **Editing** models accept input images; models without it are
skipped when the agent passes input images. **Try it** runs one generation.

### Embedding model

Knowledge-base retrieval needs an embedding model, which by default is the first embedding GGUF found in the model
folder, run through the same local llama.cpp runtime as chat models. Download one from **Get models** if the folder
has none, and keep the runtime installed. The advanced fields (context, batch, physical batch, GPU layers) are part
of the embedding settings and usually stay on their defaults.

### Get models

Search by name, or paste `owner/repo` or a link. The left column lists repositories, the right column lists the
GGUF quantizations of the selected one — pick a quantization, then **Download**; the queue in the same tab shows
progress and supports pause, continue, retry and cancel. **Recommended** marks the usual choice, and the memory
estimate is shown next to it. If the repository has vision files, the vision projector can be downloaded along with
the model. Gated repositories need a HuggingFace token (see **Download Sources**). If the llama.cpp runtime is not
installed yet, the engine package is queued before the model.

### Local engine usage

The memory chip in the title bar of the **Model** page — it also sits next to the model picker in a conversation —
is the machine snapshot the load-risk warnings are based on: free memory, VRAM and CPU. Click it for **This machine**
and the list of **Local models**: every local model process that is running or loading, its memory use, and a
**Stop** button that gives the memory back. Remote models never show up here.

## Conversations

**New session** on the left starts a conversation. The right pane lets you pick the character that speaks in it
(and shows the context occupancy). The switcher next to the title bar toggles between **Chat** (a character) and
**Agent** (a character with tools and a workspace).

- **Model**: the title bar model is the default. A session can override it — the picker then shows a **Session**
  badge, and **Default (follow global)** clears it again.
- **Composer**: `Ctrl/⌘+Enter` sends, `Enter` inserts a newline, and files or images can be dragged onto the window.
  In agent sessions `Tab` switches between **Build** and **Plan**.
- **While it answers**: **Stop** interrupts, and typing something and sending it queues an **interjection** that is
  read at the next safe point (withdraw it before then if you change your mind).
- **Message actions**: copy, **View full text** (opens the long-text window), branch, delete, and regenerate the
  last reply.
- **Search**: `Ctrl/⌘+F` searches inside the current conversation, optionally including thinking and tool calls.
  The search box above the session list searches session titles and message contents.
- **Thinking and Markdown**: **Auto-collapse thinking** folds reasoning when a segment finishes; the **Plaintext**
  option shows raw Markdown instead of a rendered view.
- **Read aloud**: the speaker button reads new replies of the open conversation with the character's voice; the
  switch is saved on the character.
- **Extensions approved**: **Don't ask for extensions (this session)** lets extension commands that normally ask
  first run without asking in this session. Handing a browser or a window back to you, and opening local or
  intranet addresses, still ask every time.
- **Context panel**: shows this turn and session totals, cached input, and the compaction watermarks — **Fold tool
  results**, **Handoff**, **Drop oldest**. When history reaches the handoff watermark the app writes a context
  handoff document and the model can no longer see anything before it; if that fails, the oldest messages are
  dropped instead.
- **Long text**: messages and tool results that are too long to read in place open in a separate text window with
  line numbers, search, word wrap, Markdown preview and syntax highlighting. It is also the **Text editor** entry
  in the quick menu.

### Knowledge base per conversation

A session can be bound to a knowledge base (the same library you build for characters, see **Knowledge library**).
The memory panel next to the composer shows the bound base and its index state, and opens the picker/editor.
Retrieval uses the index, so after changing sources you must **Update knowledge index**; until then chats keep
using the previous index. With the knowledge tool enabled the model searches on demand; otherwise relevant
snippets are injected automatically each turn.

### Session list

Sessions are grouped by kind and can be searched, renamed, deleted, or selected in **Batch** mode (running sessions
are skipped). A session that is running in another open UiharuMind shows a notice instead of silently diverging.

## Agents

An agent is a character with tools. Turn on **Enable agent capabilities** in the character editor; its
conversations then move to the **Agent** side, and the character stops sending a greeting.

- **Workspace**: every agent session works inside a folder. **Workspace → Change…** picks it; recent workspaces are
  listed. Files the agent creates or changes are in **Artifacts**/**Session artifacts**, with drafts kept apart in
  the workspace's draft room so scratch output does not pollute the project.
- **Permission mode**: **Read-only** (no modifying operations), **Auto edit** (file edits run automatically, risky
  commands still ask), **Full auto** (nothing asks). **Plan** mode is a separate toggle: the agent plans first and
  does not execute until you switch back to **Build**. Tool switches, skills, extensions and sub-agents are
  configured per character, in the character editor.
- **Approvals**: when a tool needs permission, a card appears with **Allow once**, **Always allow (session)**,
  **Allow similar commands (session)** and **Deny**. File writes always ask (outside the workspace especially),
  shell commands ask per command, and extension calls that can change something or cost money ask by default.
- **Tools**: file operations (Read, Write, Edit, Glob, Grep), shell execution, web search and page fetching,
  knowledge search, image understanding, image generation, speech, task list, scheduled tasks, background tasks,
  and delegation.
- **Extensions** (browser, desktop, image, audio, plus MCP servers) are kept out of the tool list. The model looks
  one up when it needs it — see **Extensions, MCP and Python**.
- **Sub-agents**: the agent can hand a task to another agent and only gets the report back, so the raw material
  never enters the main context. Sub-sessions are real sessions: open one from the tool card or the
  **Sub-agents** panel, keep talking to it, and it is saved with the session. A sub-agent's capabilities are the
  intersection of its own and its parent's — never wider.
- **Task list**: the sidebar shows the agent's todos. Turning this capability off hides the panel.
- **Scheduled tasks**: create a task that runs later without a window open. Only pre-authorized shell commands can
  run in that mode; the sidebar lists pending, completed, failed and missed runs, and each run opens as a session.
- **Goal mode**: give the session a goal and a wake schedule (**right after each stop**, **every N minutes**,
  **daily at**, **once at**). The model keeps working after each turn and can end by itself or call `goal.end` when
  it needs you. No progress only slows the wake (backoff) — it never ends the mode. **Auto-approve approvals**
  makes wake turns run unattended; it is high risk and asks for an explicit confirmation first.
- **Background tasks**: long commands started with **RunBackgroundTask** show a status row in the conversation and
  can be stopped from there.

## Characters and role-play

The **Character** page lists every character with filters for **Kind** (Chat/Agent), **Origin** (Built-in/Mine) and
**Favorites only**. Import and copy are in the list's context menu; on a character you can **Start chat** and pick
the opening line when it has alternate greetings.

The editor has five tabs:

| Tab | Contents |
| --- | --- |
| Basic | Name, source work, description, system prompt, user-card injection, and the agent switch. |
| Agent abilities | Tools and capabilities, skills, extensions (MCP) and delegatable sub-agents, each with its rough per-turn token cost. Only shown for agents. |
| Greetings | First greeting plus alternate greetings. |
| World setting | The world book attached to this character. |
| Advanced | Depth prompt (text inserted N messages from the end, with role), sampling parameters, and **Omit sampling parameters** for remote models. |

**User card** is a character kind of its own; **Inject user card** appends it to the system prompt so characters
know who you are, and edits stay live for every character that has it on.

### World books

World books are shared between characters. An editor entry has keywords, content, **Always on**, **Position**
(before/after the character), **Order** and the book's **Token budget per turn**. Entries are activated by their
keywords within that budget, so a character only pays for the setting that is relevant.

### Knowledge library

A character can carry a **Character Knowledge Library** — curated notes plus imported plain-text files, indexed
into vectors and recalled during the conversation as private context. The library view shows the index state
(**Not indexed**, **Update required**, **Index health**, last indexed), and updating builds a new index while the
previous one stays usable. If the embedding model changes dimension, rebuild the index. Notes are meant to be
factual: identity, relationships, events, world rules.

### Voice

A character can have a read-aloud voice and **Read new replies aloud** enabled, in which case every new reply in the
open conversation is spoken. Queued replies are read in order and stopping skips the rest.

## Group chats and away mode

A group is a conversation with several characters. **New group chat** asks for:

- **Type**: **Agent group** (both kinds may join, members work in a workspace) or **Chat group** (chat characters
  only, no workspace).
- **Members and speaking order**: the order you tick them is the order they speak.
- **Scheduling**: **Serial** (members take turns in order, one round) or **Parallel** (woken members speak at the
  same time, each message reaches whoever is still speaking).
- **Host**: in parallel mode a message without `@` wakes only the host, who calls on the right members.
- **Stop policy**: **Conservative** (a member called on by another member cannot call on anyone else, so the chat
  settles by itself) or **Aggressive** (members can keep calling each other; once it quiets down, anyone who missed
  part of the chat gets one more turn).
- **Earlier messages**: for a member joining later — **None**, **Summary** (written by the host or latest speaker),
  or **All** (delivered at once next round, up to a message cap).
- **Workspace and permission mode** (agent groups): members follow the group unless you change it per member.

While the group runs: `@name` calls on someone, **Another round** continues a serial group, and approvals appear
above the group input box (unanswered after ten minutes they are denied automatically). **Members** opens each
member's own session read-only, shows **Private chat** for talking to one member alone, and shows each member's
token total. **Group artifacts** collects drafts and workspace files the group produced. Members that left keep
their session under **Left** and get it back, memories intact, when re-added. Renaming the group, switching the
host, or changing the roster rewrites every member's system prompt, so the next round is recomputed at full price
and prefix caching resumes afterwards.

### Away mode

**Go away** puts your avatar in charge of an agent group. You give it a message and optional notes that only the
avatar can see, and at the end of each wave it decides for you and either keeps things moving or stops. The guards
in **Avatar settings** end it automatically: maximum avatar turns, maximum away hours, end after N waves without
new output, and how long after a stop the avatar wakes.

**Infinite mode (manual stop only)** turns all guards off — only you can end it, so make the goal explicit. While
away you can **Wake now** or **Update** the message; **End away** stops the avatar without touching a wave that is
already running. The receipt afterwards says why it ended: goal reached, needs your decision, ended by you, time
limit, avatar turn limit, or no new output for many waves. The avatar never approves writes outside the workspace for
you — that one is denied on the spot and recorded in the receipt.

## Quick tools

Global shortcuts (all rebindable in **Settings → Shortcuts**):

| Action | Default |
| --- | --- |
| Screen capture | `Alt+Shift+Z` |
| Quick menu (quick ask) | `Alt+Shift+A` |
| Clipboard history | `Alt+Shift+S` |
| Quick translation | `Alt+Shift+Q` |
| Auto click | `Alt+Shift+G` |
| Hold to talk | Right Option on macOS, right Ctrl elsewhere |

### Screen capture

The capture tool dims the screen and lets you drag a selection. Inside the selection: drag to move, drag an
edge/corner to resize, `Enter` to capture, `Esc` to cancel. The magnifier shows the pixel colour — hold `Shift` to
switch RGB/HEX, press `Ctrl/⌘+C` to copy it. On the captured shot the dock offers:

- **Select Text** — pick text out of the image by dragging.
- **OCR** — extract the text.
- **Explain** — ask the model about the image.
- **Edit** — annotate (the toolbar has save and cancel).
- **Copy** / **Save**.

Captures go to the quick tools; text results can be sent on to the model. On macOS both screen recording and
accessibility permission are needed; after reinstalling, remove the stale entry in System Settings and grant again.

### Copy popup and quick menu

After you copy text, a small button appears at the cursor. Its main action is **Explain**; hovering it opens
**Translation**, **Syntactic Analysis**, **Think** and **Ask**. Results open in a small window that can show raw
Markdown and can be turned into a normal session with **ConvertToChat**.

The **Quick menu** shortcut opens the wheel: **File Search** (search files and folders, paste a path to jump to it),
**Ask** (a small window for a one-off question — opened from the copy popup it carries the copied text as a quote),
and **Text Editor** (the long-text window).

### Clipboard history

The quick window lists recent copies; click one to put it back on the clipboard, or right-click for **Favorite**,
**Delete** and **Clear All**. There is a search box and an entry count. **Settings → General** has
**Clipboard history auto-clean (days)**: `0` never cleans, and favorites are always kept.

### Quick translation

The translation window takes a target language, an optional character (style), an optional extra request, and the
text; it translates with the current model or a configured remote model. Previous results are in the tray menu
under **Translation**. Which model text quick tools use is set in **Settings → Quick tools**.

### Auto click

The auto click window records and replays mouse, keyboard and wheel actions. Sessions are saved, searchable,
renameable, duplicateable. The timeline holds steps (left/right click, key, text, delay, wheel, loop) which you can
reorder, duplicate or remove, and the inspector edits the selected step's parameters (position, delay, duration,
loop count, text, keys, wheel delta). Playback settings: **Repeat** (`0` = until stopped), **Interval**, **Speed**.
**Advanced** adds mouse-path recording (frame rate, only while a button is pressed) — it is experimental and off by
default because raw cursor paths replay unevenly. Press the auto click shortcut again to stop; the window also
shows the stop key.

On Windows, input simulation into fullscreen or administrator games needs the app at the same privilege level —
**Settings → General → Fullscreen game input support** requests administrator rights at startup for exactly this.

### Dictation

Hold the dictation key (default right Option / right Ctrl) while the window is in front and talk; release and the
recognised text goes into the input box. Presses shorter than 0.3 s are ignored. **Use in other apps** lets you hold
the key while another app is in front and have the text pasted there — the clipboard is borrowed and restored, and
the entry does not enter clipboard history; on macOS this needs accessibility permission. Dictation needs a local
speech recognition model (**Voice models**).

## Extensions, MCP and Python

### Extensions

Extensions are capabilities that stay out of the tool list: the model looks one up when it needs it, and by default
every call that can change something or cost money asks you first. The built-ins are:

| Extension | Commands |
| --- | --- |
| `browser` | Take a snapshot, navigate, act (click/type/scroll), hand the browser to you |
| `desktop` | List windows, snapshot a window, act on it, launch an app, hand it to you |
| `image` | Generate or edit an image |
| `audio` | Synthesise speech, transcribe audio |

Handing a browser or a window to you and opening local or intranet addresses always ask, even with
**Don't ask for extensions** on. Each extension can be switched off per character.

### MCP servers

**Settings → Agent → MCP Servers** manages MCP servers. A server has a name, a transport (**stdio** with command,
arguments and environment variables, or **HTTP** with an endpoint and headers), a one-line description shown to the
model, and **Host this server** — hosting is global, while which agents may use its tools is set per character.
**Test connection** saves first, then connects and lists the tools. The config file uses the standard `mcpServers`
format, so a config from another MCP client can be pasted in; **Reload from file** picks up external edits.

A project can also declare its own servers in `.mcp.json` inside the workspace. Because that file is committed to
the repository, the first time a project wants to start its servers you get a confirmation listing the commands;
run and window-handoff calls still ask as usual. Approved projects are listed under **Projects allowed to start
their own MCP servers** and can be revoked.

### Skills

A skill is a folder with a `SKILL.md` (name and description, plus the body that is loaded on demand). Skill
packages live in the skills folder (**Open skills folder**); nesting is scanned at any depth, and a skill whose name
does not match its directory name is rejected. **Settings → Agent → Skills** manages the packages and can create a
new one; which skills an agent may use is chosen in that agent's editor. Skills can be invoked explicitly by typing
`/name` in an agent chat; **user-invoked** skills are only reachable that way. **Send skills to the model** keeps the
roster out of the prompt when you are short on context — the model then cannot reach skills on its own.

### Managed Python

**Settings → Agent → Managed Python environment** keeps your interpreter and the agent's environment separate: you
point at a host interpreter (or auto-detect one from `PATH`), the app creates a virtual environment, and the agent
runs Python through the shell tool and installs packages into that environment only. Your system Python is never
touched.

## Settings

Settings is one window with three groups on the left and a **Search settings** box at the top that jumps to the
matching row.

### General

**Application Update** shows the installed version and checks GitHub releases for updates; a matching package is
downloaded into the data folder and **Install** unpacks it. **User Interface** has **Language** (English,
简体中文; switching applies immediately to the main UI) and **Theme** (System / Light / Dark). **General Settings**
has **Clipboard history auto-clean (days)**, **Fullscreen game input support** (Windows only) and **Launch at
login**.

### Shortcuts

Rebinds screen capture, quick menu, clipboard history, quick translation and auto click — conflicts are refused —
plus **Voice input**: **Hold to talk** and **Use in other apps**.

### Quick tools

**Default model** for text quick tools (explain, translate, think, ask). Empty follows the title bar model.
**Default vision model** for OCR and image questions; empty follows automatic vision model selection.

### Agent

- **General**: default permission mode, the models used by general / brain / explorer sub-agents (empty follows the
  main agent model), and **Memory list size limit** — how much of the agent's memory index (file name and first line
  of each note) is handed to it. The global capability settings also live here:
  - **Model shell**: the interpreter the model actually calls when running commands through the Shell tool (Auto
    follows the system default, or pick Git Bash / PowerShell / a custom path).
  - **Managed Python environment**: the interpreter-is-yours, environment-is-ours setup (see above).
  - **Web search API keys** and **Search engines**: the fallback order with a **Test** button per engine.
- **MCP Servers**, **Skills**: see the sections above.
- **Avatar settings**: the runaway guards for away mode.
- The **image model list** lives on the Models page (**Image models**); it is no longer listed here.

### Local Models (Text) / (Voice) / (Image)

The three local engines: engine version and update channel, engine-specific parameters (see **Local models**),
plus the installed/downloadable model lists for voice and image. **Restore** resets this page's parameters — not the
engine version or the download sources.

### Download Sources

**Model source** applies to both searching and downloading in **Get models**: HuggingFace, hf-mirror (a HuggingFace
mirror), ModelScope, or a custom HuggingFace-compatible address. Optional **HuggingFace token** (needed for gated
repositories; also sent to the mirror and the custom address), **ModelScope token** (private repositories only) and
a **GitHub proxy prefix** for engine package downloads.

### Help and About

**Help** is this document, in your UI language when a translation exists. **About** shows the version, the
license, a short introduction, the font credit and a link list of the open-source projects the app is built on.

## Permissions, logs and troubleshooting

### Permission modes

| Mode | Behaviour |
| --- | --- |
| **Read-only** | No modifying operations at all: files can be read and searched, nothing written, no shell. |
| **Auto edit** | File edits run automatically; shell commands and risky calls still ask. |
| **Full auto** | Everything runs without prompts. |

**Plan** mode is orthogonal: the agent works out a plan and does not execute until you switch to **Build**. In
groups the permission mode follows the group unless overridden per member; scheduled task runs and goal-mode wake
turns use a restricted, pre-authorized set — goal mode can auto-approve everything, but warns you first. Every
approval and every command is recorded in the session.

### Log

The **Log** page streams application logs. **Level** sets the minimum recorded level (**All levels**, **Warning and
above**, **Errors only**) — it decides what gets recorded, not only what is shown; **Category** filters general logs,
model requests and model responses. You can export the current filter, clear the view, or open the log folder. After
rotation an old entry's body may be gone, which the page marks.

### Troubleshooting

**A model in the folder is not listed.** Check the file is a supported GGUF, the model folder is the one the app
scans (**Model → folder icon** shows it), and rescan. For local models the runtime must be installed first.

**The local engine will not start.** Open **Settings → Local Models (Text)**, install or update the engine, and
check the load stall timeout. The **Log** page shows the last error; high-risk loads may simply be out of memory.

**A remote model fails.** Verify the endpoint is the full request URL, the model ID exists, the key is valid, and
the account can call that model. Models with fixed sampling parameters need **Omit sampling parameters**.

**Images are rejected or ignored.** The model must have **Vision support**; otherwise image questions fall back to
a vision model if one exists, and an agent's vision tool can route around it.

**Screenshot or shortcuts do nothing on macOS.** Grant screen recording and accessibility in System Settings →
Privacy & Security. If an old entry is there but ineffective, remove it, restart the app and grant again. Running the
bare executable instead of the signed `.app` cannot see these grants.

**Knowledge retrieval returns nothing.** The index must be built after adding sources (**Update knowledge index**),
and an embedding model must be available — the local runtime plus an embedding GGUF in the model folder. A dimension
mismatch needs a rebuild.

**Hold-to-talk does nothing.** A local speech recognition model is required; check **Voice models** and the default
recognition model.

**A group member is stuck.** If it is waiting for approval, decide above the group input box; otherwise open its own
session from **Members** to see what it is doing.

**Input simulation does not reach a game.** On Windows, run UiharuMind at the same privilege level as the game, or
enable **Fullscreen game input support**.

**Two instances.** Multiple instances can share one profile; a session running elsewhere is read-only until it
finishes there. For scripts and screenshots, point `UIHARU_HOME` at a scratch folder so your real data is never
touched.
