# Builds

## <code>build_<wbr>hosts</code>

List the build hosts this server knows and what each can do right now: whether it answers over ssh, which
toolchains it has with their versions — dotnet, xcode and docker — and how many gigabytes are free under its home. A host
that does not answer says why. Changes nothing. The first host is the one a call that names none gets.

*read-only · repeat-safe · stays on this machine.*

Takes no parameters.

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {}
}
```

</details>

## <code>build_<wbr>provision</code>

Make a host ready to build: create its root with its folders, install the .NET SDK the settings name under the
root with its workloads, add the mirrored package feeds to its NuGet sources, build the images the settings
name for the linux and android kinds when docker is there, put xcodegen under the root when Xcode is there,
and look at Xcode, Docker, Rosetta and sleep — those a person installs or sets, since each means a licence or
a password, and the answer says how. Run it on a new host, and again after the settings change;
what is in place already is left as it is. The work runs as a job: the answer is the job's report, with the
steps as its result when it ends within the wait, or the running job, whose resource
job://buildbridge/provision/&lt;id&gt; carries them once it ends. A workload install is minutes and the android
image longer; the job may run up to 3600 seconds.

*only adds · repeat-safe · may reach outside services.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `host` | `string?` | — | `null` | The host to use, by the name build_hosts lists; the first host when not given. |
| `wait` | `integer` | — | `60` | Seconds to wait for the work before answering with the running job; 60 when not given, 0 answers with the job at once. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "host": {
      "description": "The host to use, by the name build_hosts lists; the first host when not given.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "wait": {
      "description": "Seconds to wait for the work before answering with the running job; 60 when not given, 0 answers with the job at once.",
      "type": "integer",
      "default": 60
    }
  }
}
```

</details>

## <code>build_<wbr>sync</code>

Deliver the working copy a path lies in to a host's workspace, or with dryRun list what a delivery would send
without sending it. The workspace is one per repository and branch under the host's root. Only what changed
goes over the wire; what git ignores and what a build makes again — bin, obj, artifacts, TestResults — never
goes, and a tracked file deleted here is deleted there. Build outputs already on the host are left alone, so a
build there stays incremental. A linked worktree is delivered without its .git. A delivery may take up to 600
seconds. With Sync set to mutagen in the settings a session keeps the workspace mirrored as the copy changes,
a delivery only waits for it to catch up and names no files, and dryRun still lists the difference through
rsync. build_run delivers by itself before it runs, so call this to check a delivery, not before every run.

*only adds · repeat-safe · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `path` | `string` | yes | — | A path inside the working copy to deliver, such as the repository's root. |
| `host` | `string?` | — | `null` | The host to use, by the name build_hosts lists; the first host when not given. |
| `dryRun` | `boolean` | — | `false` | List what would be sent and send nothing; false when not given. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "path": {
      "description": "A path inside the working copy to deliver, such as the repository\u0027s root.",
      "type": "string"
    },
    "host": {
      "description": "The host to use, by the name build_hosts lists; the first host when not given.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "dryRun": {
      "description": "List what would be sent and send nothing; false when not given.",
      "type": "boolean",
      "default": false
    }
  },
  "required": [
    "path"
  ]
}
```

</details>

## <code>build_<wbr>run</code>

Build, test or publish a working copy on a host and get back how it went. The copy a path lies in is delivered
first (as build_sync does), then the program of the kind — dotnet, xcode, docker, linux, android or script: dotnet, xcodebuild, docker, or dotnet
inside the host's s3l-build-dotnet or s3l-build-android image — runs in the workspace with the arguments
given, such as test -c Release, or build -scheme App -destination 'platform=iOS Simulator,name=iPhone 17', or
compose up -d --build. The linux kind is for a Linux target, Native AOT for Linux or tests on Linux, the
android kind for an Android build; both mount the workspace, the packages and the feeds into the container,
and the android image runs under emulation on an Apple Silicon host. The script kind runs a script of the
repository as it is: the first argument is its path from the workspace's root, such as
tools/native/build-xcframework.sh, the rest its arguments; it must be executable, as git keeps it, and finds
the host's dotnet on its path — for what a build needs that no one program says, before another run. The
answer says whether it succeeded, the
exit code, what the tests said when there were any, and where the log is on this machine, which build_logs
reads. Outputs stay on the host, where the next run finds them; build_fetch brings named paths back. An
xcodebuild run keeps its derived data under the host's root, a destination that names a simulator by name
gets a clone of it for the run alone, deleted when the run ends, and a project with a project.yml beside it is
generated on the host by xcodegen first, since a generated project is what git ignores. A docker build or run carries the label
snail.owner, and a compose stand is the project s3l-&lt;repository&gt;-&lt;branch&gt; unless -p names one. The work
runs as a job: the answer is the job's report, with the build as its result when it ends within the wait, or
the running job, whose resource job://buildbridge/&lt;kind&gt;/&lt;id&gt; carries it once it ends and whose message names
the log. A run may take up to 3600 seconds; one workspace takes one run at a time, and a second waits its turn.

*only adds · every call adds · may reach outside services.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `path` | `string` | yes | — | A path inside the working copy to build, such as the repository's root. |
| `kind` | `string` | yes | — | Which program runs: dotnet, xcode, docker, linux, android or script. |
| `arguments` | `array` | yes | — | The program's arguments, one word each, as they would be typed in the workspace's root: ["test", "-c", "Release"]. |
| `host` | `string?` | — | `null` | The host to use, by the name build_hosts lists; the first host when not given. |
| `wait` | `integer` | — | `60` | Seconds to wait for the work before answering with the running job; 60 when not given, 0 answers with the job at once. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "path": {
      "description": "A path inside the working copy to build, such as the repository\u0027s root.",
      "type": "string"
    },
    "kind": {
      "description": "Which program runs: dotnet, xcode, docker, linux, android or script.",
      "type": "string"
    },
    "arguments": {
      "description": "The program\u0027s arguments, one word each, as they would be typed in the workspace\u0027s root: [\u0022test\u0022, \u0022-c\u0022, \u0022Release\u0022].",
      "type": "array",
      "items": {
        "type": "string"
      }
    },
    "host": {
      "description": "The host to use, by the name build_hosts lists; the first host when not given.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "wait": {
      "description": "Seconds to wait for the work before answering with the running job; 60 when not given, 0 answers with the job at once.",
      "type": "integer",
      "default": 60
    }
  },
  "required": [
    "path",
    "kind",
    "arguments"
  ]
}
```

