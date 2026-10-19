using Saaskit.Core.Errors;
using Saaskit.Core.Platform;

namespace Saaskit.Identity.Application;

/// <summary>The Identity module's error catalog.</summary>
[ErrorCatalog(Modules.AccessManagement)]
public static class IdentityErrors
{
    public static readonly ErrorCode EmailInUse = new("access-management.email-in-use", 409, "errors.identity.emailInUse");
    public static readonly ErrorCode UnknownModule = new("access-management.unknown-module", 422, "errors.identity.unknownModule");
    public static readonly ErrorCode PrincipalNotFound = new("access-management.principal-not-found", 404, "errors.identity.principalNotFound");
    public static readonly ErrorCode GroupNotFound = new("access-management.group-not-found", 404, "errors.identity.groupNotFound");
    public static readonly ErrorCode GroupNameInUse = new("access-management.group-name-in-use", 409, "errors.identity.groupNameInUse");
    public static readonly ErrorCode DuplicateAssignment = new("access-management.duplicate-assignment", 409, "errors.identity.duplicateAssignment");
    public static readonly ErrorCode ServicePrincipalsHaveNoGroups = new("access-management.service-principals-have-no-groups", 422, "errors.identity.servicePrincipalsHaveNoGroups");
    public static readonly ErrorCode EnrollmentInvalid = new("access-management.enrollment-invalid", 422, "errors.identity.enrollmentInvalid");
    public static readonly ErrorCode ModuleNotEntitledForAssignment = new("access-management.module-not-entitled", 422, "errors.identity.moduleNotEntitled");
}
