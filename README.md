# Snail.MCP.BuildBridge

Builds, tests and Docker work of a working copy on another Mac over ssh, with the results brought back: the MacBook
stays the place code is written, the host does the heavy work. Nothing of the server's is installed on the host — ssh,
rsync and the toolchains are all it needs — and everything the server makes there lives under one root,
`~/.s3l-build`, which can be erased whole.

Three ideas carry it:

- **The host holds no original.** A workspace on the host is a copy of this machine's working copy, its outputs and
  caches rebuild themselves, a job's log was already answered. So nothing on the host needs keeping, a sweep erases,
  and the root can go at any time.
- **A delivery, not a push.** The working copy is mirrored as it stands, uncommitted changes included, with its `.git`
  and without what git ignores or a build makes again. Only what changed goes over the wire; build outputs already on
  the host stay, so a build there is incremental.
- **Look, decide, act.** A sweep is planned and shown first under a `planId`, kept ten minutes, and only that plan is
  carried out. What the server did not make, it does not touch.

Its tools: [docs/reference/tools](docs/reference/tools/index.md), written from the server itself. Built on
[Snail.Sdk](https://github.com/orldev/Snail.Sdk): the rules, the dotnet tool package, publishing into `~/.s3l-mcp`, the
shared workflows.

```bash
dotnet publish src -c Release -r osx-arm64 -p:SnailMcpInstall=true    # ~/.s3l-mcp/Snail.MCP.BuildBridge/current
```

## Tools

| Tool | What it does |
| --- | --- |
| `build_hosts` | The hosts and what each can do right now: reachable, its toolchains with versions, its room. Changes nothing. |
| `build_provision` | Makes a host ready as a job: the root and its folders, the SDK and workloads under the root, the feeds; names what a person installs — Xcode, OrbStack, sleep. |
| `build_run` | Delivers the copy and runs `dotnet`, `xcodebuild` or `docker` in its workspace as a job; answers the exit code, what the tests said, the log. |
| `build_logs` | The end of a run's log on this machine, while it runs or after. |
| `build_fetch` | Named paths of a workspace brought back: test results, a simulator bundle, a package. |
| `build_cancel` | Stops a running job, on the host too. |
| `build_sync` | A delivery on its own, or with `dryRun` what one would send. |
| `build_clean` | A sweep of a scope, planned and shown first, carried out by its `planId`. |
| `build_diagnose` | The server's state: version, hosts, the programs it works through, its folders. |

Every answer is JSON with an `ok` flag; a failure carries `error` and a `hint`, and is flagged `isError`. A job answers
within its `wait` with the result, or with the running job, whose resource `job://buildbridge/<kind>/<id>` carries the
result once it ends.

## A run

1. The copy a path lies in is read as git sees it: root, repository, branch, commit, whether it is a linked worktree.
2. The host is probed: reachable, and with the toolchain the kind needs — `dotnet`, `xcode` or `docker`.
3. When the host has less room than the settings allow, the caches are swept first.
4. The copy is mirrored into `~/.s3l-build/workspaces/<repository>/<branch>` with rsync: `.git` goes along, what git
   ignores and `bin`, `obj`, `artifacts`, `TestResults`, `node_modules` do not; a tracked file deleted here is deleted
   there; outputs already there stay. A linked worktree goes without its `.git`, which would name nothing on the host.
   A marker, `.s3l-build.json`, is what proves the workspace the server's. The feeds the settings name are mirrored
   under `feeds/`.
5. The command runs in the workspace, its output streamed to a log under `~/.s3l-mcp-buildbridge/jobs/`, which
   `build_logs` reads. One workspace takes one run at a time; a second waits its turn. A run stopped from this side —
   cancelled, or past its time — is stopped on the host too.
   - `xcodebuild` keeps its derived data under the root, and a destination naming a simulator by name gets a clone of
     it for the run alone, `snail-build-<word>`, deleted when the run ends.
   - `docker build` and `docker run` carry the label `snail.owner=buildbridge/<repository>/<branch>`, which
     [Snail.MCP.CleanUp](../Snail.MCP.CleanUp) reads; a compose stand is the project `s3l-<repository>-<branch>`.
6. The log is read for what the tests said, in the words of VSTest or of the testing platform.
7. The repository's workspaces not built for 14 days, and derived data of workspaces that are gone, are swept.

## The host

A Mac with Remote Login on and this machine's key in its `authorized_keys`; `ssh <address>` from a terminal shows
what is wrong when a probe does not reach it. On it:

- Xcode of the same version as here, opened once, its licence accepted — for anything under Apple;
- OrbStack, started — for anything under Docker;
- a user logged in at the console, and the machine kept awake: simulators need the session, and a sleeping host takes
  no builds;
- nothing else: `build_provision` installs the .NET SDK the settings name under `~/.s3l-build/dotnet` with its
  workloads, keeps packages under `~/.s3l-build/nuget`, and adds the mirrored feeds to the host's NuGet sources.

A host that is this machine — `localhost` — is worked without ssh, straight into a shell: what the tests use.

## Settings

`~/.config/s3l-mcp-buildbridge/config.json`, under the `SNAIL_MCP_BUILDBRIDGE_` variables, which win:

```json
{
  "Hosts": [
    { "Name": "mini", "Address": "orldev@mini.local" }
  ],
  "SdkVersion": "10.0.302",
  "Workloads": ["maui-ios", "maui-maccatalyst", "maui-android", "wasm-tools"],
  "Feeds": [
    { "Name": "LocalPackages", "Path": "/Users/orldev/Projects/nuget" }
  ]
}
```

| Setting | Default | What it is |
| --- | --- | --- |
| `Hosts` | none | Each a `Name`, an `Address` ssh is given, and a `Root` (`~/.s3l-build`); the first is the one a call that names none gets. |
| `SdkVersion` | `10.0.302` | The .NET SDK provisioning installs under the root; empty leaves the host's own dotnet. |
| `Workloads` | the MAUI set | The workloads that SDK gets. |
| `Feeds` | none | NuGet feeds of this machine mirrored to the host and added to its sources by name. |
| `Excludes` | `bin obj artifacts TestResults BenchmarkDotNet.Artifacts node_modules .DS_Store` | Folders a delivery never sends. |
| `TransferSeconds`, `RunSeconds`, `ProbeSeconds` | 600, 3600, 20 | How long a delivery, a run, a probe may take. |
| `WorkspaceDays`, `JobDays`, `ImageDays`, `BuildCacheHours` | 14, 30, 7, 24 | The ages a sweep goes by. |
| `LowDiskGigabytes` | 20 | Free gigabytes on a host below which a run sweeps the caches first. |

## What is swept, and when

| What | Proof it is the server's | Goes when |
| --- | --- | --- |
| a workspace | its marker | not built for `WorkspaceDays`, after a run of its repository; its scope asked for; `caches`, `everything` |
| derived data | named for a workspace | its workspace is gone; `everything` |
| images | the label `snail.owner` | older than `ImageDays` in `caches`; all in `everything` |
| the build cache | shared | entries not used for `BuildCacheHours` in `caches`; all in `everything` |
| the packages under the root | under the root | `everything` only |
| logs on this machine | under the server's data folder | older than `JobDays` |

The SDK under the root stays: a sweep never costs a download. To start over, erase `~/.s3l-build` and provision again.

## Run it

Add it to an MCP client — Claude Code's `~/.claude.json`:

```json
{
  "mcpServers": {
    "s3l-mcp-buildbridge": { "command": "/Users/<you>/.s3l-mcp/Snail.MCP.BuildBridge/current/Snail.MCP.BuildBridge" }
  }
}
```

or install the tool — `dotnet tool install -g Snail.MCP.BuildBridge`, or `dnx Snail.MCP.BuildBridge --yes` without
installing — and use `s3l-mcp-buildbridge` as the command: on macOS and Linux it is the native program, elsewhere the
framework-dependent one.