</details>

## <code>build_<wbr>logs</code>

Read the end of a build's log on this machine, as build_run named it, while the build runs or after it ended.
Only a log of this server is read. The last 100 lines unless asked for more, at most 2000.

*read-only · repeat-safe · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `log` | `string` | yes | — | The log's path, as build_run's answer or its job's message names it. |
| `lines` | `integer` | — | `100` | How many lines from the end to read; 100 when not given, at most 2000. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "log": {
      "description": "The log\u0027s path, as build_run\u0027s answer or its job\u0027s message names it.",
      "type": "string"
    },
    "lines": {
      "description": "How many lines from the end to read; 100 when not given, at most 2000.",
      "type": "integer",
      "default": 100
    }
  },
  "required": [
    "log"
  ]
}
```

</details>

## <code>build_<wbr>cancel</code>

Stop a running job of this server — a build or a provisioning — by the job resource build_run or build_provision
answered with: the command is stopped on the host too. A job that ended already is left as it is.

*may overwrite or delete · repeat-safe · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `job` | `string` | yes | — | The job's resource, job://buildbridge/&lt;kind&gt;/&lt;id&gt;. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "job": {
      "description": "The job\u0027s resource, job://buildbridge/\u003Ckind\u003E/\u003Cid\u003E.",
      "type": "string"
    }
  },
  "required": [
    "job"
  ]
}
```

</details>

## <code>build_<wbr>fetch</code>

Bring named paths of a workspace back to this machine: test results, a bundle for a simulator, a package, a
published folder. Paths are relative to the workspace's root, such as tests/TestResults or
src/bin/Release/net10.0/publish; they land under their own path inside the folder named, or inside this
server's data folder by repository and branch when none is. Only what changed goes over the wire.

*only adds · repeat-safe · may reach outside services.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `path` | `string` | yes | — | A path inside the working copy whose workspace is read, such as the repository's root. |
| `paths` | `array` | yes | — | The paths to bring back, relative to the workspace's root. |
| `host` | `string?` | — | `null` | The host to use, by the name build_hosts lists; the first host when not given. |
| `into` | `string?` | — | `null` | The folder on this machine they land in; this server's data folder, by repository and branch, when not given. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "path": {
      "description": "A path inside the working copy whose workspace is read, such as the repository\u0027s root.",
      "type": "string"
    },
    "paths": {
      "description": "The paths to bring back, relative to the workspace\u0027s root.",
      "type": "array",
      "items": {
        "type": "string"
      }
    },
    "host": {
      "description": "The host to use, by the name build_hosts lists; the first host when not given.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "into": {
      "description": "The folder on this machine they land in; this server\u0027s data folder, by repository and branch, when not given.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "path",
    "paths"
  ]
}
```

</details>

## <code>build_<wbr>clean</code>

Give a host its room back, in two calls: first a plan of a scope is decided and shown under a planId, then that
plan, and only it, is carried out when called again with the planId. Nothing on a host is the only copy of
anything, so a sweep erases. The scopes are workspace, repository, caches or everything: one workspace (the copy at path, or repository and
branch), every workspace of a repository, what has gone stale — workspaces not built for 14 days, derived
data of workspaces that are gone, images older than 7 days, build cache not used for 24 hours, logs older
than 30 days — or everything under the root but the SDK. A plan is kept 10 minutes. build_run sweeps by itself:
a repository's stale workspaces after each run, and the caches before one when the host has less than 20 GB
free.

*may overwrite or delete · every call adds · stays on this machine.*

| Parameter | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `scope` | `string` | yes | — | How much is looked at: workspace, repository, caches or everything. |
| `path` | `string?` | — | `null` | A path inside the working copy whose workspace or repository is meant; not needed for caches or everything. |
| `repository` | `string?` | — | `null` | The repository's name, instead of a path, for a repository sweep. |
| `host` | `string?` | — | `null` | The host to use, by the name build_hosts lists; the first host when not given. |
| `planId` | `string?` | — | `null` | The planId of a plan shown before, to carry it out; not given, a new plan is shown. |

<details><summary>JSON Schema</summary>

```json
{
  "type": "object",
  "properties": {
    "scope": {
      "description": "How much is looked at: workspace, repository, caches or everything.",
      "type": "string"
    },
    "path": {
      "description": "A path inside the working copy whose workspace or repository is meant; not needed for caches or everything.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "repository": {
      "description": "The repository\u0027s name, instead of a path, for a repository sweep.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "host": {
      "description": "The host to use, by the name build_hosts lists; the first host when not given.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    },
    "planId": {
      "description": "The planId of a plan shown before, to carry it out; not given, a new plan is shown.",
      "type": [
        "string",
        "null"
      ],
      "default": null
    }
  },
  "required": [
    "scope"
  ]
}
```

</details>

