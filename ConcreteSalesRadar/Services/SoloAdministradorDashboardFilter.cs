using ConcreteSalesRadar.Models.Entities;
using Hangfire.Dashboard;

namespace ConcreteSalesRadar.Services;

/// <summary>
/// Permite ver el dashboard de Hangfire solo a usuarios con el rol Administrador
/// (el mismo rol que protege AdminController).
/// </summary>
public class SoloAdministradorDashboardFilter : IDashboardAuthorizationFilter
{
    public bool Authorize(DashboardContext context)
    {
        var http = context.GetHttpContext();
        return http.User.Identity?.IsAuthenticated == true
            && http.User.IsInRole(Roles.Administrador);
    }
}
