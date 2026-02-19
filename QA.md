# DALI Plugin - QA Checklist

## 1. Installation and Startup

| # | Scenario | Expected | Pass |
|---|----------|----------|------|
| 1.1 | Install for Revit 2024 (net48) | Plugin loads, ribbon button appears | [ ] |
| 1.2 | Install for Revit 2026 (net8.0) | Plugin loads, ribbon button appears | [ ] |
| 1.3 | Click Dali ribbon button | Modeless window opens | [ ] |
| 1.4 | Close and reopen window | No crash, state resets properly | [ ] |

## 2. Settings Validation

| # | Scenario | Expected | Pass |
|---|----------|----------|------|
| 2.1 | Open Settings tab | Current settings displayed | [ ] |
| 2.2 | Change parameter names and save | Settings persisted to JSON | [ ] |
| 2.3 | Enter empty parameter name | Validation error shown | [ ] |
| 2.4 | Change included categories | Reflected in Batch Setup and Grouping | [ ] |
| 2.5 | Reload settings after Revit restart | Previous settings restored | [ ] |

## 3. Batch Setup

| # | Scenario | Expected | Pass |
|---|----------|----------|------|
| 3.1 | Load batch setup data | DataGrid populated with FamilySymbols | [ ] |
| 3.2 | Edit mA load value (valid) | Cell updated, row marked dirty | [ ] |
| 3.3 | Edit mA load value (negative) | Rejected or clamped | [ ] |
| 3.4 | Edit address count (valid) | Cell updated, row marked dirty | [ ] |
| 3.5 | Save only dirty rows | Only modified types written | [ ] |
| 3.6 | Save with missing parameter | Status shows which types skipped | [ ] |
| 3.7 | Large project (1000+ types) | Loads without freezing UI | [ ] |

## 4. Grouping / Selection Totals

| # | Scenario | Expected | Pass |
|---|----------|----------|------|
| 4.1 | Empty selection -> Refresh | "0 valid, 0 skipped" shown | [ ] |
| 4.2 | Valid selection -> Refresh | Load mA and address count shown | [ ] |
| 4.3 | Mixed selection (valid + skipped categories) | Correct counts, warnings for skipped | [ ] |
| 4.4 | Selection with linked elements | Linked elements skipped with reason | [ ] |
| 4.5 | Selection with ElementTypes | Types ignored, counted as skipped | [ ] |
| 4.6 | Missing type parameter data | Warning lists which types lack data | [ ] |
| 4.7 | Gauge shows normal (< 80%) | Green arc, white percent text | [ ] |
| 4.8 | Gauge shows warning (80-99%) | Amber arc, amber percent text | [ ] |
| 4.9 | Gauge shows error (>= 100%) | Red arc, red percent text | [ ] |
| 4.10 | Large selection (500+ elements) | Completes in reasonable time | [ ] |

## 5. Add to Line

| # | Scenario | Expected | Pass |
|---|----------|----------|------|
| 5.1 | Valid selection under limits | All elements assigned, success message | [ ] |
| 5.2 | Over mA load limit | Blocked with warning, no writes | [ ] |
| 5.3 | Over address count limit | Blocked with warning, no writes | [ ] |
| 5.4 | Missing instance parameter | Elements skipped, partial success | [ ] |
| 5.5 | Read-only instance parameter | Elements skipped with reason | [ ] |
| 5.6 | Reassign from existing line | Reassigned count shown | [ ] |
| 5.7 | Empty line name | Blocked with validation error | [ ] |
| 5.8 | IsBusy indicator during operation | Overlay shown, buttons disabled | [ ] |

## 6. View Filter Highlighting

| # | Scenario | Expected | Pass |
|---|----------|----------|------|
| 6.1 | First Add to Line | Filter "DALI_Line_X" created, color applied | [ ] |
| 6.2 | Second Add to Line (same line) | Existing filter reused | [ ] |
| 6.3 | Add to Line (different line) | New filter with different color | [ ] |
| 6.4 | Deterministic color | Same line name -> same color across sessions | [ ] |
| 6.5 | View not supporting filters | Warning message, no crash | [ ] |
| 6.6 | Template-controlled view | Warning: "does not allow graphic overrides" | [ ] |
| 6.7 | Filter name collision (existing incompatible filter) | Suffixed name used, warning logged | [ ] |

## 7. Reset Overrides

| # | Scenario | Expected | Pass |
|---|----------|----------|------|
| 7.1 | Reset in view with tracked filters | Overrides cleared, count shown | [ ] |
| 7.2 | Reset in view with no tracked filters | "No DALI overrides tracked" message | [ ] |
| 7.3 | Reset does not delete filters | Filters remain in view, only overrides cleared | [ ] |
| 7.4 | Reset does not affect user-created filters | Only tool-managed filters touched | [ ] |

## 8. Logging

| # | Scenario | Expected | Pass |
|---|----------|----------|------|
| 8.1 | AddToLine logged | Key milestones visible in Debug output | [ ] |
| 8.2 | RefreshSelection logged | Element counts and totals visible | [ ] |
| 8.3 | ResetOverrides logged | Cleared count visible | [ ] |
| 8.4 | Exceptions logged with stack trace | Full exception in Debug output | [ ] |
| 8.5 | SessionLogger ring buffer | Recent entries accessible via App.Logger | [ ] |

## 9. Performance Smoke Tests

| # | Scenario | Expected | Pass |
|---|----------|----------|------|
| 9.1 | Project with 2000+ family types | Batch Setup loads < 10s | [ ] |
| 9.2 | Selection of 500 elements | Totals computed < 5s | [ ] |
| 9.3 | Repeated Refresh Selection | Type cache hit, faster on second call | [ ] |
| 9.4 | Multiple Add to Line operations | No memory leak or degradation | [ ] |
