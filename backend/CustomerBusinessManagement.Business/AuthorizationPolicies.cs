using Microsoft.AspNetCore.Authorization;

namespace CustomerBusinessManagement.Business;

public static class AuthorizationPolicies
{
    public const string SuperAdmin = "SuperAdmin";
    public const string Admin = "Admin";

    public static AuthorizationPolicy CreatePolicy(params string[] roles) =>
        new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .RequireRole(roles)
            .Build();

    public static AuthorizationPolicy AdminPolicy =>
        CreatePolicy(Admin, SuperAdmin);
}
