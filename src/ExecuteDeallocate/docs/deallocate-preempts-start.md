# Preemption of Start Operations by Deallocate Operations

## Overview

When a **Deallocate** operation is submitted for a virtual machine that already has a pending or in-progress **Start** operation, the Deallocate operation **preempts** (cancels) the Start operation and proceeds with deallocation. This ensures that the most recent user intent is honored — if you decide to deallocate a VM that is being started, the system will cancel the start and deallocate the VM instead.

This behavior applies to operations submitted through the **ScheduledActions** APIs (`virtualMachines/submitDeallocate`, `virtualMachines/submitStart`, `virtualMachines/executeDeallocate`, `virtualMachines/executeStart`).

## When Preemption Occurs

Preemption is triggered when **all** of the following conditions are met:

1. A **Start** operation already exists for a VM (in either `PendingScheduling`/`Scheduled` or `Executing` state)
2. A new **Deallocate** operation is submitted for the **same VM**
3. The two operations fall within the conflict detection time window (based on deadline proximity)

The Deallocate operation will:
- Cancel the existing Start operation
- Schedule itself normally for the VM

## What Happens to Each Operation

### The preempted Start operation

The original Start operation transitions to the **Failed** state with the following error:

| Field | Value |
|-------|-------|
| `state` | `Failed` |
| `resourceOperationError.errorCode` | `OperationCancelled` |
| `resourceOperationError.errorDetails` | `Preempted by user request` |

The `completedAt` timestamp is set to the time the preemption was executed.

> **Note:** If the Start operation was already executing (i.e., CRP had begun processing the VM start), the CRP operation itself may still report as `Succeeded` in the CRP (Azure Resource Manager) layer. However, the Kronox ScheduledActions operation will be marked as failed. The Deallocate operation proceeds regardless of the CRP-level outcome of the preempted Start.

### The new Deallocate operation

The Deallocate operation is scheduled and executed normally. It appears in the API response with a new `operationId` and transitions through the standard lifecycle:

`PendingScheduling` → `Scheduled` → `Executing` → `Succeeded`/`Failed`

## API Response Behavior

### Submitting the Deallocate operation

When you submit a Deallocate that preempts a Start, the Deallocate submission **succeeds** (HTTP 200). The response includes the new Deallocate operation for the preempted VM with a new `operationId` and state `PendingScheduling` or `Scheduled`.

Unlike a standard conflict (which returns an `OperationConflict` error for the VM), preemption resolves the conflict automatically — the VM is included in the successful results.

### Polling operation status (GetOperationStatus)

When you poll the status of both operations:

- **Start operation**: Returns `Failed` state with `OperationCancelled` error code
- **Deallocate operation**: Returns the current state of the deallocate (e.g., `Executing`, `Succeeded`)

## Example Scenario

### Single VM: Start then Deallocate

1. **Submit Start** for VM `myVM` at deadline `T`
   - Response: Start operation created with `operationId: A`, state: `PendingScheduling`

2. **Submit Deallocate** for VM `myVM` at deadline `T+5s`
   - Kronox detects the existing Start operation as preemptable
   - Start operation `A` is cancelled
   - Response: Deallocate operation created with `operationId: B`, state: `PendingScheduling`

3. **Poll status** of both operations:

```json
{
  "results": [
    {
      "resourceId": "/subscriptions/.../virtualMachines/myVM",
      "operation": {
        "operationId": "A",
        "opType": "Start",
        "state": "Failed",
        "resourceOperationError": {
          "errorCode": "OperationCancelled",
          "errorDetails": "Preempted by user request"
        },
        "completedAt": "2026-03-12T21:04:49.295Z"
      }
    },
    {
      "resourceId": "/subscriptions/.../virtualMachines/myVM",
      "operation": {
        "operationId": "B",
        "opType": "Deallocate",
        "state": "Executing",
        "deadline": "2026-03-12T21:04:49.260Z",
        "deadlineType": "InitiateAt"
      }
    }
  ]
}
```

## Edge Cases

### Start operation has already completed

If the Start operation has already reached a terminal state (`Succeeded` or `Failed`) by the time the Deallocate is submitted, no preemption occurs. The Deallocate is scheduled normally — there is no conflict because the Start operation has already finished.

### Preemption of a scheduled (not yet executing) Start

If the Start operation has not yet been dispatched to CRP (i.e., it is in `PendingScheduling` or `Scheduled` state), Kronox cancels it directly. No CRP call is ever made for the Start operation. The Start operation transitions immediately to `Failed` with `OperationCancelled`.

## Retry Policy Interaction

The preempted Start operation's retry policy is **not** honored after preemption. Once preempted, the Start operation is terminal (`Failed`) and will not be retried. The Deallocate operation follows its own retry policy independently.
