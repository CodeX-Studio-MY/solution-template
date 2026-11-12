# Offboarding a tenant

The lifecycle is `Active → Suspended → PendingDeletion → Deleted`; each step is an audited operator action, and nothing is deleted
before the grace period ends.

1. **Agree it with the customer** (contract end, written request) and record who asked.
2. **Suspend** (optional, to stop writes while the export runs): `POST /api/tenancy/v1/operator/tenants/{id}/suspend`.
3. **Begin offboarding**: `POST /api/tenancy/v1/operator/tenants/{id}/offboard`. The platform:
   - produces the **full, machine-readable export** (`ITenantExporter`: one exporter for the whole platform, registered in the Tenancy module;
     modules can't add their own). The template registers `NoExportYet`, which exports nothing, so replace it with an exporter that covers
     every module that stores customer data *before* you rely on this step;
   - revokes every token and session of the tenant (so nobody can sign in), removes callback registrations, and sets the **grace period**
     (`DeleteAfter` in the catalog).
4. **Deliver the export** to the customer through the channel you agreed, and keep the confirmation.
5. **Grace period**: the data is still there. To undo, `POST /api/tenancy/v1/operator/tenants/{id}/cancel-offboarding`.
6. **Complete the deletion** after the grace period: `POST /api/tenancy/v1/operator/tenants/{id}/complete-deletion` drops the tenant database
   and marks the catalog row `Deleted` (the slug stays reserved so it cannot be taken by someone else by accident).
   It also removes the tenant's rows in `TenantPrincipalAccess` and `TenantPrincipalLogins`, and clears the addresses in
   `TenantPrincipalAccessAudit`. Shared principals stay, because they may have access to other tenants.
7. **Files**: delete the tenant's prefix in object storage (`{tenantId}/`), and let versions expire per the bucket policy.
8. **Backups** age out per the retention policy. Tell the customer when the last copy will be gone.
9. **Confirm in writing**: the date of deletion, what was exported, and when backups expire. The audit log (`catalog` schema, Tenancy)
   holds the operator actions and times.

What stays: the audit entries about the tenant's lifecycle and its access changes (without addresses), and aggregate billing records (no personal data).
