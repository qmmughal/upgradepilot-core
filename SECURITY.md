# Security Policy

## Reporting a vulnerability

Please do **not** open a public GitHub issue for security vulnerabilities.

Instead, report privately via GitHub's [Security Advisories](../../security/advisories/new)
for this repository, or email **qmmughal@gmail.com** with:

- A description of the vulnerability and its potential impact
- Steps to reproduce (proof-of-concept code, if available)
- Any known mitigations

We aim to acknowledge reports within 5 business days and to provide a remediation
timeline once the report is triaged.

## Scope

This applies to `upgradepilot-core` itself — the agent framework, CLI/MCP integration,
and Roslyn/tree-sitter analysis code. Given the nature of this project (agents that
read and modify source code), we're particularly interested in reports involving:

- Arbitrary code execution via crafted repository content
- Path traversal or sandbox escape in MCP tool implementations
- Prompt injection that causes an agent to exfiltrate data or bypass validation rules
- Supply-chain issues in dependency resolution

## Supported versions

Pre-1.0: only the latest `main` branch is supported. A versioned support policy will
be published at 1.0.
