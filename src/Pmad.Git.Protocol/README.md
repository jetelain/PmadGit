# Pmad.Git.Protocol

`Pmad.Git.Protocol` is a lightweight .NET 8 library providing core low-level Git wire protocol framing and packfile utilities.

It includes:
- **Packet-Line (`pkt-line`) framing**: `PktLine`, `PktLineReader`, and `PktLineWriter` for parsing and encoding Git wire protocol messages.
- **Git Packfile utilities**: `GitPackBuilder` (building packfiles), `GitPackReader` (unpacking packfiles into object stores), `GitDeltaApplier` (OFS_DELTA / REF_DELTA application), and `GitObjectWalker` (reachability graph traversal).

This package is used by both server-side implementations (such as `Pmad.Git.HttpServer`) and client-side implementations (such as `Pmad.Git.RemoteClient`).

