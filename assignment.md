# Technical Assignment: Containerized Chat Application

## Objective

Develop a simple chat application that allows multiple clients to connect to a server and exchange messages. The application should be containerized using Docker, and both the server and client should be able to communicate over a network.

**Expected delivery deadline: Friday at 13:00 GMT+2**.

---

# Requirements

## Server Application

Implement a multi-threaded chat server using sockets.

The server should:

- Accept connections from multiple clients.
- Allow clients to send messages to the server.
- Broadcast received messages to all connected clients.
- Log all messages with timestamps to a log file.
- Handle unexpected client disconnections gracefully.

---

## Client Application

Implement a client application that can connect to the chat server.

The client should:

- Allow the user to send messages.
- Display received messages in real-time.
- Handle connection loss and attempt to reconnect automatically.

---

## Dockerization

Create:

- A container for the server application.
- One or more client containers that can connect to the server.

Requirements:

- Create Dockerfiles for both the server and client applications.
- Use Docker Compose to define and manage the multi-container application.

---

## Networking

Ensure that:

- The server and client containers can communicate over the network.
- Clients resolve the server hostname using Docker's internal DNS (service name resolution).
- The server address and port can be configured using environment variables or a similarly simple configuration mechanism.

---

## Testing

Provide instructions describing how to:

1. Build the solution.
2. Start the server.
3. Start multiple clients using Docker Compose.
4. Demonstrate that clients can connect and exchange messages.

---

# Bonus Points

Additional credit will be given for any of the following:

### Authentication

Implement a simple authentication mechanism where clients must provide a username and password before participating in the chat.

### Private Messaging

Allow clients to send private messages directly to other connected clients.

### Persistent Storage

Persist chat logs using a database solution such as:

- PostgreSQL
- SQLite

---

# Deliverables

Please provide:

- Source code for the server and client applications (GitHub, GitLab, Bitbucket, or equivalent).
- Dockerfiles for both applications.
- Docker Compose configuration.
- README file containing:
  - Build instructions
  - Run instructions
  - Testing instructions
  - Any assumptions made during implementation
- Additional documentation describing design decisions, trade-offs, or architectural considerations.

---

# Technologies

## Programming Language

Use one of the following technologies:

- **C# / .NET**
- **C / C++**

Choose whichever platform you are most comfortable with.

## Networking

- Use low-level socket programming.
- Do not use chat-specific frameworks or messaging platforms.

## Containerization

- Docker
- Docker Compose

## Version Control

Submit your solution through a Git repository.

---

# Evaluation Criteria

## Correctness

- Does the application meet the functional requirements?
- Can multiple clients connect and exchange messages successfully?

## Code Quality

- Is the code clean, readable, and maintainable?
- Is the project structure reasonable?
- Are error handling and logging implemented appropriately?

## Dockerization

- Are Dockerfiles implemented correctly?
- Does the Docker Compose setup work as expected?

## Networking

- Is communication between containers reliable?
- Are Docker networking and hostname resolution used correctly?

## Resilience

- How well does the application handle:
  - Client disconnects
  - Connection loss
  - Invalid input
  - Unexpected errors

## Documentation

- Is the README clear and complete?
- Can another developer build and run the application using the provided instructions?

## Bonus Features

Additional points will be awarded for successfully implementing any bonus requirements.

---

# Notes

The goal of this exercise is not to build a production-ready chat platform, but to demonstrate:

- Software design and implementation skills
- Networking fundamentals
- Concurrent programming
- Docker and containerization knowledge
- Problem-solving ability
- Code quality and communication through documentation

Please keep the solution focused, practical, and maintainable.
