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
| `build_provision` | Makes a host ready as a job: the root and its folders, the SDK and workloads under the root, the feeds, the images of the container kinds; names what a person installs — Xcode, OrbStack, sleep. |
| `build_run` | Delivers the copy and runs `dotnet`, `xcodebuild` or `docker` in its workspace as a job — or `dotnet` inside the host's Linux or Android image, or a script of the repository; answers the exit code, what the tests said, the log. |
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
2. The host is probed: reachable, and with the toolchain the kind needs — `dotnet`, `xcode`, or `docker` for the
   `docker`, `linux` and `android` kinds; a `script` asks for nothing in advance.
3. When the host has less room than the settings allow, the caches are swept first.
4. The copy is mirrored into `~/.s3l-build/workspaces/<repository>/<branch>` with rsync: `.git` goes along, what git
   ignores and `bin`, `obj`, `artifacts`, `TestResults`, `node_modules` do not; a tracked file deleted here is deleted
   there; outputs already there stay. A linked worktree goes without its `.git`, which would name nothing on the host.
   A marker, `.s3l-build.json`, is what proves the workspace the server's. The feeds the settings name are mirrored
   under `feeds/`. With `Sync` set to `mutagen`, a [Mutagen](https://mutagen.io) session keeps the workspace mirrored
   as the copy changes — one-way, with the same ignores — and a run only waits for it to catch up.
5. The command runs in the workspace, its output streamed to a log under `~/.s3l-mcp-buildbridge/jobs/`, which
   `build_logs` reads. One workspace takes one run at a time; a second waits its turn. A run stopped from this side —
   cancelled, or past its time — is stopped on the host too.
   - `xcodebuild` keeps its derived data under the root, and a destination naming a simulator by name gets a clone of
     it for the run alone, `snail-build-<word>`, deleted when the run ends. A project with a `project.yml` beside it is
     generated on the host by `xcodegen` first: a generated project is what git ignores, so it never travels, and the
     spec does.
   - `docker build` and `docker run` carry the label `snail.owner=buildbridge/<repository>/<branch>`, which
     [Snail.MCP.CleanUp](../Snail.MCP.CleanUp) reads; a compose stand is the project `s3l-<repository>-<branch>`.
   - the `linux` and `android` kinds run `dotnet` inside the host's `s3l-build-dotnet` or `s3l-build-android` image,
     with the workspace mounted at `/work`, the root's packages at `/root/.nuget/packages` and the feeds at `/feeds`,
     named by a NuGet configuration provisioning wrote under `containers/`. Linux targets, Native AOT for Linux and
     tests on Linux go to the first; an Android build to the second, which is amd64 — the Android SDK's tools come for
     that architecture — and so runs under emulation on an Apple Silicon host, slowly by design.
   - the `script` kind runs a script of the repository as it is — `["tools/native/build-xcframework.sh", "Debug"]` —
     through its own shebang, with the root's dotnet on its path: for what a build needs that no one program says,
     such as a core packed into a framework before an `xcode` run. It must be executable, as git keeps it, and lie
     inside the copy.
6. The log is read for what the tests said, in the words of VSTest or of the testing platform.
7. The repository's workspaces not built for 14 days, and derived data of workspaces that are gone, are swept.

## The host

A Mac with Remote Login on and this machine's key in its `authorized_keys`; `ssh <address>` from a terminal shows
what is wrong when a probe does not reach it. On it:

- Xcode of the same version as here, opened once, its licence accepted — for anything under Apple;
- OrbStack, started — for anything under Docker;
- a user logged in at the console, and the machine kept awake: simulators need the session, and a sleeping host takes
  no builds;
- Rosetta, on Apple Silicon: build tools still ship as x64 — `protoc` of Grpc.Tools among them — and installing it
  means agreeing to Apple's licence, which `build_provision` only points at: `softwareupdate --install-rosetta
  --agree-to-license`;
- nothing else: `build_provision` installs the .NET SDK the settings name under `~/.s3l-build/dotnet` with its
  workloads, keeps packages under `~/.s3l-build/nuget`, adds the mirrored feeds to the host's NuGet sources, and
  builds the images the settings name from the Dockerfiles under [deploy/images](deploy/images), which it writes under
  `~/.s3l-build/images` — once; an image already there is kept, and a sweep of stale images leaves them alone. Where
  Xcode is, it also puts [XcodeGen](https://github.com/yonaskolb/XcodeGen) under `~/.s3l-build/xcodegen`, from the
  release zip the settings name, checked against its SHA-256 — rather than Homebrew, which would want a password.

The account's login shell does not matter: every script runs in `/bin/sh`, with `/usr/local/bin`, `/opt/homebrew/bin`
and `~/.orbstack/bin` on its path, which a non-interactive shell would not look in.

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
| `Images` | `dotnet android` | The images provisioning builds for the `linux` and `android` kinds; an empty list builds none. |
| `HasXcodeGen`, `XcodeGenVersion`, `XcodeGenSha256` | `true`, `2.46.0`, its SHA-256 | Whether provisioning puts XcodeGen under the root of a host that has Xcode, which release, and the checksum its zip must match; an empty checksum checks nothing. |
| `Sync` | `rsync` | What carries a copy: `rsync`, a mirror before every run, or `mutagen`, a session per workspace that keeps it mirrored; `Mutagen` names the program. |
| `Excludes` | `bin obj artifacts TestResults BenchmarkDotNet.Artifacts node_modules .DS_Store` | Folders a delivery never sends. |
| `TransferSeconds`, `RunSeconds`, `ProbeSeconds` | 600, 3600, 20 | How long a delivery, a run, a probe may take. |
| `WorkspaceDays`, `JobDays`, `ImageDays`, `BuildCacheHours` | 14, 30, 7, 24 | The ages a sweep goes by. |
| `LowDiskGigabytes` | 20 | Free gigabytes on a host below which a run sweeps the caches first. |

## What is swept, and when

| What | Proof it is the server's | Goes when |
| --- | --- | --- |
| a workspace | its marker | not built for `WorkspaceDays`, after a run of its repository; its scope asked for; `caches`, `everything` |
| derived data | named for a workspace | its workspace is gone; `everything` |
| images a run made | the label `snail.owner` | older than `ImageDays` in `caches`; all in `everything` |
| the images of the container kinds | the label `snail.image` | `everything` only |
| the build cache | shared | entries not used for `BuildCacheHours` in `caches`; all in `everything` |
| the packages under the root | under the root | `everything` only |
| logs on this machine | under the server's data folder | older than `JobDays` |

The SDK under the root stays: a sweep never costs a download. To start over, erase `~/.s3l-build` and provision again.
A Mutagen session goes with its workspace; `mutagen sync list` on this machine shows the ones there are, named
`s3l-<repository>-<branch>-<host>`.

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
