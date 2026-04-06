# Retry and Fallback Policy for BulkAction

This guide explains how to configure retry and fallback behavior for VM operations submitted through the BulkAction API. These features help maximize the success rate of your operations by automatically retrying on transient failures and optionally performing a fallback action when all retries are exhausted.

## Overview

When you submit a VM operation (such as Start, Deallocate, Hibernate, or Create), the operation may encounter transient errors from the underlying compute platform. The **Retry Policy** controls how the system automatically retries failed operations, and the **Fallback (OnFailureAction)** provides a safety net when retries are exhausted.

---

## Retry Policy

The retry policy is an **optional** configuration specified in the `executionParameters.retryPolicy` field of your request. It controls how long the system will retry a failed operation. If not provided, the operation is attempted once with no retries.

### Fields

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `retryWindowInMinutes` | integer | No | The maximum duration (in minutes) during which retries are allowed, measured from when the operation first starts executing. **Defaults to `0` if not provided.** |
| `onFailureAction` | string | No | The fallback action to perform when all retry attempts are exhausted. See [Fallback (OnFailureAction)](#fallback-onfailureaction) below. |

### How retries work

1. The system submits the operation to the compute platform.
2. If the operation fails with a **retriable error**, the system waits for a backoff interval and then retries.
3. The system continues retrying as long as the elapsed time since the operation started is within the `retryWindowInMinutes` window.
4. If the retry window is reached and a fallback is configured, the fallback action is executed (see below).
5. If no fallback is configured, the operation is marked as **Failed**.

### Understanding the retry window

The `retryWindowInMinutes` controls the window during which the system is allowed to **submit** new retry requests to the compute platform — it is **not** a timeout and not a hard deadline for the overall operation to complete.

For example, suppose an operation starts at 1:00 AM with a retry window of 30 minutes:
- At 1:25 AM the system receives a failure response from the platform. Since 1:25 AM is within the 30-minute window, the system submits another retry request.
- That retry request may take longer than the remaining 5 minutes to complete. The system will wait for the platform to respond, even if the response arrives after 1:30 AM.

In other words, the retry window governs when the **last retry can be initiated**, not when the final response must be received. An operation's total duration may extend beyond the retry window if a retry was dispatched before the window expired.

> **Important:** The retry window is not a timeout. Once a retry request has been submitted to the platform, the system waits for the platform to respond. The VM operation may still be ongoing even after the retry window has elapsed.

### Example: Start operation with retry policy

```json
{
  "resources": {
    "ids": [
      "/subscriptions/{sub-id}/resourceGroups/{rg}/providers/Microsoft.Compute/virtualMachines/{vm-name}"
    ]
  },
  "executionParameters": {
    "retryPolicy": {
      "retryWindowInMinutes": 120
    }
  },
  "correlationId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}
```

> 📝 **SDK sample:** See [StartWithCleanBootFallback.cs](StartWithCleanBootFallback.cs) for a complete .NET example of submitting a Start operation with retry policy.

### Default behavior (no retry)

By default, if you do not provide a `retryPolicy` — or provide one without specifying `retryWindowInMinutes` — **no retries are performed**. The operation is attempted once, and if it fails, it is immediately marked as failed.

To enable retries, you must explicitly provide a `retryPolicy` with a `retryWindowInMinutes` value greater than `0`.

---

## Fallback (OnFailureAction)

The fallback action (`onFailureAction`) provides a safety net when an operation fails and all retry attempts have been exhausted. Instead of simply failing, the system can perform an alternative operation to leave the VM in a known, safe state.

### Supported fallback actions by operation type

Not all operations support fallback actions. The table below shows which fallback action is available for each operation type:

| Operation Type | Supported `onFailureAction` | Fallback Behavior |
|----------------|----------------------------|-------------------|
| **Start** | `Start` | If a resume (start on a hibernated VM) fails after all retries, the system performs a clean boot — bringing the VM back online from scratch. The hibernated session state is not preserved. |
| **Hibernate** | `Deallocate` | If hibernate fails after all retries, the system deallocates the VM instead. The VM is deallocated (not hibernated), but resources are released. |
| **Create** | `Delete` | If create fails after all retries, the system deletes the partially-created VM to clean up resources. |
| **Deallocate** | _Not supported_ | Deallocate operations do not support fallback actions. |
| **Delete** | _Not supported_ | Delete operations do not support fallback actions. |

> **Note:** Specifying an unsupported `onFailureAction` for an operation type will result in a `400 Bad Request` error.

### How fallback works

The fallback action is the **last resort** — it only executes after all retries and the retry window have been exhausted. The sequence is:

1. **Initial attempt** — The system executes the requested operation (e.g., Start).
2. **Retries** — On retriable failure, the system retries the operation within the `retryWindowInMinutes` window.
3. **Fallback** — If all retries fail (or the retry window expires), and an `onFailureAction` is configured, the system executes the fallback action. The fallback is always allowed to run to completion, even if the retry window has expired.
4. **Final status** — The operation is reported with the original operation's error, plus a `fallbackOperationInfo` field in the response showing the fallback outcome.

### Fallback behavior details

- **Optional.** The `onFailureAction` field is optional within the retry policy. If you do not specify it, no fallback is executed when retries are exhausted.
- **Executed at most once.** The fallback is a one-time, last-resort action. If the fallback itself fails, the operation is marked as failed.
- **Not retried.** The fallback action is not repeated. If it fails, the entire operation fails.
- **Ignores the retry window.** The fallback is exempt from the retry window and is always allowed to finish. The total operation duration may extend beyond `retryWindowInMinutes` while the fallback completes.
- **Original error is preserved.** The response includes both the original error (`resourceOperationError`) and the fallback result (`fallbackOperationInfo`), so you can see what went wrong and whether the fallback recovered.

### Example: Start with fallback

```json
{
  "resources": {
    "ids": [
      "/subscriptions/{sub-id}/resourceGroups/{rg}/providers/Microsoft.Compute/virtualMachines/{vm-name}"
    ]
  },
  "executionParameters": {
    "retryPolicy": {
      "retryWindowInMinutes": 120,
      "onFailureAction": "Start"
    }
  },
  "correlationId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}
```

> 📝 **SDK sample:** See [StartWithCleanBootFallback.cs](StartWithCleanBootFallback.cs) for a complete .NET example including status polling and fallback interpretation.

### Example: Hibernate with Deallocate fallback

```json
{
  "resources": {
    "ids": [
      "/subscriptions/{sub-id}/resourceGroups/{rg}/providers/Microsoft.Compute/virtualMachines/{vm-name}"
    ]
  },
  "executionParameters": {
    "retryPolicy": {
      "retryWindowInMinutes": 60,
      "onFailureAction": "Deallocate"
    }
  },
  "correlationId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
}
```

> 📝 **SDK sample:** See [HibernateWithDeallocateFallback.cs](HibernateWithDeallocateFallback.cs) for a complete .NET example including status polling and fallback interpretation.

---

## Checking operation status

When you query the status of an operation, the response includes information about the retry policy that was applied and, if applicable, the fallback operation result.

### Response fields

| Field | Description |
|-------|-------------|
| `retryPolicy` | The retry policy that was applied to this operation. |
| `resourceOperationError` | The error from the primary operation, if it failed. |
| `fallbackOperationInfo` | Present only if a fallback was executed. Contains the fallback operation type, status, and any error. |

### Example: Operation succeeded via fallback

```json
{
  "operationId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "resourceId": "/subscriptions/.../virtualMachines/my-vm",
  "opType": "Start",
  "state": "Failed",
  "retryPolicy": {
    "retryWindowInMinutes": 120,
    "onFailureAction": "Start"
  },
  "resourceOperationError": {
    "errorCode": "AllocationFailed",
    "errorDetails": "Allocation failed. Please try again later."
  },
  "fallbackOperationInfo": {
    "lastOpType": "Start",
    "status": "Succeeded",
    "error": null,
    "errorDetails": null
  }
}
```

In this example:
- The original Start operation failed with `AllocationFailed` after exhausting all retries.
- The fallback (clean boot) was executed and succeeded.
- The `fallbackOperationInfo.status` is `Succeeded`, meaning the VM was successfully started via the fallback path.

### Example: Fallback was executed but also failed

```json
{
  "operationId": "xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx",
  "resourceId": "/subscriptions/.../virtualMachines/my-vm",
  "opType": "Start",
  "state": "Failed",
  "retryPolicy": {
    "retryWindowInMinutes": 120,
    "onFailureAction": "Start"
  },
  "resourceOperationError": {
    "errorCode": "AllocationFailed",
    "errorDetails": "Allocation failed. Please try again later."
  },
  "fallbackOperationInfo": {
    "lastOpType": "Start",
    "status": "Failed",
    "error": {
      "errorCode": "AllocationFailed",
      "errorDetails": "Allocation failed in fallback attempt."
    }
  }
}
```

In this example, both the primary operation and the fallback failed. The `fallbackOperationInfo` contains the error from the fallback attempt. Note the `lastOpType` field — it tells you the last operation the system attempted, which is useful for understanding what final action was taken on the VM before the failure.

---

## Interpreting the response when using fallback

When you configure an `onFailureAction`, it is important to understand how to read the operation status response correctly. The top-level `state` field reflects the outcome of the **primary** operation — not the fallback. This means that **even when the fallback succeeds, the `state` will still be `Failed`** because the original operation did not succeed on its own.

To determine the actual outcome of your operation, check the `fallbackOperationInfo` field:

| `state` | `fallbackOperationInfo.status` | What happened | Is your VM in the desired state? |
|---------|-------------------------------|---------------|----------------------------------|
| `Succeeded` | _(not present)_ | The primary operation succeeded. No fallback was needed. | ✅ Yes |
| `Failed` | `Succeeded` | The primary operation failed, but the **fallback recovered successfully**. | ✅ Yes — the fallback brought the VM to the desired state. |
| `Failed` | `Failed` | Both the primary operation and the fallback failed. | ❌ No — manual intervention may be needed. |
| `Failed` | _(not present)_ | The primary operation failed and no fallback was configured or the error was non-retriable. | ❌ No |

### Recommended logic for checking results

When processing operation results with fallback enabled, use the following logic:

```
if operation.state == "Succeeded":
    # Primary operation succeeded — nothing else to check
else if operation.state == "Failed" and operation.fallbackOperationInfo is not None:
    if operation.fallbackOperationInfo.status == "Succeeded":
        # Fallback recovered — VM is in the desired state
    else:
        # Both primary and fallback failed — check errors
        # operation.resourceOperationError → primary error
        # operation.fallbackOperationInfo.error → fallback error
        # operation.fallbackOperationInfo.lastOpType → the last operation attempted
else:
    # Primary operation failed, no fallback was executed
    # Check operation.resourceOperationError for details
```

> **Key takeaway:** When using fallback, do not rely solely on the `state` field. If `state` is `Failed` and you configured an `onFailureAction`, check `fallbackOperationInfo.status` to determine whether the fallback successfully recovered the operation. If the fallback also failed, check `fallbackOperationInfo.lastOpType` to identify the last operation the system attempted on the VM.

---

## .NET SDK Samples

These samples require `Azure.ResourceManager.ComputeSchedule` version **1.2.0-alpha.20260401.1** or later, which introduces:
- `UserRequestRetryPolicy` with `OnFailureAction` property
- `FallbackOperationInfo` on `ResourceOperationDetails` for typed fallback results

| File | Description |
|------|-------------|
| [HibernateWithDeallocateFallback.cs](HibernateWithDeallocateFallback.cs) | Hibernate a VM with automatic Deallocate if hibernate fails |
| [StartWithCleanBootFallback.cs](StartWithCleanBootFallback.cs) | Resume a hibernated VM with automatic clean boot if resume fails |
| [CreateWithDeleteFallback.cs](CreateWithDeleteFallback.cs) | Create a VM with automatic Delete cleanup if creation fails |
| [HibernateFallbackOnlyNoRetry.cs](HibernateFallbackOnlyNoRetry.cs) | Hibernate with Deallocate fallback, no retry window (single attempt → fallback) |
| [OperationStatusHelper.cs](OperationStatusHelper.cs) | Shared helper for polling status and interpreting fallback results |

---

## Summary

| Concept | Description |
|---------|-------------|
| **Retry Policy** | Controls automatic retries via `retryWindowInMinutes` (time limit). **Defaults to no retries if not explicitly configured.** |
| **OnFailureAction (Fallback)** | An alternative operation executed after all retries are exhausted. Supported: Start→clean boot, Hibernate→Deallocate, Create→Delete. |
| **Non-retriable errors** | Some errors are permanent and cannot be resolved by retrying. For these errors, the operation fails immediately — retry policy and fallback are not applied. |

---

## Common Questions

**Q: What happens if I don't specify a retry policy at all?**

The operation is attempted once. If it fails, it is immediately marked as failed with no retries and no fallback. To enable retries, you must explicitly provide a `retryPolicy` with a `retryWindowInMinutes` greater than `0`.

**Q: Can I use `onFailureAction` without retries?**

Yes. You can omit `retryWindowInMinutes` (or set it to `0`) and still specify an `onFailureAction`. In this case, if the single attempt fails with a retriable error, the system will go directly to the fallback action without any retries.

**Q: Why did my operation fail without retrying, even though I set a retry policy?**

This typically happens when the error is a **non-retriable** (permanent) error. Non-retriable errors indicate a fundamental issue (e.g., the VM does not exist, invalid configuration, or insufficient permissions) that retrying would not resolve. The system skips retries and fallback entirely for these errors. Check the `resourceOperationError` in the response for the specific error code.

**Q: My operation took longer than `retryWindowInMinutes` to complete. Is that expected?**

Yes. The retry window controls when new retry attempts can be **initiated**, not when the operation must finish. If a retry or fallback was started before the window expired, it is allowed to complete. The retry window is not a timeout.

**Q: What happens if the fallback itself fails?**

The operation is marked as failed. The response will include both the original error (in `resourceOperationError`) and the fallback error (in `fallbackOperationInfo`). The fallback is not retried — it is a one-time, last-resort attempt.

**Q: Does the fallback count against my retry window?**

No. The fallback is separate from the retry window. It is only triggered **after** the retry window expires (or retries are exhausted). The fallback is always allowed to run to completion regardless of the retry window.

**Q: If the Start fallback performs a clean boot, will I lose my hibernated state?**

Yes. The Start fallback discards the hibernated session state and boots the VM fresh. This trade-off is made to maximize the chance of the VM coming back online.

**Q: Can I configure the fallback to use a different action than the one listed?**

No. Each operation type has a single supported `onFailureAction` value. Specifying an unsupported value will result in a `400 Bad Request` error. See [Supported fallback actions by operation type](#supported-fallback-actions-by-operation-type) for the allowed combinations.

**Q: Does the Deallocate or Delete operation support fallback?**

No. Deallocate and Delete operations do not support `onFailureAction`. If specified, the request will be rejected with a `400 Bad Request` error.
