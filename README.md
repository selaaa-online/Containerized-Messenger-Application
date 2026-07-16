# Containerized Chat Application

A simple, multi-client chat system built with **C# / .NET 8** using **low-level TCP sockets**
(`System.Net.Sockets`). The server and clients run as separate Docker containers and
communicate over a Docker bridge network, with chat history persisted in **PostgreSQL**
via **EF Core (code-first)**.

---

## Features

### Core requirements
- **Multi-threaded socket server** — accepts many simultaneous clients, each handled on its own asynchronous task.
- **Broadcast messaging** — messages from any client are relayed to all connected clients.
- **Timestamped logging** — every message is written to a log file (`logs/chat.log`) *and* to PostgreSQL.
- **Graceful disconnect handling** — unexpected drops are detected, the client is removed, and others are notified.
- **Auto-reconnect client** — the client retries with exponential backoff when the connection is lost.
- **Configurable** — host/port and database settings come from environment variables.
- **Docker DNS resolution** — clients reach the server by its Compose service name (`server`).

### Bonus features (all implemented)
- **Authentication** — clients must supply a username + password. Unknown users are auto-registered; known users are verified against a PBKDF2-hashed password.
- **Private messaging** — `/msg <user> <text>` delivers a direct message to a single online user. Use `/list` to list who is currently online.
- **Persistent storage** — users and all chat messages are stored in PostgreSQL using EF Core code-first migrations.

---

## Project structure

```
chat-application/
├── src/
│   ├── Shared/            # Wire protocol: Message, MessageType, MessageChannel (newline-framed JSON)
│   ├── ChatServer/        # TCP server, auth, EF Core data layer, file+DB logging
│   │   ├── Auth/          # PasswordHasher (PBKDF2), AuthService
│   │   ├── Data/          # Entities, DbContext, migrations, factories
│   │   ├── Logging/       # MessageLogger (file + database)
│   │   └── Server/        # ChatHost (accept loop, broadcast, private routing)
│   └── ChatClient/        # Console client with auto-reconnect
├── docker/
│   ├── Dockerfile.server
│   └── Dockerfile.client
├── docker-compose.yml     # postgres + server + client services on one network
└── README.md
```

---

## Protocol

Communication is **newline-delimited JSON** over TCP. Each message is a single line of
UTF-8 JSON terminated by `\n`. This keeps the protocol dependency-free (no chat framework)
while remaining structured and easy to debug.

| Type         | Direction        | Purpose                                   |
|--------------|------------------|-------------------------------------------|
| `Auth`       | client → server  | Login / auto-register (username+password) |
| `AuthResult` | server → client  | Success/failure of authentication         |
| `Chat`       | both             | Broadcast message                         |
| `Private`    | both             | Direct message to one user                |
| `System`     | server → client  | Join/leave notices, errors, usage hints   |

---

## Configuration (environment variables)

### Server
| Variable            | Default            | Description                     |
|---------------------|--------------------|---------------------------------|
| `SERVER_PORT`       | `9000`             | TCP port to listen on           |
| `LOG_FILE_PATH`     | `logs/chat.log`    | Path to the append-only log     |
| `POSTGRES_HOST`     | `localhost`        | Database host                   |
| `POSTGRES_PORT`     | `5432`             | Database port                   |
| `POSTGRES_DB`       | `chatdb`           | Database name                   |
| `POSTGRES_USER`     | `chat`             | Database user                   |
| `POSTGRES_PASSWORD` | `chatpassword`     | Database password               |

### Client
| Variable        | Default     | Description                                   |
|-----------------|-------------|-----------------------------------------------|
| `SERVER_HOST`   | `localhost` | Server hostname (Compose service name `server`) |
| `SERVER_PORT`   | `9000`      | Server port                                   |
| `CHAT_USERNAME` | *(prompt)*  | Username; if unset, the client prompts        |
| `CHAT_PASSWORD` | *(prompt)*  | Password; if unset, the client prompts        |

---

## Build & run with Docker Compose

### 1. Build the solution
```powershell
docker compose build
```

### 2. Start PostgreSQL and the server
```powershell
docker compose up -d postgres server
```
The server waits for PostgreSQL to be healthy, applies EF Core migrations automatically,
then begins listening on port `9000`.

You can follow server logs with:
```powershell
docker compose logs -f server
```

### 3. Start one or more clients
Open a **separate terminal for each client** and run:
```powershell
docker compose run --rm client
```
Each invocation prompts for a username and password (any values — new users are registered
automatically). Repeat in additional terminals to simulate multiple participants.

> Using `docker compose run` (rather than `up`) gives each client its own interactive
> terminal, which is what a console chat client needs.

### 4. Chat
- Type a message and press **Enter** to broadcast it to everyone.
- Send a private message: `/msg <username> <message>`
- Quit: `/quit`

### 5. Shut down
```powershell
docker compose down          # stop containers
docker compose down -v       # also remove the PostgreSQL volume
```

---

## Demonstrating multi-client messaging

