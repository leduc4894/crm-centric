# P0 Contract Gaps Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [x]`) syntax for tracking.

**Goal:** Khép gaps đặc tả/contract P0, tạo executable contract gate, giữ acceptance runtime mở.

**Architecture:** Contract-first, API-only external modules. Inbox ID nối dedup với automation; schema kiểm tra cấu trúc, runtime kiểm tra authorization và network policy.

**Tech Stack:** Python unittest, JSON Schema Draft 2020-12, OpenAPI 3.1, pinned contract tooling. Runtime .NET/React chưa được scaffold trong gói này.

**Spec:** `docs/superpowers/specs/2026-11-08-p0-contract-gaps-design.md`

## Global Constraints
- Không shared DB với external module; không principal từ Contact.
- Không external side effect trong DB transaction; không tenant_id trong mutation body.
- Không phát triển P1/P2. Chỉ một agent sửa mã; reviewer chỉ đọc.

## Review Focus
- Thiếu capability descriptor: schema phải reject.
- Event ID trùng giữa installation: không gộp execution.
- Retry sau replay window: ký timestamp mới, giữ raw event bytes/identity.
- Predicate sai kiểu hoặc quá sâu: reject.
- Schema tests pass không chứng minh tenant isolation, durable commit hay restore.

## Task 1: Contract tests và tooling
**Files:** `tests/Contract/test_contracts.py`, `requirements-contracts.lock`, `.github/workflows/contracts.yml`.
**Interfaces:** Consumes schema/sample JSON và OpenAPI; produces `python -m unittest discover -s tests/Contract -v`.
- [x] Viết tests cho manifest descriptors, DSL, API request/error/concurrency và samples.
- [x] Chạy RED: failure đúng do schema/contract thiếu.
- [x] Pin toàn bộ dependencies, thêm CI chạy cùng command.

## Task 2: Contracts và samples
**Files:** `docs/contracts/*`, `docs/examples/*`.
**Interfaces:** Consumes test expectations; produces manifest v2, typed P0 OpenAPI, bounded DSL, protocol fixtures.
- [x] Bổ sung schema, samples, webhook/action protocol và migration policy.
- [x] Chạy tests GREEN; kiểm tra negative cases và OpenAPI validation.

## Task 3: Architecture và acceptance
**Files:** ADR, architecture, PRD, operations, backlog, README; `docs/engineering/03-ACCEPTANCE-MATRIX.md`.
**Interfaces:** Consumes finalized contracts; produces story/test/wave/evidence matrix và verification report.
- [x] Đồng bộ event identity, data constraints, lifecycle, permissions, SLO và DR.
- [x] Ghi rõ API-only boundaries, audit, RLS, runtime gates còn mở.
- [x] Review chỉ đọc, sửa findings; chạy full suite và `git diff --check`.
- [x] Ghi verification/migration/rollback/risk vào báo cáo; để thay đổi trong nhánh cho người dùng review.
