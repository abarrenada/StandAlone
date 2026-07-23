# Label Decision .NET Migration

This project replaces the Progress/OpenEdge-based PLC-to-label-printing workflow with a standalone .NET system.

## Architecture

- LabelDecisionApp.Console: Core service for CSV parsing and label decision logic
- LabelDecisionApp.Integration: Input adapters (file, serial), PLC payload parser, printer interface
- LabelDecisionApp.Worker: Long-running background service for production
- LabelDecisionApp.Tests: Unit tests

## Key Features

- Replaces Progress database lookups with CSV-based input
- Replaces shell script batch processing with .NET background service
- File watcher for automatic PLC payload processing
- Extensible printer and input adapter interfaces
- Comprehensive logging and error handling
- Unit tests for label decision logic
