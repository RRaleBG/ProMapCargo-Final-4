using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ProMapCargo.Api.Models;
using ProMapCargo.Api.Services;

namespace ProMapCargo.Api.Pages.Account.Admin;

[Authorize(Policy = "users.read")]
public sealed class IndexModel(IAccountAdminService accountAdminService) : PageModel
{
    private bool HasPermission(string permission)
        => User.IsInRole("Administrator") || User.HasClaim("permission", permission);

    private IActionResult ForbidIfMissing(string permission)
        => HasPermission(permission) ? null! : Forbid();

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string Status { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    public string Role { get; set; } = "all";

    [BindProperty(SupportsGet = true)]
    // Page number of the user list (hides PageModel.Page(), which this page does not use).
    public new int Page { get; set; } = 1;

    [BindProperty(SupportsGet = true)]
    public int PageSize { get; set; } = 20;

    public AccountAdminSummary Summary { get; private set; } = new();
    public IReadOnlyList<AccountAdminUserRow> Users { get; private set; } = [];
    public IReadOnlyList<AccountAdminRoleRow> AvailableRoles { get; private set; } = [];
    public IReadOnlyList<AccountAdminRolePermissionRow> RolePermissions { get; private set; } = [];
    public IReadOnlyList<AuditLog> RecentAuditLogs { get; private set; } = [];
    public IReadOnlyList<AccountAdminDriverOption> DriverOptions { get; private set; } = [];
    public AccountAdminPager Pager { get; private set; } = new();

    [BindProperty]
    public CreateUserInput CreateUser { get; set; } = new();

    [BindProperty]
    public UpdateUserInput EditUser { get; set; } = new();

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IActionResult> OnPostToggleActiveAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!HasPermission("users.manage"))
        {
            return Forbid();
        }
        var result = await accountAdminService.ToggleActiveAsync(userId, cancellationToken).ConfigureAwait(false);
        TempData[result.Success ? "Toast.Success" : "Toast.Error"] = result.Message;
        return RedirectWithFilters();
    }

    public async Task<IActionResult> OnPostToggleLockAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (!HasPermission("users.manage"))
        {
            return Forbid();
        }
        var result = await accountAdminService.ToggleLockAsync(userId, cancellationToken).ConfigureAwait(false);
        TempData[result.Success ? "Toast.Success" : "Toast.Error"] = result.Message;
        return RedirectWithFilters();
    }

    public async Task<IActionResult> OnPostSetRoleAsync(Guid userId, string roleName, bool assign, CancellationToken cancellationToken)
    {
        if (!HasPermission("roles.manage"))
        {
            return Forbid();
        }
        var result = await accountAdminService.SetRoleAsync(userId, roleName, assign, cancellationToken).ConfigureAwait(false);
        TempData[result.Success ? "Toast.Success" : "Toast.Error"] = result.Message;
        return RedirectWithFilters();
    }

    public async Task<IActionResult> OnPostTogglePermissionAsync(Guid roleId, string permission, bool grant, CancellationToken cancellationToken)
    {
        if (!HasPermission("permissions.manage"))
        {
            return Forbid();
        }
        var result = await accountAdminService.TogglePermissionAsync(roleId, permission, grant, cancellationToken).ConfigureAwait(false);
        TempData[result.Success ? "Toast.Success" : "Toast.Error"] = result.Message;
        return RedirectWithFilters();
    }

    public async Task<IActionResult> OnPostCreateUserAsync(CancellationToken cancellationToken)
    {
        if (!HasPermission("users.manage"))
        {
            return Forbid();
        }
        var result = await accountAdminService.CreateUserAsync(new AccountAdminCreateUserInput(
            CreateUser.DisplayName,
            CreateUser.Email,
            CreateUser.Password,
            CreateUser.PhoneNumber,
            CreateUser.DriverId,
            CreateUser.RoleNames,
            CreateUser.ProfileImage), cancellationToken).ConfigureAwait(false);
        TempData[result.Success ? "Toast.Success" : "Toast.Error"] = result.Message;
        return RedirectWithFilters();
    }

    public async Task<IActionResult> OnPostUpdateUserAsync(CancellationToken cancellationToken)
    {
        if (!HasPermission("users.manage"))
        {
            return Forbid();
        }
        var result = await accountAdminService.UpdateUserAsync(new AccountAdminUpdateUserInput(
            EditUser.UserId,
            EditUser.DisplayName,
            EditUser.Email,
            EditUser.Password,
            EditUser.PhoneNumber,
            EditUser.DriverId,
            EditUser.RoleNames,
            EditUser.ProfileImage), cancellationToken).ConfigureAwait(false);
        TempData[result.Success ? "Toast.Success" : "Toast.Error"] = result.Message;
        return RedirectWithFilters();
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var dashboard = await accountAdminService
            .GetDashboardAsync(new AccountAdminQuery(Search, Status, Role, Page, PageSize), cancellationToken)
            .ConfigureAwait(false);

        Summary = dashboard.Summary;
        Users = dashboard.Users;
        AvailableRoles = dashboard.AvailableRoles;
        RolePermissions = dashboard.RolePermissions;
        RecentAuditLogs = dashboard.RecentAuditLogs;
        DriverOptions = dashboard.DriverOptions;
        Pager = dashboard.Pager;

        Page = Pager.Page;
        PageSize = Pager.PageSize;
    }

    private RedirectToPageResult RedirectWithFilters()
    {
        var fallbackSearch = Search ?? Request.Query["Search"].ToString();
        var fallbackStatus = string.IsNullOrWhiteSpace(Status) ? Request.Query["Status"].ToString() : Status;
        var fallbackRole = string.IsNullOrWhiteSpace(Role) ? Request.Query["Role"].ToString() : Role;

        var fallbackPage = Page;
        if (fallbackPage <= 0 && int.TryParse(Request.Query["Page"], out var queryPage) && queryPage > 0)
        {
            fallbackPage = queryPage;
        }

        var fallbackPageSize = PageSize;
        if ((fallbackPageSize <= 0 || fallbackPageSize > 100) && int.TryParse(Request.Query["PageSize"], out var queryPageSize) && queryPageSize > 0)
        {
            fallbackPageSize = queryPageSize;
        }

        return RedirectToPage(new
        {
            Search = fallbackSearch,
            Status = string.IsNullOrWhiteSpace(fallbackStatus) ? "all" : fallbackStatus,
            Role = string.IsNullOrWhiteSpace(fallbackRole) ? "all" : fallbackRole,
            Page = fallbackPage <= 0 ? 1 : fallbackPage,
            PageSize = fallbackPageSize is < 5 or > 100 ? 20 : fallbackPageSize
        });
    }
}

public class CreateUserInput
{
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? PhoneNumber { get; set; }
    public Guid? DriverId { get; set; }
    public List<string> RoleNames { get; set; } = [];
    public IFormFile? ProfileImage { get; set; }
}

public sealed class UpdateUserInput : CreateUserInput
{
    public Guid UserId { get; set; }
}
