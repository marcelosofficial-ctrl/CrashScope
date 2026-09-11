# Architecture

CrashScope starts as a Windows-first modular monolith with an independent dashboard.

## CrashScope.Core

Contains domain models, contracts, validation rules, sessions, incidents, and telemetry abstractions.

Core must not depend on Windows APIs, LibreHardwareMonitor, SQLite, ASP.NET Core, or frontend code.

## CrashScope.Infrastructure

Contains implementations such as hardware telemetry adapters, Windows Event Log integration, Windows Error Reporting integration, SQLite persistence, and environment discovery.

Infrastructure depends on Core.

## CrashScope.Agent

The executable host. It will own the central sampling clock, rolling telemetry buffer, session management, incident correlation, persistence, localhost REST API, and WebSocket streaming.

## Sampling rule

Hardware is read once per interval. One normalized telemetry frame is then shared with memory buffering, persistence, and dashboard clients.

Opening additional dashboards must not multiply hardware polling.

## Privileges

CrashScope runs unelevated by default. Elevation is reserved for isolated capabilities that prove they require it.