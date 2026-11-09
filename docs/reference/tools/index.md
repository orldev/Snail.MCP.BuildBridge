# Tools

9 tools in 2 families.
The names, descriptions and schemas are generated from the server itself, so they are exactly what the model receives in `tools/list`.

| Tool | Family | What it does |
| --- | --- | --- |
| [`build_hosts`](builds.md#build_hosts) | Builds | List the build hosts this server knows and what each can do right now: whether it answers over ssh, which toolchains it has with their versions — dotnet, xco… |
| [`build_provision`](builds.md#build_provision) | Builds | Make a host ready to build: create its root with its folders, install the .NET SDK the settings name under the root with its workloads, add the mirrored pack… |
| [`build_sync`](builds.md#build_sync) | Builds | Deliver the working copy a path lies in to a host's workspace, or with dryRun list what a delivery would send without sending it. |
| [`build_run`](builds.md#build_run) | Builds | Build, test or publish a working copy on a host and get back how it went: the copy a path lies in is delivered first (as build_sync does), the program of the… |
| [`build_logs`](builds.md#build_logs) | Builds | Read the end of a build's log on this machine, as build_run named it, while the build runs or after it ended. Only a log of this server is read: a path that … |
| [`build_cancel`](builds.md#build_cancel) | Builds | Stop a running job of this server — a build or a provisioning — by the job resource build_run or build_provision answered with: the command is stopped on the… |
| [`build_fetch`](builds.md#build_fetch) | Builds | Bring named paths of a workspace back to this machine: test results, a bundle for a simulator, a package, a published folder. |
| [`build_clean`](builds.md#build_clean) | Builds | Give a host its room back, in two calls: first a plan of a scope is decided and shown under a planId, then that plan, and only it, is carried out when called… |
| [`build_diagnose`](diagnostics.md#build_diagnose) | Diagnostics | Say what state this server is in: its version, the hosts its settings name, whether ssh, rsync, git and mutagen are on this machine, what carries a copy, the… |