1. `docker compose up -d postgres server`
2. Terminal A: `docker compose run --rm client` → log in as `alice`
3. Terminal B: `docker compose run --rm client` → log in as `bob`
4. In A, type `hello everyone` → it appears in B as `alice: hello everyone`.
5. In B, type `/msg alice hi there` → only A receives `[private] bob -> alice: hi there`.
6. Close B's terminal (Ctrl+C) → A shows `[system] bob left the chat.`
7. Inspect persisted history:
   ```powershell
   docker compose exec postgres psql -U chat -d chatdb -c "SELECT \"SenderUsername\", \"Recipient\", \"IsPrivate\", \"Content\", \"Timestamp\" FROM \"ChatLogs\" ORDER BY \"Timestamp\";"
   ```
8. Inspect the file log: `logs/chat.log` on the host (bind-mounted from the server).

---

## Running locally without Docker (optional)

Requires the .NET 8 SDK and a reachable PostgreSQL instance.

```powershell
# Server (set DB env vars to point at your PostgreSQL)
$env:POSTGRES_HOST="localhost"
dotnet run --project src/ChatServer

# Client (in another terminal)
$env:SERVER_HOST="localhost"
dotnet run --project src/ChatClient
```

Migrations are applied automatically on server startup. To create/update migrations
manually:
```powershell
dotnet ef migrations add <Name> --project src/ChatServer --startup-project src/ChatServer -o Data/Migrations
```

---

## Design decisions & trade-offs

- **Sockets, not frameworks.** The transport is raw `TcpListener` / `TcpClient` with a
  hand-written framing layer (`MessageChannel`), satisfying the "low-level socket
  programming" requirement.
- **Newline-delimited JSON.** Chosen over a fixed binary protocol for readability and
  easy extensibility. Framing by `\n` avoids partial-message issues from TCP's stream nature.
- **Task-per-connection concurrency.** Each client runs on its own async task over the
  thread pool (see the *Concurrency model* section above); the shared client registry is a
  `ConcurrentDictionary`, and outbound writes are serialized per connection via a semaphore
  in `MessageChannel`. This keeps the concurrency model simple and safe without manual
  locking around the socket.
- **Short-lived DbContexts.** A lightweight `IDbContextFactory` creates a fresh
  `ChatDbContext` per operation, which is the recommended pattern for concurrent access
  and avoids sharing a context across threads.
- **Auto-register on first login.** Simplifies the demo; a production system would separate
  registration from login. Passwords are hashed with **PBKDF2 (SHA-256, 100k iterations)**
  and a per-user salt — never stored in plaintext.
- **Migration-on-startup with retry.** The server polls for the database before applying
  migrations, so container start order doesn't cause crashes (in addition to the Compose
  health check).
- **Dual logging.** Messages go to both a file (quick inspection, requirement) and the
  database (durable, queryable — the persistence bonus).

- Concurrency model (how "multi-threaded" is achieved)

  The server handles many clients **concurrently across multiple threads**, but it does so
  using .NET's **task-based asynchronous model over the thread pool** rather than a dedicated
  OS thread per client.

  - The accept loop (`await AcceptTcpClientAsync`) spawns an independent `Task`
  (`HandleClientAsync`) for every connection — see [ChatHost.cs](src/ChatServer/Server/ChatHost.cs).

  - While a connection waits for the next message (`await channel.ReadAsync(...)`), the method
    **suspends and releases its thread back to the thread pool** instead of blocking it. When
    data arrives, the continuation resumes on any available pool thread (backed by OS I/O
    completion ports).

  - Concurrent shared state is therefore accessed from multiple threads and is protected
    accordingly: the connected-client registry is a `ConcurrentDictionary`, and per-connection
    writes are serialized with a `SemaphoreSlim` in [MessageChannel.cs](src/Shared/MessageChannel.cs).

  **Why this instead of `new Thread(...)` per client?** A thread-per-client design pins one OS
  thread (≈1 MB stack) to each connection, most of which sits blocked on a synchronous read.
  The async model services thousands of connections with a small pool of threads proportional
  to the CPU count, giving the same concurrency with far lower memory and context-switching
  overhead. It is the idiomatic, recommended approach for scalable socket servers in modern
  .NET. Functionally it is genuinely multi-threaded — message handling for different clients
  runs in parallel on different threads — it simply does not waste a dedicated thread per idle
  socket.

| Aspect                  | Thread-per-client             | **This project** (async + thread pool) |
| ----------------------  | ----------------------------  | -------------------------------------- |
| Threads for _N_ clients | `N` — one dedicated per client| Small shared pool (~CPU cores)         |
| While waiting for I/O   | Thread **blocked** on read    | Thread **returned to the pool**        |
| Memory per idle client  | ~1 MB thread stack            | A few KB of state                      |
| Context switching       | High under load               | Minimal                                |
| Scalability             | Limited (hundreds)            | High (thousands)                       |

## Assumptions

- Implementation mainly backend centric + chat interactivity via CLI 
- Out of scope client centric UI 
- Usernames are unique and case-insensitive; a user may only be connected once at a time.
- Any username/password is accepted on first use (self-service registration) for demo ease.
- Private messages are delivered only when the recipient is currently online.
- Console rendering is line-based; inbound messages may interleave with what a user is
  typing (acceptable for a plain console client, as specified).
- The PostgreSQL credentials in `docker-compose.yml` are for local/demo use only.

